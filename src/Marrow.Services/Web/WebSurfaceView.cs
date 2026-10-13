using Marrow.Core.Models;
using Marrow.Core.Web;
using System.Text.Json;

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
    private int bridgeCalls;
    private int openArtifactPanes;
    private int userHolds;
    private int scriptHolds;
    private readonly List<(string Json, string? Event)> pendingMessages = [];
    private string? navigationTarget;
    private (string Token, IWebEngineView Engine, TaskCompletionSource<bool> Ready)? discardNotice;
    internal bool IsDocumentReady => navigationTarget is null && engine is { IsAttached: true };
    /// <summary>The address the main frame is loading now, or null once it finished.</summary>
    internal string? NavigationTarget => navigationTarget;
    private TaskCompletionSource? liveHidden;
    private WebSandbox? sandbox;
    private readonly List<IDisposable> guardSubscriptions = [];
    private readonly List<WebAgentNote> agentNotes = [];
    private WebPointerLockRequest? pointerLockAsk;

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

    /// <summary>
    /// W8b (DESIGN 5.4, 5.5, 8.4): a question of this page waits for the user (a download, a permission, a sign-in, a page
    /// dialog, a file for the model's page). The engine holds the page's side of it, so the view is neither suspended nor
    /// discarded until every hold is released.
    /// </summary>
    public bool IsHeldForUser => userHolds > 0;

    /// <summary>
    /// W8b: a page dialog (alert, confirm, prompt, leave page) waits for the user. The page's script is stopped inside it, so the
    /// hang probe (2.7) does not run: a page waiting on its reader is not "not responding".
    /// </summary>
    public bool IsScriptHeldForUser => scriptHolds > 0;

    /// <summary>
    /// W8b: holds the view's engine for a question the user has not answered yet; dispose when it is answered.
    /// <paramref name="blocksScript"/> is true for a page dialog, which stops the page's script until it is answered.
    /// </summary>
    public IDisposable HoldForUser(bool blocksScript = false)
    {
        userHolds++;
        if (blocksScript) scriptHolds++;
        return new UserHold(this, blocksScript);
    }

    internal bool IsSuspending { get; set; }
    internal bool IsDiscarding { get; set; }
    internal bool NeedsLiveReload { get; set; }
    internal bool IsBridgeCallRunning => bridgeCalls > 0;
    internal bool HasOpenArtifactPane => openArtifactPanes > 0;
    internal DateTimeOffset? DiscardRetryAt { get; set; }
    internal long ActivityVersion { get; set; }
    internal bool IsProbing { get; set; }
    internal CancellationTokenSource? ProbeCancellation { get; set; }
    internal DateTimeOffset? LastProbeAt { get; set; }
    internal DateTimeOffset? HungSince { get; set; }
    internal bool StepHangReported { get; set; }

    public event Action? Changed;
    internal event Action? BecameLive;

    /// <summary>
    /// W8b: the page's engine went (a crash, a runtime that went missing, the view closing): every question it was waiting on
    /// is void, so its asker withdraws them. Raised before the engine is disposed.
    /// </summary>
    public event Action? EngineReleased;
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

    /// <summary>W8b (DESIGN 5.5): a browse tab's page notification; nth shows it as its own toast (other profiles never raise it).</summary>
    public event EventHandler<WebNotification>? NotificationReceived;

    /// <summary>W8b (8.4): a page asked to lock the pointer (browse, preview and private tabs only); unanswered asks are refused.</summary>
    public event EventHandler<WebPointerLockRequest>? PointerLockRequested;

    /// <summary>W8b (8.4): the pointer was locked (true) or released (false), as the page's gate reported it.</summary>
    public event Action<bool>? PointerLockChanged;

    /// <summary>W8b (8.4): an agent tab's page opened a file chooser; nth intercepted it (the guarded card answers, 5.10).</summary>
    public event EventHandler<WebFileChooser>? FileChooserIntercepted;

    /// <summary>W8b (8.4): something the model is told about its tab (a dismissed dialog, a sign-in it cannot do).</summary>
    public event Action<WebAgentNote>? AgentNoted;

    /// <summary>W8b: the pointer is locked in the page now (the gate's last report).</summary>
    public bool IsPointerLocked { get; private set; }

    /// <summary>W8b: the latest notes for the model, oldest first (at most <see cref="AgentNoteLimit"/>).</summary>
    public IReadOnlyList<WebAgentNote> AgentNotes => agentNotes;

    /// <summary>How many notes an agent tab keeps.</summary>
    public const int AgentNoteLimit = 50;

    /// <summary>
    /// DESIGN 6.5 (ED-NEW-web-w13-1): a sandboxed view tried to leave its own origin (a link, a script, a refresh, a form, a
    /// new window). The navigation is already cancelled; a user's click is offered to the link setting by the surface.
    /// </summary>
    public event EventHandler<WebSandboxNavigation>? SandboxNavigationBlocked;

    /// <summary>
    /// W8a (DESIGN 3.7): the chords the view's owner takes from inside the page before the page sees them, beside the reserved
    /// ones (Visus's browser keys: new tab, close tab, the address field, reload, back and forward). Null claims none.
    /// </summary>
    public Func<KeyChord, bool>? ClaimsChord { get; set; }

    /// <summary>A chord <see cref="ClaimsChord"/> took from inside the page; raised after the key event, on the UI thread.</summary>
    public event Action<KeyChord>? ClaimedChordPressed;

    internal void RaiseClaimedChord(KeyChord chord) => ClaimedChordPressed?.Invoke(chord);

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
        ActivateForContent();
        if (engine is { IsAttached: true } live)
        {
            CancelDiscardNotice();
            navigationTarget = url;
            live.Navigate(url);
        }
        else pendingUrl = url;
    }

    /// <summary>W8a: the page's favicon (PNG), read by the browser from the tab's own profile; null when there is none.</summary>
    public Task<byte[]?> GetFaviconAsync(CancellationToken cancellationToken = default) =>
        engine is { IsAttached: true } live ? live.GetFaviconAsync(cancellationToken) : Task.FromResult<byte[]?>(null);

    /// <summary>Content for an on-screen view first restores it; pending engines receive it on attach.</summary>
    public void PostMessage(string json)
    {
        var sessionReply = false;
        string? contentEvent = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                sessionReply = document.RootElement.TryGetProperty("s", out _);
                if (document.RootElement.TryGetProperty("event", out var ev) && ev.ValueKind == JsonValueKind.String) contentEvent = ev.GetString();
            }
        }
        catch (JsonException) { }
        var oldEngine = engine;
        ActivateForContent();
        // A reply belongs to the requesting document, never to a replacement engine. Its bridge is already listening.
        if (sessionReply)
        {
            if (engine == oldEngine && engine is { IsAttached: true } replyEngine) replyEngine.PostMessage(json);
            return;
        }
        if (liveness is WebLiveness.Suspended or WebLiveness.Discarded or WebLiveness.Closed) return;
        if (engine is { IsAttached: true } live && navigationTarget is null) live.PostMessage(json);
        else
        {
            // Streaming previews and themes need only their newest value while the document is loading.
            if (contentEvent is "morph" or "theme")
                pendingMessages.RemoveAll(m => m.Event == contentEvent);
            pendingMessages.Add((json, contentEvent));
        }
    }

    private void ActivateForContent()
    {
        if (IsShown && (NeedsLiveReload || IsSuspending || liveness is WebLiveness.Suspended or WebLiveness.Discarded)) owner.Activate(this);
    }

    internal void CancelPendingMorph() => pendingMessages.RemoveAll(m => m.Event == "morph");

    /// <summary>The pane shows or hides the view (DESIGN 2.3: hidden views run at low memory and may be discarded).</summary>
    public void SetShown(bool shown) => owner.SetShown(this, shown);

    /// <summary>Marks a model step on the view; while any runs, the view is never suspended or discarded.</summary>
    public IDisposable BeginModelStep()
    {
        modelSteps++;
        owner.Touch(this);
        return new StepScope(this);
    }

    internal IDisposable BeginBridgeCall()
    {
        bridgeCalls++;
        owner.Touch(this);
        return new BridgeScope(this);
    }

    internal void HoldArtifactPane(bool hold) => openArtifactPanes += hold ? 1 : -1;

    private sealed class UserHold(WebSurfaceView view, bool blocksScript) : IDisposable
    {
        private bool done;

        public void Dispose()
        {
            if (done) return;
            done = true;
            view.userHolds--;
            if (blocksScript) view.scriptHolds--;
            view.owner.Touch(view);
        }
    }

    private sealed class BridgeScope(WebSurfaceView view) : IDisposable
    {
        private bool done;
        public void Dispose()
        {
            if (done) return;
            done = true;
            view.bridgeCalls--;
            view.owner.Touch(view);
            view.owner.EnforceCapSoon();
        }
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
        ApplyGuard(engine, cdp);
        if (sandbox is not null) ApplySandbox(engine);
        if (pendingUrl is null && lastUrl is not null) pendingUrl = lastUrl;
        SetLiveness(WebLiveness.Pending);
    }

    internal void ReleaseEngine()
    {
        owner.CancelProbe(this);
        if (engine is null) return;
        CancelDiscardNotice();
        navigationTarget = null;
        if (engine.Url is { } url) lastUrl = url;
        Unhook(engine);
        EngineReleased?.Invoke();
        foreach (var subscription in guardSubscriptions) subscription.Dispose();
        guardSubscriptions.Clear();
        pointerLockAsk?.Answer(false);
        pointerLockAsk = null;
        if (IsPointerLocked)
        {
            IsPointerLocked = false;
            PointerLockChanged?.Invoke(false);
        }
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
    internal void RaiseBecameLive() => BecameLive?.Invoke();

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
        view.NotificationReceived += OnNotification;
        view.ScreenCaptureStarting += OnScreenCapture;
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
        view.NotificationReceived -= OnNotification;
        view.ScreenCaptureStarting -= OnScreenCapture;
    }

    private void OnAttached(object? sender, EventArgs e)
    {
        if (engine is null) return;
        SendGuardCalls();
        SetLiveness(WebLiveness.Live);
        if (pendingUrl is { } url)
        {
            pendingUrl = null;
            navigationTarget = url;
            engine.Navigate(url);
        }

        owner.OnViewLive(this);
        if (navigationTarget is null) FlushMessages();
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
        if (e.IsMainFrame)
        {
            CancelDiscardNotice();
            navigationTarget = e.Url;
        }
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
        if (e.IsSuccess && (navigationTarget is null || navigationTarget == e.Url))
        {
            navigationTarget = null;
            FlushMessages();
        }
    }

    private void FlushMessages()
    {
        if (engine is not { IsAttached: true } live || liveness != WebLiveness.Live) return;
        foreach (var message in pendingMessages) live.PostMessage(message.Json);
        pendingMessages.Clear();
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
        bool appAsks = Kind == WebSurfaceKind.McpApp && sandbox?.Prompts?.Invoke(e.Kind) == true;
        if ((Creation.Kind is WebProfileKind.Agent or WebProfileKind.Artifacts) && !appAsks)
        {
            e.Respond(WebPermissionDecision.Deny);
            if (Kind == WebSurfaceKind.AgentTab) Note(WebAgentNoteKind.FeatureDenied, $"The page asked for a permission ({e.Kind}); the model's tab never gets one.");
        }
        else if (e.Kind is WebPermissionKind.Unknown or WebPermissionKind.PersistentStorage) e.Respond(WebPermissionDecision.Deny);
        else if (Creation.Kind == WebProfileKind.Preview && e.Kind == WebPermissionKind.Notifications) e.Respond(WebPermissionDecision.Deny);
        PermissionRequested?.Invoke(this, e);
    }

    private void OnDownload(object? sender, WebDownloadRequest e)
    {
        // DESIGN 5.4: artifacts, inline widgets and MCP Apps cannot download (export goes through nth's own control).
        if (IsSandboxed) e.Respond(WebDownloadDecision.Cancel);
        // DESIGN 5.4: a model-started download waits for the user in the same prompt; the model only learns it waits.
        else if (Kind == WebSurfaceKind.AgentTab) Note(WebAgentNoteKind.DownloadWaiting, $"A download is waiting for the user to save or cancel it: {e.SuggestedFileName}");
        DownloadStarting?.Invoke(this, e);
    }

    private void OnTitle(object? sender, EventArgs e) => TitleChanged?.Invoke(this, e);

    private void OnFavicon(object? sender, EventArgs e) => FaviconChanged?.Invoke(this, e);

    private void OnHistory(object? sender, EventArgs e) => HistoryChanged?.Invoke(this, e);

    private void OnMessage(object? sender, WebMessage e)
    {
        if (discardNotice is { } notice && sender == notice.Engine && engine == notice.Engine
            && string.Equals(e.SourceOrigin?.TrimEnd('/'), SandboxOrigin, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var document = JsonDocument.Parse(e.Json);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("nth", out var nth) && nth.ValueKind == JsonValueKind.Number && nth.TryGetInt32(out var n) && n == 1
                    && root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String && type.GetString() == "discard.ready"
                    && root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String && token.GetString() == notice.Token)
                    notice.Ready.TrySetResult(root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True);
            }
            catch (JsonException) { }
        }
        WebMessageReceived?.Invoke(this, e);
    }

    internal void CancelDiscardNotice() => discardNotice?.Ready.TrySetResult(false);

    internal async Task<bool> NotifyDiscardAsync(TimeProvider time)
    {
        if (engine is not { IsAttached: true } live) return false;
        using var stop = new CancellationTokenSource();
        var timeout = Task.Delay(TimeSpan.FromSeconds(1), time, stop.Token);
        try
        {
            if (!IsSandboxed)
            {
                live.PostMessage("{\"nth\":1,\"event\":\"willDiscard\"}");
                // Ordinary tabs have no nth bridge; wait for the page-message dispatch round trip.
                var dispatched = live.ExecuteScriptAsync("void 0", CancellationToken.None);
                if (await Task.WhenAny(dispatched, timeout) != dispatched) return false;
                await dispatched;
                return true;
            }
            var token = Guid.NewGuid().ToString("N");
            var ready = new TaskCompletionSource<bool>();
            discardNotice = (token, live, ready);
            live.PostMessage(JsonSerializer.Serialize(new { nth = 1, @event = "willDiscard", token }));
            return await Task.WhenAny(ready.Task, timeout) == ready.Task && await ready.Task;
        }
        finally
        {
            if (discardNotice is { } notice && engine == live
                && !(notice.Ready.Task.IsCompletedSuccessfully && notice.Ready.Task.Result))
                live.PostMessage(JsonSerializer.Serialize(new { nth = 1, @event = "discard.cancel", token = notice.Token }));
            discardNotice = null;
            stop.Cancel();
        }
    }

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
        if (Kind == WebSurfaceKind.AgentTab) Note(WebAgentNoteKind.SignInNeeded, $"page needs sign-in ({WebPolicy.SourceName(e.Uri)}); the model cannot sign in.");
        AuthenticationRequested?.Invoke(this, e);
    }

    private void OnScriptDialog(object? sender, WebScriptDialogRequest e)
    {
        // DESIGN 8.4: agent tabs auto-dismiss (logged for the model); sandboxed content has none.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab) e.Respond(WebScriptDialogDecision.Dismiss);
        if (Kind == WebSurfaceKind.AgentTab) Note(WebAgentNoteKind.DialogDismissed, $"The page showed a {DialogWord(e.Kind)} and nth dismissed it: {Clip(e.Message)}");
        ScriptDialogOpening?.Invoke(this, e);
    }

    private void OnCertificateError(object? sender, WebCertificateErrorRequest e)
    {
        // DESIGN 8.1: "Continue anyway" is never for the model, and never for sandboxed content.
        if (IsSandboxed || Kind == WebSurfaceKind.AgentTab) e.Respond(false);
        CertificateErrorDetected?.Invoke(this, e);
    }

    private void OnFullScreen(object? sender, bool e)
    {
        // DESIGN 8.4: fullscreen is denied in agent tabs and sandboxed content: nth ends it and never expands anything.
        if (Kind == WebSurfaceKind.AgentTab || IsSandboxed)
        {
            if (!e || engine is not { IsAttached: true } live) return;
            _ = RunQuietly(live.ExecuteScriptAsync(WebFeatureGuard.ExitFullscreenScript, CancellationToken.None));
            if (Kind == WebSurfaceKind.AgentTab) Note(WebAgentNoteKind.FeatureDenied, "The page asked for fullscreen; the model's tab never gets it.");
            return;
        }
        FullScreenChanged?.Invoke(this, e);
    }

    /// <summary>W8b (8.4): ends the page's fullscreen (Esc, Ctrl+Shift+\, the pane restored or closed).</summary>
    public void ExitFullScreen()
    {
        if (engine is { IsAttached: true } live) _ = RunQuietly(live.ExecuteScriptAsync(WebFeatureGuard.ExitFullscreenScript, CancellationToken.None));
    }

    private void OnNotification(object? sender, WebNotification e)
    {
        // DESIGN 5.5: notifications are blocked in preview and agent profiles (and private tabs keep nothing); in browse they map
        // to nth's own toast.
        if (Creation.Kind != WebProfileKind.Browse) return;
        NotificationReceived?.Invoke(this, e);
    }

    // W8b (ED-NEW-web-w8b-3): the browser's own picker shows in Visus page tabs (the user picks what to share in it); agent tabs
    // and sandboxed content never capture the screen.
    private void OnScreenCapture(object? sender, WebScreenCaptureRequest e) =>
        e.Respond(Kind is WebSurfaceKind.VisusTab or WebSurfaceKind.VisusPrivateTab or WebSurfaceKind.PreviewTab);

    /// <summary>
    /// W8b: a key the page got (not reserved, not claimed). Esc releases a locked pointer (8.4 "Esc always releases"), whatever the
    /// page does with it.
    /// </summary>
    internal void OnPageKeyDown(KeyChord chord)
    {
        if (chord == EscapeChord) ReleasePointerLock();
    }

    /// <summary>W8b (8.4): releases a locked pointer (Esc, whoever took the key: the page, or a prompt that claimed it).</summary>
    public void ReleasePointerLock()
    {
        if (!IsPointerLocked || engine is not { IsAttached: true } || cdp is not { } broker) return;
        _ = RunQuietly(broker.CallAsync("Runtime.evaluate", new System.Text.Json.Nodes.JsonObject
        {
            ["expression"] = WebFeatureGuard.ExitPointerLockExpression,
            ["userGesture"] = true,
        }));
    }

    /// <summary>
    /// W8b (8.4, 5.10): the files the user chose in the guarded file upload card go to the agent tab's file input
    /// (<c>DOM.setFileInputFiles</c> on the input the chooser named). Only nth's card calls this, never the model; the paths are
    /// not logged or noted.
    /// </summary>
    public async Task<bool> GiveFilesAsync(WebFileChooser chooser, IReadOnlyList<string> paths)
    {
        if (Kind != WebSurfaceKind.AgentTab || chooser.BackendNodeId is not { } node || paths.Count == 0) return false;
        if (engine is not { IsAttached: true } || cdp is not { } broker) return false;
        var files = new System.Text.Json.Nodes.JsonArray();
        foreach (var path in paths) files.Add(path);
        try
        {
            await broker.CallAsync("DOM.setFileInputFiles", new System.Text.Json.Nodes.JsonObject { ["files"] = files, ["backendNodeId"] = node });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly KeyChord EscapeChord = new(0x01, Marrow.Core.Port.ChordModifiers.None);

    private void ApplyGuard(IWebEngineView view, CdpBroker broker)
    {
        if (IsSandboxed) return;
        foreach (var script in WebFeatureGuard.DocumentScriptsFor(Kind)) view.AddDocumentCreatedScript(script);
        if (Kind is WebSurfaceKind.VisusTab or WebSurfaceKind.VisusPrivateTab or WebSurfaceKind.PreviewTab)
            guardSubscriptions.Add(broker.Subscribe("Runtime.bindingCalled", OnBindingCalled));
        if (Kind == WebSurfaceKind.AgentTab)
            guardSubscriptions.Add(broker.Subscribe("Page.fileChooserOpened", OnFileChooserOpened));
    }

    private void SendGuardCalls()
    {
        if (IsSandboxed || cdp is not { } broker) return;
        foreach (var (method, parameters) in WebFeatureGuard.AttachCallsFor(Kind)) _ = RunQuietly(broker.CallAsync(method, parameters));
    }

    private void OnBindingCalled(WebCdpEvent e)
    {
        if (WebFeatureGuard.ReadBindingCall(e.ParamsJson, out var context) is not { } call) return;
        owner.Post(() => OnPointerLockCall(call.Kind, call.Id, context, e.SessionId));
    }

    private void OnPointerLockCall(string kind, int id, int context, string? sessionId)
    {
        if (kind is "locked" or "released")
        {
            var locked = kind == "locked";
            if (IsPointerLocked == locked) return;
            IsPointerLocked = locked;
            PointerLockChanged?.Invoke(locked);
            return;
        }
        var origin = WebPolicy.OriginOf(Url) ?? Url ?? "";
        // One ask at a time per tab: a page that asks again while nth is asking is refused at once (no prompt flood).
        if (pointerLockAsk is { IsAnswered: false } || PointerLockRequested is null)
        {
            AnswerPointerLock(id, context, sessionId, allow: false);
            return;
        }
        var ask = new WebPointerLockRequest(origin, allow =>
        {
            pointerLockAsk = null;
            AnswerPointerLock(id, context, sessionId, allow);
        });
        pointerLockAsk = ask;
        PointerLockRequested?.Invoke(this, ask);
    }

    private void AnswerPointerLock(int id, int context, string? sessionId, bool allow)
    {
        if (cdp is not { } broker) return;
        var parameters = new System.Text.Json.Nodes.JsonObject
        {
            ["expression"] = WebFeatureGuard.AnswerExpression(id, allow),
            ["userGesture"] = true,
        };
        if (context > 0) parameters["contextId"] = context;
        _ = RunQuietly(broker.CallAsync("Runtime.evaluate", parameters, sessionId));
    }

    private void OnFileChooserOpened(WebCdpEvent e)
    {
        if (WebFeatureGuard.ReadFileChooser(e.ParamsJson) is not { } chooser) return;
        owner.Post(() =>
        {
            Note(WebAgentNoteKind.FileChooserBlocked, "The page asked for a file. Only the user can choose one, in nth's file upload card.");
            FileChooserIntercepted?.Invoke(this, chooser);
        });
    }

    private void Note(WebAgentNoteKind kind, string text)
    {
        var note = new WebAgentNote(kind, text, owner.Now);
        agentNotes.Add(note);
        if (agentNotes.Count > AgentNoteLimit) agentNotes.RemoveAt(0);
        AgentNoted?.Invoke(note);
    }

    private static string DialogWord(WebScriptDialogKind kind) => kind switch
    {
        WebScriptDialogKind.Confirm => "confirm dialog",
        WebScriptDialogKind.Prompt => "prompt dialog",
        WebScriptDialogKind.BeforeUnload => "leave-page dialog",
        _ => "alert",
    };

    private static string Clip(string text)
    {
        var one = text.ReplaceLineEndings(" ").Trim();
        return one.Length <= 300 ? one : one[..297] + "…";
    }

    private static async Task RunQuietly(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception)
        {
            // A switch the engine refused (an older runtime, a view going away): the script gates still hold, and the safe answer
            // stands (a pointer-lock ask with no answer stays refused in the page).
        }
    }

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
