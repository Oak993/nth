using Marrow.Core.Models;

namespace Marrow.Services.Web;

// Web surfaces W7 (DESIGN 2.2): the events IWebEngine raises, in engine-neutral shapes. Requests that need a decision
// (permission, download, new window, external protocol, HTTP auth, script dialog, certificate error) carry a safe
// default that applies when no handler answers: deny, cancel or block (DESIGN 5.4, 5.5, 8.1, 8.3, 8.4). A handler that
// answers later calls Defer() first, and the engine holds the platform deferral until Respond().

/// <summary>A request the engine holds until nth decides; <see cref="Default"/> applies when nobody answers.</summary>
public abstract class WebDeferredRequest<TDecision>(TDecision @default)
{
    private readonly TaskCompletionSource<TDecision> decision = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>The safe answer used when no handler answers or defers.</summary>
    public TDecision Default { get; } = @default;

    /// <summary>True after a handler called <see cref="Defer"/>.</summary>
    public bool IsDeferred { get; private set; }

    /// <summary>The decision, once made.</summary>
    public Task<TDecision> Decision => decision.Task;

    /// <summary>Keeps the request open past the event handler; answer later with <see cref="Respond"/>.</summary>
    public void Defer() => IsDeferred = true;

    /// <summary>Answers the request. Only the first answer counts.</summary>
    public bool Respond(TDecision value) => decision.TrySetResult(value);

    /// <summary>The engine calls this after raising the event: an unanswered, undeferred request takes its default.</summary>
    public void CompleteIfUnanswered()
    {
        if (!IsDeferred) decision.TrySetResult(Default);
    }
}

/// <summary>DESIGN 2.2, 8.1: a top-level or frame navigation is starting; set <see cref="Cancel"/> to stop it.</summary>
public sealed class WebNavigationStarting(string url, bool isUserInitiated, bool isRedirected, bool isMainFrame) : EventArgs
{
    public string Url { get; } = url;
    public bool IsUserInitiated { get; } = isUserInitiated;
    public bool IsRedirected { get; } = isRedirected;
    public bool IsMainFrame { get; } = isMainFrame;
    public bool Cancel { get; set; }
}

/// <summary>A navigation finished.</summary>
public sealed record WebNavigationCompleted(string Url, bool IsSuccess, int HttpStatusCode, string ErrorStatus);

/// <summary>DESIGN 8.1: <c>NewWindowRequested</c> is always handled; by default the pop-up is blocked.</summary>
public sealed class WebNewWindowRequest(string url, bool isUserInitiated) : WebDeferredRequest<WebNewWindowDecision>(WebNewWindowDecision.Block)
{
    public string Url { get; } = url;
    public bool IsUserInitiated { get; } = isUserInitiated;
}

/// <summary>What happens to a new-window request: never a real pop-up window.</summary>
public enum WebNewWindowDecision
{
    /// <summary>Blocked ("Pop-up blocked" chip).</summary>
    Block,

    /// <summary>Handled by nth (a new Visus tab, or the default browser per the link setting); the page gets no window.</summary>
    HandledByNth,
}

/// <summary>DESIGN 5.5: the permission kinds nth handles.</summary>
public enum WebPermissionKind
{
    Unknown,
    Camera,
    Microphone,
    Location,
    Notifications,
    ClipboardRead,
    Midi,
    Sensors,
    LocalFonts,
    WindowManagement,
    FileSystemAccess,
    Autoplay,
    MultipleDownloads,

    /// <summary>
    /// <c>navigator.storage.persist()</c>. Not among DESIGN 5.5's kinds handled, so it is denied with no prompt in every
    /// profile (ED-NEW-web-w7-1), as is <see cref="Unknown"/>.
    /// </summary>
    PersistentStorage,
}

/// <summary>A permission decision.</summary>
public enum WebPermissionDecision
{
    Deny,
    AllowOnce,
    AllowAlways,
}

/// <summary>DESIGN 5.5: <c>PermissionRequested</c> is denied by default; nth shows its own prompt.</summary>
public sealed class WebPermissionRequest(string origin, WebPermissionKind kind, bool isUserInitiated)
    : WebDeferredRequest<WebPermissionDecision>(WebPermissionDecision.Deny)
{
    public string Origin { get; } = origin;
    public WebPermissionKind Kind { get; } = kind;
    public bool IsUserInitiated { get; } = isUserInitiated;
}

/// <summary>A download decision: cancel, or save to a path nth chose (DESIGN 5.4).</summary>
public sealed record WebDownloadDecision(bool Save, string? ResultFilePath)
{
    public static WebDownloadDecision Cancel { get; } = new(false, null);
}

/// <summary>DESIGN 5.4: every download asks; <c>DownloadStarting</c> is held and cancelled unless nth saves it.</summary>
public sealed class WebDownloadRequest(string url, string suggestedFileName, long? totalBytes, string mimeType, string sourceOrigin)
    : WebDeferredRequest<WebDownloadDecision>(WebDownloadDecision.Cancel)
{
    private readonly TaskCompletionSource<IWebDownloadOperation?> started = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public string Url { get; } = url;
    public string SuggestedFileName { get; } = suggestedFileName;
    public long? TotalBytes { get; } = totalBytes;
    public string MimeType { get; } = mimeType;
    public string SourceOrigin { get; } = sourceOrigin;

    /// <summary>
    /// W8b: the transfer, once the engine applied a Save (null after a Cancel). Its progress feeds the downloads list and the
    /// Background tasks pane (DESIGN 5.1 "downloads (with a progress ring)", 3.10).
    /// </summary>
    public Task<IWebDownloadOperation?> Started => started.Task;

    /// <summary>The engine reports the transfer it started for the decision (or null). Only the first report counts.</summary>
    public void ReportStarted(IWebDownloadOperation? operation) => started.TrySetResult(operation);
}

/// <summary>W8b: a saved download's state (<c>CoreWebView2DownloadState</c>, with Paused split out of InProgress).</summary>
public enum WebDownloadState
{
    InProgress,
    Paused,
    Interrupted,
    Completed,
}

/// <summary>
/// W8b (DESIGN 5.4): one transfer the user saved. The file is never opened by nth; the browser keeps the Mark of the Web and its
/// SmartScreen check. Every member runs on the UI thread.
/// </summary>
public interface IWebDownloadOperation
{
    /// <summary>Where the file lands (nth chose it: the Downloads folder or the Save as… path).</summary>
    string ResultFilePath { get; }

    long BytesReceived { get; }

    /// <summary>The size, when the server said it.</summary>
    long? TotalBytes { get; }

    WebDownloadState State { get; }

    /// <summary>Why it stopped (the engine's reason, as text), when <see cref="State"/> is Interrupted.</summary>
    string? InterruptReason { get; }

    bool CanResume { get; }

    void Cancel();

    void Pause();

    void Resume();

    /// <summary>Bytes, state or the path changed.</summary>
    event EventHandler? Changed;
}

/// <summary>
/// W8b (DESIGN 5.5 "Notifications ... in browse they map to nth's own toast"): a page's notification, held from Windows'
/// default. The engine always marks it handled; nth shows it as its own toast or drops it.
/// </summary>
public sealed class WebNotification(string senderOrigin, string title, string body, string? tag, bool isSilent) : EventArgs
{
    public string SenderOrigin { get; } = senderOrigin;
    public string Title { get; } = title;
    public string Body { get; } = body;
    public string? Tag { get; } = tag;
    public bool IsSilent { get; } = isSilent;

    /// <summary>nth showed it (the engine reports it shown to the page).</summary>
    public bool Shown { get; set; }
}

/// <summary>
/// W8b: <c>ScreenCaptureStarting</c> (<c>getDisplayMedia</c>), before the browser's own picker shows. Denied by default; the
/// Visus page profiles let the picker show (the user picks what to share in it); agent tabs and sandboxed content never.
/// </summary>
public sealed class WebScreenCaptureRequest(string origin) : WebDeferredRequest<bool>(false)
{
    /// <summary>The frame's site that asked.</summary>
    public string Origin { get; } = origin;
}

/// <summary>DESIGN 2.7: the process failure kinds the engine reports.</summary>
public enum WebProcessFailureKind
{
    BrowserProcessExited,
    RenderProcessExited,
    RenderProcessUnresponsive,
    FrameRenderProcessExited,
    GpuProcessExited,
    UtilityProcessExited,
    SandboxHelperProcessExited,
    PpapiPluginProcessExited,
    PpapiBrokerProcessExited,
    UnknownProcessExited,
}

/// <summary><c>ProcessFailed</c> in engine-neutral form.</summary>
public sealed record WebProcessFailed(WebProcessFailureKind Kind, string Reason, int ExitCode);

/// <summary>A web message from a page (only nth-owned origins are listened to; DESIGN 8.1).</summary>
public sealed record WebMessage(string SourceOrigin, string Json);

/// <summary>The key event kinds of <c>AcceleratorKeyPressed</c>.</summary>
public enum WebKeyEventKind
{
    KeyDown,
    KeyUp,
    SystemKeyDown,
    SystemKeyUp,
}

/// <summary>
/// DESIGN 3.7: a key the web view raised before the page saw it (<c>AcceleratorKeyPressed</c>). <see cref="Chord"/> is
/// nth's chord form (ED-099: scan code, extended keys as 0xE000 | code; Control, Alt, Shift). Set <see cref="Handled"/>
/// to keep the key from the page. In windowed mode the event is synchronous and the browser waits for the handler, so a
/// handler decides at once and runs any work later (posted).
/// </summary>
public sealed class WebAcceleratorKey(KeyChord chord, uint virtualKey, WebKeyEventKind kind, bool isRepeat) : EventArgs
{
    public KeyChord Chord { get; } = chord;
    public uint VirtualKey { get; } = virtualKey;
    public WebKeyEventKind Kind { get; } = kind;
    public bool IsRepeat { get; } = isRepeat;
    public bool Handled { get; set; }

    /// <summary>A key going down (plain or with Alt), the only events chords act on.</summary>
    public bool IsKeyDown => Kind is WebKeyEventKind.KeyDown or WebKeyEventKind.SystemKeyDown;
}

/// <summary>DESIGN 10: the page asked to move focus out (Tab past the last element, Shift+Tab past the first).</summary>
public sealed class WebMoveFocusRequest(bool forward) : EventArgs
{
    public bool Forward { get; } = forward;
    public bool Handled { get; set; }
}

/// <summary>DESIGN 8.3: <c>LaunchingExternalUriScheme</c>; denied by default.</summary>
public sealed class WebExternalUriRequest(string uri, string initiatingOrigin, bool isUserInitiated)
    : WebDeferredRequest<bool>(false)
{
    public string Uri { get; } = uri;
    public string InitiatingOrigin { get; } = initiatingOrigin;
    public bool IsUserInitiated { get; } = isUserInitiated;
}

/// <summary>An HTTP auth answer: null cancels.</summary>
public sealed record WebCredentials(string UserName, string Password);

/// <summary>DESIGN 8.4: <c>BasicAuthenticationRequested</c>; the native dialog never shows, cancelled by default.</summary>
public sealed class WebAuthRequest(string uri, string challenge) : WebDeferredRequest<WebCredentials?>(null)
{
    public string Uri { get; } = uri;
    public string Challenge { get; } = challenge;
}

/// <summary>Script dialog kinds.</summary>
public enum WebScriptDialogKind
{
    Alert,
    Confirm,
    Prompt,
    BeforeUnload,
}

/// <summary>A script dialog answer: accept (with the prompt text) or dismiss.</summary>
public sealed record WebScriptDialogDecision(bool Accept, string? Text)
{
    public static WebScriptDialogDecision Dismiss { get; } = new(false, null);
}

/// <summary>DESIGN 8.4: script dialogs are nth cards (or auto-dismissed); dismissed by default.</summary>
public sealed class WebScriptDialogRequest(string uri, WebScriptDialogKind kind, string message, string defaultText)
    : WebDeferredRequest<WebScriptDialogDecision>(WebScriptDialogDecision.Dismiss)
{
    public string Uri { get; } = uri;
    public WebScriptDialogKind Kind { get; } = kind;
    public string Message { get; } = message;
    public string DefaultText { get; } = defaultText;
}

/// <summary>DESIGN 8.1: <c>ServerCertificateErrorDetected</c>; cancelled by default ("Continue anyway" only for dev hosts).</summary>
public sealed class WebCertificateErrorRequest(string uri, string errorStatus) : WebDeferredRequest<bool>(false)
{
    public string Uri { get; } = uri;
    public string ErrorStatus { get; } = errorStatus;
}

/// <summary>A DevTools Protocol event (DESIGN 5.8, 5.10): its name, the session it came from (null for the page), its params.</summary>
public sealed record WebCdpEvent(string Name, string? SessionId, string ParamsJson);

/// <summary>DESIGN 6.5: what a resource request is for (the engine's resource context).</summary>
public enum WebResourceKind
{
    Document,
    Stylesheet,
    Image,
    Media,
    Font,
    Script,
    Fetch,
    XmlHttpRequest,
    EventSource,
    WebSocket,
    Manifest,
    Ping,
    Other,
}

/// <summary>One request a sandboxed view is about to make (<c>WebResourceRequested</c>), before it reaches the network.</summary>
public sealed record WebResourceRequest(string Uri, string Method, WebResourceKind Kind);

/// <summary>nth's answer to a request: status, headers and the whole body.</summary>
public sealed record WebResourceResponse(int StatusCode, string ReasonPhrase, IReadOnlyList<KeyValuePair<string, string>> Headers, byte[] Body)
{
    /// <summary>The value of a header (case-insensitive), or null.</summary>
    public string? Header(string name) => Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
}

/// <summary>
/// DESIGN 6.5: answers a sandboxed view's requests. Return a response to answer it (nth's own hosts, or a block), or
/// null to let it go to the network (only hosts the user allowed for that surface). Runs on the UI thread.
/// </summary>
public interface IWebResourceHandler
{
    WebResourceResponse? Answer(WebResourceRequest request);
}

/// <summary>DESIGN 6.5: a sandboxed view's site: its origin, its request handler and its document scripts.</summary>
public sealed record WebSandbox(string Origin, IWebResourceHandler Resources, IReadOnlyList<string> DocumentScripts)
{
    /// <summary>
    /// PORT-ONLY (web surfaces W19, DESIGN.md 7.6 "Permissions from _meta.ui.permissions still prompt the user (5.5)"): the
    /// permission kinds this site may ask the user for (an MCP App's declared camera, microphone and location). Every other
    /// kind stays denied with no prompt, as for every sandboxed view. Null: none.
    /// </summary>
    public Func<WebPermissionKind, bool>? Prompts { get; init; }

    /// <summary>
    /// PORT-ONLY (web surfaces W19, DESIGN.md 7.6 frameDomains): a frame (never the top document) this site may load from
    /// another origin, because the user allowed the MCP App's declared frame domains. Null: none, as for every sandboxed view.
    /// </summary>
    public Func<string, bool>? AllowsFrame { get; init; }
}

/// <summary>DESIGN 6.5: a navigation (or new window) a sandboxed view was not allowed to make.</summary>
public sealed record WebSandboxNavigation(string Url, bool IsUserInitiated, bool IsMainFrame, bool IsNewWindow);
