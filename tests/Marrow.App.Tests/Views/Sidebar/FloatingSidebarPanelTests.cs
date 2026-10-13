using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.App.Views.Sidebar;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Sidebar;

// Source: DroppyCode/App/Views/Sidebar/FloatingSidebarPanel.swift (B3 52bc792), :1-127. PR-SIDEBAR-143 is the constants and the two
// clamps; PR-SIDEBAR-144 to -148, -158 and UI-SIDEBAR-045 to -049 drive the built panel through its pointer, with every
// expected value transcribed from the Swift line cited on the test (FloatingSidebarPanelRig builds and drives it).
[Collection("Headless")]
public sealed class FloatingSidebarPanelTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:17-20
    public void The_panel_declares_the_source_default_size_minimum_margin_and_strip_height()
    {
        Assert.Equal(new Size(300, 560), FloatingSidebarPanel.DefaultSize);
        Assert.Equal(new Size(240, 320), FloatingSidebarPanel.MinSize);
        Assert.Equal(12, FloatingSidebarPanel.FloatingSidebarPanelMargin);
        Assert.Equal(44, FloatingSidebarPanel.StripHeight);
        Assert.Equal(72, Chrome.ContentTopInset);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:121-125
    public void The_origin_is_clamped_to_the_area_with_the_margin()
    {
        var clamp = typeof(FloatingSidebarPanel).GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = Assert.IsType<Point>(clamp.Invoke(null, [new Point(-20, 900), new Size(240, 320), new Size(500, 600)]));

        Assert.Equal(new Point(12, 268), result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:121-125
    public void The_lower_margin_wins_when_the_area_is_too_small()
    {
        var clamp = typeof(FloatingSidebarPanel).GetMethod("Clamp", BindingFlags.NonPublic | BindingFlags.Static)!;

        var result = Assert.IsType<Point>(clamp.Invoke(null, [new Point(900, -20), new Size(240, 320), new Size(100, 100)]));

        Assert.Equal(new Point(12, 12), result);
    }

    [Fact]
    [Trait("Req", "PR-SIDEBAR-143")] // App/Views/Sidebar/FloatingSidebarPanel.swift:28-32
    public void The_stored_size_is_floored_at_the_minimum_and_capped_by_the_area_less_the_margins()
    {
        var readSize = typeof(FloatingSidebarPanel).GetMethod("ReadSize", BindingFlags.NonPublic | BindingFlags.Static)!;

        var belowMinimum = Assert.IsType<Size>(readSize.Invoke(null, [new Size(100, 200), new Size(500, 600)]));
        Assert.Equal(new Size(240, 320), belowMinimum);

        var aboveAreaCap = Assert.IsType<Size>(readSize.Invoke(null, [new Size(800, 900), new Size(500, 600)]));
        Assert.Equal(new Size(476, 576), aboveAreaCap);

        var areaSmallerThanMinimum = Assert.IsType<Size>(readSize.Invoke(null, [new Size(300, 560), new Size(100, 100)]));
        Assert.Equal(new Size(240, 320), areaSmallerThanMinimum);
    }

    // ---- PR-SIDEBAR-144, UI-SIDEBAR-046: the strip ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:41-43
    [Trait("Req", "UI-SIDEBAR-046")] // App/Views/Sidebar/FloatingSidebarPanel.swift:41-43
    public void The_title_reads_Threads_in_12_5_medium_in_the_label_colour_at_0_92() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        var title = rig.Title;

        Assert.Equal("Threads", title.Text);
        RootViewRig.AssertStyle(title, 12.5, FontWeight.Medium);
        RootViewRig.AssertForeground(title, RootViewRig.Primary.Opacity(0.92));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:44-54
    [Trait("Req", "UI-SIDEBAR-046")] // App/Views/Sidebar/FloatingSidebarPanel.swift:44-54
    public void The_title_capsule_is_12_wide_padded_28_high_plus_2_inset_12_from_the_top_and_14_from_the_leading_edge() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        // The drag handle laid over the capsule has exactly the capsule's rectangle (:50-52 .overlay).
        var capsule = rig.InSurface(rig.TitleHandle);

        Assert.Equal(14, capsule.X); // :54 .padding(.leading, Chrome.chromeHorizontalPadding)
        Assert.Equal(12, capsule.Y); // :53 .padding(.top, Chrome.chromeTopPadding)
        Assert.Equal(28 + 2 * 2, capsule.Height); // :45-46 .frame(height: 28) then .padding(.vertical, 2)
        Assert.Equal(rig.Title.DesiredSize.Width + 2 * 12, capsule.Width); // :44 .padding(.horizontal, 12), :47 .fixedSize()
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:50-52
    [Trait("Req", "UI-SIDEBAR-046")] // App/Views/Sidebar/FloatingSidebarPanel.swift:49-52
    public void Pressing_the_title_drags_the_panel_too() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        var before = rig.Frame.TopLeft;

        rig.Drag(rig.TitleHandle, 60, 20);

        Assert.Equal(before + new Point(60, 20), rig.Frame.TopLeft);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:40,55-59,60-66
    [Trait("Req", "UI-SIDEBAR-047")] // App/Views/Sidebar/FloatingSidebarPanel.swift:55-59
    public void The_drag_handle_fills_the_strip_between_the_title_and_the_close_button_8_points_from_each_44_high() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), canClose: true);
        var title = rig.InSurface(rig.TitleHandle);
        var handle = rig.InSurface(rig.StripHandle);
        var close = rig.InSurface(Assert.Single(rig.All<ChromeCircleButton>()));

        Assert.Equal(0, handle.Y); // the strip is the top of the panel
        Assert.Equal(44, handle.Height); // :57 .frame(height: Self.stripHeight)
        Assert.Equal(title.Right + 8, handle.X); // :40 HStack(spacing: 8)
        Assert.Equal(close.X - 8, handle.Right); // :56 .frame(maxWidth: .infinity) takes what is left
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:55-57,60
    [Trait("Req", "UI-SIDEBAR-047")] // App/Views/Sidebar/FloatingSidebarPanel.swift:55-57
    public void Without_the_close_button_the_drag_handle_runs_to_the_panels_trailing_edge() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), canClose: false);

        var handle = rig.InSurface(rig.StripHandle);

        Assert.Equal(300, handle.Right); // no close button, so no 8 point gap and no trailing inset
        Assert.Equal(44, handle.Height);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:58-59
    [Trait("Req", "UI-SIDEBAR-047")] // App/Views/Sidebar/FloatingSidebarPanel.swift:55-59; the record: tooltip and accessibility label both exactly `Drag to move`
    public void The_drag_handle_is_tooltipped_and_labelled_Drag_to_move() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        var handle = rig.StripHandle;

        Assert.Equal("Drag to move", ToolTip.GetTip(handle));
        Assert.Equal("Drag to move", AutomationProperties.GetName(handle));
        // The handle draws nothing of its own (the UI record: transparent): no grip is configured on it.
        Assert.Null(Assert.IsType<DragHandleView>(handle.Child).HandleGrip);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:60-66
    [Trait("Req", "UI-SIDEBAR-048")] // App/Views/Sidebar/FloatingSidebarPanel.swift:60-66
    public void The_close_button_is_a_circle_with_an_xmark_inset_12_from_the_top_and_14_from_the_trailing_edge_and_closes_on_click() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), canClose: true);

        var button = Assert.Single(rig.All<ChromeCircleButton>());
        var frame = rig.InSurface(button);
        Assert.Equal(300 - 14 - 32, frame.X); // :65 .padding(.trailing, Chrome.chromeHorizontalPadding)
        Assert.Equal(12, frame.Y); // :64 .padding(.top, Chrome.chromeTopPadding)
        Assert.Equal(32, frame.Width); // a chrome circle button is the height of a chrome capsule
        Assert.Equal(32, frame.Height);
        var symbol = Assert.Single(RootViewRig.All<SwiftImage>(button));
        Assert.Equal("xmark", symbol.SymbolName); // :61
        var tipped = Assert.Single(RootViewRig.All<Button>(button));
        Assert.Equal("Back into the column", ToolTip.GetTip(tipped)); // :61 help:
        Assert.Equal("Back into the column", AutomationProperties.GetName(tipped));

        Assert.Equal(0, rig.Closed);
        rig.Click(button);
        Assert.Equal(1, rig.Closed); // :61-63 close()
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-144")] // App/Views/Sidebar/FloatingSidebarPanel.swift:60 `if canClose`
    [Trait("Req", "UI-SIDEBAR-048")] // App/Views/Sidebar/FloatingSidebarPanel.swift:60 `if canClose`
    public void Without_canClose_there_is_no_close_button_at_all() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), canClose: false);

        Assert.Empty(rig.All<ChromeCircleButton>());
        Assert.DoesNotContain(rig.All<Button>(), button => ToolTip.GetTip(button) as string == "Back into the column");
        Assert.DoesNotContain(rig.All<SwiftImage>(), image => image.SymbolName == "xmark");
    });

    // ---- UI-SIDEBAR-045: the default geometry ----

    [Fact]
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:17,28-33 default 300x560 at (12, Chrome.contentTopInset)
    public void With_nothing_stored_the_panel_is_300_by_560_at_12_72() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(900, 800));

        Assert.Equal(new Rect(12, 72, 300, 560), rig.Frame);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:29-32 a stored size below 240x320 reads as the minimum
    public void A_stored_size_below_the_minimum_reads_as_240_by_320() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(900, 800), size: new Size(100, 100));

        Assert.Equal(new Size(240, 320), rig.Frame.Size);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:29-33 capped at the area less 2 * 12, then clamped in
    public void A_stored_size_larger_than_the_area_is_capped_at_the_area_less_two_margins() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), size: new Size(2000, 2000));

        Assert.Equal(new Rect(12, 12, 776, 676), rig.Frame);
    });

    [Fact]
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:33 a stored origin is where the panel rests, clamped when drawn
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:33,120-125 the stored origin is only clamped for display
    public void A_stored_origin_is_where_the_panel_rests_and_one_outside_the_area_is_drawn_clamped_but_left_stored() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using (var inside = new FloatingSidebarPanelRig(launch, new Size(800, 700), origin: new Point(300, 100)))
            Assert.Equal(new Point(300, 100), inside.Frame.TopLeft);

        using var outside = new FloatingSidebarPanelRig(launch, new Size(800, 700), origin: new Point(900, 900));

        Assert.Equal(new Point(800 - 300 - 12, 700 - 560 - 12), outside.Frame.TopLeft);
        Assert.Equal(new Point(900, 900), outside.Model.Settings.SidebarPanelOrigin); // the clamp is applied on read, never written back
    });

    // ---- PR-SIDEBAR-145, UI-SIDEBAR-047: moving the panel ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:105-118 follows the pointer, writes settings only on release
    [Trait("Req", "UI-SIDEBAR-047")] // App/Views/Sidebar/FloatingSidebarPanel.swift:56-61,105-118
    public void Dragging_the_strip_moves_the_panel_live_and_the_origin_is_written_only_on_release() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        Assert.Null(rig.Model.Settings.SidebarPanelOrigin);

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(100, 30);

        Assert.Equal(new Point(112, 102), rig.Frame.TopLeft); // :111 start + translation
        Assert.Null(rig.Model.Settings.SidebarPanelOrigin); // :116 only onDragEnd writes
        Assert.Null(launch.Harness.Defaults.Object("sidebarPanelOrigin"));

        rig.MouseReleaseBy(100, 30);

        Assert.Equal(new Point(112, 102), rig.Model.Settings.SidebarPanelOrigin);
        Assert.Equal(new List<object> { 112.0, 102.0 }, launch.Harness.Defaults.Object("sidebarPanelOrigin")); // the exact key, a two-element array
        Assert.Equal(new Point(112, 102), rig.Frame.TopLeft);
    });

    [Theory]
    [InlineData(1000, 1000, 488, 128)] // :123-124 max(margin, area - size - margin): 800-300-12 and 700-560-12
    [InlineData(-1000, -1000, 12, 12)] // :123-124 never beyond the 12 point margin
    [InlineData(200, -500, 212, 12)]
    [InlineData(-500, 500, 12, 128)]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:111,121-125 the dragged origin is clamped inside the area, live and on release
    [Trait("Req", "UI-SIDEBAR-047")] // App/Views/Sidebar/FloatingSidebarPanel.swift:105-125
    public void The_dragged_origin_never_leaves_the_area_by_more_than_the_margin(double dx, double dy, double expectedX, double expectedY) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), windowSize: new Size(2400, 2200));

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(dx, dy);
        Assert.Equal(new Point(expectedX, expectedY), rig.Frame.TopLeft);
        rig.MouseReleaseBy(dx, dy);

        Assert.Equal(new Point(expectedX, expectedY), rig.Model.Settings.SidebarPanelOrigin);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:121-125 `max(margin, ...)`: the lower bound wins in a too small area
    public void In_an_area_too_small_for_the_panel_the_lower_margin_wins_while_dragging() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(200, 300), windowSize: new Size(900, 800));

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(50, 50);

        Assert.Equal(new Point(12, 12), rig.Frame.TopLeft); // the 240x320 minimum panel does not fit; max(12, negative) = 12
        rig.MouseReleaseBy(50, 50);
        Assert.Equal(new Point(12, 12), rig.Model.Settings.SidebarPanelOrigin);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:114-118 the grab and the drag origin are cleared on release
    public void The_next_drag_starts_from_where_the_panel_came_to_rest() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        rig.Drag(rig.StripHandle, 100, 30);

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(10, 0);

        Assert.Equal(new Point(122, 102), rig.Frame.TopLeft); // :109 grab ?? rest, with rest the new origin
        rig.MouseReleaseBy(10, 0);
        Assert.Equal(new Point(122, 102), rig.Model.Settings.SidebarPanelOrigin);
    });

    // ---- PR-SIDEBAR-146, PR-SIDEBAR-158, UI-SIDEBAR-049: resizing the panel ----

    // web W1 (DESIGN 4.3, ED-NEW-web-w1-6): the corner grip is the shared Splitter, still 16 by 16 at the bottom-right corner;
    // its tooltip is the shared "Resize" chip (the "Drag to resize" ToolTip, :84, is superseded) and it draws the shared corner
    // glyph only while hovered, pressed or focused.
    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:69-85 the 16x16 grip at the bottom-right corner
    [Trait("Decision", "ED-NEW-web-w1-6")]
    public void The_grip_is_16_by_16_at_the_bottom_trailing_corner_with_the_shared_Resize_tooltip() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        var grip = rig.Grip;
        var frame = RootViewRig.In(grip, rig.Surface);

        Assert.Equal(new Rect(300 - 16, 560 - 16, 16, 16), frame); // :83 .frame(width: 16, height: 16) in .bottomTrailing
        Assert.Null(ToolTip.GetTip(grip)); // the shared chip instead of a ToolTip
        Assert.Equal("Resize", Assert.IsType<TextBlock>(grip.TipChip.Child).Text);
        Assert.Equal(SplitterCorner.BottomRight, grip.Corner);
        Assert.Null(grip.Handle.HandleGrip); // the mechanics draw nothing; the shared grip draws the corner glyph
        Assert.False(grip.Grip.IsShown); // at rest
        Assert.Equal("Resize sidebar", grip.Label);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:80-81 .frameResize(position: .bottomRight, directions: .all) on hover and while dragging
    [Trait("Req", "UI-SIDEBAR-049")] // App/Views/Sidebar/FloatingSidebarPanel.swift:80-81
    public void The_grip_shows_the_bottom_right_resize_cursor_on_hover_and_while_dragging() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        var view = rig.Grip.Handle;

        Assert.Equal("BottomRightCorner", view.HandleCursor.ToString());
        Assert.Equal("BottomRightCorner", view.DragCursor!.ToString());
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:70-79 the size is written on every drag tick, not on release
    [Trait("Req", "UI-SIDEBAR-049")] // App/Views/Sidebar/FloatingSidebarPanel.swift:70-79
    public void Dragging_the_grip_resizes_the_panel_live_and_writes_the_size_on_every_tick() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        Assert.Null(rig.Model.Settings.SidebarPanelSize);

        rig.MousePress(rig.Grip);
        rig.MouseMoveBy(40, 30);

        Assert.Equal(new Size(340, 590), rig.Model.Settings.SidebarPanelSize); // :74 start + translation, written while still held
        Assert.Equal(new Size(340, 590), rig.Frame.Size);
        Assert.Equal(new List<object> { 340.0, 590.0 }, launch.Harness.Defaults.Object("sidebarPanelSize")); // the exact key, a two-element array

        rig.MouseMoveBy(100, 80);

        // :75-76 the outer cap is the area less the origin and the margin: 800-12-12 wide, 700-72-12 high.
        Assert.Equal(new Size(400, 616), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Size(400, 616), rig.Frame.Size);

        rig.MouseReleaseBy(100, 80);

        Assert.Equal(new Size(400, 616), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Point(12, 72), rig.Frame.TopLeft); // resizing never moves the panel...
        Assert.Null(rig.Model.Settings.SidebarPanelOrigin); // ...nor writes an origin
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:75-76 max(minSize, ...) inside the area cap
    public void Dragging_the_grip_up_and_left_stops_at_the_240_by_320_minimum() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        rig.Drag(rig.Grip, -200, -400);

        Assert.Equal(new Size(240, 320), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new Size(240, 320), rig.Frame.Size);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:79 onDragEnd: { resizeStart = nil }
    public void The_next_resize_starts_from_the_size_the_last_one_left() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        rig.Drag(rig.Grip, 100, 20);
        Assert.Equal(new Size(400, 580), rig.Model.Settings.SidebarPanelSize);

        rig.Drag(rig.Grip, -50, 0);

        Assert.Equal(new Size(350, 580), rig.Model.Settings.SidebarPanelSize); // :72 resizeStart ?? size: the size now, not the one before
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:75-76 upstream defect 56: the area cap is applied last, so a narrow area writes a sub-minimum size
    [Trait("Req", "UI-SIDEBAR-049")] // App/Views/Sidebar/FloatingSidebarPanel.swift:75-76 (the notes: the clamp can persist a size below 240x320 in a narrow area)
    public void In_a_narrow_area_the_written_size_can_be_below_the_minimum_while_the_panel_is_drawn_at_the_minimum() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(260, 300), windowSize: new Size(900, 800));
        Assert.Equal(new Size(240, 320), rig.Frame.Size); // :30-31 the read path floors it
        Assert.Equal(new Point(12, 12), rig.Frame.TopLeft);

        rig.Drag(rig.Grip, 10, 10);

        // :75 min(max(240, 250), 260 - 12 - 12) = 236 and :76 min(max(320, 330), 300 - 12 - 12) = 276: both below the minimum.
        Assert.Equal(new Size(236, 276), rig.Model.Settings.SidebarPanelSize);
        Assert.Equal(new List<object> { 236.0, 276.0 }, launch.Harness.Defaults.Object("sidebarPanelSize"));
        Assert.Equal(new Size(240, 320), rig.Frame.Size); // drawn at the floor again
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-158")] // App/Views/Sidebar/FloatingSidebarPanel.swift:34,75-76 upstream defect 57: the resize clamp reads `dragOrigin ?? rest`
    [Trait("Req", "PR-SIDEBAR-146")] // App/Views/Sidebar/FloatingSidebarPanel.swift:34,75-76 the clamp is anchored on the dragged origin while the title strip is held
    [Trait("Req", "UI-SIDEBAR-049")] // App/Views/Sidebar/FloatingSidebarPanel.swift:34,75-76 (the notes: anchors on the dragged origin while the strip is also held)
    public void Resizing_while_the_strip_is_held_clamps_against_the_dragged_origin_not_the_resting_one() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(400, 50); // the strip is held at the dragged origin (412, 122); the resting origin is still (12, 72)
        Assert.Equal(new Point(412, 122), rig.Frame.TopLeft);

        rig.TouchPress(rig.Grip);
        rig.TouchMoveBy(100, 100); // wants 300 + 100 wide and 560 + 100 high

        // :75-76 the caps are 800 - 412 - 12 = 376 and 700 - 122 - 12 = 566 against the dragged origin; against the resting
        // origin they would be 776 and 616, and the size 400 by 616.
        Assert.Equal(new Size(376, 566), rig.Model.Settings.SidebarPanelSize);

        rig.TouchReleaseBy(100, 100);
        rig.MouseReleaseBy(400, 50);

        Assert.Equal(new Size(376, 566), rig.Model.Settings.SidebarPanelSize); // a size clamped against a transient origin stays
        Assert.Equal(new Point(412, 122), rig.Model.Settings.SidebarPanelOrigin);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-158")] // App/Views/Sidebar/FloatingSidebarPanel.swift:34,75-76 without the strip held the resting origin is the anchor
    public void Resizing_with_the_strip_not_held_clamps_against_the_resting_origin() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), origin: new Point(400, 72));

        rig.Drag(rig.Grip, 500, 0);

        Assert.Equal(new Size(800 - 400 - 12, 560), rig.Model.Settings.SidebarPanelSize); // 388: area less the resting origin and the margin
    });

    // ---- PR-SIDEBAR-147, UI-SIDEBAR-045: the surface ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-SIDEBAR-147")] // App/Views/Sidebar/FloatingSidebarPanel.swift:35,86-101
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:86-101; App/Views/Chat/HydraPanel.swift:33
    public void The_panel_is_a_22_point_clipped_glass_surface_with_the_tint_wash_a_backing_plate_and_a_shadow(bool dark) => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), theme: theme);

        var surface = rig.Surface;
        var plate = rig.Plate;

        // :35, :95 a 22 point continuous rounded rectangle, the content clipped to it (the surface holds the strip and the sidebar)
        Assert.Equal(new CornerRadius(22), surface.CornerRadius);
        Assert.True(surface.ClipToBounds);
        Assert.Contains(rig.StripHandle, LogicalQuery.SelfAndDescendants<PanelDragHandle>(surface));
        Assert.Single(rig.All<SidebarView>());
        Assert.Contains(rig.All<SidebarView>()[0], LogicalQuery.SelfAndDescendants<SidebarView>(surface));
        // :90 .glassEffect(.regular, in: shape), no tint of its own and not interactive
        Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(22, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(surface));
        // :92 Chrome.glassTint at 0.22 in dark, 0.16 in light, over the glass
        Assert.Equal(Chrome.GlassTint.Opacity(dark ? 0.22 : 0.16).Resolve(theme), Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);

        // :99 the plate behind: black at 0.3 in dark, white at 0.34 in light
        Assert.Equal(new CornerRadius(22), plate.CornerRadius);
        Assert.Equal((dark ? SwiftColor.Black.Opacity(0.3) : SwiftColor.White.Opacity(0.34)).Resolve(theme),
            Assert.IsAssignableFrom<ISolidColorBrush>(plate.Background).Color);
        Assert.Equal(rig.Frame, RootViewRig.In(plate, rig.Panel)); // the plate is the surface's own rectangle...
        var siblings = Assert.IsAssignableFrom<Panel>(surface.Parent).Children;
        Assert.True(siblings.IndexOf(plate) < siblings.IndexOf(surface)); // ...behind it
        // :100 .shadow(color: .black.opacity(isDark ? 1 : 0.65), radius: 28, y: 10)
        var shadow = rig.Shadow;
        Assert.Equal(SwiftColor.Black.Opacity(dark ? 1 : 0.65).Resolve(theme), shadow.Color);
        // ED-358 (was a BoxShadow of Blur (28 - 0.5) / 0.288675, no spread, outer, at the colour's full alpha outside the box only): radius
        // 28 (sigma 28, ED-327), y 10, drawn at the colour's alpha times the plate fill's (1.0 x 0.3 = 77; 0.65 x 0.34 = 57), under the plate.
        Assert.Equal((28d, 0d, 10d), (shadow.Radius, shadow.X, shadow.Y));
        Assert.Equal(dark ? Color.FromArgb(77, 0, 0, 0) : Color.FromArgb(57, 0, 0, 0), shadow.DrawnColor);
        Assert.Equal(siblings.IndexOf(plate) - 1, siblings.IndexOf(shadow));
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-147")] // App/Views/Sidebar/FloatingSidebarPanel.swift:87,97 `colorScheme == .dark` is read inside body, so the surface follows the scheme as it changes
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:86-101
    public void The_tint_the_plate_and_the_shadow_follow_the_scheme_when_it_changes() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), theme: ThemeVariant.Light);
        Assert.Equal(Chrome.GlassTint.Opacity(0.16).Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(rig.Surface.Background).Color);

        rig.SetTheme(ThemeVariant.Dark);

        Assert.Equal(Chrome.GlassTint.Opacity(0.22).Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(rig.Surface.Background).Color);
        Assert.Equal(SwiftColor.Black.Opacity(0.3).Resolve(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(rig.Plate.Background).Color);
        Assert.Equal(SwiftColor.Black.Opacity(1).Resolve(ThemeVariant.Dark), rig.Shadow.Color);

        rig.SetTheme(ThemeVariant.Light);

        Assert.Equal(Chrome.GlassTint.Opacity(0.16).Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(rig.Surface.Background).Color);
        Assert.Equal(SwiftColor.White.Opacity(0.34).Resolve(ThemeVariant.Light), Assert.IsAssignableFrom<ISolidColorBrush>(rig.Plate.Background).Color);
        Assert.Equal(SwiftColor.Black.Opacity(0.65).Resolve(ThemeVariant.Light), rig.Shadow.Color);
    });

    // ---- PR-SIDEBAR-148: what the panel hosts ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-148")] // App/Views/Sidebar/FloatingSidebarPanel.swift:36-38 SidebarView(inPopover: true, dismiss: nil).padding(.top, Self.stripHeight)
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:36-38 the sidebar inside is inset 44 pt from the top
    public void The_panel_hosts_the_sidebar_in_its_popover_form_inset_44_from_the_top() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));

        var sidebar = Assert.Single(rig.All<SidebarView>());

        Assert.Equal(new Rect(0, 44, 300, 560 - 44), rig.InSurface(sidebar)); // :37 .padding(.top, stripHeight), :38 the panel's width
        Assert.Empty(RootViewRig.All<WindowDragArea>(sidebar)); // popover form: no window drag area
        // popover form: no traffic-light clearance, and the search row 12 points down rather than 14
        var field = RootViewRig.In(Assert.Single(RootViewRig.All<SidebarSearchField>(sidebar)), sidebar);
        Assert.Equal(12, field.Y);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-148")] // App/Views/Sidebar/FloatingSidebarPanel.swift:36 dismiss: nil, so a selection never closes the panel
    public void Picking_a_thread_selects_it_and_the_panel_stays_open() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var model = launch.Manager.Model;
        Project project = null!;
        ChatThread first = null!, second = null!;
        launch.Harness.Main.OnMain(() =>
        {
            project = model.AddProject(new Project("P", launch.Harness.NewFolder("project")).Url);
            first = model.NewThread(project)!;
            second = model.NewThread(project)!;
            model.UpdateThread(first.Id, thread => thread with { Title = "Alpha thread" });
            model.UpdateThread(second.Id, thread => thread with { Title = "Beta thread" });
            model.SelectedThreadID = first.Id;
        });
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        var row = RootViewRig.Text(rig.Panel, "Beta thread");

        rig.Click(row);

        Assert.Equal(second.Id, model.SelectedThreadID); // the thread is selected...
        Assert.Equal(0, rig.Closed); // ...and nothing closed or dismissed the panel
        Assert.NotNull(TopLevel.GetTopLevel(rig.Panel));
        Assert.True(rig.Panel.IsVisible);
        Assert.Single(rig.All<SidebarView>());
    });

    // ---- PR-SIDEBAR-145: the slide ----

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:104 .animation(dragOrigin == nil ? Chrome.panelSlide : nil, value: origin)
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:104 resting: origin changes animate with Chrome.panelSlide
    public void While_not_dragging_a_change_of_origin_slides_with_the_panel_slide() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), origin: new Point(488, 128));
        Assert.Equal(new Point(488, 128), rig.Frame.TopLeft);

        // The chat area narrows by 100: the resting origin is now clamped to 700 - 300 - 12 = 388.
        launch.Harness.Main.OnMain(() => rig.Panel.UpdateArea(new Size(700, 700)));
        rig.Layout();

        Assert.Equal(488, rig.Frame.X); // it has not jumped: the slide starts where the panel was
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration / 4));
        rig.Frames.Advance(TimeSpan.Zero);
        rig.Layout();
        Assert.InRange(rig.Frame.X, 388 + 0.001, 488 - 0.001); // part of the way
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
        rig.Frames.Advance(TimeSpan.Zero);
        rig.Layout();
        Assert.Equal(388, rig.Frame.X); // and it arrives
        Assert.Equal(128, rig.Frame.Y);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:34,104 while the strip is held the origin is the dragged one and no animation runs
    public void While_the_strip_is_held_the_origin_is_the_dragged_one_and_an_area_change_does_not_move_it() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(300, 0);
        Assert.Equal(new Point(312, 72), rig.Frame.TopLeft);

        launch.Harness.Main.OnMain(() => rig.Panel.UpdateArea(new Size(900, 700)));
        rig.Layout();

        Assert.Equal(new Point(312, 72), rig.Frame.TopLeft); // :34 dragOrigin ?? rest: the held origin wins
        rig.MouseReleaseBy(300, 0);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-148")] // App/Views/Sidebar/FloatingSidebarPanel.swift:36 inPopover: true, so the rows get no listFrame and no glide can fly (SidebarView.swift:1933-1934)
    [Trait("Req", "UI-SIDEBAR-045")] // App/Views/Sidebar/FloatingSidebarPanel.swift:36 the panel hosts SidebarView(inPopover: true, dismiss: nil): no glide can fly from it
    public void Reopening_a_settled_thread_in_the_panel_launches_no_glide() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        var model = launch.Manager.Model;
        var glides = new RowGlideAnimator(new FakeGeniePlatform()); // a glide would have to render the row, which this platform refuses
        using var animator = RowGlideAnimator.Shared.Override(glides);
        ChatThread settled = null!;
        launch.Harness.Main.OnMain(() =>
        {
            var project = model.AddProject(new Project("P", launch.Harness.NewFolder("project")).Url);
            settled = model.NewThread(project)!;
            model.UpdateThread(settled.Id, thread => thread with { Title = "Settled thread", IsSettled = true });
        });
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700));
        rig.Click(Assert.Single(rig.All<Button>(), button => ToolTip.GetTip(button) as string == "Show settled threads")); // the project's settled group is folded
        rig.Settle(); // the group opens on an animation
        var reopen = Assert.Single(rig.All<Button>(), button => ToolTip.GetTip(button) as string == "Reopen thread");

        rig.Click(reopen);

        Assert.False(model.Thread(settled.Id)!.IsSettled); // it reopened...
        Assert.Empty(glides.Glides); // ...without a ghost flying
        Assert.Equal(0, rig.Closed);
    });

    [Fact]
    [Trait("Req", "PR-SIDEBAR-145")] // App/Views/Sidebar/FloatingSidebarPanel.swift:104 `dragOrigin == nil ? Chrome.panelSlide : nil`: holding the strip ends the slide
    public void Pressing_and_dragging_the_strip_while_the_panel_slides_takes_it_over_with_no_animation() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var motion = Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), origin: new Point(488, 128));
        launch.Harness.Main.OnMain(() => rig.Panel.UpdateArea(new Size(700, 700))); // the resting origin becomes (388, 128): a slide starts
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration / 4));
        rig.Frames.Advance(TimeSpan.Zero);
        rig.Layout();
        Assert.InRange(rig.Frame.X, 388 + 0.001, 488 - 0.001); // part of the way

        rig.MousePress(rig.StripHandle);
        rig.MouseMoveBy(-10, 0);

        Assert.Equal(new Point(378, 128), rig.Frame.TopLeft); // :109 the drag starts from the resting origin (388) and :111 follows at once
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(Chrome.PanelSlide.Duration));
        rig.Frames.Advance(TimeSpan.Zero);
        rig.Layout();
        Assert.Equal(new Point(378, 128), rig.Frame.TopLeft); // the slide no longer moves it while it is held
        rig.MouseReleaseBy(-10, 0);
        Assert.Equal(new Point(378, 128), rig.Model.Settings.SidebarPanelOrigin);
    });

    [Fact]
    [Trait("Decision", "OD-0147")] // the panel's glass is drawn as the canvas's sidebar glass (Translucent sidebar reaches it), named beside the source's .regular effect
    public void The_panel_glass_is_the_canvas_sidebar_glass() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        using var rig = new FloatingSidebarPanelRig(launch, new Size(800, 700), theme: ThemeVariant.Dark);
        Assert.Equal(GlassSurface.Sidebar, SwiftGlass.GetSurface(rig.Surface));
        Assert.Null(SwiftGlass.GetEffect(rig.Surface)!.Surface);
    });
}
