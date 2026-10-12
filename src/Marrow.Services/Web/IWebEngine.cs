using Marrow.Core.Web;

namespace Marrow.Services.Web;

/// <summary>
/// DESIGN 2.2 (ED-NEW-web-w7-1): the only place that touches WebView2. <see cref="WebView2Engine"/> is the real engine
/// (Avalonia's <c>NativeWebView</c> in windowed mode, wrapped <c>CoreWebView2</c>); <see cref="FakeWebEngine"/> records
/// and scripts everything for tests. Composition hosting (spike g) or nth's own thin host (the spike a fallback) would be
/// another implementation behind this seam; nothing above it changes.
/// </summary>
public interface IWebEngine
{
    /// <summary>The installed runtime's version (<c>GetAvailableCoreWebView2BrowserVersionString</c>), or null when missing.</summary>
    string? RuntimeVersion();

    /// <summary>The oldest runtime the SDK and nth's calls need (DESIGN 2.1, 2.7).</summary>
    string MinimumRuntimeVersion { get; }

    /// <summary>Orders two runtime version strings (negative, zero, positive).</summary>
    int CompareVersions(string left, string right);

    /// <summary>Creates a view; it becomes usable once the host attaches the platform view.</summary>
    IWebEngineView CreateView(WebCreationOptions creation);

    /// <summary>
    /// The shared environment for these options (created on first use, or the pre-warmed one), as the raw pointer the
    /// host passes to the platform view; zero lets the platform create its own (the fake). The engine keeps it alive.
    /// </summary>
    Task<nint> AcquireEnvironmentAsync(WebCreationOptions creation, CancellationToken cancellationToken);

    /// <summary>Drops the environment after the browser process died, so "Restart Visus" creates a new one (DESIGN 2.7).</summary>
    void ResetEnvironment();

    /// <summary>DESIGN 2.3 pre-warm: starts the browser process group with these (environment-identical) options.</summary>
    Task PrewarmAsync(WebCreationOptions creation, CancellationToken cancellationToken);

    /// <summary>Closes the pre-warmed environment (unused for 10 minutes).</summary>
    void ReleasePrewarm();

    /// <summary>True while a pre-warmed environment is held.</summary>
    bool IsPrewarmed { get; }

    /// <summary>Drops one reference on a platform pointer the host handed over and no view took (a view discarded meanwhile).</summary>
    void ReleaseNative(nint pointer);

    /// <summary><c>NewBrowserVersionAvailable</c>: shows "Restart Visus to update" in the browser menu (DESIGN 2.1).</summary>
    event EventHandler? NewRuntimeVersionAvailable;
}

/// <summary>A memory target for a view (DESIGN 2.3: hidden or background views run Low).</summary>
public enum WebMemoryLevel
{
    Normal,
    Low,
}

/// <summary>Find-in-page options and results (DESIGN 5.3).</summary>
public sealed record WebFindResult(int MatchCount, int ActiveMatchIndex);

/// <summary>
/// DESIGN 2.2: one web view. Navigation, history, zoom, find, print, CDP, scripts, messages, capture, bounds,
/// visibility, suspend and resume, and the events every surface needs. Every member runs on the UI thread.
/// </summary>
public interface IWebEngineView : IDisposable
{
    WebCreationOptions Creation { get; }

    /// <summary>True between <see cref="Attach"/> and <see cref="Detach"/> (or dispose).</summary>
    bool IsAttached { get; }

    /// <summary>Raised when the platform view attached; queued work (the first navigation) runs then.</summary>
    event EventHandler? Attached;

    /// <summary>Wraps the host's raw <c>ICoreWebView2</c> and <c>ICoreWebView2Controller</c> (DESIGN 2.1).</summary>
    void Attach(nint coreWebView2, nint controller);

    /// <summary>The platform view is gone.</summary>
    void Detach();

    string? Url { get; }
    string? Title { get; }
    string? FaviconUrl { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    bool IsSuspended { get; }

    /// <summary>The page's own background colour, read once per navigation (DESIGN 2.5), or null.</summary>
    uint? BackgroundArgb { get; }

    void Navigate(string url);
    void GoBack();
    void GoForward();

    /// <summary>Reload; <paramref name="hard"/> bypasses the cache (Shift+Reload, DESIGN 5.3).</summary>
    void Reload(bool hard);

    void Stop();

    /// <summary>Page zoom factor (DESIGN 5.3).</summary>
    double Zoom { get; set; }

    Task<WebFindResult> FindAsync(string query, bool matchCase, CancellationToken cancellationToken);
    void FindNext();
    void FindPrevious();
    void StopFind();

    void ShowPrintUI();
    Task<bool> PrintToPdfAsync(string path, CancellationToken cancellationToken);

    /// <summary>One DevTools Protocol call; <paramref name="sessionId"/> targets an attached iframe (flatten mode). Returns the result JSON.</summary>
    Task<string> CallCdpAsync(string method, string paramsJson, string? sessionId, CancellationToken cancellationToken);

    /// <summary>Subscribes to one CDP event across the page and every attached session.</summary>
    IDisposable SubscribeCdp(string eventName, Action<WebCdpEvent> handler);

    Task<string> ExecuteScriptAsync(string script, CancellationToken cancellationToken);
    void PostMessage(string json);

    /// <summary>DESIGN 2.5: one capture of the visible viewport (CDP, then the preview fallback).</summary>
    Task<WebSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken);

    /// <summary>The web rectangle in DIPs and the device scale (the HWND itself follows the host's layout).</summary>
    void SetBounds(double width, double height, double scale);

    /// <summary>The controller's visibility.</summary>
    void SetVisible(bool visible);

    /// <summary>DESIGN 2.5 settle: waits for page frames via a <c>requestAnimationFrame</c> ping.</summary>
    Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary><c>TrySuspendAsync</c>; the view must be hidden first. False when the page cannot be suspended now.</summary>
    Task<bool> SuspendAsync();

    void Resume();

    WebMemoryLevel MemoryLevel { get; set; }

    event EventHandler<WebNavigationStarting>? NavigationStarting;
    event EventHandler<WebNavigationStarting>? FrameNavigationStarting;
    event EventHandler<WebNavigationCompleted>? NavigationCompleted;
    event EventHandler<WebNewWindowRequest>? NewWindowRequested;
    event EventHandler<WebPermissionRequest>? PermissionRequested;
    event EventHandler<WebDownloadRequest>? DownloadStarting;
    event EventHandler? TitleChanged;
    event EventHandler? FaviconChanged;
    event EventHandler? HistoryChanged;
    event EventHandler<WebProcessFailed>? ProcessFailed;
    event EventHandler<WebMessage>? WebMessageReceived;
    event EventHandler<WebAcceleratorKey>? AcceleratorKeyPressed;
    event EventHandler<WebMoveFocusRequest>? MoveFocusRequested;
    event EventHandler<WebExternalUriRequest>? ExternalUriRequested;
    event EventHandler<WebAuthRequest>? AuthenticationRequested;
    event EventHandler<WebScriptDialogRequest>? ScriptDialogOpening;
    event EventHandler<WebCertificateErrorRequest>? CertificateErrorDetected;
    event EventHandler<bool>? FullScreenChanged;
}
