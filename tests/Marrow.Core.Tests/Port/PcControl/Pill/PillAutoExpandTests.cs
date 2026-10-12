// PORT-ONLY: auto-expand (OD-0145, ED-NEW-pcctl-13; DESIGN.md 10.5, 10.11): armed at start, a new ask expands once; Later
// disarms; an empty queue re-arms; an owed expand waits for the drag, the travel, the hover and a held button.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Tests.Port.PcControl.Pill;

[Trait("Decision", "ED-NEW-pcctl-13")]
public sealed class PillAutoExpandTests
{
    [Fact]
    public void Armed_at_start_a_new_ask_expands_the_card_once()
    {
        var policy = new PillAutoExpand();
        Assert.True(policy.Armed);
        Assert.False(policy.ShouldExpand(false, false, false, false));

        policy.AskArrived();
        Assert.True(policy.ShouldExpand(false, false, false, false));
        policy.Expanded();
        Assert.False(policy.ShouldExpand(false, false, false, false));
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    public void It_waits_while_dragged_travelling_hovered_or_a_button_is_held_then_expands(bool dragging, bool travelling, bool hovered, bool held)
    {
        var policy = new PillAutoExpand();
        policy.AskArrived();

        Assert.False(policy.ShouldExpand(dragging, travelling, hovered, held));
        Assert.True(policy.ShouldExpand(false, false, false, false));
    }

    [Fact]
    public void Later_disarms_it_until_every_ask_is_answered_or_withdrawn()
    {
        var policy = new PillAutoExpand();
        policy.AskArrived();
        policy.Later();

        Assert.False(policy.Armed);
        policy.AskArrived();
        Assert.False(policy.ShouldExpand(false, false, false, false));
        policy.QueueChanged(1);
        Assert.False(policy.Armed);
        policy.QueueChanged(0);
        Assert.True(policy.Armed);
        policy.AskArrived();
        Assert.True(policy.ShouldExpand(false, false, false, false));
    }
}
