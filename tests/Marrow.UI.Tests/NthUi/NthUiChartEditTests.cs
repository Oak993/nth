using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Marrow.Core.Tests.Fixtures;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 "Editable charts: editable=points bind=name lets the user drag a line or bar point; the drag sets the bound state
// list, every let recomputes, and the rest of the view follows ... with no model call. Keyboard: focus a point, Up and Down
// change it by the axis step" and 10 (web surfaces W16b; ED-NEW-web-w16b-1): a drag moves the point under the pointer exactly
// while the state keeps the snapped, clamped value and everything that reads it follows in the same frame; the axis holds
// still under the finger; Up and Down, Page Up and Page Down change a point by the axis step; Left, Right, Home and End move
// between points, and the chart is one stop in the tab order; each point is a named range control.
[Collection("Headless")]
public sealed class NthUiChartEditTests(HeadlessSession headless)
{
    private const string Free = "card \"Free\"\n  state v = [3, 5, 4]\n  chart line x=\"a,b,c\" editable=points bind=v\n  stat \"Sum\" = sum(v)\n";

    private static double Demand(NthUiRig rig, int index) => rig.View.Runtime.Get("demand").Items[index].AsNumber;

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Dragging_a_point_follows_the_pointer_exactly_while_the_state_snaps_and_everything_that_reads_it_follows() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.True(plot.Model.Editable);
        Assert.Contains("148", rig.Texts());
        var at = plot.PointOf(0, 2)!.Value;
        rig.Window.MouseDown(rig.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
        Assert.True(plot.IsDragging);
        Assert.Equal(2, plot.DragIndex);
        rig.Window.MouseMove(rig.InWindow(plot, new Point(at.X, plot.HeightOf(40.4))), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        Assert.Equal(40, Demand(rig, 2));
        Assert.Equal(40.4, plot.ShownValue(0, 2)!.Value, .05);
        Assert.Contains("163", rig.Texts());
        Assert.Contains("40", rig.TextsOf("grid"));
        Assert.Equal("Mar: 40 units", plot.Readout!.Spoken);
        rig.Window.MouseMove(rig.InWindow(plot, new Point(at.X + 20, plot.HeightOf(10.2))), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        Assert.Equal(2, plot.DragIndex);
        Assert.Equal(10, Demand(rig, 2));
        Assert.Equal(12, Demand(rig, 0));
        rig.Window.MouseUp(rig.InWindow(plot, new Point(at.X + 20, plot.HeightOf(10.2))), MouseButton.Left, RawInputModifiers.None);
        Assert.False(plot.IsDragging);
        rig.Settle();
        Assert.Equal(10, plot.ShownValue(0, 2)!.Value, 3);
        Assert.Contains("133", rig.Texts());
        Assert.Equal(0, rig.Frames.Pending);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_dot_settles_onto_the_kept_value_by_spring_when_the_pointer_lets_go() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var at = plot.PointOf(0, 4)!.Value;
        rig.Window.MouseDown(rig.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
        var to = new Point(at.X, plot.HeightOf(44.4));
        rig.Window.MouseMove(rig.InWindow(plot, to), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        rig.Window.MouseUp(rig.InWindow(plot, to), MouseButton.Left, RawInputModifiers.None);
        var first = plot.ShownValue(0, 4)!.Value;
        Assert.Equal(44.4, first, .05);
        rig.Tick();
        var second = plot.ShownValue(0, 4)!.Value;
        Assert.InRange(second, 44.0, 44.4);
        rig.Settle();
        Assert.Equal(44, plot.ShownValue(0, 4)!.Value, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_axis_holds_still_under_the_finger_and_the_point_cannot_leave_it_then_the_axis_makes_room() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(Free, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        Assert.Equal((0.0, 8.0), plot.ShownRange);
        var at = plot.PointOf(0, 1)!.Value;
        rig.Window.MouseDown(rig.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
        rig.Window.MouseMove(rig.InWindow(plot, new Point(at.X, -50)), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        Assert.Equal(8, rig.View.Runtime.Get("v").Items[1].AsNumber);
        rig.Run(.5);
        Assert.Equal((0.0, 8.0), plot.ShownRange);
        rig.Window.MouseUp(rig.InWindow(plot, new Point(at.X, -50)), MouseButton.Left, RawInputModifiers.None);
        rig.Run(5 * NthUiRig.Frame);
        var (_, midway) = plot.ShownRange;
        Assert.InRange(midway, 8.01, 9.99);
        rig.Settle();
        Assert.Equal((0.0, 10.0), plot.ShownRange);
        Assert.Equal(8, plot.ShownValue(0, 1)!.Value, 3);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void The_state_keeps_the_axis_step_the_bounds_the_author_set_and_a_press_picks_the_column_under_it() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var at = plot.PointOf(0, 0)!.Value;
        var column = plot.PointOf(0, 3)!.Value;
        rig.Window.MouseDown(rig.InWindow(plot, new Point(column.X + 6, plot.HeightOf(55.4))), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(3, plot.DragIndex);
        rig.Window.MouseMove(rig.InWindow(plot, new Point(column.X, plot.HeightOf(75))), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        Assert.Equal(60, Demand(rig, 3));
        rig.Window.MouseMove(rig.InWindow(plot, new Point(column.X, plot.HeightOf(-20))), RawInputModifiers.LeftMouseButton);
        rig.Pump();
        Assert.Equal(0, Demand(rig, 3));
        rig.Window.MouseUp(rig.InWindow(plot, new Point(column.X, plot.HeightOf(-20))), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(12, Demand(rig, 0));
        Assert.True(at.X < column.X);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Up_and_Down_change_a_focused_point_by_the_axis_step_and_Page_Up_and_Down_by_ten() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var handle = plot.Handles[2];
        handle.Focus(NavigationMethod.Tab);
        rig.Pump();
        Assert.True(handle.IsKeyboardFocusWithin);
        rig.Key(handle, Key.Up);
        Assert.Equal(26, Demand(rig, 2));
        Assert.Contains("149", rig.Texts());
        Assert.Equal("Mar: 26 units", plot.Readout!.Spoken);
        rig.Key(handle, Key.Down);
        rig.Key(handle, Key.Down);
        Assert.Equal(24, Demand(rig, 2));
        rig.Key(handle, Key.PageUp);
        Assert.Equal(34, Demand(rig, 2));
        rig.Key(handle, Key.PageDown);
        rig.Key(handle, Key.PageDown);
        rig.Key(handle, Key.PageDown);
        Assert.Equal(4, Demand(rig, 2));
        rig.Settle();
        Assert.Equal(4, plot.ShownValue(0, 2)!.Value, 3);
        for (var i = 0; i < 5; i++) rig.Key(handle, Key.PageUp);
        for (var i = 0; i < 6; i++) rig.Key(handle, Key.Up);
        Assert.Equal(60, Demand(rig, 2));
        rig.Key(handle, Key.Up);
        Assert.Equal(60, Demand(rig, 2));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Left_Right_Home_and_End_move_between_points_and_the_chart_is_one_tab_stop() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var handles = plot.Handles;
        Assert.Equal(6, handles.Count);
        Assert.Equal(1, handles.Count(h => h.IsTabStop));
        handles[2].Focus(NavigationMethod.Tab);
        rig.Pump();
        rig.Key(handles[2], Key.Right);
        Assert.True(handles[3].IsKeyboardFocusWithin);
        Assert.Equal([false, false, false, true, false, false], handles.Select(h => h.IsTabStop));
        rig.Key(handles[3], Key.End);
        Assert.True(handles[5].IsKeyboardFocusWithin);
        rig.Key(handles[5], Key.Right);
        Assert.True(handles[5].IsKeyboardFocusWithin);
        rig.Key(handles[5], Key.Home);
        Assert.True(handles[0].IsKeyboardFocusWithin);
        rig.Key(handles[0], Key.Left);
        Assert.True(handles[0].IsKeyboardFocusWithin);
        rig.Key(handles[0], Key.Right);
        Assert.True(handles[1].IsKeyboardFocusWithin);
        Assert.Equal((0, 1), (plot.Active!.Value.Series, plot.Active.Value.Index));
        Assert.Equal("Feb: 18 units", plot.Readout!.Spoken);
        Assert.Equal([12.0, 18, 25, 30, 28, 35], rig.View.Runtime.Get("demand").Items.Select(v => v.AsNumber));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Each_point_is_a_named_range_control_with_the_axis_step_and_the_value_the_reader_sees() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig(NthUiView.Show(NthUiSamples.EditableChart, culture: NthUiRig.English));
        rig.Settle();
        var plot = rig.Chart().Plot!;
        var handle = plot.Handles[3];
        var peer = ControlAutomationPeer.CreatePeerForElement(handle);
        Assert.Equal(AutomationControlType.Slider, peer.GetAutomationControlType());
        Assert.Equal("Apr", peer.GetName());
        Assert.Equal("30 units", AutomationProperties.GetItemStatus(handle));
        Assert.Equal(NthUiStrings.PointHint, AutomationProperties.GetHelpText(handle));
        var range = Assert.IsAssignableFrom<IRangeValueProvider>(peer);
        Assert.Equal((0d, 60d, 30d, 1d), (range.Minimum, range.Maximum, range.Value, range.SmallChange));
        Assert.Equal(10, range.LargeChange / range.SmallChange);
        Assert.Equal(AutomationControlType.Group, ControlAutomationPeer.CreatePeerForElement(plot).GetAutomationControlType());
        rig.Key(handle, Key.Up);
        Assert.Equal(31d, range.Value);
        Assert.Equal("31 units", AutomationProperties.GetItemStatus(handle));
        Assert.Equal(plot.Handles.Select((_, i) => rig.Chart().Model.Label(i)), plot.Handles.Select(h => ControlAutomationPeer.CreatePeerForElement(h).GetName()));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void An_editable_bar_chart_drags_by_its_top_and_a_chart_that_is_not_editable_ignores_the_pointer() => headless.OnUiThread(() =>
    {
        using var bars = new NthUiRig(NthUiView.Show("card \"Bars\"\n  state v = [3, 5, 4]\n  chart bar x=\"a,b,c\" editable=points bind=v min=0 max=10\n  stat \"Sum\" = sum(v)\n", culture: NthUiRig.English));
        bars.Settle();
        var plot = bars.Chart().Plot!;
        Assert.True(plot.Model.Editable);
        Assert.Equal(3, plot.Handles.Count);
        var at = plot.PointOf(0, 1)!.Value;
        bars.Window.MouseDown(bars.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
        bars.Window.MouseMove(bars.InWindow(plot, new Point(at.X, plot.HeightOf(8))), RawInputModifiers.LeftMouseButton);
        bars.Pump();
        Assert.Equal(8, bars.View.Runtime.Get("v").Items[1].AsNumber);
        bars.Window.MouseUp(bars.InWindow(plot, new Point(at.X, plot.HeightOf(8))), MouseButton.Left, RawInputModifiers.None);
        Assert.Contains("15", bars.Texts());

        using var fixedLine = new NthUiRig(NthUiView.Show("chart line x=\"a,b,c\" y=[3, 5, 4]\n", culture: NthUiRig.English));
        fixedLine.Settle();
        var still = fixedLine.Chart().Plot!;
        Assert.Empty(still.Handles);
        var mark = still.PointOf(0, 1)!.Value;
        fixedLine.Window.MouseDown(fixedLine.InWindow(still, mark), MouseButton.Left, RawInputModifiers.None);
        Assert.False(still.IsDragging);
        fixedLine.Window.MouseMove(fixedLine.InWindow(still, new Point(mark.X, still.HeightOf(1))), RawInputModifiers.LeftMouseButton);
        fixedLine.Window.MouseUp(fixedLine.InWindow(still, mark), MouseButton.Left, RawInputModifiers.None);
        Assert.Equal(5, still.ShownValue(0, 1)!.Value, 3);
    });
}
