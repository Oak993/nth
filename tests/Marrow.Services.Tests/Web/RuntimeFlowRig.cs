using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

/// <summary>A downloaded bootstrapper that remembers whether it was disposed (removed).</summary>
internal sealed class FakeBootstrapperFile(string path, List<string> log) : IBootstrapperFile
{
    public string Path { get; } = path;

    public bool Disposed { get; private set; }

    public void Dispose()
    {
        Disposed = true;
        log.Add("file disposed");
    }
}

/// <summary>Stands in for Microsoft's download: succeeds with a fake file, fails with a chosen reason, or waits on a task the test completes.</summary>
internal sealed class FakeBootstrapperSource(List<string> log) : IBootstrapperSource
{
    public int Calls { get; private set; }

    public FakeBootstrapperFile? Last { get; private set; }

    public WebRuntimeInstallFailure? Fail { get; set; }

    public TaskCompletionSource<IBootstrapperFile>? Pending { get; set; }

    public Task<IBootstrapperFile> DownloadAsync(CancellationToken cancellation)
    {
        Calls++;
        log.Add("download");
        if (Fail is { } failure) throw new WebRuntimeInstallException(failure, "fake");
        if (Pending is { } pending)
        {
            cancellation.Register(() => pending.TrySetCanceled(cancellation));
            return pending.Task;
        }

        Last = new FakeBootstrapperFile(@"C:\fake\MicrosoftEdgeWebview2Setup.exe", log);
        return Task.FromResult<IBootstrapperFile>(Last);
    }
}

/// <summary>The signature check with a chosen answer (Microsoft's by default).</summary>
internal sealed class FakeSignatureCheck(List<string> log) : IAuthenticodeCheck
{
    public AuthenticodeResult Result { get; set; } = new(true, "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US");

    public List<string> Checked { get; } = [];

    public Task<AuthenticodeResult> CheckAsync(string path, CancellationToken cancellation)
    {
        log.Add("verify");
        Checked.Add(path);
        return Task.FromResult(Result);
    }
}

/// <summary>Runs nothing: records the call, lets the test act while it "runs", and exits with a chosen code, failure or wait.</summary>
internal sealed class FakeBootstrapperRunner(List<string> log) : IBootstrapperRunner
{
    public List<(string Path, string Arguments, TimeSpan Timeout)> Runs { get; } = [];

    public int Exit { get; set; }

    public WebRuntimeInstallFailure? Fail { get; set; }

    public Action? WhileRunning { get; set; }

    public Task<int>? Block { get; set; }

    public async Task<int> RunAsync(string path, string arguments, TimeSpan timeout, CancellationToken cancellation)
    {
        log.Add("run");
        Runs.Add((path, arguments, timeout));
        WhileRunning?.Invoke();
        if (Fail is { } failure) throw new WebRuntimeInstallException(failure, "fake");
        if (Block is { } block) await block;
        return Exit;
    }
}

/// <summary>
/// W23 test rig: the W7 surface rig with the runtime flow on four fakes. Every seam answers in line, and the probe pause is counted,
/// not waited, so a whole flow runs inside the call that pressed the button.
/// </summary>
internal sealed class RuntimeFlowRig : IDisposable
{
    public RuntimeFlowRig(string? installed = null)
    {
        Web = new WebRig(installed);
        Source = new FakeBootstrapperSource(Log);
        Signature = new FakeSignatureCheck(Log);
        Runner = new FakeBootstrapperRunner(Log);
        Installer = new WebRuntimeInstaller(Web.Surface, Source, Signature, Runner, Opened.Add, (_, _) =>
        {
            Delays++;
            OnDelay?.Invoke(Delays);
            return Task.CompletedTask;
        });
        Installer.Attach();
    }

    public WebRig Web { get; }

    public List<string> Log { get; } = [];

    public FakeBootstrapperSource Source { get; }

    public FakeSignatureCheck Signature { get; }

    public FakeBootstrapperRunner Runner { get; }

    public WebRuntimeInstaller Installer { get; }

    public List<string> Opened { get; } = [];

    public int Delays { get; private set; }

    public Action<int>? OnDelay { get; set; }

    public void RuntimeArrives(string version = "141.0.3537.71") => Web.Engine.InstalledVersion = version;

    public void Dispose()
    {
        Installer.Dispose();
        Web.Dispose();
    }
}
