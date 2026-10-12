using System.Net;
using System.Net.Http.Headers;
using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// nth web surfaces, unit W23 (DESIGN 2.1): the real parts of the runtime flow, on stand-ins that reach nobody. The download runs over
// a scripted HTTP handler (no network), the signature check over the .NET runtime's own Microsoft-signed assembly and an unsigned
// file (the real WinVerifyTrust, no network call), the process runner over cmd.exe with a harmless command. The real download from
// Microsoft is WebRuntimeInstallerNativeTests, run alone.
[Trait("Decision", "ED-NEW-web-w23-2")]
[Trait("Feature", "W-23")]
public sealed class WebRuntimePlatformTests : IDisposable
{
    private readonly string temp = Directory.CreateTempSubdirectory("nth-w23-platform ").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(temp, recursive: true);
        }
        catch (IOException)
        {
            // a child process may still hold its working folder for a moment
        }
    }

    private HttpBootstrapperSource Source(HttpMessageHandler handler) =>
        new(() => new HttpClient(handler, disposeHandler: false), () => temp, allowTestHost: true);

    private static HttpResponseMessage Bytes(int length, HttpStatusCode status = HttpStatusCode.OK, Uri? finalUri = null)
    {
        var content = new ByteArrayContent(new byte[length]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = new HttpResponseMessage(status) { Content = content };
        if (finalUri is not null) response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri);
        return response;
    }

    private string[] DownloadFolders() => Directory.GetDirectories(temp, "nth-webview2-*");

    [Fact]
    public async Task A_good_download_lands_in_its_own_folder_under_the_temp_folder_and_is_held_until_it_is_disposed()
    {
        var handler = new ScriptedHandler(_ => Bytes(2_000_000, finalUri: new Uri("https://msedge.sf.dl.delivery.mp.microsoft.com/files/MicrosoftEdgeWebview2Setup.exe")));
        var source = Source(handler);

        var file = await source.DownloadAsync(CancellationToken.None);

        Assert.Equal(1, handler.Requests);
        Assert.Equal("MicrosoftEdgeWebview2Setup.exe", Path.GetFileName(file.Path));
        Assert.StartsWith(temp, file.Path, StringComparison.Ordinal);
        Assert.Equal(2_000_000, new FileInfo(file.Path).Length);
        // Nothing can swap or remove it between the signature check and the start.
        Assert.Throws<IOException>(() => File.Delete(file.Path));
        Assert.Throws<IOException>(() => File.OpenWrite(file.Path));
        using (File.Open(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { }

        file.Dispose();

        Assert.False(File.Exists(file.Path));
        Assert.Empty(DownloadFolders());
        file.Dispose(); // twice is fine
    }

    [Fact]
    public async Task Two_downloads_never_share_a_folder()
    {
        var source = Source(new ScriptedHandler(_ => Bytes(100_000, finalUri: new Uri("https://dl.test/x.exe"))));
        using var first = await source.DownloadAsync(CancellationToken.None);
        using var second = await source.DownloadAsync(CancellationToken.None);
        Assert.NotEqual(Path.GetDirectoryName(first.Path), Path.GetDirectoryName(second.Path));
    }

    [Fact]
    public async Task A_download_that_ends_on_a_plain_http_address_is_refused_and_leaves_nothing()
    {
        var source = Source(new ScriptedHandler(_ => Bytes(100_000, finalUri: new Uri("http://dl.test/x.exe"))));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.DownloadFailed, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task An_http_error_is_a_download_failure_not_offline_and_leaves_nothing(HttpStatusCode status)
    {
        var source = Source(new ScriptedHandler(_ => Bytes(10, status, new Uri("https://dl.test/x.exe"))));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.DownloadFailed, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(65_535)]
    [InlineData(8_388_609)]
    [InlineData(20_000_000)]
    public async Task A_file_the_wrong_size_for_the_bootstrapper_is_dropped(int length)
    {
        var source = Source(new ScriptedHandler(_ => Bytes(length, finalUri: new Uri("https://dl.test/x.exe"))));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.DownloadFailed, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Fact]
    public async Task A_stream_longer_than_it_said_is_cut_off_at_the_limit()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new EndlessStream()) };
        response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("https://dl.test/x.exe"));
        var source = Source(new ScriptedHandler(_ => response));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.DownloadFailed, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError)]
    [InlineData(HttpRequestError.ConnectionError)]
    [InlineData(HttpRequestError.ProxyTunnelError)]
    public async Task No_way_through_to_Microsoft_is_offline(HttpRequestError reason)
    {
        var source = Source(new ThrowingHandler(new HttpRequestException(reason, "no route")));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.Offline, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Fact]
    public async Task A_connection_that_timed_out_before_our_own_limit_is_offline()
    {
        var source = Source(new ThrowingHandler(new TaskCanceledException("connect timed out")));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.Offline, error.Failure);
        Assert.Empty(DownloadFolders());
    }

    [Fact]
    public async Task A_tls_failure_is_a_failed_download_not_offline()
    {
        var source = Source(new ThrowingHandler(new HttpRequestException(HttpRequestError.SecureConnectionError, "tls")));
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => source.DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.DownloadFailed, error.Failure);
    }

    [Fact]
    public async Task The_callers_cancellation_passes_through_and_leaves_nothing()
    {
        using var cancel = new CancellationTokenSource();
        var source = Source(new ScriptedHandler(_ =>
        {
            cancel.Cancel();
            cancel.Token.ThrowIfCancellationRequested();
            return Bytes(1);
        }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.DownloadAsync(cancel.Token));
        Assert.Empty(DownloadFolders());
    }

    // ---- the signature check: the real WinVerifyTrust, no network ----

    [Fact]
    public void A_file_with_no_signature_is_not_valid()
    {
        var file = Path.Combine(temp, "unsigned.exe");
        File.WriteAllBytes(file, [0x4D, 0x5A, 0x90, 0x00, 1, 2, 3]);

        var result = new WindowsAuthenticodeCheck().Check(file);

        Assert.False(result.Valid);
        Assert.Null(result.Subject);
        Assert.False(WebRuntimeBootstrapper.IsAcceptableSigner(result.Subject));
    }

    [Fact]
    public void A_missing_file_is_not_valid_and_does_not_throw()
    {
        Assert.False(new WindowsAuthenticodeCheck().Check(Path.Combine(temp, "nothing.exe")).Valid);
    }

    [Fact]
    public void The_WebView2_loader_shipped_beside_the_app_is_signed_by_Microsoft_Corporation_and_passes()
    {
        var loader = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");

        var result = new WindowsAuthenticodeCheck().Check(loader);

        Assert.True(result.Valid, "WebView2Loader.dll should carry Microsoft's Authenticode signature: " + loader);
        Assert.True(WebRuntimeBootstrapper.IsAcceptableSigner(result.Subject), result.Subject);
    }

    [Fact]
    public async Task The_async_check_gives_the_same_answer_as_the_direct_one()
    {
        var loader = Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll");
        var check = new WindowsAuthenticodeCheck();
        var task = check.CheckAsync(loader, CancellationToken.None);
        var result = await task;

        Assert.Equal(check.Check(loader), result);
        Assert.True(result.Valid);
        Assert.False((await check.CheckAsync(Path.Combine(temp, "nothing.exe"), CancellationToken.None)).Valid);
    }

    [Fact]
    public void A_valid_Microsoft_signature_under_another_common_name_does_not_pass_the_signer_rule()
    {
        // The .NET runtime's own assemblies are validly signed with O=Microsoft Corporation but CN=.NET: not the bootstrapper's signer.
        var result = new WindowsAuthenticodeCheck().Check(typeof(object).Assembly.Location);

        Assert.True(result.Valid);
        Assert.Contains("O=Microsoft Corporation", result.Subject, StringComparison.Ordinal);
        Assert.False(WebRuntimeBootstrapper.IsAcceptableSigner(result.Subject), result.Subject);
    }

    [Fact]
    public void A_signed_file_that_was_changed_afterwards_is_not_valid()
    {
        var copy = Path.Combine(temp, "WebView2Loader.dll");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "WebView2Loader.dll"), copy);
        using (var stream = new FileStream(copy, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = 1024;
            var b = stream.ReadByte();
            stream.Position = 1024;
            stream.WriteByte((byte)(b ^ 0xFF));
        }

        Assert.False(new WindowsAuthenticodeCheck().Check(copy).Valid);
    }

    // ---- the process runner: a harmless child ----

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task The_runner_returns_the_exit_code_of_a_process_it_started_without_a_window()
    {
        var runner = new ProcessBootstrapperRunner(allowTestHost: true);
        Assert.Equal(0, await runner.RunAsync(Cmd, "/c exit 0", TimeSpan.FromSeconds(30), CancellationToken.None));
        Assert.Equal(7, await runner.RunAsync(Cmd, "/c exit 7", TimeSpan.FromSeconds(30), CancellationToken.None));
    }

    [Fact]
    public async Task A_program_that_cannot_start_is_could_not_start()
    {
        var runner = new ProcessBootstrapperRunner(allowTestHost: true);
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => runner.RunAsync(Path.Combine(temp, "missing.exe"), "/silent /install", TimeSpan.FromSeconds(5), CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.CouldNotStart, error.Failure);
    }

    [Fact]
    public async Task A_program_that_outlasts_the_timeout_is_timed_out_and_left_to_finish_on_its_own()
    {
        var runner = new ProcessBootstrapperRunner(allowTestHost: true);
        var started = DateTime.UtcNow;
        var error = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => runner.RunAsync(Cmd, "/c ping -n 3 127.0.0.1 >nul", TimeSpan.FromMilliseconds(300), CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.TimedOut, error.Failure);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(2), "it stops waiting at the timeout");
    }

    [Fact]
    public async Task Cancelling_stops_the_wait_with_the_cancellation_not_a_timeout()
    {
        var runner = new ProcessBootstrapperRunner(allowTestHost: true);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(Cmd, "/c ping -n 3 127.0.0.1 >nul", TimeSpan.FromSeconds(30), cancel.Token));
    }
}
