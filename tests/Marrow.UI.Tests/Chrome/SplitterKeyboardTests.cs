using Avalonia.Input;
using Avalonia.Media;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Chrome;

// web W1 (DESIGN 4.2, 13.3): the shared Splitter's keyboard. Arrows 8 DIP (Shift 48), Home and End, Enter resets, Space
// toggles the collapsible side, Ctrl+Arrow toward it collapses and away restores; right-to-left mirrors the arrows; the
// focus ring and the size readout come with keyboard focus only.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w1-2")]
public sealed class SplitterKeyboardTests(HeadlessSession headless)
{
    [Fact]
    public void Arrows_move_eight_dip_and_shift_arrows_48_each_one_spring_and_one_commit() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        rig.FocusByKeyboard();
        rig.Key(Key.Right);
        Assert.Equal(276, rig.Value.Model.State.Value);
        Assert.True(rig.Value.IsAnimating); // the pane eases there
        rig.Settle();
        Assert.Equal(276, rig.Size, 3);
        rig.Key(Key.Left, RawInputModifiers.Shift);
        rig.Settle();
        Assert.Equal(228, rig.Size, 3);
        rig.Key(Key.Left, RawInputModifiers.Shift); // clamps at the minimum
        rig.Settle();
        Assert.Equal(210, rig.Size, 3);
        Assert.Equal(new double[] { 276, 228, 210 }, rig.Commits.Select(state => state.Value));
        rig.Key(Key.Up); // across the axis: nothing
        rig.Key(Key.Down);
        Assert.Equal(3, rig.Commits.Count);
    });

    [Fact]
    public void Home_and_End_go_to_the_minimum_and_maximum_and_Enter_resets_to_the_default() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        rig.FocusByKeyboard();
        rig.Key(Key.End);
        rig.Settle();
        Assert.Equal(440, rig.Size, 3);
        rig.Key(Key.Home);
        rig.Settle();
        Assert.Equal(210, rig.Size, 3);
        rig.Key(Key.Enter);
        rig.Settle();
        Assert.Equal(268, rig.Size, 3);
        Assert.Equal(new double[] { 440, 210, 268 }, rig.Commits.Select(state => state.Value));
    });

    [Fact]
    public void Space_toggles_the_collapsible_side_and_restores_the_size_it_had() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(start: 333);
        rig.FocusByKeyboard();
        rig.Key(Key.Space);
        Assert.True(rig.Value.Model.State.IsCollapsed);
        rig.Settle();
        Assert.Equal(0, rig.Size, 3);
        rig.Key(Key.Right); // arrows do not reopen a collapsed side
        Assert.True(rig.Value.Model.State.IsCollapsed);
        rig.Key(Key.Space);
        rig.Settle();
        Assert.Equal(333, rig.Size, 3);
        Assert.Equal(new[] { true, false }, rig.Commits.Select(state => state.IsCollapsed));
    });

    [Fact]
    public void Ctrl_arrow_toward_the_collapsible_side_collapses_it_and_away_restores_it() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(start: 300);
        rig.FocusByKeyboard();
        rig.Key(Key.Right, RawInputModifiers.Control); // away from an open side: nothing to restore
        Assert.Empty(rig.Commits);
        rig.Key(Key.Left, RawInputModifiers.Control);
        Assert.True(rig.Value.Model.State.IsCollapsed);
        rig.Key(Key.Left, RawInputModifiers.Control); // already collapsed
        Assert.Single(rig.Commits);
        rig.Key(Key.Right, RawInputModifiers.Control);
        rig.Settle();
        Assert.False(rig.Value.Model.State.IsCollapsed);
        Assert.Equal(300, rig.Size, 3);
    });

    [Fact]
    public void A_horizontal_splitter_whose_lower_pane_grows_upward_steps_with_up_and_down_and_collapses_with_ctrl_down() => headless.OnUiThread(() =>
    {
        var limits = new SplitterLimits(140, 760, 260) { Collapse = SplitterCollapse.BelowMinimum, CollapsedValue = 0, Growth = -1 };
        using var rig = new SplitterRig(SplitterOrientation.Horizontal, limits, start: 300, collapseSide: SplitterSide.Trailing);
        rig.FocusByKeyboard();
        rig.Key(Key.Up); // the edge moves up: the drawer below grows
        Assert.Equal(308, rig.Value.Model.State.Value);
        rig.Key(Key.Down, RawInputModifiers.Shift);
        Assert.Equal(260, rig.Value.Model.State.Value);
        rig.Key(Key.Left); // across the axis
        Assert.Equal(260, rig.Value.Model.State.Value);
        rig.Key(Key.Down, RawInputModifiers.Control); // toward the drawer: it closes
        Assert.True(rig.Value.Model.State.IsCollapsed);
        rig.Key(Key.Up, RawInputModifiers.Control);
        Assert.Equal(260, rig.Value.Model.State.Value);
    });

    [Fact]
    public void Right_to_left_mirrors_the_arrows_and_the_pointer_travel() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(flow: FlowDirection.RightToLeft);
        Assert.True(rig.Splitter.IsMirrored);
        rig.FocusByKeyboard();
        rig.Key(Key.Left); // the pane sits on the right: moving its edge left grows it
        Assert.Equal(276, rig.Value.Model.State.Value);
        rig.Key(Key.Right, RawInputModifiers.Control); // toward the pane: collapse
        Assert.True(rig.Value.Model.State.IsCollapsed);
        rig.Key(Key.Left, RawInputModifiers.Control);
        rig.Settle();
        Assert.Equal(276, rig.Size, 3);

        var at = rig.Centre;
        rig.Down(at);
        rig.Move(at + new Avalonia.Vector(-30, 0), RawInputModifiers.LeftMouseButton);
        Assert.Equal(306, rig.Size, 3);
        rig.Up(at + new Avalonia.Vector(-30, 0));

        // A splitter placed in window coordinates (a floating panel's grip) does not mirror.
        rig.Splitter.MirrorsInRightToLeft = false;
        Assert.False(rig.Splitter.IsMirrored);
    });

    [Fact]
    public void Keyboard_focus_shows_the_ring_the_pill_and_the_size_readout_and_losing_it_hides_them() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig();
        Assert.False(rig.Splitter.IsFocusVisible);
        rig.FocusByKeyboard();
        Assert.True(rig.Splitter.IsFocusVisible);
        Assert.True(rig.Splitter.Grip.HasFocusRing);
        Assert.True(rig.Splitter.Grip.IsShown);
        Assert.True(rig.Splitter.IsReadoutOpen);
        Assert.Equal("268 DIP", rig.Splitter.ReadoutText);
        rig.Key(Key.Right);
        Assert.Equal("276 DIP", rig.Splitter.ReadoutText);
        rig.Settle();
        Assert.Equal(1, rig.Splitter.Grip.RingOpacity, 3);

        var other = new Avalonia.Controls.Button { Content = "x" };
        rig.Root.Children.Add(other);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        other.Focus(NavigationMethod.Tab);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.False(rig.Splitter.IsFocusVisible);
        Assert.False(rig.Splitter.Grip.HasFocusRing);
        Assert.False(rig.Splitter.IsReadoutOpen);

        rig.Splitter.Focus(NavigationMethod.Pointer); // focus that came from a pointer shows no ring and no readout
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.True(rig.Splitter.IsFocused);
        Assert.False(rig.Splitter.IsFocusVisible);
        Assert.False(rig.Splitter.Grip.HasFocusRing);
        Assert.False(rig.Splitter.IsReadoutOpen);
    });

    [Fact]
    public void Under_Reduce_Motion_a_key_eases_out_in_0_18_seconds_and_a_drag_past_a_limit_stops_hard() => headless.OnUiThread(() =>
    {
        using var rig = new SplitterRig(reduceMotion: true);
        rig.FocusByKeyboard();
        rig.Key(Key.Right, RawInputModifiers.Shift);
        Assert.True(rig.Value.IsAnimating);
        var frames = 0;
        while (rig.Value.IsAnimating && frames < 100)
        {
            rig.Frame(10);
            frames++;
        }
        Assert.InRange(frames, 17, 19); // 0.18 s at 10 ms frames
        Assert.Equal(316, rig.Size, 6);
        Assert.All(rig.Drawn, value => Assert.InRange(value, 268, 316)); // an ease-out never overshoots

        var at = rig.Centre;
        rig.Down(at);
        rig.Move(at + rig.Along(200), RawInputModifiers.LeftMouseButton); // 76 past 440: no rubber band
        Assert.Equal(440, rig.Size, 6);
        rig.Up(at + rig.Along(200));
        Assert.False(rig.Value.IsAnimating);
        Assert.Equal(1, rig.Splitter.Grip.Scale, 6); // the pill fades only, it never scales
    });
}
