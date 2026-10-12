namespace Marrow.Core.Web;

/// <summary>
/// DESIGN 2.2, 2.5 (ED-NEW-web-w7-1): what the UI host (<c>Marrow.UI.Web.WebViewHost</c>) needs from one web view. The
/// engine layer implements it (<c>Marrow.Services.Web.WebSurfaceView</c>, over <c>IWebEngine</c>); the App hands the
/// object to the host, so the host never references Services and the engine never references Avalonia (ED-064).
/// Every member is called on the UI thread.
/// </summary>
public interface IWebViewSurface
{
    /// <summary>The pre-creation options the host applies in <c>NativeWebView.EnvironmentRequested</c> (DESIGN 2.1).</summary>
    WebCreationOptions Creation { get; }

    /// <summary>Live, suspended, discarded (DESIGN 2.3). The host shows the snapshot whenever the view is not live.</summary>
    WebLiveness Liveness { get; }

    /// <summary>The failure card to draw over the dimmed snapshot (DESIGN 2.7), or null.</summary>
    WebFailureCard? Failure { get; }

    /// <summary>The newest frame of this view, if any (kept for suspended and discarded views).</summary>
    WebSnapshot? LastSnapshot { get; }

    /// <summary>
    /// Whether the live web view is visible now (<see cref="SetLiveVisible"/>, or the engine hiding a view it suspends
    /// or a tab that is no longer shown). The host shows it again when it is live and nothing asks for the snapshot.
    /// </summary>
    bool IsLiveVisible { get; }

    /// <summary>Raised when <see cref="Liveness"/>, <see cref="Failure"/> or <see cref="LastSnapshot"/> changes.</summary>
    event Action? Changed;

    /// <summary>
    /// The page asked to move keyboard focus out of the web view (Tab or Shift+Tab past its last or first element;
    /// <c>MoveFocusRequested</c>, Avalonia WebView issue #76, DESIGN 10). True means forward (Tab), false backward.
    /// </summary>
    event Action<bool>? FocusExitRequested;

    /// <summary>
    /// DESIGN 2.1, 2.3, 2.7: the environment the host hands the platform view before it is created
    /// (<c>ExplicitEnvironment</c>, a raw <c>ICoreWebView2Environment</c> the engine keeps alive), so every view shares
    /// one browser process group created with identical options, a pre-warmed environment is the one used, and "Restart
    /// Visus" gets a fresh environment rather than a cached dead one. Zero lets the platform create its own.
    /// </summary>
    Task<nint> AcquireEnvironmentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The platform view exists (<c>NativeWebView.AdapterCreated</c>): the host passes the adapter's raw
    /// <c>ICoreWebView2</c> and <c>ICoreWebView2Controller</c> pointers (<c>IWindowsWebView2PlatformHandle</c>), which
    /// the engine wraps for permissions, downloads, CDP and accelerator keys (DESIGN 2.1). The surface owns one
    /// reference on each pointer and releases it. Zero for either means the platform made a view that is not WebView2:
    /// the surface treats the runtime as missing (DESIGN 2.7).
    /// </summary>
    void AttachNative(nint coreWebView2, nint controller);

    /// <summary>The platform view is gone (<c>NativeWebView.AdapterDestroyed</c>).</summary>
    void DetachNative();

    /// <summary>
    /// DESIGN 2.5: a one-shot capture for a gesture that is starting. Returns a fresh capture when it lands within the
    /// capture budget; otherwise the intent cache's newest frame and the capture still in flight (the host cross-fades
    /// to it). A view that is not live returns its last snapshot.
    /// </summary>
    Task<WebGestureFrame> CaptureForGestureAsync(CancellationToken cancellationToken);

    /// <summary>DESIGN 2.5 intent: the pointer is over a gutter, header or pane toggle, or a workspace chord is down.</summary>
    void NoteIntent();

    /// <summary>The web rectangle's size in DIPs and the device scale, after each layout the host applies.</summary>
    void SetBounds(double width, double height, double scale);

    /// <summary>Shows or hides the live web view (the controller's <c>IsVisible</c>). The host orders it against the snapshot image.</summary>
    void SetLiveVisible(bool visible);

    /// <summary>
    /// DESIGN 2.5 settle: waits for <paramref name="frames"/> frames from the page (a <c>requestAnimationFrame</c> ping),
    /// at most <paramref name="timeout"/>. True when the page answered in time.
    /// </summary>
    Task<bool> WaitForFramesAsync(int frames, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>The user showed or clicked a suspended or discarded view: make it live again (DESIGN 2.3).</summary>
    void Activate();

    /// <summary>A button on the failure card was pressed (DESIGN 2.7).</summary>
    void InvokeFailureAction(WebFailureAction action);
}
