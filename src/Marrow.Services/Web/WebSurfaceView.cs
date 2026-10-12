using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// One web view as <see cref="WebSurface"/> owns it (DESIGN 2.2, 2.3): a stable object for the pane and the host that
/// outlives the engine view underneath it (an engine view is replaced when a discarded tab is restored or Visus
/// restarts). It applies the baseline policy of each profile before re-raising the engine's requests (DESIGN 5.4, 5.5,
/// 8.1, 8.3, 8.4): agent and sandboxed views never prompt for permissions, sandboxed views never download, open windows
/// or launch protocols, and so on. A request the baseline answered is final; later answers are ignored.
/// </summary>
public sealed class WebSurfaceView : IWebViewSurface
{
    private readonly WebSurface owner;
    private IWebEngineView? engine;
    private CdpBroker? cdp;
    private WebLiveness liveness = WebLiveness.Pending;
    private WebFailureCard? failure;
    private string? pendingUrl;
    private string? lastUrl;
    private int modelSteps;
    private TaskCompletionSource? liveHidden;
    private WebSandbox? sandbox;

    internal WebSurfaceView(WebSurface owner, int sequence, WebSurfaceKind kind, string windowId, WebCreationOptions creation, string? url)
    {
        this.owner = owner;
        Sequence = sequence;
        Kind = kind;
        WindowId = windowId;
        Creation = creation;
        pendingUrl = url;
        lastUrl = url;
    }

    /// <summary>Open order across the process (the first pane gets the "Visus stopped" card, DESIGN 2.7).</summary>
    public int Sequence { get; }

    public WebSurfaceKind Kind { get; }

    /// <summary>The <c>ThreadWindow</c> the view lives in; the live-view cap counts across all of them (DESIGN 2.3).</summary>
    public string WindowId { get; }

    public WebCreationOptions Creation { get; }

    public WebLiveness Liveness => liveness;

    public WebFailureCard? Failure => failure;

    public WebSnapshot? LastSnapshot => owner.Cache.Newest(this);

    /// <summary>The engine view now underneath, or null (discarded, closed, runtime missing).</summary>
    public IWebEngineView? Engine => engine;

    /// <summary>The CDP broker of the current engine view, or null.</summary>
    public CdpBroker? Cdp => cdp;

    /// <summary>The page's address: live, waiting for the first navigation, or remembered across a discard.</summary>
    public string? Url => engine?.Url ?? pendingUrl ?? lastUrl;

    /// <summary>True while a pane shows the view (the tab is selected and the pane is open).</summary>
    public bool IsShown { get; internal set; }

    /// <summary>True while the live view is visible (not hidden behind its snapshot by the host).</summary>
    public bool IsLiveVisible { get; private set; } = true;

    /// <summary>Last time the view was shown or used; the cap suspends the least recent first.</summary>
    public DateTimeOffset LastUsedAt { get; internal set; }

    /// <summary>When the view stopped being shown, or null while shown.</summary>
    public DateTimeOffset? HiddenSince { get; internal set; }

    /// <summary>True while a model step drives the view (never suspended or discarded then, DESIGN 2.3).</summary>
    public bool IsModelStepRunning => modelSteps > 0;

    internal bool IsSuspending { get; set; }
    internal bool IsProbing { get; set; }
    internal DateTimeOffset? LastProbeAt { get; set; }
    internal DateTimeOffset? HungSince { get; set; }
    internal bool StepHangReported { get; set; }

    public event Action? Changed;
    public event Action<bool>? FocusExitRequested;
    public event EventHandler<WebNavigationStarting>? NavigationStarting;
    public event EventHandler<WebNavigationStarting>? FrameNavigationStarting;
    public event EventHandler<WebNavigationCompleted>? NavigationCompleted;
    public event EventHandler<WebNewWindowRequest>? NewWindowRequested;
    public event EventHandler<WebPermissionRequest>? PermissionRequested;
    public event EventHandler<WebDownloadRequest>? DownloadStarting;
    public event EventHandler? TitleChanged;
    public event EventHandler? FaviconChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler<WebMessage>? WebMessageReceived;
    public event EventHandler<WebExternalUriRequest>? ExternalUriRequested;
    public event EventHandler<WebAuthRequest>? AuthenticationRequested;
    public event EventHandler<WebScriptDialogRequest>? ScriptDialogOpening;
    public event EventHandler<WebCertificateErrorRequest>? CertificateErrorDetected;
    public event EventHandler<bool>? FullScreenChanged;

    /// <summary>
    /// DESIGN 6.5 (ED-NEW-web-w13-1): a sandboxed view tried to leave its own origin (a link, a script, a refresh, a form, a
    /// new window). The navigation is already cancelled; a user's click is offered to the link setting by the surface.
    /// </summary>
    public event EventHandler<WebSandboxNavigation>? SandboxNavigationBlocked;

    /// <summary>Sandboxed content: Tabula, inline HTML widgets and MCP Apps (DESIGN 6.5, 7.6).</summary>
    public bool IsSandboxed => Kind is WebSurfaceKind.Artifact or WebSurfaceKind.InlineWidget or WebSurfaceKind.McpApp;

    /// <summary>The sandbox's own origin, or null before <see cref="SetSandbox"/> (then every navigation is blocked).</summary>
    public string? SandboxOrigin => sandbox?.Origin;

    /// <summary>
    /// DESIGN 6.5: binds a sandboxed view to its site: the requests it makes go to the handler, the document scripts run
    /// before the page's own in every document, and it may never navigate away from <see cref="WebSandbox.Origin"/>. The
    /// binding outlives engine views (a discarded view is restored with it). Call before the platform view attaches.
    /// </summary>
    public void SetSandbox(WebSandbox value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsSandboxed) throw new InvalidOperationException("Only artifacts, inline widgets and MCP Apps run in a sandbox.");
        sandbox = value;
        if (engine is not null) ApplySandbox(engine);
    }

    /// <summary>True when a sandboxed view may go to <paramref name="url"/>: its own origin, or a blank or srcdoc frame.</summary>
    public bool SandboxAllows(string url)
    {
        if (url is "about:blank" or "about:srcdoc") return true;
        var address = url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ? url[5..] : url;
        return sandbox is { } own && SandboxSites.OriginOf(address) is { } origin && origin.Equals(own.Origin, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Navigates now, or when the platform view attaches; the address is remembered for a discard.</summary>
    public void Navigate(string url)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        lastUrl = url;
        if (engine is { IsAttached: true } live) live.Navigate(url);
        else pendingUrl = url;
    }

    /// <summary>The pane shows or hides the view (DESIGN 2.3: hidden views run at low memory and may be discarded).</summary>
    public void SetShown(bool shown) => owner.SetShown(this, shown);

    /// <summary>Marks a model step on the view; while any runs, the view is never suspended or discarded.</summary>
    public IDisposable BeginModelStep()
    {
        modelSteps++;
        owner.Touch(this);
        return new StepScope(this);
    }

    /// <summary>
    /// Asks for an automatic reload (live reload of a static preview, the reload at turn end). False once the page
    /// crashed <see cref="WebSurfaceOptions.CrashLimit"/> times within <see cref="WebSurfaceOptions.CrashWindow"/>.
    /// </summary>
    public bool TryAutomaticReload()
    {
        if (!owner.AllowsAutomaticReload(Url)) return false;
        if (engine is not { IsAttached: true } live) return false;
        live.Reload(hard: false);
        return true;
    }

    // IWebViewSurface

    public Task<nint> AcquireEnvironmentAsync(CancellationToken cancellationToken) => owner.Engine.AcquireEnvironmentAsync(Creation, cancellationToken);

    public void AttachNative(nint coreWebView2, nint controller)
    {
        if (engine is null || coreWebView2 == 0 || controller == 0)
        {
            // Discarded or closed meanwhile, or a platform view that is not WebView2: the references are dropped here.
            if (coreWebView2 != 0) owner.Engine.ReleaseNative(coreWebView2);
            if (controller != 0) owner.Engine.ReleaseNative(controller);
            if (engine is not null) owner.OnPlatformViewUnusable(this);
            return;
        }

        engine.Attach(coreWebView2, controller);
    }

    public void DetachNative() => engine?.Detach();

    public Task<WebGestureFrame> CaptureForGestureAsync(CancellationToken cancellationToken) => owner.CaptureForGestureAsync(this, cancellationToken);

    public void NoteIntent() => owner.NoteIntent(this);

    public void SetBounds(double width, double height, double scale) => engine?.SetBounds(width, height, scale);

    public void SetLiveVisible(bool visible)
    {
        IsLiveVisible = visible;
        engine?.SetVisible(visible);
        if (visible || liveHidden is not { } waiting) return;
        liveHidden = null;
        waiting.TrySetResult();
    }

    public Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken) =>
        engine is { IsAttached: true } live ? owner.WaitForFramesAsync(live, frames, timeout, cancellationToken) : Task.FromResult(false);

    public void Activate() => owner.Activate(this);

    public void InvokeFailureAction(WebFailureAction action) => owner.OnFailureAction(this, action);

    // Owner-side state changes.

    /// <summary>Completes when the live view is hidden (the host hides it once its snapshot is on screen).</summary>
    internal Task WhenLiveHiddenAsync()
    {
        if (!IsLiveVisible) return Task.CompletedTask;
        liveHidden ??= new TaskCompletionSource();
        return liveHidden.Task;
    }

    internal void CreateEngine()
    {
        engine = owner.Engine.CreateView(Creation);
        cdp = new CdpBroker(engine);
        Hook(engine);
        if (sandbox is not null) ApplySandbox(engine);
        if (pendingUrl is null && lastUrl is not null) pendingUrl = lastUrl;
        SetLiveness(WebLiveness.Pending);
    }

    internal void ReleaseEngine()
    {
        if (engine is null) return;
        if (engine.Url is { } url) lastUrl = url;
        Unhook(engine);
        cdp?.Dispose();
        cdp = null;
        engine.Dispose();
        engine = null;
    }

    internal void SetLiveness(WebLiveness value)
    {
        if (liveness == value) return;
        liveness = value;
        Changed?.Invoke();
    }

    internal void SetFailure(WebFailureCard? card)
    {
        if (Equals(failure, card)) return;
        failure = card;
        Changed?.Invoke();
    }

    internal void RaiseChanged() => Changed?.Invoke();

    private void Hook(IWebEngineView view)
    {
        view.Attached += OnAttached;
        view.ProcessFailed += OnProcessFailed;
        view.AcceleratorKeyPressed += OnAccelerator;
        view.MoveFocusRequested += OnMoveFocus;
        view.NavigationStarting += OnNavigationStarting;
        view.FrameNavigationStarting += OnFrameNavigationStarting;
        view.NavigationCompleted += OnNavigationCompleted;
        view.NewWindowRequested += OnNewWindow;
        view.PermissionRequested += OnPermission;
        view.DownloadStarting += OnDownload;
        view.TitleChanged += OnTitle;
        view.FaviconChanged += OnFavicon;
        view.HistoryChanged += OnHistory;
        view.WebMessageReceived += OnMessage;
        view.ExternalUriRequested += OnExternalUri;
        view.AuthenticationRequested += OnAuth;
        view.ScriptDialogOpening += OnScriptDialog;
        view.CertificateErrorDetected += OnCertificateError;
        view.FullScreenChanged += OnFullScreen;
    }

    private void Unhook(IWebEngineView view)
    {
        view.Attached -= OnAttached;
        view.ProcessFailed -= OnProcessFailed;
        view.AcceleratorKeyPressed -= OnAccelerator;
        view.MoveFocusRequested -= OnMoveFocus;
        view.NavigationStarting -= OnNavigationStarting;
        view.FrameNavigationStarting -= OnFrameNavigationStarting;
        view.NavigationCompleted -= OnNavigationCompleted;
        view.NewWindowRequested -= OnNewWindow;
        view.PermissionRequested -= OnPermission;
        view.DownloadStarting -= OnDownload;
        view.TitleChanged -= OnTitle;
        view.FaviconChanged -= OnFavicon;
        view.HistoryChanged -= OnHistory;
        view.WebMessageReceived -= OnMessage;
        view.ExternalUriRequested -= OnExternalUri;
        view.AuthenticationRequested -= OnAuth;
        view.ScriptDialogOpening -= OnScriptDialog;
        view.CertificateErrorDetected -= OnCertificateError;
        view.FullScreenChanged -= OnFullScreen;
    }

    private void OnAttached(object? sender, EventArgs e)
    {
        if (engine is null) return;
        SetLiveness(WebLiveness.Live);
        if (pendingUrl is { } url)
        {
            pendingUrl = null;
            engine.Navigate(url);
        }

        owner.OnViewLive(this);
    }

    private void OnProcessFailed(object? sender, WebProcessFailed e) => owner.OnProcessFailed(this, e);

    private void OnAccelerator(object? sender, WebAcceleratorKey e) => owner.OnAccelerator(this, e);

    private void OnMoveFocus(object? sender, WebMoveFocusRequest e)
    {
        e.Handled = true;
        FocusExitRequested?.Invoke(e.Forward);
    }

    private void ApplySandbox(IWebEngineView view)
    {
        view.SetResourceHandler(sandbox!.Resources);
        foreach (var script in sandbox.DocumentScripts) view.AddDocumentCreatedScript(script);
    }

    private void OnNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (BlockSandboxNavigation(e)) return;
        NavigationStarting?.Invoke(this, e);
    }

    private void OnFrameNavigationStarting(object? sender, WebNavigationStarting e)
    {
        if (BlockSandboxNavigation(e)) return;
        FrameNavigationStarting?.Invoke(this, e);
    }

    /// <summary>DESIGN 6.5, 8.1: a sandboxed view never leaves its own origin, top level or frame, whoever starts it.</summary>
    private bool BlockSandboxNavigation(WebNavigationStarting e)
    {
        if (!IsSandboxed || SandboxAllows(e.Url)) return false;
        if (!e.IsMainFrame && sandbox?.AllowsFrame?.Invoke(e.Url) == true) return false; // W19: a frame domain the user allowed
        e.Cancel = true;
        SandboxNavigationBlocked?.Invoke(this, new WebSandboxNavigation(e.Url, e.IsUserInitiated, e.IsMainFrame, IsNewWindow: false));
        return true;
    }

    private void OnNavigationCompleted(object? sender, WebNavigationCompleted e)
    {
        lastUrl = e.Url;
        owner.OnNavigated(this);
        NavigationCompleted?.Invoke(this, e);
    }

    private void OnNewWindow(object? sender, WebNewWindowRequest e)
    {
        // DESIGN 8.1: sandboxed views never open windows, no exceptions; a user's click is still offered as a link.
        if (IsSandboxed)
        {
            e.Respond(WebNewWindowDecision.Block);
            SandboxNavigationBlocked?.Invoke(this, new WebSandboxNavigation(e.Url, e.IsUserInitiated, IsMainFrame: true, IsNewWindow: true));
        }

        NewWindowRequested?.Invoke(this, e);
    }

    private void OnPermission(object? sender, WebPermissionRequest e)
    {
        // DESIGN 5.5: agent profile and artifacts are always denied, no prompt; notifications are blocked in preview; a
        // kind outside the design's list is denied with no prompt everywhere (ED-NEW-web-w7-1).
        // W19: an MCP App's declared kinds go to its host, which asks the user (DESIGN 7.6); every other sandboxed request is denied.
        if (Creation.Kind is WebProfileKind.Agent or WebProfileKind.Artifacts && !(Kind == WebSurfaceKind.McpApp && sandbox?.Prompts?.Invoke(e.Kind) == true)) e.Respond(WebPermissionDecision.Deny);
        else if (e.Kind is WebPermissionKind.Unknown or WebPermissionKind.PersistentStorage) e.Respond(WebPermissionDecision.Deny);
        else if (Creation.Kind == WebProfileKind.Preview && e.Kind == WebPermissionKind.Notifications) e.Respond(WebPermissionDecision.Deny);
        PermissionRequested?.Invoke(this, e);
    }

    private void OnDownload(object? sender, WebDownloadRequest e)
    {
        // DESIGN 5.4: artifacts, inline widgets and MCP Apps cannot download (export goes through nth's own control).
        if (IsSandboxed) e.Respond(WebDownloadDecision.Cancel);
        DownloadStarting?.Invoke(this, e);
    }

    private void OnTitle(object? sender, EventArgs e) => TitleChanged?.Invoke(this, e);

    private void OnFavicon(object? sender, EventArgs e) => FaviconChanged?.Invoke(this, e);

    private void OnHistory(object? sender, EventArgs e) => HistoryChanged?.Invoke(this, e);

    private void OnMessage(object? sender, WebMessage e) => WebMessageReceived?.Invoke(this, e);

    private void OnExternalUri(object? sender, WebExternalUriRequest e)
    {
        // DESIGN 8.3: sandboxed views and agent tabs always deny with no prompt; Visus tabs deny silently unless the
        // launch comes from a user gesture.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab || !e.IsUserInitiated) e.Respond(false);
        ExternalUriRequested?.Invoke(this, e);
    }

    private void OnAuth(object? sender, WebAuthRequest e)
    {
        // DESIGN 8.4: HTTP auth is denied for agent tabs and sandboxed content.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab) e.Respond(null);
        AuthenticationRequested?.Invoke(this, e);
    }

    private void OnScriptDialog(object? sender, WebScriptDialogRequest e)
    {
        // DESIGN 8.4: agent tabs auto-dismiss (logged for the model); sandboxed content has none.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab) e.Respond(WebScriptDialogDecision.Dismiss);
        ScriptDialogOpening?.Invoke(this, e);
    }

    private void OnCertificateError(object? sender, WebCertificateErrorRequest e)
    {
        // DESIGN 8.1: "Continue anyway" is never for the model, and never for sandboxed content.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab) e.Respond(false);
        CertificateErrorDetected?.Invoke(this, e);
    }

    private void OnFullScreen(object? sender, bool e) => FullScreenChanged?.Invoke(this, e);

    private sealed class StepScope(WebSurfaceView view) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            view.modelSteps--;
            if (view.modelSteps == 0) view.StepHangReported = false;
            view.owner.Touch(view);
            view.owner.EnforceCapSoon();
        }
    }
}
