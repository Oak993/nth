namespace Marrow.Core.Web;

/// <summary>Where a snapshot came from (DESIGN 2.5): a CDP capture, the WebView2 preview fallback, or a test fake.</summary>
public enum WebSnapshotSource
{
    /// <summary>CDP <c>Page.captureScreenshot</c> (png at device scale, <c>optimizeForSpeed</c>, the visible viewport).</summary>
    Cdp,

    /// <summary><c>CoreWebView2.CapturePreviewAsync</c>, the fallback when the CDP capture fails.</summary>
    Preview,

    /// <summary>A canned frame from <c>FakeWebEngine</c>.</summary>
    Fake,
}

/// <summary>
/// DESIGN 2.5: one frame of a web view, pixel-identical to the live page at capture time. The host shows it anchored
/// top-left at 1:1 device scale (never stretched); growing past it reveals <see cref="BackgroundArgb"/>, the page's own
/// background colour (CDP <c>getComputedStyle(document.body).backgroundColor</c>, read once per navigation), or, when
/// the page gave none, the snapshot's edge pixels (the host samples them).
/// </summary>
public sealed record WebSnapshot(
    byte[] Png,
    int PixelWidth,
    int PixelHeight,
    double Scale,
    DateTimeOffset CapturedAt,
    uint? BackgroundArgb,
    WebSnapshotSource Source)
{
    /// <summary>The frame's size in device-independent pixels at its own scale.</summary>
    public (double Width, double Height) DipSize => Scale > 0 ? (PixelWidth / Scale, PixelHeight / Scale) : (PixelWidth, PixelHeight);
}

/// <summary>
/// DESIGN 2.5, gesture start: the frame to show now, and, when the fresh capture missed the capture budget, the fresh
/// capture still in flight. The host shows <see cref="Snapshot"/> at once and cross-fades (80 ms) to the fresher frame
/// the moment it lands.
/// </summary>
public sealed record WebGestureFrame(WebSnapshot? Snapshot, Task<WebSnapshot?>? Fresher)
{
    /// <summary>True when <see cref="Snapshot"/> is a fresh capture taken for this gesture (no cross-fade follows).</summary>
    public bool IsFresh => Fresher is null;
}

/// <summary>DESIGN 2.3: whether a web view is a live page, a suspended one, or discarded to its snapshot and URL.</summary>
public enum WebLiveness
{
    /// <summary>Created, waiting for the platform view (<c>AdapterCreated</c>).</summary>
    Pending,

    /// <summary>A live page.</summary>
    Live,

    /// <summary>Over the live-view cap: suspended (<c>TrySuspendAsync</c>) and shown as its snapshot.</summary>
    Suspended,

    /// <summary>Not shown for 10 minutes: closed to its snapshot plus URL, restored when shown again.</summary>
    Discarded,

    /// <summary>Closed for good.</summary>
    Closed,
}
