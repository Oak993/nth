using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Marrow.Core.Port;
using Marrow.UI.Port;
using PortAnimation = Marrow.UI.Port.Animation;

namespace Marrow.UI.Chrome;

// Source: DroppyCode/UI/Chrome/WindowChrome.swift (B2 1fbf81e), :1-572.
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
// UNRESOLVED (O-.. not yet assigned): PR-WIN-037's own guard on `!window.inLiveResize` has no ported signal to
// read (no live-resize notification exists anywhere in the C# tree at the time of this unit). In this design that
// guard is structurally vacuous rather than unimplemented: button position here is driven only by sidebarVisible
// and the sidebar's laid-out width (SidebarLayout.NoteLaidOutWidth), never by the window's frame, so a live
// resize cannot itself trigger a placement call the way AppKit's container frameDidChange did, and so there is no
// per-frame animation restart for a resize to suppress. `animated: false` is still honoured literally (first
// placement, sidebar-edge drag) via the `animated` parameter placeTrafficLights already carries in the source.

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
            // :26-28. This design has no per-frame resize coupling (top-of-file note), so the source's
            // `!window.inLiveResize` half of the guard is vacuous here; `!sidebarDragging` is honoured.
            WindowChrome.PlaceTrafficLights(window, sidebarVisible, animated: !sidebarDragging);
        }
    }

    // :40-67 viewDidMoveToWindow(). The container-fit / didEndLiveResize observers (:42-49, :58-66) are not
    // registered: this design has no titlebar container to observe (top-of-file note).
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return; // :50 guard let window
        WindowChrome.Configure(window); // :51
        WindowChrome.PlaceTrafficLights(window, sidebarVisible, animated: false); // :52
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        window = null; // the probe's previous window keeps nothing to unregister in this design (see above)
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
    }

    private sealed class TrafficLightButtons(Border close, Border miniaturize, Border zoom)
    {
        public Border[] Items { get; } = [close, miniaturize, zoom]; // :183 order: close, miniaturize, zoom
    }

    // :105. `NSHashTable<NSWindow>.weakObjects()` -> a weak-keyed table so a closed, released window drops out
    // on its own and is never kept alive by this registry (PR-WIN-022's windows_path).
    private static readonly ConditionalWeakTable<Window, WindowState> states = new();

    private static WindowState StateFor(Window window) => states.GetOrCreateValue(window);

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

    /// <summary>:177-230 <c>static func placeTrafficLights(on:sidebarVisible:animated:)</c>.</summary>
    public static void PlaceTrafficLights(Window window, bool sidebarVisible, bool animated)
    {
        // :178-179
        var leading = sidebarVisible ? Chrome.TrafficLightLeading : Chrome.SheetInset + Chrome.ChromeHorizontalPadding;
        var top = sidebarVisible ? Chrome.TrafficLightTop : CollapsedTrafficLightTop;

        var state = StateFor(window);
        // :188-199 first placement creates the three buttons (stands in for activating the four constraints once).
        var buttons = state.Buttons ??= CreateButtons(window);

        var changed = false; // :182
        var targets = new double[buttons.Items.Length];
        for (var index = 0; index < buttons.Items.Length; index++) // :184 for (index, type) in types.enumerated()
        {
            var button = buttons.Items[index];
            button.IsVisible = true; // :185-186 button.isHidden = false, unconditionally, every placement
            var x = leading + index * Chrome.TrafficLightPitch; // :187
            targets[index] = x;
            // :202-210 rewrite only when the value actually differs
            if (!Equal(Canvas.GetLeft(button), x) || !Equal(Canvas.GetTop(button), top)) changed = true;
        }
        if (!changed) return; // :214 guard changed else { return }

        PortAnimation? animation = null;
        if (animated) // :216 guard animated, !window.inLiveResize else { ...; return } (see top-of-file note)
        {
            // :223-227
            var reduceMotion = Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;
            var duration = reduceMotion ? 0.18 : 0.32; // :225
            animation = PortAnimation.EaseOut(duration); // :226 CAMediaTimingFunction(name: .easeOut)
        }

        for (var index = 0; index < buttons.Items.Length; index++)
        {
            var button = buttons.Items[index];
            if (animation is null)
            {
                button.Transitions = null; // :217-218 titlebar.layoutSubtreeIfNeeded(), no animation group
            }
            else
            {
                var duration = TimeSpan.FromSeconds(animation.Duration);
                var easing = animation.ToEasing();
                button.Transitions = new Avalonia.Animation.Transitions
                {
                    new Avalonia.Animation.DoubleTransition { Property = Canvas.LeftProperty, Duration = duration, Easing = easing },
                    new Avalonia.Animation.DoubleTransition { Property = Canvas.TopProperty, Duration = duration, Easing = easing },
                };
            }
            Canvas.SetLeft(button, targets[index]);
            Canvas.SetTop(button, top);
        }
    }

    private static bool Equal(double a, double b) => !double.IsNaN(a) && Math.Abs(a - b) < 0.0001;

    /// <summary>The three window buttons for <paramref name="window"/>, close/miniaturize/zoom order (:183),
    /// exposed for tests via InternalsVisibleTo.</summary>
    internal static (Control Close, Control Miniaturize, Control Zoom) Buttons(Window window)
    {
        var buttons = StateFor(window).Buttons ?? CreateButtons(window);
        StateFor(window).Buttons ??= buttons;
        return (buttons.Items[0], buttons.Items[1], buttons.Items[2]);
    }

    private static TrafficLightButtons CreateButtons(Window window)
    {
        // :183 order close, miniaturize, zoom; :197-198 width = height = trafficLightDiameter.
        var close = MakeButton(Win32Properties.Win32HitTestValue.Close);
        var miniaturize = MakeButton(Win32Properties.Win32HitTestValue.MinButton);
        var zoom = MakeButton(Win32Properties.Win32HitTestValue.MaxButton);
        var canvas = new Canvas();
        canvas.Children.Add(close);
        canvas.Children.Add(miniaturize);
        canvas.Children.Add(zoom);
        // Added through the overlay layer, never into the app's own content tree: AppKit's buttons are window
        // chrome, not part of RootView's view hierarchy, and this mirrors that (PA note).
        OverlayLayer.GetOverlayLayer(window)?.Children.Add(canvas);
        return new TrafficLightButtons(close, miniaturize, zoom);
    }

    private static Border MakeButton(Win32Properties.Win32HitTestValue kind)
    {
        var button = new Border
        {
            Width = Chrome.TrafficLightDiameter, // :197
            Height = Chrome.TrafficLightDiameter, // :198
            CornerRadius = new CornerRadius(Chrome.TrafficLightDiameter / 2),
            IsVisible = false,
        };
        Win32Properties.SetNonClientHitTestResult(button, kind);
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
