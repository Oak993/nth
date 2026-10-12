using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-5 (DESIGN.md rev 5, 4.1, 4.2, 12.6, 12.6b; ED-NEW-dextra-as5-1, -9): the queue holds every card until the
// owner answers it, never answers one itself, refuses an answer the card does not take, holds cards on Stop and Pause as
// 4.2 says, and raises a held card again bound to its inputs (withdrawn and raised anew when they changed).
[Trait("Decision", "ED-NEW-dextra-as5-9")]
[Trait("Feature", "AS-5")]
public sealed class DecisionQueueTests
{
    private static readonly Guid Mission = Guid.Parse("60000000-0000-4000-8000-000000000006");

    private readonly ManualTimeProvider clock = new();

    private static DecisionItem Card(DecisionWeight weight = DecisionWeight.Reversible, string title = "Run npm test?", Guid? mission = null, string? request = "r1") =>
        new(Guid.NewGuid(), weight == DecisionWeight.Question ? DecisionKind.Question : DecisionKind.Approval, weight, title)
        {
            Inputs = new ValueList<DecisionInput>([new DecisionInput("Command", title)]),
            AssistantId = "dextra",
            MissionId = mission ?? Mission,
            ThreadId = Guid.Parse("60000000-0000-4000-8000-000000000001"),
            RequestId = request,
        };

    private static DecisionItem Merge(Guid? mission = null) =>
        new(Guid.NewGuid(), DecisionKind.Merge, DecisionWeight.Irreversible, "Merge?") { AssistantId = "dextra", MissionId = mission ?? Mission };

    [Fact]
    public void A_card_waits_until_the_owner_answers_it_and_then_runs_its_action_once()
    {
        var queue = new DecisionQueue(clock);
        var changes = 0;
        queue.Changed += () => changes++;
        var answers = new List<DecisionAnswer>();
        var card = queue.Raise(Card(), (_, answer) => answers.Add(answer));
        Assert.Equal(DecisionState.Open, card.State);
        Assert.Equal(clock.GetUtcNow(), card.Raised);
        Assert.Equal([card], queue.Waiting);
        Assert.Equal([card], queue.Of(Mission));
        Assert.Empty(answers);

        Assert.Null(queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.Equal([DecisionAnswer.AllowOnce], answers);
        Assert.Empty(queue.Waiting);
        var settled = queue.Find(card.Id)!;
        Assert.Equal(DecisionState.Answered, settled.State);
        Assert.Equal(DecisionAnswer.AllowOnce, settled.Answer);
        Assert.Equal("It is already answered.", queue.Answer(card.Id, DecisionAnswer.Deny()));
        Assert.Single(answers);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void An_answer_the_card_does_not_take_is_refused_and_nothing_runs()
    {
        var queue = new DecisionQueue(clock);
        var ran = 0;
        var irreversible = queue.Raise(Card(DecisionWeight.Irreversible, "git push origin main"), (_, _) => ran++);
        Assert.Equal("This card does not take that answer.", queue.Answer(irreversible.Id, DecisionAnswer.AllowOnce));
        Assert.Equal("This card does not take that answer.", queue.Answer(irreversible.Id, DecisionAnswer.ForThisMission));
        Assert.Equal("There is no such card.", queue.Answer(Guid.NewGuid(), DecisionAnswer.DoIt));
        Assert.Equal(0, ran);
        Assert.Null(queue.Answer(irreversible.Id, DecisionAnswer.Dont()));
        Assert.Equal(1, ran);
    }

    [Fact]
    public void A_check_can_refuse_an_answer_the_rules_allow()
    {
        var queue = new DecisionQueue(clock);
        var ran = 0;
        var question = queue.Raise(Card(DecisionWeight.Question, "What is the password?"), (_, _) => ran++, (_, _) => "It asks for a secret: answer it in the chat.");
        Assert.Equal("It asks for a secret: answer it in the chat.", queue.Answer(question.Id, DecisionAnswer.Discuss("hunter")));
        Assert.Equal(0, ran);
        Assert.Equal(DecisionState.Open, queue.Find(question.Id)!.State);
    }

    [Fact]
    public void The_same_request_is_not_raised_twice_while_it_waits()
    {
        var queue = new DecisionQueue(clock);
        var first = queue.Raise(Card(), (_, _) => { });
        var again = queue.Raise(Card(), (_, _) => { });
        Assert.Equal(first.Id, again.Id);
        Assert.Single(queue.Waiting);
        var other = queue.Raise(Card(request: "r2"), (_, _) => { });
        Assert.NotEqual(first.Id, other.Id);
        Assert.Equal(2, queue.Waiting.Count);
    }

    [Fact]
    public void A_withdrawn_card_says_why_and_takes_no_answer()
    {
        var queue = new DecisionQueue(clock);
        var ran = 0;
        var card = queue.Raise(Card(), (_, _) => ran++);
        Assert.True(queue.WithdrawRequest(card.ThreadId!.Value, "r1", "The agent cancelled this request."));
        var withdrawn = queue.Find(card.Id)!;
        Assert.Equal(DecisionState.Withdrawn, withdrawn.State);
        Assert.Equal("The agent cancelled this request.", withdrawn.Note);
        Assert.Equal("It was withdrawn.", queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.False(queue.Withdraw(card.Id, "again"));
        Assert.False(queue.WithdrawRequest(card.ThreadId!.Value, "r1", "again"));
        Assert.Equal(0, ran);
    }

    [Fact]
    public void Pause_holds_only_irreversible_and_merge_cards_and_stop_holds_every_one()
    {
        var queue = new DecisionQueue(clock);
        var reversible = queue.Raise(Card(), (_, _) => { });
        var irreversible = queue.Raise(Card(DecisionWeight.Irreversible, "Delete the old build folder", request: "r2"), (_, _) => { });
        var merge = queue.Raise(Merge(), (_, _) => { });
        var elsewhere = queue.Raise(Card(mission: Guid.NewGuid(), request: "r3"), (_, _) => { });

        var paused = queue.Hold(c => c.MissionId == Mission, DecisionHold.Pause);
        Assert.Equal([irreversible.Id, merge.Id], paused.Select(c => c.Id));
        Assert.Equal(DecisionState.Open, queue.Find(reversible.Id)!.State);
        Assert.Null(queue.Answer(reversible.Id, DecisionAnswer.AllowOnce));
        Assert.Equal("Held by Pause.", queue.Answer(irreversible.Id, DecisionAnswer.DoIt));

        var reversible2 = queue.Raise(Card(request: "r4"), (_, _) => { });
        var stopped = queue.Hold(c => c.MissionId == Mission, DecisionHold.Stop);
        Assert.Equal([reversible2.Id], stopped.Select(c => c.Id));
        Assert.All(new[] { irreversible, merge, reversible2 }, c => Assert.Equal(DecisionHold.Stop, queue.Find(c.Id)!.HeldBy));
        Assert.Equal("Held by Stop.", queue.Answer(merge.Id, DecisionAnswer.Merge));
        Assert.Equal(DecisionState.Open, queue.Find(elsewhere.Id)!.State);
        Assert.Equal(3, queue.Of(Mission).Count);
    }

    [Fact]
    public void Release_raises_a_held_card_again_and_a_changed_one_anew()
    {
        var queue = new DecisionQueue(clock);
        var answers = new List<(Guid Card, DecisionAnswer Answer)>();
        var same = queue.Raise(Card(DecisionWeight.Irreversible, "git push origin main"), (card, answer) => answers.Add((card.Id, answer)));
        var changed = queue.Raise(Card(DecisionWeight.Irreversible, "Send the report", request: "r2"), (card, answer) => answers.Add((card.Id, answer)));
        queue.Hold(c => c.MissionId == Mission, DecisionHold.Pause);
        clock.Advance(TimeSpan.FromMinutes(5));

        var newInputs = new ValueList<DecisionInput>([new DecisionInput("Command", "Send the report"), new DecisionInput("To", "someone-else@example.org")]);
        var again = queue.Release(c => c.MissionId == Mission, c => c.Id == changed.Id ? newInputs : null);
        Assert.Equal(2, again.Count);
        Assert.Equal(same.Id, again[0].Id);
        Assert.Equal(DecisionState.Open, queue.Find(same.Id)!.State);
        var old = queue.Find(changed.Id)!;
        Assert.Equal(DecisionState.Withdrawn, old.State);
        Assert.Equal("Its inputs changed while it was held.", old.Note);
        var renewed = again[1];
        Assert.NotEqual(changed.Id, renewed.Id);
        Assert.Equal(newInputs, renewed.Inputs);
        Assert.NotEqual(changed.Binding, renewed.Binding);
        Assert.Equal(clock.GetUtcNow(), renewed.Raised);

        Assert.Null(queue.Answer(renewed.Id, DecisionAnswer.DoIt));
        Assert.Equal([(renewed.Id, DecisionAnswer.DoIt)], answers);
    }

    [Fact]
    public void The_queue_keeps_at_most_200_settled_cards()
    {
        var queue = new DecisionQueue(clock);
        var first = queue.Raise(Card(request: "r-0"), (_, _) => { });
        queue.Answer(first.Id, DecisionAnswer.AllowOnce);
        for (var i = 1; i <= DecisionQueue.SettledKept; i++)
        {
            var card = queue.Raise(Card(request: "r-" + i), (_, _) => { });
            queue.Withdraw(card.Id, "gone");
        }
        var open = queue.Raise(Card(request: "open"), (_, _) => { });
        Assert.Null(queue.Find(first.Id));
        Assert.Equal(DecisionQueue.SettledKept + 1, queue.Items.Count);
        Assert.Equal([open], queue.Waiting);
    }
}
