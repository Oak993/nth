using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-9 (DESIGN.md rev 5, 13.1; ED-NEW-dextra-as9-2): the queue tells a listener of each card put in front of the owner, once, and of nothing else:
// not of a card already waiting with the same binding, not of an answer, a hold, a release or a withdrawal. The toast listens to this.
[Trait("Feature", "AS-9")]
[Trait("Decision", "ED-NEW-dextra-as9-2")]
public sealed class DecisionQueueRaisedTests
{
    private readonly ManualTimeProvider clock = new();

    [Fact]
    public void Raised_fires_once_for_each_new_card_with_the_card_as_the_queue_holds_it_before_changed()
    {
        var queue = new DecisionQueue(clock);
        var order = new List<string>();
        var seen = new List<DecisionItem>();
        queue.Raised += card => { order.Add("raised"); seen.Add(card); };
        queue.Changed += () => order.Add("changed");
        var raised = queue.Raise(DecisionToastRig.Approval(), (_, _) => { });
        Assert.Equal(["raised", "changed"], order);
        var heard = Assert.Single(seen);
        Assert.Equal(raised.Id, heard.Id);
        Assert.Equal(DecisionState.Open, heard.State);
        Assert.Equal(clock.GetUtcNow(), heard.Raised);
    }

    [Fact]
    public void A_card_already_waiting_with_the_same_binding_raises_nothing_again()
    {
        var queue = new DecisionQueue(clock);
        var heard = 0;
        queue.Raised += _ => heard++;
        var card = DecisionToastRig.Approval(request: "r1");
        queue.Raise(card, (_, _) => { });
        queue.Raise(card with { Id = Guid.NewGuid() }, (_, _) => { });
        Assert.Equal(1, heard);
    }

    [Fact]
    public void An_answer_a_hold_a_release_and_a_withdrawal_raise_nothing()
    {
        var queue = new DecisionQueue(clock);
        var heard = 0;
        var a = queue.Raise(DecisionToastRig.Approval("a"), (_, _) => { });
        var b = queue.Raise(DecisionToastRig.Approval("b"), (_, _) => { });
        var c = queue.Raise(DecisionToastRig.Approval("c"), (_, _) => { });
        queue.Raised += _ => heard++;
        Assert.Null(queue.Answer(a.Id, DecisionAnswer.AllowOnce));
        queue.Hold(card => card.Id == b.Id, DecisionHold.Stop);
        queue.Release(card => card.Id == b.Id);
        Assert.True(queue.Withdraw(c.Id, "gone"));
        Assert.Equal(0, heard);
    }

    [Fact]
    public void A_card_renewed_because_its_inputs_changed_while_held_is_raised_anew()
    {
        var queue = new DecisionQueue(clock);
        var card = queue.Raise(DecisionToastRig.Approval("Send it?", actionClass: ActionClass.Outward), (_, _) => { });
        queue.Hold(c => c.Id == card.Id, DecisionHold.Stop);
        var heard = new List<DecisionItem>();
        queue.Raised += heard.Add;
        queue.Release(c => c.Id == card.Id, _ => new Marrow.Core.Port.ValueList<DecisionInput>([new DecisionInput("Command", "Send it to someone else?")]));
        var renewed = Assert.Single(heard);
        Assert.NotEqual(card.Id, renewed.Id);
        Assert.Equal(DecisionState.Open, renewed.State);
    }

    [Fact]
    public void A_listener_is_called_outside_the_queues_lock_so_it_may_ask_the_queue()
    {
        var queue = new DecisionQueue(clock);
        IReadOnlyList<DecisionItem>? waiting = null;
        queue.Raised += card => waiting = queue.Waiting;
        var raised = queue.Raise(DecisionToastRig.Approval(), (_, _) => { });
        Assert.Equal([raised], waiting);
    }
}
