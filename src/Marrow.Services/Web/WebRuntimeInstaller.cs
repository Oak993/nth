using Marrow.Core.Port;
using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>Where the runtime flow is (W23). The pane shows the matching card; nothing else reads this.</summary>
public enum WebRuntimeInstallPhase
{
    Idle,
    Downloading,
    Verifying,
    Installing,
    Succeeded,
    Failed,
}

/// <summary>How a run of the runtime flow ended.</summary>
/// <param name="Failure"><see cref="WebRuntimeInstallFailure.None"/> when a usable runtime is there now.</param>
/// <param name="Version">The runtime version Windows reports afterwards, or null when there is none.</param>
/// <param name="ExitCode">The bootstrapper's exit code, or null when it never ran.</param>
public sealed record WebRuntimeInstallResult(WebRuntimeInstallFailure Failure, string? Version, int? ExitCode)
{
    public bool Succeeded => Failure == WebRuntimeInstallFailure.None;
}

/// <summary>A step of the runtime flow failed in a way the card can name.</summary>
public sealed class WebRuntimeInstallException(WebRuntimeInstallFailure failure, string message, Exception? inner = null) : Exception(message, inner)
{
    public WebRuntimeInstallFailure Failure { get; } = failure;
}

/// <summary>A downloaded bootstrapper. Disposing it removes the file and its folder.</summary>
public interface IBootstrapperFile : IDisposable
{
    string Path { get; }
}

/// <summary>Gets Microsoft's bootstrapper onto disk (<see cref="WebRuntimeBootstrapper.DownloadUrl"/>).</summary>
public interface IBootstrapperSource
{
    /// <exception cref="WebRuntimeInstallException">Offline or DownloadFailed.</exception>
    Task<IBootstrapperFile> DownloadAsync(CancellationToken cancellation);
}

/// <summary>What the Authenticode check found: a valid signature, and the subject of its signer.</summary>
public readonly record struct AuthenticodeResult(bool Valid, string? Subject);

/// <summary>The signature check on the downloaded file (WinVerifyTrust plus the signer's name). Async because building the certificate chain can take a while and must not stop the UI thread.</summary>
public interface IAuthenticodeCheck
{
    Task<AuthenticodeResult> CheckAsync(string path, CancellationToken cancellation);
}

/// <summary>Starts the bootstrapper and waits for it.</summary>
public interface IBootstrapperRunner
{
    /// <returns>The exit code.</returns>
    /// <exception cref="WebRuntimeInstallException">CouldNotStart or TimedOut.</exception>
    Task<int> RunAsync(string path, string arguments, TimeSpan timeout, CancellationToken cancellation);
}

/// <summary>
/// DESIGN 2.1 (W23): the WebView2 runtime flow. When the runtime is missing (or older than the minimum) the pane's card offers
/// "Install" (or "Update"); only that press starts anything. The flow downloads Microsoft's Evergreen bootstrapper
/// (<see cref="WebRuntimeBootstrapper.DownloadUrl"/>) over HTTPS, refuses it unless Windows verifies its Authenticode
/// signature and the signer is Microsoft Corporation, runs it with <c>/silent /install</c> without elevation (a per-user
/// install, no UAC prompt), looks for the runtime again, and puts the pane's page back, or a card that says why not and
/// offers another go and Microsoft's own page. The model cannot start it: it hangs off the card's button, which only the
/// user's pointer or keyboard presses (<see cref="WebSurface.RuntimeActionRequested"/>).
/// ENGINEERING DECISIONS (ED-NEW-web-w23-2): the runtime found afterwards decides success, never the exit code (the bootstrapper
/// exits non-zero when the runtime is already there); an installer that exits 0 is given up to
/// <see cref="ProbeWindow"/> for Windows to report the runtime; a press first looks again, so a runtime the user installed from
/// Microsoft's page since is used at once and nothing is downloaded; one flow runs at a time; the installer is never
/// killed once started (a half-run installer would leave the machine worse), nth only stops waiting.
/// It runs on the UI thread, like <see cref="WebSurface"/>: continuations return there.
/// </summary>
public sealed class WebRuntimeInstaller : IDisposable
{
    /// <summary>How long after a clean exit nth keeps looking for the runtime before it calls the install failed.</summary>
    public static readonly TimeSpan ProbeWindow = TimeSpan.FromSeconds(30);

    /// <summary>How often it looks inside <see cref="ProbeWindow"/>.</summary>
    public static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    private readonly WebSurface surface;
    private readonly IBootstrapperSource source;
    private readonly IAuthenticodeCheck signature;
    private readonly IBootstrapperRunner runner;
    private readonly Action<string> openPage;
    private readonly Func<TimeSpan, CancellationToken, Task> delay;
    private readonly Logger log = new(Logger.Subsystem, "web");
    private readonly CancellationTokenSource lifetime = new();
    private Task<WebRuntimeInstallResult>? running;
    private bool attached;
    private bool disposed;

    /// <param name="surface">The surface whose panes wait on the runtime.</param>
    /// <param name="source">Microsoft's bootstrapper, downloaded.</param>
    /// <param name="signature">The signature check.</param>
    /// <param name="runner">Runs the bootstrapper.</param>
    /// <param name="openPage">Opens an address in the user's default browser (Microsoft's runtime page).</param>
    /// <param name="delay">Waits between looks for the runtime; <see cref="Task.Delay(TimeSpan, CancellationToken)"/> by default.</param>
    public WebRuntimeInstaller(WebSurface surface, IBootstrapperSource source, IAuthenticodeCheck signature, IBootstrapperRunner runner,
        Action<string> openPage, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        this.surface = surface;
        this.source = source;
        this.signature = signature;
        this.runner = runner;
        this.openPage = openPage;
        this.delay = delay ?? Task.Delay;
    }

    public WebRuntimeInstallPhase Phase { get; private set; }

    /// <summary>The phase changed; raised on the thread the flow runs on.</summary>
    public event Action<WebRuntimeInstallPhase>? PhaseChanged;

    /// <summary>The result of the last finished flow, or null before the first.</summary>
    public WebRuntimeInstallResult? LastResult { get; private set; }

    /// <summary>Listens to the surface's runtime card buttons. Idempotent. Nothing runs until one is pressed.</summary>
    public void Attach()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (attached) return;
        attached = true;
        surface.RuntimeActionRequested += OnAction;
    }

    private void OnAction(WebFailureAction action)
    {
        switch (action)
        {
            case WebFailureAction.InstallRuntime:
                _ = RunAsync(updating: false);
                break;
            case WebFailureAction.UpdateRuntime:
                _ = RunAsync(updating: true);
                break;
            case WebFailureAction.OpenRuntimePage:
                openPage(WebRuntimeBootstrapper.RuntimePageUrl);
                break;
        }
    }

    /// <summary>
    /// Runs the flow for the user's press on Install (<paramref name="updating"/> false) or Update. A flow already running is
    /// returned as it is: one at a time.
    /// </summary>
    public Task<WebRuntimeInstallResult> RunAsync(bool updating)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (running is { IsCompleted: false } active) return active;
        return running = FlowAsync(updating);
    }

    private async Task<WebRuntimeInstallResult> FlowAsync(bool updating)
    {
        var token = lifetime.Token;
        try
        {
            // A runtime the user installed from Microsoft's page meanwhile: use it, download nothing.
            if (surface.RefreshRuntime().State == WebRuntimeState.Ready) return Finish(new(WebRuntimeInstallFailure.None, surface.Runtime.Version, null));

            surface.ShowRuntimeFlowCard(WebFailureCard.RuntimeInstalling(updating));
            log.Info($"web: runtime flow started (update={updating})");
            SetPhase(WebRuntimeInstallPhase.Downloading);
            IBootstrapperFile file;
            try
            {
                file = await source.DownloadAsync(token);
            }
            catch (WebRuntimeInstallException ex)
            {
                return Fail(updating, ex.Failure, null);
            }

            using (file)
            {
                SetPhase(WebRuntimeInstallPhase.Verifying);
                var check = await signature.CheckAsync(file.Path, token);
                if (!check.Valid || !WebRuntimeBootstrapper.IsAcceptableSigner(check.Subject))
                {
                    log.Error($"web: runtime flow refused the download (signature valid={check.Valid})");
                    return Fail(updating, WebRuntimeInstallFailure.NotSigned, null);
                }

                SetPhase(WebRuntimeInstallPhase.Installing);
                int code;
                try
                {
                    code = await runner.RunAsync(file.Path, WebRuntimeBootstrapper.Arguments, WebRuntimeBootstrapper.InstallTimeout, token);
                }
                catch (WebRuntimeInstallException ex)
                {
                    return Fail(updating, ex.Failure, null);
                }

                log.Info($"web: runtime installer exited with {code}");
                var status = await LookForRuntimeAsync(code == 0, token);
                if (status.State == WebRuntimeState.Ready) return Finish(new(WebRuntimeInstallFailure.None, status.Version, code));
                var failure = code != 0 ? WebRuntimeInstallFailure.InstallerFailed
                    : status.State == WebRuntimeState.TooOld ? WebRuntimeInstallFailure.StillTooOld
                    : WebRuntimeInstallFailure.StillMissing;
                return Fail(updating, failure, status.Version, code);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // nth is shutting down: nothing to show.
            SetPhase(WebRuntimeInstallPhase.Idle);
            return LastResult = new(WebRuntimeInstallFailure.DownloadFailed, null, null);
        }
    }

    private async Task<WebRuntimeStatus> LookForRuntimeAsync(bool cleanExit, CancellationToken token)
    {
        var status = surface.RefreshRuntime();
        if (!cleanExit) return status;
        for (var waited = TimeSpan.Zero; status.State != WebRuntimeState.Ready && waited < ProbeWindow; waited += ProbeInterval)
        {
            await delay(ProbeInterval, token);
            status = surface.RefreshRuntime();
        }

        return status;
    }

    private WebRuntimeInstallResult Fail(bool updating, WebRuntimeInstallFailure failure, string? version, int? code = null)
    {
        surface.ShowRuntimeFlowCard(WebFailureCard.RuntimeInstallFailed(updating, WebRuntimeBootstrapper.ReasonFor(failure, version)));
        SetPhase(WebRuntimeInstallPhase.Failed);
        log.Error($"web: runtime flow ended: {failure}");
        return LastResult = new(failure, version, code);
    }

    private WebRuntimeInstallResult Finish(WebRuntimeInstallResult result)
    {
        surface.RefreshRuntime();
        SetPhase(WebRuntimeInstallPhase.Succeeded);
        log.Info($"web: runtime flow ended with a usable runtime ({result.Version})");
        return LastResult = result;
    }

    private void SetPhase(WebRuntimeInstallPhase phase)
    {
        if (Phase == phase) return;
        Phase = phase;
        PhaseChanged?.Invoke(phase);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (attached) surface.RuntimeActionRequested -= OnAction;
        lifetime.Cancel();
    }
}
