using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// web W1 (DESIGN 4.2, 13.3): the shared Splitter under a real headless pointer: 1:1 drags at frame granularity, the rubber
// band, collapse and restore, the double-click reset, pointer-down settling, hover timing, slop and focus.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-2")]
public sealed class SplitterPointerTests(HeadlessSession headless)
{
    [Fact]
    public void A_drag_follows_the_pointer_one_to_one_on_every_frame_and_commits_once_on_release() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var start = rig.Centre;
        rig.Down(start);
        Assert.Equal(268, rig.Size);
        foreach (var travel in new[] { 1.0, 7, 40, 95, 120, -30, -58 })
        {
            rig.Move(start + rig.Along(travel), RawInputModifiers.LeftMouseButton);
            Assert.Equal(268 + travel, rig.Size); // drawn at once, no spring between the pointer and the edge
            rig.Frame();
            Assert.Equal(268 + travel, rig.Size); // and the next frame does not move it
            Assert.False(rig.Value.IsAnimating);
        }
        Assert.True(rig.Splitter.IsDragging);
        Assert.Empty(rig.Commits); // nothing is written while dragging
        rig.Up(start + rig.Along(-58));
        Assert.False(rig.Splitter.IsDragging);
        Assert.Equal(new SplitterState(210), Assert.Single(rig.Commits));
        Assert.Equal(210, rig.Size);
    });

    [Fact]
    public void A_mouse_drag_begins_after_one_dip_and_a_click_without_movement_changes_nothing() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var start = rig.Centre;
        rig.Down(start);
        rig.Move(start + rig.Along(0.5), RawInputModifiers.LeftMouseButton);
        Assert.False(rig.Splitter.IsDragging); // under the 1 DIP mouse slop
        rig.Up(start + rig.Along(0.5));
        Assert.Empty(rig.Commits);
        Assert.Equal(268, rig.Size);

        var lower = start + new Vector(0, 120); // further along the divider: not the second click of a double-click
        rig.Down(lower);
        rig.Move(lower + rig.Along(1), RawInputModifiers.LeftMouseButton);
        Assert.True(rig.Splitter.IsDragging);
        Assert.Equal(269, rig.Size); // the slop is not lost: the edge is where the pointer is
        rig.Up(lower + rig.Along(1));
        Assert.Equal(269, Assert.Single(rig.Commits).Value);
    });

    [Fact]
    public void Past_a_limit_the_edge_gives_three_tenths_of_the_travel_up_to_24_dip_and_springs_back_on_release() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(start: 400);
        var start = rig.Centre;
        rig.Down(start);
        rig.Move(start + rig.Along(60), RawInputModifiers.LeftMouseButton); // 20 past 440
        Assert.Equal(446, rig.Size, 6);
        rig.Move(start + rig.Along(240), RawInputModifiers.LeftMouseButton); // 200 past: the give stops at 24
        Assert.Equal(464, rig.Size, 6);
        Assert.Equal(440, rig.Value.Model.State.Value); // the value itself never leaves its range

        rig.Drawn.Clear();
        rig.Up(start + rig.Along(240));
        Assert.Equal(440, Assert.Single(rig.Commits).Value);
        Assert.True(rig.Value.IsAnimating); // a spring, not a cut
        var frames = rig.Settle();
        Assert.InRange(frames, 5, 120);
        Assert.Equal(440, rig.Size, 3);
        Assert.All(rig.Drawn, value => Assert.InRange(value, 439.5, 464.0001));
        for (var i = 1; i < rig.Drawn.Count; i++) Assert.True(rig.Drawn[i] <= rig.Drawn[i - 1] + 0.05, "the band returns without bouncing back out");
    });

    [Fact]
    public void Dragged_56_dip_past_the_minimum_the_pane_collapses_with_a_spring_and_dragging_back_restores_it_under_the_pointer() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var start = rig.Centre;
        rig.Down(start);
        rig.Move(start + rig.Along(-114), RawInputModifiers.LeftMouseButton); // 154: exactly 56 under 210, not past it
        Assert.False(rig.Value.Model.State.IsCollapsed);
        rig.Move(start + rig.Along(-115), RawInputModifiers.LeftMouseButton);
        Assert.True(rig.Value.Model.State.IsCollapsed);
        Assert.True(rig.Value.IsAnimating); // it slides away, it does not vanish
        rig.Settle();
        Assert.Equal(0, rig.Size, 3);

        rig.Move(start + rig.Along(-20), RawInputModifiers.LeftMouseButton); // back over the open range
        Assert.False(rig.Value.Model.State.IsCollapsed);
        rig.Settle();
        Assert.Equal(248, rig.Size, 3);
        rig.Move(start + rig.Along(-10), RawInputModifiers.LeftMouseButton); // tracking resumes 1:1 once it has caught up
        Assert.Equal(258, rig.Size, 3);
        rig.Up(start + rig.Along(-10));
        Assert.Equal(new SplitterState(258), Assert.Single(rig.Commits));

        // Released collapsed: the commit says so and keeps the size to come back to.
        var again = rig.Centre;
        rig.Down(again);
        rig.Move(again + rig.Along(-200), RawInputModifiers.LeftMouseButton);
        rig.Up(again + rig.Along(-200));
        var collapsed = rig.Commits[^1];
        Assert.True(collapsed.IsCollapsed);
        Assert.Equal(0, collapsed.Value);
        Assert.Equal(258, collapsed.BeforeCollapse);
    });

    [Fact]
    public void A_double_click_springs_to_the_default_and_a_press_lands_any_spring_still_in_flight() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(start: 400);
        var at = rig.Centre;
        rig.Down(at);
        rig.Up(at);
        Assert.Empty(rig.Commits);
        rig.Window.MouseDown(at, MouseButton.Left, RawInputModifiers.LeftMouseButton); // the second press of a double-click
        rig.Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(268, Assert.Single(rig.Commits).Value);
        Assert.True(rig.Value.IsAnimating);
        rig.Frame();
        rig.Frame();
        var midway = rig.Size;
        Assert.InRange(midway, 268.5, 399.5); // on its way, not cut

        // Pointer-down settling: the next press lands the spring before anything else, in that same event.
        var press = rig.Centre;
        rig.Window.MouseDown(press, MouseButton.Left, RawInputModifiers.LeftMouseButton);
        Dispatcher.UIThread.RunJobs();
        Assert.False(rig.Value.IsAnimating);
        Assert.Equal(268, rig.Size, 6);
        rig.Move(press + rig.Along(12), RawInputModifiers.LeftMouseButton); // and the drag starts from the settled layout
        Assert.Equal(280, rig.Size, 6);
        rig.Up(press + rig.Along(12));
    });

    [Fact]
    public void Hovering_shows_the_pill_after_150_ms_and_the_Resize_tooltip_400_ms_later_and_leaving_cancels_both() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var at = rig.Centre;
        rig.Move(at);
        Assert.False(rig.Splitter.Grip.IsShown);
        rig.Wait(140);
        Assert.False(rig.Splitter.Grip.IsShown);
        rig.Wait(20);
        Assert.True(rig.Splitter.Grip.IsShown);
        Assert.False(rig.Splitter.IsTipOpen);
        rig.Wait(370); // 530 ms since entering
        Assert.False(rig.Splitter.IsTipOpen);
        rig.Wait(30);
        Assert.True(rig.Splitter.IsTipOpen);
        Assert.Equal("Resize", Assert.IsType<TextBlock>(rig.Splitter.TipChip.Child).Text);

        rig.Move(at + rig.Along(200)); // away
        Assert.False(rig.Splitter.IsTipOpen);
        Assert.False(rig.Splitter.Grip.IsShown);

        // A pass over the splitter shorter than the delay shows nothing.
        rig.Move(at);
        rig.Wait(100);
        rig.Move(at + rig.Along(200));
        rig.Wait(600);
        Assert.False(rig.Splitter.Grip.IsShown);
        Assert.False(rig.Splitter.IsTipOpen);
    });

    [Fact]
    public void A_press_shows_the_pill_at_once_hides_the_tooltip_and_a_drag_brightens_and_lengthens_it() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var at = rig.Centre;
        rig.Move(at);
        rig.Wait(600);
        Assert.True(rig.Splitter.IsTipOpen);
        rig.Down(at);
        Assert.False(rig.Splitter.IsTipOpen);
        Assert.True(rig.Splitter.Grip.IsShown);
        rig.Move(at + rig.Along(20), RawInputModifiers.LeftMouseButton);
        Assert.True(rig.Splitter.Grip.IsDragging);
        rig.Settle();
        Assert.Equal(SplitterGrip.DragLength, rig.Splitter.Grip.Length, 3);
        Assert.Equal(1, rig.Splitter.Grip.Brightness, 3);
        rig.Up(at + rig.Along(20));
        rig.Settle();
        Assert.False(rig.Splitter.Grip.IsDragging);
        Assert.Equal(SplitterGrip.RestLength, rig.Splitter.Grip.Length, 3);
        Assert.Equal(0, rig.Splitter.Grip.Brightness, 3);
    });

    [Fact]
    public void A_press_never_takes_keyboard_focus_from_where_it_is() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        var other = new TextBox { Width = 120, Height = 30, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        rig.Root.Children.Add(other);
        Dispatcher.UIThread.RunJobs();
        other.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.True(other.IsFocused);

        var at = rig.Centre;
        rig.Down(at);
        rig.Move(at + rig.Along(10), RawInputModifiers.LeftMouseButton);
        rig.Up(at + rig.Along(10));
        Assert.True(other.IsFocused);
        Assert.False(rig.Splitter.IsFocused);
        Assert.False(rig.Splitter.IsFocusVisible);
        Assert.True(rig.Splitter.Focusable); // still reachable by Tab
    });

    [Fact]
    public void The_hit_area_is_ten_dip_for_a_mouse_and_the_full_twelve_for_touch_with_a_four_dip_slop() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        Assert.Equal(Splitter.DividerThickness, rig.Splitter.Bounds.Width);
        Assert.Equal(new Thickness(1, 0), rig.Splitter.Handle.Margin); // a fine pointer hits the middle 10 DIP
        SplitterPointer.SetIsCoarse(rig.Window, true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(default, rig.Splitter.Handle.Margin);
        SplitterPointer.SetIsCoarse(rig.Window, false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new Thickness(1, 0), rig.Splitter.Handle.Margin);
        Assert.Equal(Splitter.MouseSlop, rig.Splitter.Handle.Slop);
        Assert.Equal(4, Splitter.CoarseSlop);
        Assert.Equal(0.5, Splitter.LongPressSeconds);
    });

    [Fact]
    public void The_cursor_is_the_axis_resize_cursor_and_the_corner_cursor_follows_the_corner() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        Assert.Equal("SizeWestEast", rig.Splitter.Handle.HandleCursor!.ToString());
        var horizontal = new Splitter(SplitterOrientation.Horizontal, new SplitterTarget());
        Assert.Equal("SizeNorthSouth", horizontal.Handle.HandleCursor!.ToString());
        var corner = new Splitter(SplitterOrientation.Both, new SplitterTarget(), SplitterCorner.BottomRight);
        Assert.Equal("BottomRightCorner", corner.Handle.HandleCursor!.ToString());
        corner.SetCorner(SplitterCorner.BottomLeft);
        Assert.Equal("BottomLeftCorner", corner.Handle.HandleCursor!.ToString());
        corner.SetCorner(SplitterCorner.TopLeft);
        Assert.Equal("BottomRightCorner", corner.Handle.HandleCursor!.ToString());
        Assert.Equal(SplitterCorner.TopLeft, corner.Grip.Corner);
    });

    [Fact]
    public void An_inactive_splitter_takes_no_drag_no_focus_and_shows_no_grip() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        rig.Splitter.IsActive = false;
        var at = rig.Centre;
        rig.Move(at);
        rig.Wait(600);
        Assert.False(rig.Splitter.Grip.IsShown);
        rig.Down(at);
        rig.Move(at + rig.Along(40), RawInputModifiers.LeftMouseButton);
        rig.Up(at + rig.Along(40));
        Assert.Empty(rig.Commits);
        Assert.Equal(268, rig.Size);
        Assert.False(rig.Splitter.Focusable);
        Assert.False(rig.Splitter.IsTabStop);
    });
}
