using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.UI.Port;

namespace Marrow.UI.Web;

/// <summary>DESIGN 2.5: which layer a <see cref="WebViewHost"/> shows.</summary>
public enum WebViewHostState
{
    /// <summary>The live web view (its child HWND) is visible; the snapshot image is gone.</summary>
    Live,

    /// <summary>The snapshot image is on screen and the live view is hidden behind it.</summary>
    Snapshot,

    /// <summary>The final bounds are set and the host waits for the page's frames before the live view shows again.</summary>
    Settling,
}

/// <summary>
/// DESIGN 2.4, 2.5, 2.7 (ED-NEW-web-w7-3): wraps one web view. The live view is a child HWND that always draws above
/// Avalonia content, so every gesture, window transition, in-tree overlay, suspension or failure swaps it for a
/// pixel-identical snapshot first: the image goes on screen, and only once a composed frame carries it is the live view
/// hidden; on the way back the final bounds are set, the page answers two frames (or 120 ms pass), the live view shows,
/// and only then is the image removed. So no frame shows neither. The image is anchored top-left at the device scale it
/// was captured at and never stretches; growing reveals the page's own background colour. While the snapshot shows, the
/// hidden live view is resized at most every 100 ms. Failure cards (2.7) are native glass over the dimmed snapshot.
/// Every member runs on the UI thread.
/// </summary>
public sealed class WebViewHost : Panel
{
    /// <summary>Frames the page must answer before the live view shows again (DESIGN 2.5).</summary>
    public const int SettleFrames = 2;

    /// <summary>The settle wait's bound (DESIGN 2.5: 120 ms).</summary>
    public static readonly TimeSpan SettleTimeout = TimeSpan.FromMilliseconds(120);

    /// <summary>While the snapshot shows, the hidden live view takes a new size at most this often (DESIGN 2.5: 100 ms).</summary>
    public static readonly TimeSpan HeldResizeInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>The cross-fade to a fresh capture that lands after the gesture started (DESIGN 2.5: 80 ms).</summary>
    public static readonly TimeSpan CrossFadeDuration = TimeSpan.FromMilliseconds(80);

    /// <summary>The longest the host waits for a composed frame before it goes on (a window that is not rendering).</summary>
    public static readonly TimeSpan PresentTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The failure card's dim over the snapshot (DESIGN 2.7: "the pane keeps its last snapshot, dimmed").</summary>
    public const double FailureDimOpacity = 0.32;

    /// <summary>The failure card comes in from this scale with the pane spring (DESIGN 2.6 open: 0.96 to 1; none under Reduce Motion).</summary>
    public const double CardStartScale = 0.96;

    private static readonly List<WeakReference<WebViewHost>> Hosts = [];

    private readonly Func<IWebViewSurface, Control> liveFactory;
    private readonly TimeProvider time;
    private readonly Logger log = new(Logger.Subsystem, "web");
    private readonly Decorator liveSlot = new();
    private readonly Border backdrop = new() { IsHitTestVisible = false, IsVisible = false };
    private readonly Image image = NewImage();
    private readonly Image fresher = NewImage();
    private readonly Border dim = new() { IsHitTestVisible = false, IsVisible = false };
    private readonly Panel failureLayer = new() { IsVisible = false };
    private WebSnapshot? shownFrame;
    private Bitmap? shownBitmap;
    private WebFailureCard? shownCard;
    private WebLiveness seenLiveness;
    private int gestures;
    private WebSnapshot? gestureFrame;
    private int holds;
    private WebSnapshot? holdFrame;
    private bool running;
    private bool dirty;
    private TaskCompletionSource? idle;
    private Size? heldSize;
    private DateTimeOffset heldAt;
    private bool heldTimerQueued;
    private (double Width, double Height, double Scale)? reportedBounds;
    private readonly ScaleTransform cardScale = new(1, 1);
    private int cardMotion;
    private bool cardAppearPending;
    private double cardDimFrom = 1;
    private Action<TimeSpan>? cardTick;
    private IFrameSource? cardFrames;

    /// <param name="surface">The web view (the engine layer's <c>WebSurfaceView</c> in the app).</param>
    /// <param name="liveFactory">Makes the live content for the surface; the default is <see cref="CreateNativeView"/>
    /// (Avalonia <c>NativeWebView</c> in HWND mode). Tests pass a plain control.</param>
    /// <param name="time">The clock for the held resize, the cross-fade and the present bound.</param>
    public WebViewHost(IWebViewSurface surface, Func<IWebViewSurface, Control>? liveFactory = null, TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        Surface = surface;
        this.liveFactory = liveFactory ?? CreateNativeView;
        this.time = time ?? TimeProvider.System;
        seenLiveness = surface.Liveness;
        ClipToBounds = true; // the snapshot is clipped to the animating rectangle
        dim.Background = new SolidColorBrush(Colors.Black, FailureDimOpacity);
        fresher.Opacity = 0;
        Children.Add(liveSlot);
        Children.Add(backdrop);
        Children.Add(image);
        Children.Add(fresher);
        Children.Add(dim);
        Children.Add(failureLayer);
        surface.Changed += OnSurfaceChanged;
        surface.FocusExitRequested += OnFocusExit;
    }

    public IWebViewSurface Surface { get; }

    public WebViewHostState State { get; private set; }

    /// <summary>The live content (the platform web view), or null while the view is discarded, closed or failed to start.</summary>
    public Control? LiveContent => liveSlot.Child;

    /// <summary>True while the snapshot layer (image or background) is on screen.</summary>
    public bool IsShowingSnapshot => backdrop.IsVisible;

    /// <summary>The failure card on screen, or null.</summary>
    public WebFailureCard? ShownCard => shownCard;

    /// <summary>The frame on the snapshot image, or null.</summary>
    public WebSnapshot? ShownFrame => shownFrame;

    /// <summary>Diagnostics in order: "image shown", "presented", "live hidden", "settling", "bounds W x H", "frames",
    /// "live shown", "image removed", "card shown", "card hidden", "live created", "live dropped", "fresh frame",
    /// "focus out". Tests read the ordering from it.</summary>
    public event Action<string>? Step;

    internal Image SnapshotImage => image;

    internal Panel FailureLayer => failureLayer;

    internal Size? HeldLiveSize => heldSize;

    /// <summary>Hosts attached to <paramref name="topLevel"/> now (the window-transition path walks them).</summary>
    public static IReadOnlyList<WebViewHost> HostsIn(TopLevel topLevel)
    {
        ArgumentNullException.ThrowIfNull(topLevel);
        Hosts.RemoveAll(entry => !entry.TryGetTarget(out _));
        return [.. Hosts.Select(entry => entry.TryGetTarget(out var host) ? host : null)
            .OfType<WebViewHost>()
            .Where(host => TopLevel.GetTopLevel(host) == topLevel)];
    }

    /// <summary>
    /// DESIGN 2.1 window-level transitions: a window open, close, minimise, restore or theme change fades the whole HWND
    /// with layered alpha, which WebView2's GPU surface ignores. Every web view in the window shows its fresh snapshot
    /// before the fade starts (the returned task completes when they are all on screen) and goes live again when the
    /// returned lease is disposed after the transition ends.
    /// </summary>
    public static async Task<IDisposable> BeginWindowTransitionAsync(TopLevel topLevel, CancellationToken cancellationToken = default)
    {
        var hosts = HostsIn(topLevel);
        if (hosts.Count == 0) return new Lease(null);
        var leases = await Task.WhenAll(hosts.Select(host => host.BeginGestureAsync(cancellationToken)));
        return new Lease(() =>
        {
            foreach (var lease in leases) lease.Dispose();
        });
    }

    /// <summary>
    /// DESIGN 2.5 gesture start (pointer down on a gutter or header, a pane toggle, a workspace shortcut, a window
    /// transition, an in-tree overlay over the web rectangle): takes a one-shot capture of the live view and shows it. The
    /// task completes when the snapshot is on screen and the live view hidden, so the caller starts its spring then. A
    /// fresh capture that lands after the budget cross-fades in (80 ms). Dispose the lease when the gesture ends; the
    /// view goes live again when the last lease ends.
    /// </summary>
    public async Task<IDisposable> BeginGestureAsync(CancellationToken cancellationToken = default)
    {
        gestures++;
        var lease = new Lease(EndGesture);
        try
        {
            if (gestures == 1 && State != WebViewHostState.Live)
            {
                // Already on the snapshot (settling, suspended): the gesture keeps the frame on screen.
                gestureFrame = shownFrame;
            }
            else if (gestures == 1 && Surface.Liveness == WebLiveness.Live && Surface.Failure is null && liveSlot.Child is not null && IsAttached)
            {
                var frame = await Surface.CaptureForGestureAsync(cancellationToken);
                if (frame.Snapshot is null) log.Info("web: no frame for the gesture; the view stays live");
                else if (gestures > 0)
                {
                    gestureFrame = frame.Snapshot;
                    if (frame.Fresher is { } later) _ = CrossFadeWhenLandsAsync(frame.Snapshot, later);
                }
            }

            await Kick();
            return lease;
        }
        catch (OperationCanceledException)
        {
            lease.Dispose();
            throw;
        }
    }

    /// <summary>
    /// PORT-ONLY (web surfaces W19, DESIGN.md 7.6 "Scrolling starts: snapshot in the same frame"; ED-NEW-web-w19-4): holds the
    /// snapshot up without capturing first. The newest frame there is (the one on screen, else the intent cache's) goes up at
    /// once and the live view hides once a frame carries it; disposing the last hold settles back to live. A thread that
    /// scrolls holds every inline widget this way, because a capture would land a frame or two after the scroll began.
    /// </summary>
    public IDisposable HoldSnapshot()
    {
        holds++;
        if (holds == 1) holdFrame = shownFrame ?? Surface.LastSnapshot;
        _ = Kick();
        return new Lease(EndHold);
    }

    /// <summary>True while a <see cref="HoldSnapshot"/> lease is out.</summary>
    public bool IsHeld => holds > 0;

    private void EndHold()
    {
        if (holds == 0) return;
        holds--;
        if (holds > 0) return;
        holdFrame = null;
        _ = Kick();
    }

    /// <summary>Completes when no swap is in progress.</summary>
    public Task WhenIdleAsync() => running && idle is { } pending ? pending.Task : Task.CompletedTask;

    /// <summary>
    /// The live content in the app: an Avalonia <c>NativeWebView</c> in HWND mode (DESIGN 2.1). Its environment request
    /// applies the surface's creation options (profile, InPrivate, user data folder, language, browser arguments,
    /// DevTools) and hands over the engine's shared environment before the controller is created; <c>AdapterCreated</c>
    /// passes the raw <c>ICoreWebView2</c> and controller to the engine.
    /// </summary>
    public static Control CreateNativeView(IWebViewSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        var view = new NativeWebView { Focusable = true };
        view.EnvironmentRequested += (_, e) => OnEnvironmentRequested(surface, e);
        view.AdapterCreated += (_, e) => OnAdapterCreated(surface, e);
        view.AdapterDestroyed += (_, _) => surface.DetachNative();
        return view;
    }

    private static async void OnEnvironmentRequested(IWebViewSurface surface, WebViewEnvironmentRequestedEventArgs e)
    {
        var creation = surface.Creation;
        e.EnableDevTools = creation.EnableDevTools;
        if (e is not WindowsWebView2EnvironmentRequestedEventArgs windows) return;
        windows.ExperimentalOffscreen = false; // HWND mode (DESIGN 2.1: not the offscreen comp adapter)
        windows.ProfileName = creation.ProfileName;
        windows.IsInPrivateModeEnabled = creation.IsInPrivate;
        windows.UserDataFolder = creation.UserDataFolder;
        windows.Language = creation.Language;
        windows.AdditionalBrowserArguments = creation.AdditionalBrowserArguments;
        using var deferral = e.GetDeferral();
        try
        {
            var environment = await surface.AcquireEnvironmentAsync(CancellationToken.None);
            if (environment != 0) windows.ExplicitEnvironment = environment;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or IOException or UnauthorizedAccessException)
        {
            new Logger(Logger.Subsystem, "web").Error($"web: shared environment unavailable, the view makes its own: {ex.Message}");
        }
    }

    private static void OnAdapterCreated(IWebViewSurface surface, WebViewAdapterEventArgs e)
    {
        if (e.TryGetPlatformHandle() is IWindowsWebView2PlatformHandle handle)
        {
            // Each getter hands out a new reference (ED-NEW-web-w7-1); read once, the surface owns and releases them.
            var core = handle.CoreWebView2;
            var controller = handle.CoreWebView2Controller;
            surface.AttachNative(core, controller);
            return;
        }

        surface.AttachNative(0, 0); // not WebView2: the surface shows the runtime card
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        liveSlot.Measure(heldSize ?? size);
        backdrop.Measure(size);
        image.Measure(Size.Infinity);
        fresher.Measure(Size.Infinity);
        dim.Measure(size);
        failureLayer.Measure(size);
        return size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var live = LiveSize(finalSize);
        liveSlot.Arrange(new Rect(live));
        backdrop.Arrange(new Rect(finalSize));
        image.Arrange(new Rect(0, 0, ImageWidth(image), ImageHeight(image)));
        fresher.Arrange(new Rect(0, 0, ImageWidth(fresher), ImageHeight(fresher)));
        dim.Arrange(new Rect(finalSize));
        failureLayer.Arrange(new Rect(finalSize));
        ReportBounds(live);
        return finalSize;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Hosts.RemoveAll(entry => !entry.TryGetTarget(out var host) || host == this);
        Hosts.Add(new WeakReference<WebViewHost>(this));
        _ = Kick();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Hosts.RemoveAll(entry => !entry.TryGetTarget(out var host) || host == this);
        // A view that leaves the tree leaves no frame request behind; the card is at rest when it comes back.
        if (cardTick is not null || cardAppearPending)
        {
            StopCardMotion();
            PoseCard(1, scale: false);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        // DESIGN 2.3: clicking a suspended or discarded view makes it live again.
        if (Surface.Liveness is WebLiveness.Suspended or WebLiveness.Discarded && Surface.Failure is null) Surface.Activate();
    }

    private bool IsAttached => TopLevel.GetTopLevel(this) is not null;

    private void OnSurfaceChanged() => _ = Kick();

    private void EndGesture()
    {
        if (gestures == 0) return;
        gestures--;
        if (gestures > 0) return;
        gestureFrame = null;
        _ = Kick();
    }

    // The swap loop: one transition at a time, re-evaluated until nothing changed meanwhile.
    private Task Kick()
    {
        dirty = true;
        if (running) return idle!.Task;
        running = true;
        idle = new TaskCompletionSource();
        var done = idle.Task;
        _ = RunAsync();
        return done;
    }

    private async Task RunAsync()
    {
        try
        {
            while (dirty)
            {
                dirty = false;
                await StepAsync();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or OperationCanceledException)
        {
            log.Error($"web: host swap failed: {ex.Message}");
        }
        finally
        {
            running = false;
            var pending = idle;
            idle = null;
            pending?.TrySetResult();
        }
    }

    private async Task StepAsync()
    {
        if (!IsAttached) return;
        SyncCard();
        try
        {
            var wants = WantsSnapshot(out var frame);
            switch (State)
            {
                case WebViewHostState.Live when wants:
                    await EnterSnapshotAsync(frame);
                    break;
                case WebViewHostState.Snapshot when wants:
                    if (frame is not null && !ReferenceEquals(frame, shownFrame)) await ShowFrameAsync(frame);
                    break;
                case WebViewHostState.Snapshot:
                    await SettleAsync();
                    break;
                case WebViewHostState.Live when liveSlot.Child is not null && Surface.Liveness == WebLiveness.Live && !Surface.IsLiveVisible:
                    // The view was hidden while the host was live (a tab shown again): wait for the page, then show it.
                    await SettleAsync();
                    break;
            }
        }
        finally
        {
            // The card's motion starts once the snapshot is on screen and the live view hidden: never under the HWND.
            if (cardAppearPending) StartCardMotion();
        }

        SyncLiveContent();
    }

    private bool WantsSnapshot(out WebSnapshot? frame)
    {
        frame = gestureFrame ?? holdFrame ?? Surface.LastSnapshot;
        if (gestures > 0 && gestureFrame is not null) return true;
        if (holds > 0) return true; // W19: held, with the newest frame there is (or the page's own colour)
        if (Surface.Failure is not null) return true;
        return Surface.Liveness switch
        {
            WebLiveness.Suspended or WebLiveness.Discarded or WebLiveness.Closed => true,
            // A view being recreated (restored from a discard, or after "Restart Visus") keeps its last frame until live.
            WebLiveness.Pending => (frame is not null && seenLiveness != WebLiveness.Pending) || State != WebViewHostState.Live,
            _ => false,
        };
    }

    private async Task EnterSnapshotAsync(WebSnapshot? frame)
    {
        heldSize = Bounds.Size;
        heldAt = time.GetUtcNow();
        await ShowFrameAsync(frame);
        backdrop.IsVisible = true;
        State = WebViewHostState.Snapshot;
        Raise("image shown");
        await PresentAsync();
        Raise("presented");
        Surface.SetLiveVisible(false);
        Raise("live hidden");
    }

    private async Task SettleAsync()
    {
        State = WebViewHostState.Settling;
        Raise("settling");
        heldSize = null;
        InvalidateMeasure();
        InvalidateArrange();
        await PresentAsync(); // the final bounds are laid out and the HWND moved before the page is asked for frames
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        ReportBounds(Bounds.Size, scale, force: true);
        var answered = await Surface.WaitForFramesAsync(SettleFrames, SettleTimeout, CancellationToken.None);
        Raise(answered ? "frames" : "frames timed out");
        if (WantsSnapshot(out _))
        {
            // A new gesture or a failure arrived meanwhile: stay on the snapshot.
            heldSize = Bounds.Size;
            heldAt = time.GetUtcNow();
            State = WebViewHostState.Snapshot;
            return;
        }

        if (liveSlot.Child is null)
        {
            ClearImage();
            State = WebViewHostState.Live;
            return;
        }

        Surface.SetLiveVisible(true);
        Raise("live shown");
        await PresentAsync();
        ClearImage();
        State = WebViewHostState.Live;
        Raise("image removed");
    }

    private async Task ShowFrameAsync(WebSnapshot? frame)
    {
        if (frame is null)
        {
            SetBackdrop(null, null);
            return;
        }

        if (ReferenceEquals(frame, shownFrame)) return;
        var bitmap = await DecodeAsync(frame);
        if (bitmap is null) return;
        var previous = shownBitmap;
        shownFrame = frame;
        shownBitmap = bitmap;
        Place(image, frame, bitmap);
        image.IsVisible = true;
        SetBackdrop(frame, bitmap);
        if (previous is not null && !ReferenceEquals(previous, bitmap)) Dispatcher.UIThread.Post(previous.Dispose, DispatcherPriority.Background);
    }

    private async Task CrossFadeWhenLandsAsync(WebSnapshot started, Task<WebSnapshot?> later)
    {
        WebSnapshot? fresh;
        try
        {
            fresh = await later;
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException or COMException)
        {
            return;
        }

        if (fresh is null || gestures == 0 || !ReferenceEquals(gestureFrame, started)) return;
        gestureFrame = fresh;
        if (State != WebViewHostState.Snapshot || !ReferenceEquals(shownFrame, started)) return;
        var bitmap = await DecodeAsync(fresh);
        if (bitmap is null || State != WebViewHostState.Snapshot || !ReferenceEquals(shownFrame, started)) return;
        if (!EnvironmentValues.GetAccessibilityReduceMotion(this))
        {
            Place(fresher, fresh, bitmap);
            fresher.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = CrossFadeDuration }];
            fresher.IsVisible = true;
            fresher.Opacity = 1;
            await Task.Delay(CrossFadeDuration, time);
            fresher.Transitions = null;
            fresher.Opacity = 0;
            fresher.IsVisible = false;
            fresher.Source = null;
            if (State != WebViewHostState.Snapshot || !ReferenceEquals(shownFrame, started)) return;
        }

        var previous = shownBitmap;
        shownFrame = fresh;
        shownBitmap = bitmap;
        Place(image, fresh, bitmap);
        SetBackdrop(fresh, bitmap);
        previous?.Dispose();
        Raise("fresh frame");
    }

    private void ClearImage()
    {
        image.IsVisible = false;
        image.Source = null;
        fresher.IsVisible = false;
        fresher.Source = null;
        backdrop.IsVisible = false;
        shownBitmap?.Dispose();
        shownBitmap = null;
        shownFrame = null;
    }

    private void SetBackdrop(WebSnapshot? frame, Bitmap? bitmap)
    {
        // DESIGN 2.5: growing reveals the page's own background colour, read once per navigation, else the frame's edge.
        var argb = frame?.BackgroundArgb ?? (bitmap is null ? null : EdgeColor(bitmap));
        backdrop.Background = argb is { } value ? new SolidColorBrush(Color.FromUInt32(value)) : null;
        backdrop.IsVisible = true;
    }

    private static void Place(Image target, WebSnapshot frame, Bitmap bitmap)
    {
        // Anchored top-left at the device scale it was captured at: its DIP size is fixed, so it never stretches.
        var size = frame.DipSize;
        target.Source = bitmap;
        target.Width = size.Width;
        target.Height = size.Height;
    }

    private static Image NewImage()
    {
        var image = new Image
        {
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false,
            IsVisible = false,
        };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        return image;
    }

    private static double ImageWidth(Image target) => double.IsNaN(target.Width) ? 0 : target.Width;

    private static double ImageHeight(Image target) => double.IsNaN(target.Height) ? 0 : target.Height;

    private async Task<Bitmap?> DecodeAsync(WebSnapshot frame)
    {
        try
        {
            // Off the UI thread: a 4K frame is tens of milliseconds to decode.
            return await Task.Run(() =>
            {
                using var stream = new MemoryStream(frame.Png, writable: false);
                return new Bitmap(stream);
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException or IOException)
        {
            log.Error($"web: snapshot could not be decoded: {ex.Message}");
            return null;
        }
    }

    /// <summary>The mean colour of the frame's right column and bottom row, as ARGB, or null when unreadable.</summary>
    internal static uint? EdgeColor(Bitmap bitmap)
    {
        if (bitmap.Format is not { } format || (format != PixelFormats.Bgra8888 && format != PixelFormats.Rgba8888)) return null;
        var width = bitmap.PixelSize.Width;
        var height = bitmap.PixelSize.Height;
        if (width <= 0 || height <= 0) return null;
        var column = new byte[height * 4];
        var row = new byte[width * 4];
        var columnHandle = GCHandle.Alloc(column, GCHandleType.Pinned);
        var rowHandle = GCHandle.Alloc(row, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(width - 1, 0, 1, height), columnHandle.AddrOfPinnedObject(), column.Length, 4);
            bitmap.CopyPixels(new PixelRect(0, height - 1, width, 1), rowHandle.AddrOfPinnedObject(), row.Length, width * 4);
        }
        finally
        {
            columnHandle.Free();
            rowHandle.Free();
        }

        long r = 0, g = 0, b = 0, a = 0;
        var count = 0;
        foreach (var pixels in new[] { column, row })
        {
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                var (red, blue) = format == PixelFormats.Bgra8888 ? (pixels[i + 2], pixels[i]) : (pixels[i], pixels[i + 2]);
                r += red;
                g += pixels[i + 1];
                b += blue;
                a += pixels[i + 3];
                count++;
            }
        }

        if (count == 0) return null;
        var alpha = (uint)(a / count);
        if (alpha == 0) return null;
        // Premultiplied (Skia's default for decoded PNGs): un-premultiply the mean.
        uint Channel(long sum) => (uint)Math.Clamp(Math.Round(sum / (double)count * 255 / alpha), 0, 255);
        return (alpha << 24) | (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
    }

    private Size LiveSize(Size finalSize)
    {
        if (State != WebViewHostState.Snapshot || liveSlot.Child is null)
        {
            heldSize = null;
            return finalSize;
        }

        var now = time.GetUtcNow();
        if (heldSize is not { } held || now - heldAt >= HeldResizeInterval)
        {
            heldSize = finalSize;
            heldAt = now;
            return finalSize;
        }

        if (held != finalSize && !heldTimerQueued)
        {
            heldTimerQueued = true;
            var wait = HeldResizeInterval - (now - heldAt);
            DispatcherTimer.RunOnce(() =>
            {
                heldTimerQueued = false;
                InvalidateMeasure();
                InvalidateArrange();
            }, wait < TimeSpan.FromMilliseconds(1) ? TimeSpan.FromMilliseconds(1) : wait);
        }

        return held;
    }

    private void ReportBounds(Size live) => ReportBounds(live, TopLevel.GetTopLevel(this)?.RenderScaling ?? 1, force: false);

    private void ReportBounds(Size live, double scale, bool force)
    {
        var bounds = (live.Width, live.Height, scale);
        if (!force && reportedBounds == bounds) return;
        reportedBounds = bounds;
        Surface.SetBounds(live.Width, live.Height, scale);
        Raise(string.Create(CultureInfo.InvariantCulture, $"bounds {live.Width:0.##} x {live.Height:0.##}"));
    }

    private async Task PresentAsync()
    {
        if (ElementComposition.GetElementVisual(this)?.Compositor is not { } compositor) return;
        var rendered = compositor.RequestCompositionBatchCommitAsync().Rendered;
        using var stop = new CancellationTokenSource();
        var bound = Task.Delay(PresentTimeout, time, stop.Token);
        if (await Task.WhenAny(rendered, bound) == bound) log.Info("web: no composed frame within 100 ms; going on");
        stop.Cancel();
    }

    private void SyncLiveContent()
    {
        var liveness = Surface.Liveness;
        var runtimeMissing = Surface.Failure is { } runtimeCard && runtimeCard.Kind.IsRuntime();
        var recreated = liveness == WebLiveness.Pending && seenLiveness != WebLiveness.Pending;
        seenLiveness = liveness;
        var keep = liveness is WebLiveness.Pending or WebLiveness.Live or WebLiveness.Suspended && !runtimeMissing;
        // Never drop the live content while it is what the user sees; the swap above put the snapshot up first.
        if (liveSlot.Child is not null && (!keep || recreated) && State != WebViewHostState.Live)
        {
            liveSlot.Child = null;
            reportedBounds = null;
            Raise("live dropped");
        }

        if (keep && liveSlot.Child is null && IsAttached)
        {
            liveSlot.Child = liveFactory(Surface);
            Raise("live created");
            dirty = true;
        }
    }

    private void SyncCard()
    {
        var card = Surface.Failure is { ShowsCard: true } failure ? failure : null;
        if (Equals(card, shownCard)) return;
        shownCard = card;
        StopCardMotion();
        failureLayer.Children.Clear();
        failureLayer.IsVisible = card is not null;
        if (card is null)
        {
            dim.IsVisible = false;
            dim.Opacity = 1;
            Raise("card hidden");
            return;
        }

        // A dim already on screen (one card replacing another) carries on from where it is; a new one starts clear.
        cardDimFrom = dim.IsVisible ? dim.Opacity : 0;
        dim.IsVisible = card.Dimmed;
        failureLayer.Children.Add(CardView(card));
        if (FrameClock.IsBound)
        {
            cardAppearPending = true;
            PoseCard(0, scale: !ReducesMotion());
        }
        else
        {
            PoseCard(1, scale: false);
        }

        Raise("card shown");
    }

    private bool ReducesMotion() =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    // DESIGN 2.6 open, for the failure card: it fades in and scales from 0.96 to 1 with the pane spring (Reduce Motion:
    // 0.18 s ease-out, no scale) while the dim fades in under it. Opacity and a render transform only, on the frame clock;
    // a new card or the host leaving the tree interrupts it.
    private void StartCardMotion()
    {
        cardAppearPending = false;
        if (failureLayer.Children.Count == 0) return;
        var reduce = ReducesMotion();
        var animation = reduce ? Marrow.UI.Port.Animation.EaseOut(duration: 0.18) : Chrome.Chrome.PanelSlide;
        var version = ++cardMotion;
        var frames = FrameClock.Source.Current;
        var start = SwiftClock.Now;
        Action<TimeSpan>? tick = null;
        tick = _ =>
        {
            if (version != cardMotion) return;
            var seconds = (SwiftClock.Now - start).TotalSeconds;
            var done = seconds >= animation.Duration;
            PoseCard(done ? 1 : animation.Progress(seconds), scale: !reduce);
            if (done)
            {
                cardTick = null;
                cardFrames = null;
                return;
            }

            frames.RequestFrame(tick!);
        };
        cardTick = tick;
        cardFrames = frames;
        PoseCard(0, scale: !reduce);
        frames.RequestFrame(tick);
    }

    private void StopCardMotion()
    {
        cardMotion++;
        cardAppearPending = false;
        if (cardTick is { } tick) cardFrames?.CancelFrame(tick);
        cardTick = null;
        cardFrames = null;
    }

    private void PoseCard(double progress, bool scale)
    {
        var visible = Math.Clamp(progress, 0, 1);
        dim.Opacity = cardDimFrom + (1 - cardDimFrom) * visible;
        if (failureLayer.Children.Count == 0) return;
        var card = failureLayer.Children[0];
        card.Opacity = visible;
        if (progress >= 1 || !scale)
        {
            card.RenderTransform = null;
            return;
        }

        var size = CardStartScale + (1 - CardStartScale) * progress;
        cardScale.ScaleX = size;
        cardScale.ScaleY = size;
        card.RenderTransformOrigin = RelativePoint.Center;
        card.RenderTransform = cardScale;
    }

    private Control CardView(WebFailureCard card)
    {
        var title = new TextBlock { Text = card.Title, TextWrapping = TextWrapping.Wrap };
        SwiftFont.System(15, SwiftFont.Weight.Semibold).Apply(title);
        title.Foreground = Chrome.Chrome.PrimaryText.Brush(title);
        var body = new TextBlock { Text = card.Body, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        SwiftFont.System(13).Apply(body);
        body.Foreground = Chrome.Chrome.SecondaryText.Brush(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var button in card.Buttons)
        {
            var action = button.Action;
            buttons.Children.Add(new Chrome.ChromeTextButton(Symbol(action), button.Title, button.Title, () => Surface.InvokeFailureAction(action)));
        }

        // W23: the installing card has no buttons; an empty row would only add 14 DIP of blank under the text.
        var stack = new StackPanel { Orientation = Orientation.Vertical, Children = { title, body } };
        if (card.Buttons.Count > 0) stack.Children.Add(buttons);
        var surface = new Border { Padding = new Thickness(20, 18), MaxWidth = 380, Child = stack }
            .RoundedRectangle(16, RoundedCornerStyle.Continuous);
        SwiftGlass.SetEffect(surface, GlassEffect.Regular(GlassShape.RoundedRectangle(16, RoundedCornerStyle.Continuous)));
        surface.HorizontalAlignment = HorizontalAlignment.Center;
        surface.VerticalAlignment = VerticalAlignment.Center;
        surface.Margin = new Thickness(16);
        Avalonia.Automation.AutomationProperties.SetName(surface, card.Title);
        return surface;
    }

    private static string Symbol(WebFailureAction action) => action switch
    {
        WebFailureAction.Reload or WebFailureAction.RestartVisus => "arrow.clockwise",
        WebFailureAction.CloseTab => "xmark",
        WebFailureAction.Wait => "hourglass",
        WebFailureAction.InstallRuntime or WebFailureAction.UpdateRuntime => "arrow.down.to.line",
        WebFailureAction.OpenRuntimePage => "arrow.up.right",
        _ => "arrow.clockwise",
    };

    private void OnFocusExit(bool forward)
    {
        // DESIGN 10: Tab past the page's last element (Shift+Tab past its first) moves on in nth's own order, from the
        // web view's place in it.
        if (TopLevel.GetTopLevel(this)?.FocusManager is not { } focus) return;
        if (liveSlot.Child is InputElement live && !IsInside(focus.GetFocusedElement())) live.Focus(NavigationMethod.Tab);
        if (focus.TryMoveFocus(forward ? NavigationDirection.Next : NavigationDirection.Previous, new FindNextElementOptions())) Raise("focus out");
    }

    private bool IsInside(IInputElement? element) =>
        element is Visual visual && (visual == this || this.IsVisualAncestorOf(visual));

    private void Raise(string step) => Step?.Invoke(step);

    private sealed class Lease(Action? release) : IDisposable
    {
        private Action? release = release;

        public void Dispose()
        {
            var action = release;
            release = null;
            action?.Invoke();
        }
    }
}
