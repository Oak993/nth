using Marrow.Core.Accounts;
using Marrow.Testing.MultiAccountSettings;

namespace Marrow.Core.Tests.Port.MultiAccount.Thread;

// MA-10 (step 3b, job 0): the seam the cards bind to (it mirrors the routing the app will have: Changed, State, AnswerAsync), and its fake. The engine's own event and request types are used as they are.
[Trait("Feature", "MA-10")]
[Trait("Rule", "R-TEST-4")]
public sealed class CardPortTests
{
    private static readonly Guid Chat = Guid.NewGuid();

    private static HandshakeEvent Ask(Guid thread, params DecisionKind[] kinds) => new(thread, HandshakeEventKind.DecisionRequested, HandshakePhase.Asking, HandshakePhase.Asking, "a", "b", HandshakeReason.HardLimit, DateTimeOffset.UnixEpoch,
        Request: new DecisionRequest(thread, kinds, true, "a", "b", DateTimeOffset.UnixEpoch.AddHours(3)));

    [Fact]
    public void An_event_is_told_to_the_listeners_and_is_the_threads_state_until_another_replaces_it()
    {
        var port = new FakeCardPort();
        var seen = new List<HandshakeEvent>();
        port.Changed += (_, e) => seen.Add(e);
        Assert.Null(port.State(Chat));
        var first = Ask(Chat, DecisionKind.OnLimit);
        port.Raise(first);
        Assert.Same(first, port.State(Chat));
        var second = first with { Kind = HandshakeEventKind.PhaseChanged, Request = null, To = HandshakePhase.Standby };
        port.Raise(second);
        Assert.Equal([first, second], seen);
        Assert.Same(second, port.State(Chat));
        Assert.Null(port.State(Guid.NewGuid()));
    }

    [Fact]
    public async Task An_answer_hands_the_choices_and_the_one_remember_line_over_and_the_card_is_answered()
    {
        var port = new FakeCardPort();
        var seen = new List<HandshakeEvent>();
        port.Changed += (_, e) => seen.Add(e);
        port.Raise(Ask(Chat, DecisionKind.OnLimit, DecisionKind.Bring));
        Choice[] choices = [new(DecisionKind.OnLimit, DecisionValues.Switch), new(DecisionKind.Bring, DecisionValues.Full)];
        await port.AnswerAsync(Chat, choices, RememberScope.ThisThread);
        var answer = Assert.Single(port.Answers);
        Assert.Equal((Chat, RememberScope.ThisThread), (answer.Thread, answer.Scope));
        Assert.Equal(choices, answer.Choices);
        Assert.Null(port.State(Chat)?.Request);
        Assert.Equal(1, seen.Count(e => e.Request is null));
    }

    [Fact]
    public async Task Nothing_is_answered_before_a_click_and_a_card_that_is_not_waiting_refuses_an_answer()
    {
        var port = new FakeCardPort();
        port.Raise(Ask(Chat, DecisionKind.OnLimit));
        Assert.Empty(port.Answers);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.AnswerAsync(Guid.NewGuid(), [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime));
        await port.AnswerAsync(Chat, [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime);
        await Assert.ThrowsAsync<InvalidOperationException>(() => port.AnswerAsync(Chat, [new(DecisionKind.OnLimit, DecisionValues.Wait)], RememberScope.JustThisTime));
        Assert.Single(port.Answers);
    }

    [Fact]
    public async Task A_held_answer_waits_for_its_release_so_a_test_can_look_at_the_card_meanwhile()
    {
        var port = new FakeCardPort();
        port.Raise(Ask(Chat, DecisionKind.OnLimit));
        var hold = new TaskCompletionSource();
        port.AnswerHold = hold.Task;
        var pending = port.AnswerAsync(Chat, [new(DecisionKind.OnLimit, DecisionValues.Switch)], RememberScope.JustThisTime);
        Assert.False(pending.IsCompleted);
        hold.SetResult();
        await pending;
        Assert.Single(port.Answers);
    }
}
