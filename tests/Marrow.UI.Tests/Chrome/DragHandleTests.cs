using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// Source under test: DroppyCode/UI/Chrome/DragHandle.swift (B3 52bc792), :1-304. Unit UI.Chrome.DragHandle (N8).
// Records covered: PR-TERM-015, PR-TERM-019, PR-WIN-232, PR-WIN-233, PR-WIN-234 (packets_b3/UI.Chrome.DragHandle
// /part1.md). ED-202 (Avalonia raises PointerExited on a captured chain) is this unit's own hover rule; ED-202's
// tests below sit under PR-WIN-234, the record describing the component's general contract.
[Collection("Headless")]
public sealed class DragHandleTests(HeadlessSession headless)
{
    private static (Window Window, DragHandleView View) NewAttachedHandle(
        DragHandleView.Axis axis = DragHandleView.Axis.Horizontal,
        DragHandleView.Grip? grip = null,
        double slop = 0,
        double width = 20,
        double height = 200)
    {
        var view = new DragHandleView
        {
            HandleAxis = axis,
            Slop = slop,
            Width = width, Height = height,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
        view.Configure(grip);
        var window = new Window { Content = view, Width = 400, Height = 300 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        // Window.InputHitTest reads the last composed scene, which composes only on a render tick.
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    // The same real-cursor flake WindowChromeThirdTests documents (this file's own RefreshHover re-reads
    // CursorPlatform on layout/attach/release, PR-WIN-232/PR-TERM-019): a FakeCursorPlatform kept in step with
    // each simulated point stops a re-read from seeing the real, unrelated OS cursor.
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

    // ---- PR-WIN-234: axis travel signs ----

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:262, .horizontal reports x only
    [Fact]
    public void Horizontal_travel_reports_x_only()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Horizontal, width: 300, height: 300);
            Size? travel = null;
            view.OnChange = size => travel = size;
            Down(window, new Point(100, 100));
            Move(window, new Point(140, 160));
            Assert.Equal(new Size(40, 0), travel);
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:263. Avalonia coordinates: origin.Y - point.Y (plan section 3, row N8).
    [Fact]
    public void Vertical_travel_reports_the_upward_distance_as_positive()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, width: 300, height: 300);
            Size? travel = null;
            view.OnChange = size => travel = size;
            Down(window, new Point(100, 150));
            Move(window, new Point(100, 100)); // moved up 50px in Avalonia's downward-growing y
            Assert.Equal(new Size(0, 50), travel); // AppKit's "up is positive" is preserved
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:264. Avalonia coordinates: point.Y - origin.Y (plan section 3, row N8).
    [Fact]
    public void Free_travel_reports_the_downward_distance_as_positive_like_a_panel_offset()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Free, width: 300, height: 300);
            Size? travel = null;
            view.OnChange = size => travel = size;
            Down(window, new Point(100, 100));
            Move(window, new Point(160, 175)); // moved right 60, down 75
            // 6.2: SubagentPanel.cs:908-913 got this sign backwards at B2 (it computed origin.Y - point.Y here,
            // the .vertical rule); N8's own Travel uses the correct, generalized rule for every caller.
            Assert.Equal(new Size(60, 75), travel);
        });
    }

    // ---- PR-WIN-234: grip/glow defaults and metrics ----

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:22, :27-29
    [Fact]
    public void Grip_defaults_are_alpha_06_hoverGlow_005_dragGlow_010_and_no_layout_refresh()
    {
        var grip = new DragHandleView.Grip(RestLength: 10, ShownLength: 20, Thickness: 3);
        Assert.Equal(0.6, grip.Alpha);
        Assert.Null(grip.GlowInset);
        Assert.False(grip.RefreshesHoverOnLayout);
        Assert.Equal(0.05, grip.HoverGlow);
        Assert.Equal(0.10, grip.DragGlow);
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:59, the glow's corner radius is always 4
    [Fact]
    public void The_glow_corner_radius_is_always_4()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedHandle(grip: new DragHandleView.Grip(10, 22, 2.5, GlowInset: 4));
            Assert.Equal(4, view.GlowLayerForTest.CornerRadius.TopLeft);
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:60, :160-166, a press installs 0.18s ease-out transitions
    [Fact]
    public void A_press_installs_018_second_ease_out_transitions_on_both_layers()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(grip: new DragHandleView.Grip(10, 22, 2.5), width: 300);
            Down(window, new Point(150, 150));
            foreach (var layer in new[] { view.GripLayerForTest, view.GlowLayerForTest })
            {
                var transitions = Assert.IsType<Avalonia.Animation.Transitions>(layer.Transitions);
                Assert.NotEmpty(transitions);
                foreach (var transition in transitions)
                    Assert.Equal(TimeSpan.FromSeconds(0.18), Assert.IsType<Avalonia.Animation.DoubleTransition>(transition).Duration);
            }
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:198-208, slop 0 starts dragging on press
    [Fact]
    public void A_handle_with_slop_zero_starts_dragging_on_press_and_fires_onBegin()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(slop: 0, width: 300);
            var began = false;
            view.OnBegin = () => began = true;
            Down(window, new Point(10, 100));
            Assert.True(began);
            Assert.True(view.IsDraggingForTest);
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:210-220, slop > 0 waits for combined travel
    [Fact]
    public void A_handle_with_slop_above_zero_waits_for_combined_travel_before_dragging()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(slop: 5, width: 300);
            var began = false;
            view.OnBegin = () => began = true;
            Down(window, new Point(100, 100));
            Assert.False(view.IsDraggingForTest); // the press alone never starts a drag when slop > 0
            Move(window, new Point(103, 100)); // 3px, under the 5px slop
            Assert.False(view.IsDraggingForTest);
            Assert.False(began);
            Move(window, new Point(106, 100)); // 6px combined, over the slop
            Assert.True(view.IsDraggingForTest);
            Assert.True(began);
        });
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:39-44
    [Fact]
    public void Setting_a_different_cursor_while_active_updates_the_shown_cursor_immediately()
    {
        var view = new DragHandleView();
        var cursor = new Cursor(StandardCursorType.Hand);
        view.HandleCursor = cursor;
        Assert.Same(cursor, view.Cursor);
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:49-56, :188-192
    [Fact]
    public void An_inactive_handle_shows_no_cursor_and_reactivating_restores_the_configured_one()
    {
        var view = new DragHandleView();
        var cursor = new Cursor(StandardCursorType.Hand);
        view.HandleCursor = cursor;
        view.IsActive = false;
        Assert.Null(view.Cursor);
        view.IsActive = true;
        Assert.Same(cursor, view.Cursor);
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:104-114, the first configuration always snaps unanimated
    [Fact]
    public void The_first_configuration_snaps_the_grip_unanimated()
    {
        var view = new DragHandleView { HandleAxis = DragHandleView.Axis.Vertical, Width = 300, Height = 20 };
        view.Configure(new DragHandleView.Grip(12, 28, 2.5));
        Assert.Null(view.GripLayerForTest.Transitions);
        Assert.Equal(12, view.GripLayerForTest.Width); // .vertical's grip bar grows in width, not height
    }

    [Trait("Req", "PR-WIN-234")] // DragHandle.swift:110, a re-render with the same grip presence never resnaps
    [Fact]
    public void Reconfiguring_with_the_same_grip_presence_does_not_interrupt_a_running_hover_animation()
    {
        headless.OnUiThread(() =>
        {
            var grip = new DragHandleView.Grip(10, 22, 2.5);
            var (window, view) = NewAttachedHandle(grip: grip, width: 300);
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Move(window, new Point(150, 100), cursor); // hover on: installs a real transition
            Assert.NotNull(view.GripLayerForTest.Transitions);
            view.Configure(grip); // same instance, presence unchanged: must not re-snap (:110)
            Assert.NotNull(view.GripLayerForTest.Transitions); // still animating, not reset to null
        });
    }

    [Trait("Req", "PR-WIN-234")] // ED-202: hover state settles on release, not mid-drag
    [Fact]
    public void Hover_does_not_flip_off_while_the_drag_carries_the_pointer_outside_the_strip()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 30);
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Move(window, new Point(15, 150), cursor); // hover on, inside the 30-wide strip
            Assert.True(view.IsHoveringForTest);
            Down(window, new Point(15, 150), cursor);
            Move(window, new Point(300, 150), cursor); // captured drag carries the pointer well outside the strip
            Assert.True(view.IsHoveringForTest); // ED-202: Avalonia's captured PointerExited is absorbed mid-drag
            Up(window, new Point(300, 150), cursor); // release happens off-strip
            Assert.False(view.IsHoveringForTest); // re-read from the real pointer position on release (:230)
        });
    }

    // ---- PR-TERM-015: release commits the final delta, then re-reads hover ----

    [Trait("Req", "PR-TERM-015")] // DragHandle.swift:222-232
    [Fact]
    public void Releasing_applies_the_final_delta_before_ending_the_drag()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 300);
            var order = new List<string>();
            view.OnChange = size => order.Add($"change:{size.Height}");
            view.OnEnd = () => order.Add("end");
            Down(window, new Point(150, 150));
            Up(window, new Point(150, 120)); // moved up 30
            Assert.Equal(["change:30", "end"], order);
            Assert.False(view.IsDraggingForTest);
        });
    }

    [Trait("Req", "PR-TERM-015")] // DragHandle.swift:229-231, hover is re-read from the real pointer on release
    [Fact]
    public void Releasing_off_the_strip_retracts_the_grip_at_once()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 300);
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Down(window, new Point(150, 150), cursor);
            Up(window, new Point(390, 290), cursor); // released well off the strip's bounds, still inside the window
            Assert.False(view.IsHoveringForTest);
            Assert.Equal(12, view.GripLayerForTest.Width); // back to rest, not left stuck shown
        });
    }

    [Trait("Req", "PR-TERM-015")] // DragHandle.swift:198-208, :222-232, a press with no movement still commits
    [Fact]
    public void A_press_with_no_movement_still_reports_a_zero_delta_and_ends()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 300);
            var changes = new List<double>();
            var ended = false;
            view.OnChange = size => changes.Add(size.Height);
            view.OnEnd = () => ended = true;
            Down(window, new Point(150, 150));
            Up(window, new Point(150, 150));
            Assert.Equal([0.0], changes);
            Assert.True(ended);
        });
    }

    // ---- PR-TERM-019: hover tracked over the strip, re-read on release and on attach ----

    [Trait("Req", "PR-TERM-019")] // DragHandle.swift:247-255
    [Fact]
    public void Entering_and_exiting_the_strip_toggles_hovering_and_animates_the_grip()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 300);
            var cursor = new FakeCursorPlatform(null);
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            Move(window, new Point(150, 150), cursor);
            Assert.True(view.IsHoveringForTest);
            Assert.Equal(28, view.GripLayerForTest.Width); // .vertical's grip bar grows in width
            Move(window, new Point(390, 290), cursor); // leaves the strip, still inside the window
            Assert.False(view.IsHoveringForTest);
            Assert.Equal(12, view.GripLayerForTest.Width);
        });
    }

    [Trait("Req", "PR-TERM-019")] // DragHandle.swift:142-151, no hover before any pointer activity
    [Fact]
    public void Hover_state_is_false_before_any_pointer_activity()
    {
        headless.OnUiThread(() =>
        {
            using var _ = DragHandleView.CursorPlatform.Override(new FakeCursorPlatform(null));
            var (_, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, grip: new DragHandleView.Grip(12, 28, 2.5), width: 300);
            Assert.False(view.IsHoveringForTest);
        });
    }

    // ---- PR-WIN-232: attaching under a resting pointer shows the grip at once ----

    [Trait("Req", "PR-WIN-232")] // DragHandle.swift:127-138
    [Fact]
    public void Attaching_under_a_resting_pointer_shows_the_grip_at_once_unanimated()
    {
        headless.OnUiThread(() =>
        {
            var view = new DragHandleView
            {
                HandleAxis = DragHandleView.Axis.Vertical, Width = 300, Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            };
            view.Configure(new DragHandleView.Grip(12, 28, 2.5));
            // First attach: give the handle a real, laid-out size, matching a drawer that already knows its own
            // height before it is hidden and shown again.
            var staging = new Window { Content = view, Width = 400, Height = 300 };
            staging.Show();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            staging.Content = null; // detach
            Dispatcher.UIThread.RunJobs();

            var window = new Window { Width = 400, Height = 300 };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var cursor = new FakeCursorPlatform(window.PointToScreen(new Point(150, 10)));
            using var _ = DragHandleView.CursorPlatform.Override(cursor);
            window.Content = view; // re-attaches: the strip already knows its own bounds
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.IsHoveringForTest);
            Assert.Equal(28, view.GripLayerForTest.Width); // shown length, not rest
            Assert.Null(view.GripLayerForTest.Transitions); // an unanimated snap, not a transition
        });
    }

    [Trait("Req", "PR-WIN-232")] // DragHandle.swift:134, ungated by refreshesHoverOnLayout
    [Fact]
    public void A_handle_with_no_grip_configured_reads_no_hover_on_attach()
    {
        headless.OnUiThread(() =>
        {
            var (_, view) = NewAttachedHandle(axis: DragHandleView.Axis.Free); // .free never configures a grip
            Assert.False(view.IsHoveringForTest);
        });
    }

    // ---- PR-WIN-233: taken out of the window mid-drag clears state, never calls onEnd ----

    [Trait("Req", "PR-WIN-233")] // DragHandle.swift:127-133
    [Fact]
    public void Detaching_mid_drag_clears_state_without_calling_onEnd_or_onChange()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, width: 300);
            var ended = false;
            var changed = false;
            view.OnEnd = () => ended = true;
            Down(window, new Point(150, 150));
            Move(window, new Point(150, 100));
            Assert.True(view.IsDraggingForTest);
            view.OnChange = _ => changed = true; // wired only after the drag already started
            window.Content = null; // taken out mid-drag
            Assert.False(view.IsDraggingForTest);
            Assert.False(ended); // :130-133: no onEnd call
            Assert.False(changed); // :130-133: no onChange call either
        });
    }

    [Trait("Req", "PR-WIN-233")] // DragHandle.swift:132, pops a pushed drag cursor without a release
    [Fact]
    public void Detaching_mid_drag_restores_the_configured_cursor_from_a_pushed_drag_cursor()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, width: 300);
            var configured = new Cursor(StandardCursorType.SizeNorthSouth);
            var drag = new Cursor(StandardCursorType.Hand);
            view.HandleCursor = configured;
            view.DragCursor = drag;
            Down(window, new Point(150, 150));
            Move(window, new Point(150, 100));
            Assert.Same(drag, view.Cursor);
            window.Content = null;
            Assert.Same(configured, view.Cursor);
        });
    }

    [Trait("Req", "PR-WIN-233")] // Avalonia analogue: a lost capture without a matching release behaves the same way
    [Fact]
    public void Losing_pointer_capture_mid_drag_clears_state_without_calling_onEnd()
    {
        headless.OnUiThread(() =>
        {
            var (window, view) = NewAttachedHandle(axis: DragHandleView.Axis.Vertical, width: 300);
            IPointer? pointer = null;
            view.PointerPressed += (_, e) => pointer = e.Pointer;
            Down(window, new Point(150, 150));
            Move(window, new Point(150, 100));
            Assert.True(view.IsDraggingForTest);
            Assert.NotNull(pointer);
            var ended = false;
            view.OnEnd = () => ended = true;
            // Capturing a different element steals the capture out from under the handle mid-drag, which raises
            // PointerCaptureLost on the previous holder - Avalonia's own notification for a capture transfer.
            pointer!.Capture(new Border());
            Assert.False(view.IsDraggingForTest);
            Assert.False(ended); // :130-133's rule, generalized: no onEnd on a capture loss either
        });
    }
}
