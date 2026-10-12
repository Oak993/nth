// PORT-ONLY: a press on the pill (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.3, 10.11): a press on a button never becomes a drag
// at any wobble and fires only if released on it; elsewhere the threshold (mouse: the larger of the system's and 4 DIP;
// pen and touch: 10 DIP) re-bases the grab point so nothing jumps; a drag's release is swallowed; a keyboard activation is
// never swallowed.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillPressTests
{
    private static readonly PillVector Pill = new(800, 8);

    [Fact]
    public void A_press_on_a_button_never_drags_however_far_it_wobbles()
    {
        var press = new PillPress(new PillVector(1100, 30), Pill, onButton: true, PillPointerKind.Mouse);

        foreach (var wobble in new[] { 3.0, 12, 60, 400 })
        {
            Assert.Null(press.Move(new PillVector(1100 + wobble, 30 - wobble)));
        }
        Assert.False(press.Dragging);
        Assert.Equal(PillPressOutcome.Button, press.Release(stillOnButton: true));
        Assert.Equal(PillPressOutcome.Cancelled, press.Release(stillOnButton: false));
    }

    [Theory]
    [InlineData(PillPointerKind.Mouse, 4, 4)]
    [InlineData(PillPointerKind.Mouse, 2, 4)]
    [InlineData(PillPointerKind.Mouse, 6, 6)]
    [InlineData(PillPointerKind.Pen, 4, 10)]
    [InlineData(PillPointerKind.Touch, 4, 10)]
    public void The_drag_threshold_depends_on_the_pointer(PillPointerKind kind, double system, double threshold) =>
        Assert.Equal(threshold, new PillPress(PillVector.Zero, Pill, false, kind, system).Threshold);

    [Fact]
    public void At_the_threshold_the_grab_point_is_rebased_so_the_pill_starts_from_rest_and_then_tracks_one_to_one()
    {
        var press = new PillPress(new PillVector(900, 30), Pill, onButton: false, PillPointerKind.Mouse);

        Assert.Null(press.Move(new PillVector(902, 31)));
        var first = press.Move(new PillVector(905, 32));
        Assert.True(press.Dragging);
        Assert.Equal(Pill, first);
        Assert.Equal(new PillVector(905, 32), press.Grab);
        Assert.Equal(new PillVector(Pill.X + 40, Pill.Y - 5), press.Move(new PillVector(945, 27)));
        Assert.Equal(PillPressOutcome.DragEnd, press.Release(stillOnButton: false));
    }

    [Fact]
    public void Under_the_threshold_the_release_is_the_pills_click_and_a_keyboard_activation_is_never_swallowed()
    {
        var press = new PillPress(new PillVector(900, 30), Pill, onButton: false, PillPointerKind.Touch);

        Assert.Null(press.Move(new PillVector(908, 34)));
        Assert.Equal(PillPressOutcome.Click, press.Release(stillOnButton: false));
        Assert.False(PillPress.SwallowsKeyboardActivation);
    }
}
