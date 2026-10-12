using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// W23: the real parts of the runtime flow (<see cref="WebRuntimeInstaller"/>): Microsoft's bootstrapper downloaded over HTTPS,
/// its Authenticode signature checked through WinVerifyTrust, and the process started without elevation. Each is a seam
/// the flow's tests replace; the real ones refuse to act inside a test host (R-TEST-10: no test may start a real installer
/// or reach Microsoft), and no unit test calls them. The native tests in <c>WebRuntimeInstallerNativeTests</c> do, alone.
/// </summary>
public static class WebRuntimePlatform
{
    /// <summary>The flow on the real platform; <paramref name="openPage"/> opens an address in the default browser.</summary>
    public static WebRuntimeInstaller Create(WebSurface surface, Action<string> openPage) =>
        new(surface, new HttpBootstrapperSource(), new WindowsAuthenticodeCheck(), new ProcessBootstrapperRunner(), openPage);
}

/// <summary>
/// Downloads <see cref="WebRuntimeBootstrapper.DownloadUrl"/> into a fresh folder under the user's temp folder. HTTPS at the end
/// of every redirect, at most <see cref="WebRuntimeBootstrapper.MaxBytes"/>, at most <see cref="WebRuntimeBootstrapper.DownloadTimeout"/>.
/// The returned file stays open for reading with writes and deletes refused until it is disposed, so nothing can swap it
/// between the signature check and the start (ENGINEERING DECISION, ED-NEW-web-w23-2).
/// </summary>
public sealed class HttpBootstrapperSource : IBootstrapperSource
{
    private readonly Func<HttpClient> client;
    private readonly Func<string> tempRoot;
    private readonly bool allowTestHost;

    /// <param name="client">Makes the HTTP client (a fresh one per download by default; the source disposes it).</param>
    /// <param name="tempRoot">The folder the download folder is made in (the user's temp folder by default).</param>
    public HttpBootstrapperSource(Func<HttpClient>? client = null, Func<string>? tempRoot = null)
        : this(client, tempRoot, allowTestHost: false)
    {
    }

    /// <param name="allowTestHost">True only for the native tests, which run alone and do reach Microsoft (R-TEST-10 otherwise refuses).</param>
    internal HttpBootstrapperSource(Func<HttpClient>? client, Func<string>? tempRoot, bool allowTestHost)
    {
        this.client = client ?? DefaultClient;
        this.tempRoot = tempRoot ?? Path.GetTempPath;
        this.allowTestHost = allowTestHost;
    }

    private static HttpClient DefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var http = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("nth", "1"));
        return http;
    }

    public async Task<IBootstrapperFile> DownloadAsync(CancellationToken cancellation)
    {
        if (ShellGuard.InTestHost && !allowTestHost) throw new WebRuntimeInstallException(WebRuntimeInstallFailure.Offline, "a test host never reaches Microsoft");
        var folder = Path.Combine(tempRoot(), "nth-webview2-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, WebRuntimeBootstrapper.FileName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(WebRuntimeBootstrapper.DownloadTimeout);
        try
        {
            Directory.CreateDirectory(folder);
            using var http = client();
            using var response = await http.GetAsync(WebRuntimeBootstrapper.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!WebRuntimeBootstrapper.IsAcceptableSource(response.RequestMessage?.RequestUri))
                throw new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the download did not end on an HTTPS address");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && !WebRuntimeBootstrapper.IsAcceptableSize(length))
                throw new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the file is not the size of the bootstrapper");
            long total;
            await using (var network = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                total = 0;
                int read;
                while ((read = await network.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > WebRuntimeBootstrapper.MaxBytes)
                        throw new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the file is larger than the bootstrapper");
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                }
            }

            if (!WebRuntimeBootstrapper.IsAcceptableSize(total))
                throw new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the file is not the size of the bootstrapper");
            return new DownloadedBootstrapper(folder, path);
        }
        catch (Exception ex)
        {
            DownloadedBootstrapper.RemoveFolder(folder);
            throw ex switch
            {
                WebRuntimeInstallException known => known,
                OperationCanceledException when cancellation.IsCancellationRequested => ex,
                // our own limit ran out mid-download: it started and did not finish; a connect that timed out first means no way through
                OperationCanceledException when timeout.IsCancellationRequested => new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the download took too long", ex),
                OperationCanceledException => new WebRuntimeInstallException(WebRuntimeInstallFailure.Offline, "Microsoft could not be reached", ex),
                HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.ProxyTunnelError } =>
                    new WebRuntimeInstallException(WebRuntimeInstallFailure.Offline, "Microsoft could not be reached", ex),
                HttpRequestException or IOException or UnauthorizedAccessException => new WebRuntimeInstallException(WebRuntimeInstallFailure.DownloadFailed, "the download failed", ex),
                _ => ex,
            };
        }
    }

    private sealed class DownloadedBootstrapper : IBootstrapperFile
    {
        private readonly string folder;
        private FileStream? hold;

        public DownloadedBootstrapper(string folder, string path)
        {
            this.folder = folder;
            Path = path;
            hold = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public string Path { get; }

        public void Dispose()
        {
            hold?.Dispose();
            hold = null;
            RemoveFolder(folder);
        }

        internal static void RemoveFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // the installer may still hold its own copy for a moment; the temp folder is the user's to clean
            }
        }
    }
}

/// <summary>
/// WinVerifyTrust (<c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>, no UI, no revocation network call: the machine may be offline
/// from everything but Microsoft's download) for the file's embedded Authenticode signature, then the signer's subject.
/// </summary>
public sealed class WindowsAuthenticodeCheck : IAuthenticodeCheck
{
    private static readonly Guid ActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdSaferFlag = 0x100;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WintrustData
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, ref Guid pgActionId, ref WintrustData data);

    public Task<AuthenticodeResult> CheckAsync(string path, CancellationToken cancellation) => Task.Run(() => Check(path), cancellation);

    /// <summary>The check itself, on the calling thread.</summary>
    public AuthenticodeResult Check(string path)
    {
        if (!Verify(path)) return new(false, null);
        try
        {
#pragma warning disable SYSLIB0057 // the loader type has no signed-file reader; WinVerifyTrust above already validated this signature
            using var signer = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return new(true, signer.Subject);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or IOException)
        {
            return new(false, null);
        }
    }

    private static bool Verify(string path)
    {
        var fileInfo = new WintrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            pcwszFilePath = path,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero,
        };
        var fileInfoPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
        try
        {
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, false);
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = WtdUiNone,
                fdwRevocationChecks = WtdRevokeNone,
                dwUnionChoice = WtdChoiceFile,
                pFile = fileInfoPtr,
                dwStateAction = WtdStateActionVerify,
                dwProvFlags = WtdSaferFlag,
            };
            var action = ActionGenericVerifyV2;
            var result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            data.dwStateAction = WtdStateActionClose;
            WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            return result == 0;
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfoPtr);
        }
    }
}

/// <summary>
/// Starts the bootstrapper as the current user with no window and no elevation (<c>UseShellExecute</c> false), and waits for it.
/// Past the timeout it stops waiting; it never ends the process.
/// </summary>
public sealed class ProcessBootstrapperRunner : IBootstrapperRunner
{
    private readonly bool allowTestHost;

    public ProcessBootstrapperRunner()
        : this(allowTestHost: false)
    {
    }

    /// <param name="allowTestHost">True only for tests that start a harmless program of their own (R-TEST-10 otherwise refuses).</param>
    internal ProcessBootstrapperRunner(bool allowTestHost) => this.allowTestHost = allowTestHost;

    public async Task<int> RunAsync(string path, string arguments, TimeSpan timeout, CancellationToken cancellation)
    {
        if (ShellGuard.InTestHost && !allowTestHost) throw new WebRuntimeInstallException(WebRuntimeInstallFailure.CouldNotStart, "a test host never runs the real installer");
        Process process;
        try
        {
            process = Process.Start(new ProcessStartInfo(path, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            }) ?? throw new WebRuntimeInstallException(WebRuntimeInstallFailure.CouldNotStart, "the installer did not start");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            throw new WebRuntimeInstallException(WebRuntimeInstallFailure.CouldNotStart, "the installer did not start", ex);
        }

        using (process)
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                throw new WebRuntimeInstallException(WebRuntimeInstallFailure.TimedOut, "the installer did not finish in time");
            }

            return process.ExitCode;
        }
    }
}
