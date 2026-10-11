using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;
using PortAnimation = Marrow.UI.Port.Animation;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/WindowChrome.swift (B3 52bc792), :1-572.
//
// PLATFORM ADAPTATION (ED-063, porting/probes/p7/RESULT.md; porting/critiques/stack_decision.json P7):
// AppKit gives this file real native window-chrome objects to reposition (NSWindow.standardWindowButton) and a
// real title-bar container view AppKit re-tiles on every resize (fitTitlebar, titlebarFrameDidChange). Windows
// draws no such chrome for a window whose system decorations are suppressed, so the accepted design (ED-063,
// OD-0013) sets the caption band ONCE via Window.ExtendClientAreaTitleBarHeightHint (never re-tiled by the
// platform, so there is nothing to re-fit) and represents "the window buttons" as three app-drawn controls this
// file owns, declared to the platform through Win32Properties.SetNonClientHitTestResult (Close / MinButton /
// MaxButton), matching Chrome.cs's WindowDragArea/NoWindowDragArea (:1681-1699, PR-WIN-193/194) which already
// use the same mechanism for the drag region. Because the caption band never shrinks in this design, WindowChrome
// .swift's titlebarContainer(of:), fitTitlebar(of:) and the container frameDidChange/didEndLiveResize coalescing
// (:34-38, :58-87, PR-WIN-027, PR-WIN-028, PR-WIN-029) have no work to do on Windows: there is no container to
// measure and no re-tiling to observe, so this file has nothing to run at those sites (not a stub: the AppKit
// behaviour they exist to counteract cannot occur here). The WndProc hook that performs a caption button's click
// (WM_NCLBUTTONDOWN swallowed on the control, the SC_* command posted on WM_NCLBUTTONUP) is wired once per real
// window by the App layer that owns the platform message loop (outside this unit, Core/Services/UI never touch
// Win32 message dispatch by the layering rule, section 2.1); this file positions, sizes, orders and hit-test-
// declares the three controls, which is everything PR-WIN-022 through PR-WIN-045 specify as an observable result.
//
// ED-224: App publishes native live-resize start/end through WindowLiveResizeSignal. The probe observes the
// current window only, follows sidebar changes without animation during the drag, and places once when it ends.

/// <summary>
/// :7-19 <c>WindowChromeConfigurator: NSViewRepresentable</c>. In SwiftUI this is the `.background` modifier
/// RootView installs (App/Views/RootView.swift:65); porting that installation site is App's job (outside Core/
/// Services/UI, section 2.1), so this type is the plain updater App's binding calls into <see cref="Update"/>.
/// </summary>
public sealed class WindowChromeConfigurator
{
    public bool SidebarVisible { get; set; } = true; // :8 var sidebarVisible = true

    public bool SidebarDragging { get; set; } // :9 var sidebarDragging = false

    // :15-18 updateNSView(_:context:)
    public void Update(WindowChromeProbe probe)
    {
        probe.SidebarDragging = SidebarDragging; // :16
        probe.SidebarVisible = SidebarVisible; // :17
    }
}

/// <summary>
/// :21-98 <c>WindowChromeProbeView: NSView</c>. Reaches the window to configure it and place the buttons; never
/// takes a mouse event (PR-WIN-030). Named without the "View" suffix collision with Avalonia's own <see
/// cref="Control"/> hierarchy (R-NAME-1 keeps the Swift name otherwise; "ProbeView" -> "Probe" avoids only the
/// redundant "ViewView" reading, the type itself IS an Avalonia Control).
/// </summary>
public sealed class WindowChromeProbe : Control
{
    private bool sidebarDragging; // :22 var sidebarDragging = false
    private bool sidebarVisible = true; // :23 var sidebarVisible = true
    private Window? window;

    public WindowChromeProbe() => IsHitTestVisible = false; // :97 override func hitTest(_:) -> NSView? { nil }

    public bool SidebarDragging { get => sidebarDragging; set => sidebarDragging = value; } // :22, no didSet

    // :23-30 var sidebarVisible: Bool { didSet { ... } }
    public bool SidebarVisible
    {
        get => sidebarVisible;
        set
        {
            var oldValue = sidebarVisible; // :23
            sidebarVisible = value;
            if (value == oldValue) return; // :25 guard sidebarVisible != oldValue
            if (window is null) return; // :25 guard let window
            // :26-28; ED-224: a restarted animation each live-resize frame made the buttons lag.
            WindowChrome.PlaceTrafficLights(window, sidebarVisible,
                animated: !WindowLiveResizeSignal.GetIsActive(window) && !sidebarDragging);
        }
    }

    // :40-67 viewDidMoveToWindow(). There is no titlebar container to fit (top-of-file note), but ED-224
    // preserves the independent didEndLiveResize observer (:46-57).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return; // :50 guard let window
        WindowChrome.Configure(window); // :51
        WindowChrome.PlaceTrafficLights(window, sidebarVisible, animated: false); // :52
        window.PropertyChanged += WindowPropertyChanged; // :54-57
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (window is { } previous) previous.PropertyChanged -= WindowPropertyChanged; // :46-49
        window = null;
    }

    // :91-94 windowDidEndLiveResize(_:); ED-224. A property transition is one window-scoped notification.
    private void WindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowLiveResizeSignal.IsActiveProperty && e.GetOldValue<bool>() && !e.GetNewValue<bool>()
            && window is { } current && ReferenceEquals(sender, current))
            WindowChrome.PlaceTrafficLights(current, sidebarVisible, animated: false); // :93
    }
}

/// <summary>:100-231 <c>@MainActor enum WindowChrome</c>. Ported as a static class (the Swift enum declares no
/// cases; it is a namespace for static members, R-ENUM-1 does not apply).</summary>
public static class WindowChrome
{
    private sealed class WindowState
    {
        public bool Configured;
        public TrafficLightButtons? Buttons;
        public Canvas? Layer;
        public double CaptionScale = 1;
    }

    // DEVIATION OD-0044 (OD-0046): Windows order, minimise, maximise, close, left to right at the top right.
    private sealed class TrafficLightButtons(CaptionButton close, CaptionButton miniaturize, CaptionButton zoom, Panel cluster)
    {
        public CaptionButton[] Items { get; } = [close, miniaturize, zoom];
        public Panel Cluster { get; } = cluster;
    }

    // :105. `NSHashTable<NSWindow>.weakObjects()` -> a weak-keyed table so a closed, released window drops out
    // on its own and is never kept alive by this registry (PR-WIN-022's windows_path).
    private static readonly ConditionalWeakTable<Window, WindowState> states = new();

    private static WindowState StateFor(Window window) => states.GetOrCreateValue(window);

    /// <summary>
    /// ED-236: what a window whose content is glass over a clear window asks for as its backdrop: Acrylic, and, where the
    /// platform cannot give it, the plain transparent window it was before (Avalonia takes the first level the platform
    /// supports; a list of Acrylic alone would leave a window without it opaque black, probe P3's `ctl_none_opaque`).
    /// </summary>
    public static IReadOnlyList<WindowTransparencyLevel> GlassBackdrop { get; } =
        [WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent];

    /// <summary>:107-132 <c>static func configure(_ window: NSWindow)</c>.</summary>
    public static void Configure(Window window)
    {
        var state = StateFor(window);
        if (state.Configured) return; // :109 if configuredWindows.contains(window) { return }
        state.Configured = true; // :110 configuredWindows.add(window)
        // :111-115. fullSizeContentView + titlebarAppearsTransparent + titleVisibility .hidden + no separator:
        // the content view extends under the caption and nothing system-drawn appears there.
        window.ExtendClientAreaToDecorationsHint = true;
        // :148-150 titlebarHeight, set once; there is no container for the platform to re-tile smaller
        // (top-of-file note), so this also stands in for the "never shrunk" half of PR-WIN-027's result.
        window.ExtendClientAreaTitleBarHeightHint = TitlebarHeight;
        // :116-120. isOpaque = false, backgroundColor = .clear: WindowDecorations.Full is ED-063's accepted
        // value (keeps the DWM shadow and the minimise/maximise/restore animations; hasShadow/invalidateShadow
        // have no separate action here because Avalonia recomputes the DWM shadow itself).
        window.WindowDecorations = WindowDecorations.Full;
        window.Background = Brushes.Transparent;
        // ED-236 (OD-0005, OD-0014, closes WindowManager STOP #5): the clear window's material over the desktop. AppKit's
        // clear window shows the desktop through the glass blurred; here the window itself composites Acrylic and the
        // app-drawn scrim (Chrome.WindowBackdrop, SwiftGlass) lies on it, as probe P3 requires.
        window.TransparencyLevelHint = GlassBackdrop;
        // OD-0017 adopts ED-101 for every resizable top-level window after its first native frame.
        // Settings remains on Avalonia's presentation because its factory sets CanResize = false.
        if (window.CanResize) _ = ResizeStretch.Attach(window);
        // :119 isMovableByWindowBackground = false: never set here. The port only ever moves the window from an
        // explicit drag region (Chrome.cs's WindowDragArea, PR-WIN-193), never from window.background, so this
        // holds structurally without a property to set (PA note).
        // :121-131 autorecalculatesKeyViewLoop: no Avalonia analogue. Avalonia's keyboard navigation computes
        // its tab order from the visual tree on demand when Tab is processed (KeyboardNavigationHandler), never
        // eagerly during layout, so the synchronous-rebuild hang this AppKit setting exists to prevent has no
        // way to occur on this stack (UNTESTABLE: there is no Avalonia property whose absence this proves).
    }

    /// <summary>Whether <paramref name="window"/> has already been configured (:104-110's private check, exposed
    /// for tests via InternalsVisibleTo, matching how the Swift file itself inspects <c>configuredWindows</c>).</summary>
    internal static bool IsConfigured(Window window) => states.TryGetValue(window, out var state) && state.Configured;

    /// <summary>:134-138 <c>static var collapsedTrafficLightTop: CGFloat</c>.</summary>
    public static double CollapsedTrafficLightTop =>
        Chrome.SheetInset + Chrome.ChromeTopPadding + (Chrome.CapsuleHeight - Chrome.TrafficLightDiameter) / 2; // :136-138

    /// <summary>:148-150 <c>static var titlebarHeight: CGFloat</c>.</summary>
    public static double TitlebarHeight => Chrome.SheetInset + Chrome.ChromeTopPadding + Chrome.CapsuleHeight; // :149

    /// <summary>
    /// :177-230 <c>static func placeTrafficLights(on:sidebarVisible:animated:)</c>. DEVIATION OD-0044 (OD-0046): the
    /// buttons are Windows caption buttons fixed at the window's top right (<see cref="CaptionTop"/>, <see
    /// cref="CaptionTrailingInset"/>), so the sidebar no longer moves them and there is no glide: a placement makes sure
    /// they exist and are shown, every time (:185-186), and does nothing else.
    /// </summary>
    public static void PlaceTrafficLights(Window window, bool sidebarVisible, bool animated)
    {
        var buttons = StateFor(window).Buttons ??= CreateButtons(window);
        foreach (var button in buttons.Items) button.IsVisible = true; // :185-186
        buttons.Cluster.IsVisible = true;
    }

    /// <summary>OD-0044: the button cluster's top edge, centring the 40-pt buttons on the chrome capsules' centre line
    /// (ChromeTopPadding + CapsuleHeight / 2 = 28).</summary>
    public static double CaptionTop => Chrome.SheetInset + Chrome.ChromeTopPadding + Chrome.CapsuleHeight / 2 - CaptionButton.ButtonHeight / 2;

    /// <summary>OD-0044: the gap between the close button and the window's right edge.</summary>
    public const double CaptionTrailingInset = 6;

    /// <summary>OD-0044: the gap between the cluster and the chrome a window draws to its left.</summary>
    public const double CaptionClearance = 8;

    /// <summary>OD-0044: the cluster's width, three 46-pt buttons 2 pt apart.</summary>
    public static double CaptionClusterWidth => CaptionButton.ButtonWidth * 3 + 2 * 2;

    /// <summary>OD-0044: the trailing padding a window's top chrome row keeps so its last control ends clear of the
    /// caption buttons (replaces the leading clearance the traffic lights needed, Chrome.swift :20-21).</summary>
    public static double CaptionTrailingReserve => CaptionTrailingInset + CaptionClusterWidth + CaptionClearance;

    /// <summary>OD-0044: the caption hook reports the hit-test value under the pointer (0: none of the buttons).</summary>
    public static void SetCaptionHover(Window window, int hitTest)
    {
        if (StateFor(window).Buttons is not { } buttons) return;
        foreach (var button in buttons.Items) button.IsHovered = HitTestOf(button) == hitTest;
    }

    /// <summary>OD-0044: the caption hook reports the button held down (0: none).</summary>
    public static void SetCaptionPressed(Window window, int hitTest)
    {
        if (StateFor(window).Buttons is not { } buttons) return;
        foreach (var button in buttons.Items) button.IsPressed = HitTestOf(button) == hitTest;
    }

    private static int HitTestOf(CaptionButton button) => button.ButtonKind switch
    {
        CaptionButton.Kind.Close => (int)Win32Properties.Win32HitTestValue.Close,
        CaptionButton.Kind.Minimize => (int)Win32Properties.Win32HitTestValue.MinButton,
        _ => (int)Win32Properties.Win32HitTestValue.MaxButton,
    };

    private static bool Equal(double a, double b) => !double.IsNaN(a) && Math.Abs(a - b) < 0.0001;

    /// <summary>The three window buttons for <paramref name="window"/> (close, miniaturize, zoom), exposed for tests via
    /// InternalsVisibleTo.</summary>
    internal static (CaptionButton Close, CaptionButton Miniaturize, CaptionButton Zoom) Buttons(Window window)
    {
        var buttons = StateFor(window).Buttons ??= CreateButtons(window);
        return (buttons.Items[0], buttons.Items[1], buttons.Items[2]);
    }

    /// <summary>
    /// OD-0044: the layer the window's caption buttons live on, for the window's factory to lay over the window's content
    /// (last child of the content's root panel). It covers the whole client area but takes only the buttons' own hits.
    /// </summary>
    public static Canvas CaptionLayer(Window window) => StateFor(window).Layer ??= new Canvas();

    /// <summary>Owner T45: the entire invisible caption cell follows app zoom; native DPI scaling remains the window's.</summary>
    public static void SetCaptionScale(Window window, double scale)
    {
        var state = StateFor(window);
        state.CaptionScale = scale;
        if (state.Buttons is { } buttons) ScaleCaptionCluster(buttons.Cluster, scale);
    }

    private static void ScaleCaptionCluster(Panel cluster, double scale)
    {
        cluster.RenderTransformOrigin = RelativePoint.TopLeft;
        cluster.RenderTransform = scale == 1 ? null : new ScaleTransform(scale, scale);
        // Canvas positions the untransformed layout box: account for the rendered extra width at its right edge.
        Canvas.SetRight(cluster, CaptionTrailingInset * scale + CaptionClusterWidth * (scale - 1));
        Canvas.SetTop(cluster, CaptionTop * scale);
    }

    /// <summary>ED-NEW-T45: the full caption boxes take priority over the platform's title-bar drag band.</summary>
    public static int CaptionHitTest(Window window, Point clientPoint)
    {
        if (StateFor(window).Buttons is not { } buttons) return 0;
        foreach (var button in buttons.Items)
        {
            if (!button.IsEffectivelyVisible || !button.IsHitTestVisible) continue;
            var local = window.TranslatePoint(clientPoint, button);
            if (local is { } point && new Rect(button.Bounds.Size).Contains(point))
                return (int)Win32Properties.GetNonClientHitTestResult(button);
        }
        return 0;
    }

    /// <summary>The cluster holding the three buttons, exposed for tests via InternalsVisibleTo.</summary>
    internal static Panel CaptionCluster(Window window) => (StateFor(window).Buttons ??= CreateButtons(window)).Cluster;

    private static TrafficLightButtons CreateButtons(Window window)
    {
        var close = MakeButton(CaptionButton.Kind.Close, Win32Properties.Win32HitTestValue.Close);
        var miniaturize = MakeButton(CaptionButton.Kind.Minimize, Win32Properties.Win32HitTestValue.MinButton);
        var zoom = MakeButton(CaptionButton.Kind.Maximize, Win32Properties.Win32HitTestValue.MaxButton);
        // DEVIATION OD-0044: Windows order left to right (minimise, maximise, close), fixed at the top right.
        var cluster = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2, IsVisible = false };
        cluster.Children.Add(miniaturize);
        cluster.Children.Add(zoom);
        cluster.Children.Add(close);
        Canvas.SetRight(cluster, CaptionTrailingInset);
        Canvas.SetTop(cluster, CaptionTop);
        // On the window's caption layer (CaptionLayer, which the window's factory lays over its content), never inside the
        // app's own content tree: the caption buttons are window chrome, not part of RootView's view hierarchy (PA note).
        // A window with no caption layer falls back to its overlay layer, when its template has one (the port's windows
        // have no template, so they have none: an earlier version that used only the overlay layer never showed them).
        var state = StateFor(window);
        ScaleCaptionCluster(cluster, state.CaptionScale);
        if (state.Layer is { } layer) layer.Children.Add(cluster);
        else if (OverlayLayer.GetOverlayLayer(window) is { } overlay) overlay.Children.Add(cluster);
        else (state.Layer = new Canvas()).Children.Add(cluster);
        // Windows draws the restore glyph while the window is maximised, and greys maximise on a window that cannot be
        // resized (Avalonia's CanMaximize follows CanResize); the hook ignores a disabled button's click (WindowManager).
        void Sync()
        {
            zoom.IsMaximized = window.WindowState == Avalonia.Controls.WindowState.Maximized;
            // OD-0046: the Windows caption buttons' own accessible names (Windows calls the maximised one Restore).
            Avalonia.Automation.AutomationProperties.SetName(zoom, zoom.IsMaximized ? "Restore" : "Maximize");
            zoom.IsEnabled = window.CanMaximize;
            Win32Properties.SetNonClientHitTestResult(zoom, window.CanMaximize ? Win32Properties.Win32HitTestValue.MaxButton : Win32Properties.Win32HitTestValue.Client);
        }
        // OD-0068 (ED-244): an inactive window's glyphs take their dimmer colour, as Windows dims its own caption buttons.
        void SyncActive()
        {
            foreach (var button in new[] { miniaturize, zoom, close }) button.IsWindowActive = window.IsActive;
        }
        Sync();
        SyncActive();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty || e.Property == Window.CanMaximizeProperty || e.Property == Window.CanResizeProperty) Sync();
            else if (e.Property == WindowBase.IsActiveProperty) SyncActive();
        };
        return new TrafficLightButtons(close, miniaturize, zoom, cluster);
    }

    private static CaptionButton MakeButton(CaptionButton.Kind kind, Win32Properties.Win32HitTestValue hitTest)
    {
        var button = new CaptionButton(kind) { IsVisible = false };
        Win32Properties.SetNonClientHitTestResult(button, hitTest);
        Avalonia.Automation.AutomationProperties.SetName(button, kind switch
        {
            CaptionButton.Kind.Minimize => "Minimize",
            CaptionButton.Kind.Maximize => "Maximize",
            _ => "Close",
        });
        return button;
    }
}

// MARK: - Sidebar layout

/// <summary>:237-371 <c>@MainActor @Observable final class SidebarLayout</c> (R-OBS-6).</summary>
public sealed class SidebarLayout : MainActorModel
{
    public const double MinimumWidth = 210; // :240
    public const double MaximumWidth = 440; // :241
    public const double CollapseThreshold = 110; // :242
    public const double DefaultWidth = 268; // :243

    private static class Key // :245-248
    {
        public const string Width = "sidebarWidth";
        public const string StartsOpen = "sidebarStartsOpen";
    }

    private double width; // :250 private(set) var width: CGFloat
    public double Width { get => Read(width); private set => Set(ref width, value); }

    private bool isVisible; // :251 private(set) var isVisible: Bool
    public bool IsVisible { get => Read(isVisible); private set => Set(ref isVisible, value); }

    private bool startsOpen; // :255-257 var startsOpen: Bool { didSet { persist() } }
    public bool StartsOpen
    {
        get => Read(startsOpen);
        set => Set(ref startsOpen, value, didSet: _ => Persist()); // :256
    }

    private bool holdsTrafficLights; // :262 private(set) var holdsTrafficLights: Bool
    public bool HoldsTrafficLights { get => Read(holdsTrafficLights); private set => Set(ref holdsTrafficLights, value); }

    private bool isDragging; // :264 private(set) var isDragging = false
    public bool IsDragging { get => Read(isDragging); private set => Set(ref isDragging, value); }

    private bool isSliding; // :268 private(set) var isSliding = false
    public bool IsSliding { get => Read(isSliding); private set => Set(ref isSliding, value); }

    private double slideDelta; // :271 private(set) var slideDelta: CGFloat = 0
    public double SlideDelta { get => Read(slideDelta); private set => Set(ref slideDelta, value); }

    private TaskHandle? slideTask; // :272 @ObservationIgnored private var slideTask: Task<Void, Never>?
    private double anchorWidth; // :282 @ObservationIgnored private var anchorWidth: CGFloat = 0
    private double restingWidth; // :283 @ObservationIgnored private var restingWidth: CGFloat
    private double lastLaidOutWidth = -1; // :285 @ObservationIgnored private var lastLaidOutWidth: CGFloat = -1

    /// <summary>:274-277 <c>static var trafficLightsFitWidth: CGFloat</c>.</summary>
    public static double TrafficLightsFitWidth =>
        Chrome.TrafficLightLeading + Chrome.TrafficLightsWidth + Chrome.TrafficLightClearance; // :276-277

    /// <summary>:279-280 <c>private static var slideRun: Int</c>.</summary>
    private static int SlideRunMilliseconds =>
        Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion ? 220 : 420; // :280

    /// <summary>:287-296 <c>init()</c>.</summary>
    public SidebarLayout()
    {
        // :288. CaptureRun.defaults is always nil (ED-001, porting/dead_code.md entry 44: CaptureRun is not
        // ported), so `CaptureRun.defaults ?? .standard` always resolves to `.standard`.
        var defaults = UserDefaults.Standard.Current;
        var stored = Plist.AsDouble(defaults.Object(Key.Width)) ?? DefaultWidth; // :289
        restingWidth = Math.Min(MaximumWidth, Math.Max(MinimumWidth, stored)); // :290
        width = restingWidth; // :291. Field, not property: no didSet runs for an init assignment (R-OBS-1).
        var visible = Plist.AsBool(defaults.Object(Key.StartsOpen)) ?? false; // :292
        startsOpen = visible; // :293
        isVisible = visible; // :294
        holdsTrafficLights = visible; // :295
    }

    /// <summary>:298 <c>var renderedWidth: CGFloat</c>.</summary>
    public double RenderedWidth => IsVisible ? Width : 0; // :298

    /// <summary>:303-308 <c>func noteLaidOutWidth(_:)</c>.</summary>
    public void NoteLaidOutWidth(double laidOut)
    {
        if (laidOut == lastLaidOutWidth) return; // :304
        lastLaidOutWidth = laidOut; // :305
        var fits = laidOut >= TrafficLightsFitWidth; // :306
        if (fits != HoldsTrafficLights) HoldsTrafficLights = fits; // :307
    }

    /// <summary>:310-320 <c>private func beginSlide(delta:)</c>.</summary>
    private void BeginSlide(double delta)
    {
        SlideDelta = delta; // :311
        IsSliding = true; // :312
        slideTask?.Cancel(); // :313
        slideTask = MainActor.Spawn(async () => // :314 Task { @MainActor [weak self] in
        {
            await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(SlideRunMilliseconds)); // :315 try? await Task.sleep
            if (SwiftTask.IsCancelled) return; // :316 guard !Task.isCancelled, let self else { return }
            IsSliding = false; // :317
            SlideDelta = 0; // :318
        });
    }

    /// <summary>PORT-ONLY (ED-NEW-sidebarmodes-6): a Settings › Sidebar change that brings the column in or takes it away (Panel only
    /// and back) moves the chat's leading edge by <paramref name="delta"/> just as <see cref="Toggle"/> does, so it opens the same slide
    /// window (:310-320), in which the chat lays its text out once at the width it is heading for rather than on every frame.</summary>
    public void NoteColumnSlide(double delta) => BeginSlide(delta);

    /// <summary>:322-338 <c>func beginDrag()</c>.</summary>
    public void BeginDrag()
    {
        slideTask?.Cancel(); // :323
        IsSliding = false; // :324
        SlideDelta = 0; // :325
        IsDragging = true; // :326
        if (IsVisible) // :327
        {
            anchorWidth = Width; // :328
            return; // :329
        }
        anchorWidth = 0; // :331
        // :332-337. `Transaction { disablesAnimations = true }`: this write is not wrapped in an animation, so
        // the sidebar jumps straight to width 0 with no interpolation, unlike EndDrag/Toggle below.
        Transaction.WithAnimation(null, () =>
        {
            Width = 0; // :335
            IsVisible = true; // :336
        });
    }

    /// <summary>:340-342 <c>func drag(by:)</c>. Not floored at MinimumWidth: PR-WIN-043, upstream defect 64.</summary>
    public void Drag(double translation) => Width = Math.Min(MaximumWidth, Math.Max(0, anchorWidth + translation)); // :341

    /// <summary>:344-356 <c>func endDrag()</c>.</summary>
    public void EndDrag()
    {
        if (Width < CollapseThreshold) // :345
        {
            BeginSlide(Width); // :346
            Transaction.WithAnimation(Chrome.PanelSlide, () => IsVisible = false); // :347
            Width = restingWidth; // :348
        }
        else
        {
            var settled = Math.Max(MinimumWidth, Width); // :350
            restingWidth = settled; // :351
            Transaction.WithAnimation(Chrome.PanelSlide, () => Width = settled); // :352
        }
        Persist(); // :354
        IsDragging = false; // :355
    }

    /// <summary>:358-364 <c>func toggle()</c>.</summary>
    public void Toggle()
    {
        var opening = !IsVisible; // :359
        if (opening) Width = restingWidth; // :360
        BeginSlide(opening ? -restingWidth : Width); // :361
        Transaction.WithAnimation(Chrome.PanelSlide, () => IsVisible = !IsVisible); // :362 isVisible.toggle()
        Persist(); // :363
    }

    /// <summary>:366-370 <c>private func persist()</c>.</summary>
    private void Persist()
    {
        var defaults = UserDefaults.Standard.Current; // :367. See the init() note: always `.standard`.
        defaults.Set(Key.Width, restingWidth); // :368 defaults.set(Double(restingWidth), forKey: Key.width)
        defaults.Set(Key.StartsOpen, StartsOpen); // :369
    }
}

// MARK: - Resize handle

/// <summary>
/// :376-401 <c>struct SidebarResizeHandle: View</c>. B3 (unit A26; plan 2.A row A26, N8): the mechanism that used
/// to live in this file's own SidebarResizeHandleView (B2 :402-570) now lives in the shared <see
/// cref="DragHandle"/>/<see cref="DragHandleView"/> (N8, DragHandle.cs); this type is left a thin configuration
/// of it (:384-399 <c>var body: some View</c>), the same reduction the Swift diff makes at 52bc792. In SwiftUI
/// this is the two grab strips RootView installs (App/Views/RootView.swift:22-31, :54-63); porting that
/// installation site is App's job (outside Core/Services/UI, section 2.1), so this type is the plain updater
/// App's binding calls into <see cref="MakeNSView"/>/<see cref="UpdateNSView"/>, one level above <see
/// cref="DragHandle"/>'s own identically-shaped pair. Retargeting onto the shared view also gives this strip
/// N8's mid-drag detach reset (DragHandle.swift:207-223, PR-WIN-233) for free: taken out of the tree while
/// dragging, it now drops isDragging/isPressed and pops the drag cursor without firing onEnd or onChange, which
/// the old, strip-specific SidebarResizeHandleView never did.
/// </summary>
public sealed class SidebarResizeHandle
{
    public const double HitWidth = 9; // :377 static let hitWidth: CGFloat = 9, PR-WIN-049

    // :394 cursor: .columnResize. One shared cursor instance for every strip (PR-WIN-049's own "the same shared
    // IDC_SIZEWE cursor, not a new one per view" windows_path), matching this file's identical previous constant.
    private static readonly Cursor ColumnResizeCursor = new(StandardCursorType.SizeWestEast);

    public bool IsActive { get; set; } = true; // :379 let isActive: Bool
    public Action? OnBegin { get; set; } // :380 let onBegin: () -> Void
    public Action<double>? OnChange { get; set; } // :381 let onChange: (CGFloat) -> Void
    public Action? OnEnd { get; set; } // :382 let onEnd: () -> Void

    /// <summary>:384-399 <c>var body: some View</c>. The one <see cref="DragHandle"/> configuration every
    /// SidebarResizeHandle installs: horizontal axis, the 10/22/2.5 grip with a glow starting 30 points down
    /// (trafficLightTop + trafficLightDiameter), re-reading hover on every layout, and the shared column-resize
    /// cursor. Every literal is unchanged from B2.</summary>
    private DragHandle Handle() => new()
    {
        HandleAxis = DragHandleView.Axis.Horizontal, // :386 axis: .horizontal
        HandleGrip = new DragHandleView.Grip(
            RestLength: 10, ShownLength: 22, Thickness: 2.5, // :388-390
            GlowInset: Chrome.TrafficLightTop + Chrome.TrafficLightDiameter, // :391
            RefreshesHoverOnLayout: true), // :392
        HandleCursor = ColumnResizeCursor, // :394 cursor: .columnResize
        IsActive = IsActive, // :395
        OnBegin = OnBegin, // :396
        OnChange = size => OnChange?.Invoke(size.Width), // :397 onChange: { onChange($0.width) }
        OnEnd = OnEnd, // :398
    };

    // :269-286 DragHandle.makeNSView(context:), reached through this file's own body (SwiftUI resolves a View's
    // body to build the underlying NSView). App's binding calls this exactly as it called
    // SidebarResizeHandleView's own constructor before B3.
    public DragHandleView MakeNSView() => Handle().MakeNSView();

    // :288-290 DragHandle.updateNSView(_:context:), reached the same way.
    public void UpdateNSView(DragHandleView view) => Handle().UpdateNSView(view);
}
