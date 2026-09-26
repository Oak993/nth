using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

using Chrome = Marrow.UI.Chrome.Chrome; // ED-095: the Swift type Chrome, not this test namespace

// Source under test: DroppyCode/UI/Chrome/WindowChrome.swift (B3 52bc792), :373-401. Part 2 of 2 (this file): the
// rest of SidebarLayout (PR-WIN-046 through PR-WIN-048, unchanged at B3), the resize strip - at B3 a thin
// SidebarResizeHandle configuration (:376-401) of the shared UI.Chrome.DragHandle/DragHandleView (N8) - covering
// PR-WIN-049 through PR-WIN-057, and the traffic-light buttons plus the strip as screens (UI-WIN-001 through
// UI-WIN-004). See WindowChromeTests (window/titlebar/buttons, part 1) and WindowChromeSecondTests (SidebarLayout
// up to PR-WIN-045) for the earlier parts, and DragHandleTests for the shared mechanism's own generic coverage.
// Constants transcribed from WindowChrome.swift:387-393, Chrome.swift:15-19 (all unchanged B2 -> B3):
//   Grip thickness 2.5, restLength 10, shownLength 22, alpha 0.6 (Grip default), hoverGlow 0.05, dragGlow 0.10
//   (Grip defaults), duration 0.18 (DragHandleView.Metrics)
//   glowInset = trafficLightTop (16) + trafficLightDiameter (14) = 30
[Collection("Headless")]
public sealed class WindowChromeThirdTests(HeadlessSession headless)
{
    // :384-399 SidebarResizeHandle.body, reached through MakeNSView() exactly as App's binding reaches it
    // (WindowChrome.cs's own doc comment on SidebarResizeHandle). Proves the wrapper wires the shared
    // DragHandleView with the sidebar's own axis/grip/cursor, not merely that DragHandleView itself works
    // (DragHandleTests already proves the generic mechanism against arbitrary configurations).
    private static (Window Window, DragHandleView View) NewAttachedStrip(double width = 9, double height = 200)
    {
        var view = new SidebarResizeHandle().MakeNSView();
        view.Width = width; view.Height = height;
        view.HorizontalAlignment = HorizontalAlignment.Left; view.VerticalAlignment = VerticalAlignment.Top;
        var window = new Window { Content = view, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // Window.InputHitTest reads the last composed scene, which composes only on a render tick.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    // The strip's own ArrangeOverride re-reads CursorPlatform on every layout pass it runs (PR-WIN-056), which
    // Dispatcher.RunJobs can trigger after any of these calls; a FakeCursorPlatform kept in step with each
    // simulated point stops that re-read from seeing the real, unrelated OS cursor and flipping hover back off.
    private static void Move(Window window, Point at, FakeCursorPlatform? cursor = null)
    {
        if (cursor is not null) cursor.ScreenPosition = window.PointToScreen(at);
        window.MouseMove(at, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Down(Window window, Point at, FakeCursorPlatform? cursor = null)
    {
        if (cursor is not null) cursor.ScreenPosition = window.PointToScreen(at);
        window.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Up(Window window, Point at, FakeCursorPlatform? cursor = null)
    {
        if (cursor is not null) cursor.ScreenPosition = window.PointToScreen(at);
        window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    // ---- PR-WIN-046, PR-WIN-047, PR-WIN-048: the rest of SidebarLayout ----

    [Trait("Req", "PR-WIN-046")] // WindowChrome.swift:358-364
    [Fact]
    public void Toggling_open_sets_the_width_to_resting_and_slides_by_minus_that_width()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout(); // closed, restingWidth 300
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsVisible);
        Assert.Equal(300, layout.Width); // :360 width = restingWidth before the slide starts
        Assert.True(layout.IsSliding);
        Assert.Equal(-300, layout.SlideDelta); // :361 opening ? -restingWidth : width
    }

    [Trait("Req", "PR-WIN-046")] // WindowChrome.swift:358-364, closing direction
    [Fact]
    public void Toggling_closed_slides_by_the_current_width_and_flips_visibility()
    {
        using var runtime = new TestRuntime();
        runtime.Defaults.Set("sidebarStartsOpen", true);
        runtime.Defaults.Set("sidebarWidth", 300.0);
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.False(layout.IsVisible); // :362 isVisible.toggle()
        Assert.True(layout.IsSliding);
        Assert.Equal(300, layout.SlideDelta); // :361 opening ? ... : width
    }

    [Trait("Req", "PR-WIN-047")] // WindowChrome.swift:366-370, upstream defect 63
    [Fact]
    public void A_toggle_rewrites_both_keys_even_though_startsOpen_did_not_change()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.Defaults.Writes.Clear();
        runtime.OnMain(() => layout.Toggle());
        Assert.Contains("sidebarWidth", runtime.Defaults.Writes);
        Assert.Contains("sidebarStartsOpen", runtime.Defaults.Writes); // rewritten though StartsOpen is untouched
        Assert.Equal(false, runtime.Defaults.Object("sidebarStartsOpen")); // the default materialised on disk
    }

    [Trait("Req", "PR-WIN-047")] // WindowChrome.swift:368, persist always writes restingWidth, never the live width
    [Fact]
    public void Persist_writes_the_resting_width_not_the_live_width()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout(); // restingWidth 268 default
        runtime.OnMain(() => layout.BeginDrag());
        runtime.OnMain(() => layout.Drag(150)); // live width 150, above the collapse threshold, below the minimum
        runtime.OnMain(() => layout.EndDrag()); // settles below 210 minimum -> 210, becomes new restingWidth
        Assert.Equal(210.0, runtime.Defaults.Object("sidebarWidth"));
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:279-280, :310-320
    [Fact]
    public void A_slide_settles_after_420_milliseconds_by_default()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(419);
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(1);
        Assert.False(layout.IsSliding);
        Assert.Equal(0, layout.SlideDelta);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:280, reduce motion halves the run to 220
    [Fact]
    public void A_slide_settles_after_220_milliseconds_with_reduce_motion()
    {
        using var runtime = new TestRuntime();
        using var _ = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform(reduceMotion: true));
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        runtime.AdvanceMilliseconds(219);
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(1);
        Assert.False(layout.IsSliding);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:313, a later beginSlide cancels the pending clear
    [Fact]
    public void A_new_slide_cancels_the_previous_pending_clear()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle()); // opens, slide #1 armed for 420ms
        runtime.AdvanceMilliseconds(300);
        runtime.OnMain(() => layout.Toggle()); // closes, slide #2 restarts the 420ms window
        Assert.True(layout.IsSliding);
        runtime.AdvanceMilliseconds(300); // slide #1's original deadline (600ms total) has passed
        Assert.True(layout.IsSliding); // still sliding: #2 owns the clock now
        runtime.AdvanceMilliseconds(120);
        Assert.False(layout.IsSliding);
    }

    [Trait("Req", "PR-WIN-048")] // WindowChrome.swift:322-325, beginDrag cancels and clears immediately
    [Fact]
    public void BeginDrag_cancels_a_pending_slide_and_clears_it_immediately()
    {
        using var runtime = new TestRuntime();
        var layout = new SidebarLayout();
        runtime.OnMain(() => layout.Toggle());
        Assert.True(layout.IsSliding);
        runtime.OnMain(() => layout.BeginDrag());
        Assert.False(layout.IsSliding); // cleared immediately, not after the run
        Assert.Equal(0, layout.SlideDelta);
        runtime.AdvanceMilliseconds(500); // the cancelled task's own clear must never re-fire
        Assert.False(layout.IsSliding);
    }

    // ---- PR-WIN-049, UI-WIN-004: hit width, cursor ----

    [Trait("Req", "PR-WIN-049")] // WindowChrome.swift:377
    [Fact]
    public void The_hit_width_constant_is_9()
    {
        Assert.Equal(9, SidebarResizeHandle.HitWidth);
    }

    [Trait("Req", "PR-WIN-049")] // WindowChrome.swift:394 cursor: .columnResize; DragHandle.swift:39-44, :188-192
    [Trait("Req", "UI-WIN-004")] // WindowChrome.swift:373-401
    [Fact]
    public void An_active_strip_shows_a_fixed_shared_cursor_and_an_inactive_one_shows_none()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip();
            var otherActive = new SidebarResizeHandle().MakeNSView();
            Assert.NotNull(view.Cursor);
            Assert.Same(view.Cursor, otherActive.Cursor); // the same shared IDC_SIZEWE cursor, not a new one per view
            view.IsActive = false;
            Assert.Null(view.Cursor);
        });
    }

    // ---- PR-WIN-050, PR-WIN-051, UI-WIN-004: grip and glow geometry ----

    [Trait("Req", "PR-WIN-050")] // WindowChrome.swift:387-393; DragHandle.swift:153-181
    [Trait("Req", "UI-WIN-004")]
    [Fact]
    public void At_rest_the_grip_is_10_tall_and_invisible()
    {
        headless.OnUiThread(() =>
        {
            // Flake cause (evidence: a headless Window always reports Position (0,0), so NewAttachedStrip's strip
            // occupies real screen pixels (0,0)-(9,200) — confirmed by a throwaway diagnostic printing
            // window.PointToScreen and the live WindowsCursorPlatform.ScreenPosition side by side). RefreshHover
            // (DragHandle.swift:142-151) reads the real, unrelated OS cursor via CursorPlatform unless overridden,
            // so this test intermittently saw isHovering true whenever the machine's actual mouse pointer sat in
            // that corner of the screen, growing the "at rest" grip. Pin the cursor off-strip, as Move()'s
            // FakeCursorPlatform comment above already establishes for the hover-driven tests in this file.
            using var _ = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
            var (_, view) = NewAttachedStrip();
            var grip = view.GripLayerForTest;
            Assert.Equal(2.5, grip.Width);
            Assert.Equal(10, grip.Height);
            Assert.Equal(0, grip.Opacity);
            Assert.Equal(1.25, grip.CornerRadius.TopLeft);
            Assert.Equal((view.Bounds.Width - 2.5) / 2, Canvas.GetLeft(grip));
            Assert.Equal((view.Bounds.Height - 10) / 2, Canvas.GetTop(grip));
        });
    }

    [Trait("Req", "PR-WIN-050")] // DragHandle.swift:140, :159, :173. showsGrip = isActive && (hovering || dragging)
    [Fact]
    public void Hovering_grows_the_grip_to_22_tall_at_06_opacity()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Move(window, new Point(4, 100), cursor); // inside the 9-wide strip
            Assert.Equal(22, view.GripLayerForTest.Height);
            Assert.Equal(0.6, view.GripLayerForTest.Opacity);
            Assert.Equal((view.Bounds.Height - 22) / 2, Canvas.GetTop(view.GripLayerForTest));
        });
    }

    [Trait("Req", "PR-WIN-051")] // WindowChrome.swift:391 glowInset; DragHandle.swift:84-85, :174-179
    [Fact]
    public void At_rest_the_glow_is_invisible_and_starts_30_points_down()
    {
        headless.OnUiThread(() =>
        {
            // Same real-cursor flake as At_rest_the_grip_is_10_tall_and_invisible above: pin the cursor off-strip
            // so ShowsGrip (DragHandle.swift:140) can never read the live OS pointer as hovering.
            using var _ = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
            var (_, view) = NewAttachedStrip();
            var glow = view.GlowLayerForTest;
            Assert.Equal(0, glow.Opacity);
            Assert.Equal(4, glow.CornerRadius.TopLeft);
            Assert.Equal(0.5, Canvas.GetLeft(glow)); // bounds.minX + 0.5
            Assert.Equal(30, Canvas.GetTop(glow)); // trafficLightTop 16 + trafficLightDiameter 14
            Assert.Equal(view.Bounds.Width - 1, glow.Width);
            Assert.Equal(view.Bounds.Height - 30, glow.Height);
        });
    }

    [Trait("Req", "PR-WIN-051")] // DragHandle.swift:176, hover 0.05, drag 0.10 (Grip defaults)
    [Fact]
    public void Hovering_shows_the_glow_at_005_and_dragging_at_010()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            var at = new Point(4, 100);
            Move(window, at, cursor);
            Assert.Equal(0.05, view.GlowLayerForTest.Opacity);
            Down(window, at, cursor);
            // The press starts a genuine 0.18s transition (PR-WIN-052) from the already-settled hover opacity, so
            // reading it back immediately would catch it mid-flight; flipping IsActive forces the same target
            // through the unanimated snap path (DragHandle.swift:49-54), which the "at rest" tests above already
            // prove correct.
            view.IsActive = false;
            view.IsActive = true;
            Assert.Equal(0.10, view.GlowLayerForTest.Opacity);
        });
    }

    [Trait("Req", "PR-WIN-051")] // DragHandle.swift:174-179, floored at zero for a degenerate bounds
    [Fact]
    public void The_glow_size_is_floored_at_zero()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip(width: 0.4, height: 5); // narrower than 1, shorter than the 30 inset
            Assert.Equal(0, view.GlowLayerForTest.Width);
            Assert.Equal(0, view.GlowLayerForTest.Height);
        });
    }

    [Trait("Req", "UI-WIN-004")] // DragHandle.swift:87-88, glow beneath the grip
    [Fact]
    public void The_glow_layer_is_added_before_the_grip_layer_so_the_grip_draws_on_top()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip();
            Assert.Equal(2, view.Children.Count);
            Assert.Same(view.GlowLayerForTest, view.Children[0]);
            Assert.Same(view.GripLayerForTest, view.Children[1]);
        });
    }

    // ---- PR-WIN-052: animation duration/curve, animated vs snapped ----

    [Trait("Req", "PR-WIN-052")] // DragHandle.swift:160-166, :332-354, a press animates
    [Fact]
    public void A_press_installs_018_second_ease_out_transitions()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            Down(window, new Point(4, 100));
            var transitions = Assert.IsType<Avalonia.Animation.Transitions>(view.GripLayerForTest.Transitions);
            Assert.NotEmpty(transitions);
            foreach (var transition in transitions)
                Assert.Equal(TimeSpan.FromSeconds(0.18), Assert.IsType<Avalonia.Animation.DoubleTransition>(transition).Duration);
        });
    }

    [Trait("Req", "PR-WIN-052")] // DragHandle.swift:207-232, viewDidMoveToWindow uses animated: false
    [Fact]
    public void Attaching_to_a_window_installs_no_transitions()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip();
            Assert.Null(view.GripLayerForTest.Transitions);
            Assert.Null(view.GlowLayerForTest.Transitions);
        });
    }

    [Trait("Req", "PR-WIN-052")] // DragHandle.swift:100-112, the isActive didSet uses animated: false
    [Fact]
    public void Flipping_the_active_flag_snaps_with_no_transitions()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            Down(window, new Point(4, 100)); // gives it transitions first
            Assert.NotNull(view.GripLayerForTest.Transitions);
            view.IsActive = false;
            Assert.Null(view.GripLayerForTest.Transitions);
        });
    }

    [Trait("Req", "PR-WIN-052")] // DragHandle.swift:186-201, a layout pass with no hover change snaps
    [Fact]
    public void A_layout_pass_that_does_not_change_hover_installs_no_transitions()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            Assert.Null(view.GripLayerForTest.Transitions); // attach was unanimated
            window.UpdateLayout(); // another pass, hover state unchanged (false -> false)
            Assert.Null(view.GripLayerForTest.Transitions);
        });
    }

    // ---- PR-WIN-053: never moves the window ----

    [Trait("Req", "PR-WIN-053")] // DragHandle.swift:128-130, :327-329
    [Fact]
    public void The_strip_declares_a_client_hit_so_it_never_moves_the_window()
    {
        headless.OnUiThread(() =>
        {
            var view = new SidebarResizeHandle().MakeNSView();
            Assert.Equal(
                Win32Properties.Win32HitTestValue.Client,
                view.GetValue(Win32Properties.NonClientHitTestResultProperty));
            Assert.False(view.MouseDownCanMoveWindow);
        });
    }

    [Trait("Req", "PR-WIN-053")] // DragHandle.swift:323-325, a single click-drag works on an inactive window
    [Fact]
    public void AcceptsFirstMouse_is_true()
    {
        var view = new SidebarResizeHandle().MakeNSView();
        Assert.True(view.AcceptsFirstMouse(null));
    }

    // ---- PR-WIN-054: fully inert while inactive ----

    [Trait("Req", "PR-WIN-054")] // DragHandle.swift:100-112, :140
    [Fact]
    public void An_inactive_strip_is_excluded_from_hit_testing()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip();
            Assert.True(view.IsHitTestVisible);
            view.IsActive = false;
            Assert.False(view.IsHitTestVisible);
        });
    }

    [Trait("Req", "PR-WIN-054")] // DragHandle.swift:100-112, :142-151, isActive false forces hover off
    [Fact]
    public void An_inactive_strip_never_shows_hover_or_a_grown_grip()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Move(window, new Point(4, 100), cursor);
            Assert.True(view.IsHoveringForTest);
            view.IsActive = false;
            Assert.False(view.IsHoveringForTest);
            Assert.Equal(10, view.GripLayerForTest.Height); // back to rest
        });
    }

    // ---- PR-WIN-055: drag deltas from the press point, final delta before ending ----

    [Trait("Req", "PR-WIN-055")] // DragHandle.swift:198-208, :210-220; the sidebar's own onChange: { onChange($0.width) }
    [Fact]
    public void Drag_deltas_are_reported_relative_to_the_press_point()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip(width: 300); // wide, so distant move points still hit the strip
            var deltas = new List<double>();
            view.OnChange = size => deltas.Add(size.Width); // WindowChrome.swift:397 onChange: { onChange($0.width) }
            Down(window, new Point(100, 100));
            Move(window, new Point(130, 100));
            Move(window, new Point(80, 100));
            Assert.Equal([30, -20], deltas); // press itself reports no change; each move reports from the press x
        });
    }

    [Trait("Req", "PR-WIN-055")] // DragHandle.swift:222-232, onChange fires before onEnd, in that order
    [Fact]
    public void Releasing_reports_the_final_delta_before_ending_the_drag()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip(width: 300);
            var order = new List<string>();
            view.OnChange = size => order.Add($"change:{size.Width}");
            view.OnEnd = () => order.Add("end");
            Down(window, new Point(100, 100));
            Up(window, new Point(145, 100));
            Assert.Equal(["change:45", "end"], order); // the release reports the final delta before onEnd fires
            Assert.False(view.IsDraggingForTest);
        });
    }

    [Trait("Req", "PR-WIN-055")] // DragHandle.swift:207, onBegin fires on press
    [Fact]
    public void Pressing_begins_the_drag_and_fires_onBegin()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip(width: 300);
            var began = false;
            view.OnBegin = () => began = true;
            Down(window, new Point(10, 100));
            Assert.True(began);
            Assert.True(view.IsDraggingForTest);
        });
    }

    // ---- PR-WIN-056: hover re-read from the real cursor position ----

    [Trait("Req", "PR-WIN-056")] // DragHandle.swift:142-151, :186-201
    [Fact]
    public void A_layout_pass_recomputes_hover_from_the_scripted_cursor_position()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            using var _ = DragHandleView.CursorPlatform.Override(
                new FakeCursorPlatform(window.PointToScreen(new Point(view.Bounds.Width / 2, 50))));
            view.InvalidateArrange(); // forces a real layout pass: nothing else changed to make one due
            window.UpdateLayout(); // strip moved out from under a stationary cursor, in effect:
            Assert.True(view.IsHoveringForTest); // the next layout pass discovers the cursor is over it now
        });
    }

    [Trait("Req", "PR-WIN-056")] // DragHandle.swift:145, a cursor outside the strip's bounds is not hovering
    [Fact]
    public void A_cursor_outside_the_strips_bounds_is_not_hovering()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedStrip();
            using var _ = DragHandleView.CursorPlatform.Override(
                new FakeCursorPlatform(window.PointToScreen(new Point(300, 50))));
            view.InvalidateArrange();
            window.UpdateLayout();
            Assert.False(view.IsHoveringForTest);
        });
    }

    // ---- PR-WIN-057: grip and glow follow the label color ----

    [Trait("Req", "PR-WIN-057")] // DragHandle.swift:91-97, :99-102
    [Fact]
    public void The_grip_and_glow_are_filled_with_the_label_color()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedStrip();
            Assert.NotNull(view.GripLayerForTest.Background);
            Assert.NotNull(view.GlowLayerForTest.Background);
            Assert.Same(view.GripLayerForTest.Background, view.GlowLayerForTest.Background); // same NSColor.labelColor source
        });
    }

    // ---- UI-WIN-001, UI-WIN-002, UI-WIN-003: the traffic-light buttons as a screen ----
    // Geometry, reuse and animation are already proven by PR-WIN-031 through PR-WIN-037 in WindowChromeTests
    // (the same production code, WindowChrome.PlaceTrafficLights). These tests restate the screen-level result
    // each button record describes, tying the geometry to the specific button rather than to the placement pass.

    [Trait("Req", "UI-WIN-001")] // WindowChrome.swift:177-213, Chrome.swift:15-19. Close: first of three, pitch 23.
    [Fact]
    public void The_close_button_is_14_square_and_first_in_the_pitch()
    {
        headless.OnUiThread(() =>
        {
            var window = new Window();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (close, _, _) = WindowChrome.Buttons(window);
            Assert.Equal(14, ((Border)close).Width);
            Assert.Equal(14, ((Border)close).Height);
            Assert.Equal(18, Canvas.GetLeft((Border)close)); // leading + 0 * pitch
            Assert.True(close.IsVisible); // :185-186 isHidden = false, unconditionally, every placement
        });
    }

    [Trait("Req", "UI-WIN-002")] // WindowChrome.swift:183-199, Chrome.swift:15-17. Miniaturize: middle, leading + 23.
    [Fact]
    public void The_miniaturize_button_sits_at_leading_plus_one_pitch()
    {
        headless.OnUiThread(() =>
        {
            var window = new Window();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (_, miniaturize, _) = WindowChrome.Buttons(window);
            Assert.Equal(41, Canvas.GetLeft((Border)miniaturize)); // 18 + 23
            Assert.Equal(16, Canvas.GetTop((Border)miniaturize));
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
            Assert.Equal(37, Canvas.GetLeft((Border)miniaturize)); // 14 + 23, collapsed
        });
    }

    [Trait("Req", "UI-WIN-003")] // WindowChrome.swift:183-199, WindowManager.swift:81. Zoom: trailing, leading + 46.
    [Fact]
    public void The_zoom_button_sits_at_leading_plus_two_pitches()
    {
        headless.OnUiThread(() =>
        {
            var window = new Window();
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: true, animated: false);
            var (_, _, zoom) = WindowChrome.Buttons(window);
            Assert.Equal(64, Canvas.GetLeft((Border)zoom)); // 18 + 2 * 23
            WindowChrome.PlaceTrafficLights(window, sidebarVisible: false, animated: false);
            Assert.Equal(60, Canvas.GetLeft((Border)zoom)); // 14 + 2 * 23, collapsed
        });
    }
}
