using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 13.2 (ED-NEW-web-w7-1): the engine for tests. It records every call (CDP calls with their session, the
/// operation log of each view), scripts every event (navigation, permission, download, console and network through CDP
/// events, new window, process failure, keys) and returns canned snapshots. Captures, frame pings and CDP calls can be
/// held open to model slow pages. It touches no WebView2 and no window.
/// </summary>
public sealed class FakeWebEngine : IWebEngine
{
    /// <summary>A 1 x 1 PNG, the default canned frame's bytes.</summary>
    public static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    public FakeWebEngine(TimeProvider? time = null) => Time = time ?? TimeProvider.System;

    public TimeProvider Time { get; }

    /// <summary>The installed runtime version; null models a missing runtime.</summary>
    public string? InstalledVersion { get; set; } = "141.0.3537.71";

    public string MinimumRuntimeVersion { get; set; } = WebView2Engine.MinimumVersion;

    /// <summary>Every view created, in order.</summary>
    public List<FakeWebView> Views { get; } = [];

    /// <summary>Every environment request (the options each view's host asked with).</summary>
    public List<WebCreationOptions> EnvironmentRequests { get; } = [];

    public List<WebCreationOptions> Prewarms { get; } = [];

    public int PrewarmReleases { get; private set; }

    public int EnvironmentResets { get; private set; }

    public bool IsPrewarmed { get; private set; }

    /// <summary>Makes the canned frame for a view; the default is a 1 x 1 PNG sized to the view's bounds.</summary>
    public Func<FakeWebView, WebSnapshot?>? SnapshotFactory { get; set; }

    public event EventHandler? NewRuntimeVersionAvailable;

    public string? RuntimeVersion() => InstalledVersion;

    public int CompareVersions(string left, string right) => WebRuntimeStatus.CompareVersions(left, right);

    public IWebEngineView CreateView(WebCreationOptions creation)
    {
        var view = new FakeWebView(this, creation, Views.Count + 1);
        Views.Add(view);
        return view;
    }

    public Task<nint> AcquireEnvironmentAsync(WebCreationOptions creation, CancellationToken cancellationToken)
    {
        EnvironmentRequests.Add(creation);
        return Task.FromResult<nint>(0);
    }

    public void ResetEnvironment() => EnvironmentResets++;

    public Task PrewarmAsync(WebCreationOptions creation, CancellationToken cancellationToken)
    {
        Prewarms.Add(creation);
        IsPrewarmed = true;
        return Task.CompletedTask;
    }

    public void ReleasePrewarm()
    {
        if (!IsPrewarmed) return;
        IsPrewarmed = false;
        PrewarmReleases++;
    }

    /// <summary>Platform pointers released without a view taking them.</summary>
    public List<nint> ReleasedNative { get; } = [];

    public void ReleaseNative(nint pointer) => ReleasedNative.Add(pointer);

    /// <summary>Raises <c>NewBrowserVersionAvailable</c>.</summary>
    public void RaiseNewRuntimeVersion() => NewRuntimeVersionAvailable?.Invoke(this, EventArgs.Empty);
}

/// <summary>One fake web view: an operation log, recorded CDP calls, scripted events and holdable async work.</summary>
public sealed class FakeWebView : IWebEngineView
{
    private readonly FakeWebEngine engine;
    private readonly Dictionary<string, List<Action<WebCdpEvent>>> cdpHandlers = new(StringComparer.Ordinal);
    private readonly List<TaskCompletionSource<bool>> pendingFrames = [];
    private double width = 800;
    private double height = 600;
    private double scale = 1;

    internal FakeWebView(FakeWebEngine engine, WebCreationOptions creation, int number)
    {
        this.engine = engine;
        Creation = creation;
        Number = number;
    }

    /// <summary>1-based creation order across the engine.</summary>
    public int Number { get; }

    public WebCreationOptions Creation { get; }

    public bool IsAttached { get; private set; }

    public bool IsDisposed { get; private set; }

    /// <summary>Every operation in order ("Navigate https://a.test/", "SetVisible False", "Suspend", ...).</summary>
    public List<string> Log { get; } = [];

    /// <summary>Every CDP call: method, params JSON, session (null for the page).</summary>
    public List<(string Method, string Params, string? SessionId)> CdpCalls { get; } = [];

    /// <summary>Answers CDP calls; the default returns "{}". Return a held task to model a slow call.</summary>
    public Func<string, string, string?, Task<string>>? CdpHandler { get; set; }

    /// <summary>When set, captures wait on it (a slow capture); otherwise they return the canned frame at once.</summary>
    public TaskCompletionSource<WebSnapshot?>? HeldCapture { get; set; }

    /// <summary>Whether frame pings answer at once (true), or wait until <see cref="AnswerFrames"/> (false).</summary>
    public bool AnswersFrames { get; set; } = true;

    /// <summary>What <see cref="SuspendAsync"/> returns when the view is hidden (WebView2 refuses a visible view).</summary>
    public bool SuspendSucceeds { get; set; } = true;
    public TaskCompletionSource<bool>? HeldSuspend { get; set; }

    public int Captures { get; private set; }

    public bool IsVisible { get; private set; } = true;

    public string? Url { get; private set; }
    public string? Title { get; set; }
    public string? FaviconUrl { get; set; }
    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }
    public bool IsSuspended { get; private set; }
    public bool IsPlayingAudio { get; set; }
    public uint? BackgroundArgb { get; set; } = 0xFFFFFFFF;
    public double Zoom
    {
        get => zoom;
        set { if (zoom == value) return; zoom = value; ZoomChanged?.Invoke(this, EventArgs.Empty); }
    }
    private double zoom = 1;
    public event EventHandler? ZoomChanged;
    public event EventHandler<WebFindResult>? FindResultChanged;
    public Func<string, bool, CancellationToken, Task<WebFindResult>>? FindHandler { get; set; }
    public Func<string, CancellationToken, Task<bool>>? PdfHandler { get; set; }
    public void RaiseFindResult(WebFindResult result) => FindResultChanged?.Invoke(this, result);

    public WebMemoryLevel MemoryLevel
    {
        get;
        set
        {
            field = value;
            Log.Add($"Memory {value}");
        }
    }

    public event EventHandler? Attached;
    public event EventHandler<WebNavigationStarting>? NavigationStarting;
    public event EventHandler<WebNavigationStarting>? FrameNavigationStarting;
    public event EventHandler<WebNavigationCompleted>? NavigationCompleted;
    public event EventHandler<WebNewWindowRequest>? NewWindowRequested;
    public event EventHandler<WebPermissionRequest>? PermissionRequested;
    public event EventHandler<WebDownloadRequest>? DownloadStarting;
    public event EventHandler? TitleChanged;
    public event EventHandler? FaviconChanged;
    public event EventHandler? HistoryChanged;
    public event EventHandler<WebProcessFailed>? ProcessFailed;
    public event EventHandler<WebMessage>? WebMessageReceived;
    public event EventHandler<WebAcceleratorKey>? AcceleratorKeyPressed;
    public event EventHandler<WebMoveFocusRequest>? MoveFocusRequested;
    public event EventHandler<WebExternalUriRequest>? ExternalUriRequested;
    public event EventHandler<WebAuthRequest>? AuthenticationRequested;
    public event EventHandler<WebScriptDialogRequest>? ScriptDialogOpening;
    public event EventHandler<WebCertificateErrorRequest>? CertificateErrorDetected;
    public event EventHandler<bool>? FullScreenChanged;
    public event EventHandler<WebNotification>? NotificationReceived;
    public event EventHandler<WebScreenCaptureRequest>? ScreenCaptureStarting;

    public void Attach(nint coreWebView2, nint controller)
    {
        IsAttached = true;
        Log.Add("Attach");
        Attached?.Invoke(this, EventArgs.Empty);
    }

    public void Detach()
    {
        IsAttached = false;
        Log.Add("Detach");
    }

    public void Navigate(string url)
    {
        Url = url;
        Log.Add($"Navigate {url}");
    }

    /// <summary>The document scripts, in the order added (DESIGN 6.5).</summary>
    public List<string> DocumentScripts { get; } = [];

    /// <summary>The resource handler the surface installed, or null.</summary>
    public IWebResourceHandler? ResourceHandler { get; private set; }

    public void AddDocumentCreatedScript(string script)
    {
        ArgumentException.ThrowIfNullOrEmpty(script);
        DocumentScripts.Add(script);
        Log.Add("AddDocumentScript");
    }

    public void SetResourceHandler(IWebResourceHandler? handler)
    {
        ResourceHandler = handler;
        Log.Add(handler is null ? "ResourceHandler off" : "ResourceHandler on");
    }

    /// <summary>
    /// Models the page asking for an address: the handler's answer, or null when the request would go to the network
    /// (also when no handler is installed).
    /// </summary>
    public WebResourceResponse? Request(string url, WebResourceKind kind = WebResourceKind.Other, string method = "GET") =>
        ResourceHandler?.Answer(new WebResourceRequest(url, method, kind));

    public void GoBack() => Log.Add("GoBack");
    public void GoForward() => Log.Add("GoForward");
    public void Reload(bool hard) => Log.Add(hard ? "Reload hard" : "Reload");
    public void Stop() => Log.Add("Stop");

    public Task<WebFindResult> FindAsync(string query, bool matchCase, CancellationToken cancellationToken)
    {
        Log.Add($"Find {query}");
        return FindHandler?.Invoke(query, matchCase, cancellationToken) ?? Task.FromResult(new WebFindResult(0, -1));
    }

    public void FindNext() => Log.Add("FindNext");
    public void FindPrevious() => Log.Add("FindPrevious");
    public void StopFind() => Log.Add("StopFind");
    public void ShowPrintUI() => Log.Add("Print");

    public Task<bool> PrintToPdfAsync(string path, CancellationToken cancellationToken)
    {
        Log.Add($"PrintToPdf {path}");
        return PdfHandler?.Invoke(path, cancellationToken) ?? Task.FromResult(true);
    }

    public Task<string> CallCdpAsync(string method, string paramsJson, string? sessionId, CancellationToken cancellationToken)
    {
        CdpCalls.Add((method, paramsJson, sessionId));
        return CdpHandler?.Invoke(method, paramsJson, sessionId) ?? Task.FromResult("{}");
    }

    public IDisposable SubscribeCdp(string eventName, Action<WebCdpEvent> handler)
    {
        if (!cdpHandlers.TryGetValue(eventName, out var list)) cdpHandlers[eventName] = list = [];
        list.Add(handler);
        return new Unsubscribe(() => list.Remove(handler));
    }

    public Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken)
    {
        Log.Add($"Script {script}");
        return Task.FromResult("null");
    }

    public void PostMessage(string json) => Log.Add($"Post {json}");

    /// <summary>W8a: what <see cref="GetFaviconAsync"/> returns (the browser's cached favicon).</summary>
    public byte[]? FaviconBytes { get; set; }

    public Task<byte[]?> GetFaviconAsync(CancellationToken cancellationToken) => Task.FromResult(FaviconBytes);

    public Task<WebSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken)
    {
        Captures++;
        Log.Add("Capture");
        if (HeldCapture is { } held) return held.Task;
        return Task.FromResult(CannedFrame());
    }

    /// <summary>The canned frame this view returns.</summary>
    public WebSnapshot? CannedFrame() =>
        engine.SnapshotFactory is { } factory
            ? factory(this)
            : new WebSnapshot(FakeWebEngine.OnePixelPng, (int)Math.Round(width * scale), (int)Math.Round(height * scale), scale,
                engine.Time.GetUtcNow(), BackgroundArgb, WebSnapshotSource.Fake);

    public void SetBounds(double width, double height, double scale)
    {
        this.width = width;
        this.height = height;
        this.scale = scale;
        Log.Add($"Bounds {width}x{height}@{scale}");
    }

    public void SetVisible(bool visible)
    {
        IsVisible = visible;
        Log.Add($"SetVisible {visible}");
    }

    public Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken)
    {
        Log.Add($"Frames {frames}");
        if (AnswersFrames) return Task.FromResult(true);
        var pending = new TaskCompletionSource<bool>();
        pendingFrames.Add(pending);
        cancellationToken.Register(() => pending.TrySetResult(false));
        return pending.Task;
    }

    /// <summary>Answers every frame ping waiting now (the page painted).</summary>
    public void AnswerFrames()
    {
        foreach (var pending in pendingFrames.ToList()) pending.TrySetResult(true);
        pendingFrames.Clear();
    }

    public async Task<bool> SuspendAsync()
    {
        Log.Add("Suspend");
        if (IsVisible || IsPlayingAudio || !SuspendSucceeds) return false;
        if (HeldSuspend is { } held && !await held.Task) return false;
        IsSuspended = true;
        return true;
    }

    public void Resume()
    {
        IsSuspended = false;
        Log.Add("Resume");
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;
        IsAttached = false;
        Log.Add("Dispose");
        foreach (var pending in pendingFrames) pending.TrySetResult(false);
        pendingFrames.Clear();
    }

    // Scripted events. Each request returns after the engine's own default ran, as WebView2Engine does.

    public void RaiseNavigationCompleted(string url, bool success = true, int status = 200)
    {
        Url = url;
        NavigationCompleted?.Invoke(this, new WebNavigationCompleted(url, success, status, success ? "Success" : "Unknown"));
    }

    public WebNavigationStarting RaiseNavigationStarting(string url, bool userInitiated = true, bool mainFrame = true)
    {
        var e = new WebNavigationStarting(url, userInitiated, isRedirected: false, mainFrame);
        if (mainFrame) NavigationStarting?.Invoke(this, e);
        else FrameNavigationStarting?.Invoke(this, e);
        return e;
    }

    public WebNewWindowRequest RaiseNewWindow(string url, bool userInitiated = true) => Raise(new WebNewWindowRequest(url, userInitiated), NewWindowRequested);

    public WebPermissionRequest RaisePermission(string origin, WebPermissionKind kind, bool userInitiated = true) =>
        Raise(new WebPermissionRequest(origin, kind, userInitiated), PermissionRequested);

    public WebDownloadRequest RaiseDownload(string url, string fileName, long? bytes = null, string mime = "application/octet-stream", string origin = "https://a.test")
    {
        var request = Raise(new WebDownloadRequest(url, fileName, bytes, mime, origin), DownloadStarting);
        // W8b: as the real engine does, a Save starts a transfer to the chosen path (scripted with FakeDownload); a Cancel reports none.
        void Apply(WebDownloadDecision decision)
        {
            if (decision.Save && decision.ResultFilePath is { } path)
            {
                var transfer = new FakeDownload(path, bytes);
                Downloads.Add(transfer);
                Log.Add($"Download {path}");
                request.ReportStarted(transfer);
            }
            else
            {
                Log.Add($"Download cancelled {fileName}");
                request.ReportStarted(null);
            }
        }

        if (request.Decision.IsCompleted) Apply(request.Decision.Result);
        else
        {
            var context = SynchronizationContext.Current;
            var scheduler = context is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext();
            _ = request.Decision.ContinueWith(task => Apply(task.Result), CancellationToken.None, TaskContinuationOptions.None, scheduler);
        }
        return request;
    }

    /// <summary>W8b: the transfers the view started for saved downloads, oldest first.</summary>
    public List<FakeDownload> Downloads { get; } = [];

    /// <summary>W8b: a page's notification; returns it after the handlers ran (Shown tells whether nth showed it).</summary>
    public WebNotification RaiseNotification(string origin, string title, string body = "", string? tag = null, bool silent = false)
    {
        var e = new WebNotification(origin, title, body, tag, silent);
        NotificationReceived?.Invoke(this, e);
        return e;
    }

    /// <summary>W8b: a page asked to capture the screen (getDisplayMedia), before the picker.</summary>
    public WebScreenCaptureRequest RaiseScreenCapture(string origin) => Raise(new WebScreenCaptureRequest(origin), ScreenCaptureStarting);

    public WebExternalUriRequest RaiseExternalUri(string uri, string origin, bool userInitiated) =>
        Raise(new WebExternalUriRequest(uri, origin, userInitiated), ExternalUriRequested);

    public WebAuthRequest RaiseAuth(string uri, string challenge = "Basic realm=\"test\"") => Raise(new WebAuthRequest(uri, challenge), AuthenticationRequested);

    public WebScriptDialogRequest RaiseScriptDialog(string uri, WebScriptDialogKind kind, string message, string defaultText = "") =>
        Raise(new WebScriptDialogRequest(uri, kind, message, defaultText), ScriptDialogOpening);

    public WebCertificateErrorRequest RaiseCertificateError(string uri, string status = "CertificateIsInvalid") =>
        Raise(new WebCertificateErrorRequest(uri, status), CertificateErrorDetected);

    public void RaiseProcessFailed(WebProcessFailureKind kind, string reason = "Unexpected", int exitCode = 1) =>
        ProcessFailed?.Invoke(this, new WebProcessFailed(kind, reason, exitCode));

    public WebAcceleratorKey RaiseKey(Marrow.Core.Models.KeyChord chord, WebKeyEventKind kind = WebKeyEventKind.KeyDown, bool repeat = false, uint virtualKey = 0)
    {
        var e = new WebAcceleratorKey(chord, virtualKey, kind, repeat);
        AcceleratorKeyPressed?.Invoke(this, e);
        return e;
    }

    public WebMoveFocusRequest RaiseMoveFocus(bool forward)
    {
        var e = new WebMoveFocusRequest(forward);
        MoveFocusRequested?.Invoke(this, e);
        return e;
    }

    public void RaiseTitle(string title)
    {
        Title = title;
        TitleChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseFavicon(string url)
    {
        FaviconUrl = url;
        FaviconChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseHistoryChanged() => HistoryChanged?.Invoke(this, EventArgs.Empty);

    public void RaiseMessage(string origin, string json) => WebMessageReceived?.Invoke(this, new WebMessage(origin, json));

    public void RaiseFullScreen(bool full) => FullScreenChanged?.Invoke(this, full);

    /// <summary>Emits a CDP event (console, network, target attach) to every subscriber.</summary>
    public void EmitCdp(string eventName, string paramsJson, string? sessionId = null)
    {
        if (!cdpHandlers.TryGetValue(eventName, out var list)) return;
        foreach (var handler in list.ToList()) handler(new WebCdpEvent(eventName, sessionId, paramsJson));
    }

    /// <summary>How many handlers listen to a CDP event now.</summary>
    public int CdpSubscribers(string eventName) => cdpHandlers.TryGetValue(eventName, out var list) ? list.Count : 0;

    private T Raise<T, TDecision>(T request, EventHandler<T>? handler) where T : WebDeferredRequest<TDecision>
    {
        handler?.Invoke(this, request);
        request.CompleteIfUnanswered();
        return request;
    }

    private WebNewWindowRequest Raise(WebNewWindowRequest r, EventHandler<WebNewWindowRequest>? h) => Raise<WebNewWindowRequest, WebNewWindowDecision>(r, h);
    private WebPermissionRequest Raise(WebPermissionRequest r, EventHandler<WebPermissionRequest>? h) => Raise<WebPermissionRequest, WebPermissionDecision>(r, h);
    private WebDownloadRequest Raise(WebDownloadRequest r, EventHandler<WebDownloadRequest>? h) => Raise<WebDownloadRequest, WebDownloadDecision>(r, h);
    private WebExternalUriRequest Raise(WebExternalUriRequest r, EventHandler<WebExternalUriRequest>? h) => Raise<WebExternalUriRequest, bool>(r, h);
    private WebAuthRequest Raise(WebAuthRequest r, EventHandler<WebAuthRequest>? h) => Raise<WebAuthRequest, WebCredentials?>(r, h);
    private WebScriptDialogRequest Raise(WebScriptDialogRequest r, EventHandler<WebScriptDialogRequest>? h) => Raise<WebScriptDialogRequest, WebScriptDialogDecision>(r, h);
    private WebCertificateErrorRequest Raise(WebCertificateErrorRequest r, EventHandler<WebCertificateErrorRequest>? h) => Raise<WebCertificateErrorRequest, bool>(r, h);
    private WebScreenCaptureRequest Raise(WebScreenCaptureRequest r, EventHandler<WebScreenCaptureRequest>? h) => Raise<WebScreenCaptureRequest, bool>(r, h);

    private sealed class Unsubscribe(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}

/// <summary>W8b: a scripted transfer (DESIGN 5.4): the test moves its bytes and state; Cancel, Pause and Resume are recorded.</summary>
public sealed class FakeDownload(string path, long? total) : IWebDownloadOperation
{
    public string ResultFilePath { get; private set; } = path;

    public long BytesReceived { get; private set; }

    public long? TotalBytes { get; private set; } = total;

    public WebDownloadState State { get; private set; } = WebDownloadState.InProgress;

    public string? InterruptReason { get; private set; }

    public bool CanResume { get; private set; } = true;

    /// <summary>"Cancel", "Pause" and "Resume", in order.</summary>
    public List<string> Calls { get; } = [];

    public event EventHandler? Changed;

    public void Cancel()
    {
        Calls.Add("Cancel");
        if (State is WebDownloadState.Completed or WebDownloadState.Interrupted) return;
        State = WebDownloadState.Interrupted;
        InterruptReason = "UserCanceled";
        CanResume = false;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        Calls.Add("Pause");
        if (State != WebDownloadState.InProgress) return;
        State = WebDownloadState.Paused;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        Calls.Add("Resume");
        if (State != WebDownloadState.Paused) return;
        State = WebDownloadState.InProgress;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The transfer reached <paramref name="bytes"/>.</summary>
    public void Progress(long bytes)
    {
        BytesReceived = bytes;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The transfer finished.</summary>
    public void Complete()
    {
        if (TotalBytes is { } size) BytesReceived = size;
        State = WebDownloadState.Completed;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The transfer failed (network, disk).</summary>
    public void Fail(string reason = "NetworkFailed", bool canResume = false)
    {
        State = WebDownloadState.Interrupted;
        InterruptReason = reason;
        CanResume = canResume;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
