using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-6 (DESIGN.md rev 5, 4.2, 12.6b "Cards while paused"; ED-NEW-dextra-as6-9): an answer to a reversible card of a paused
// task is kept and acts on Resume; the card's own rules still judge it when it is given; a later answer replaces it; a Stop or a
// Pause that holds the card drops it; an irreversible card approved but not yet run is taken back as held and needs a new tap.
[Trait("Decision", "ED-NEW-dextra-as6-9")]
[Trait("Feature", "AS-6")]
public sealed class DecisionQueueKeptTests
{
    private static readonly Guid Mission = Guid.Parse("20000000-0000-4000-8000-000000000002");

    private static DecisionItem Reversible(string title = "Run npm test?") => new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Reversible, title)
    {
        AssistantId = "dextra",
        MissionId = Mission,
        ThreadId = Guid.NewGuid(),
        RequestId = "r1",
        Class = ActionClass.Unknown,
    };

    private static DecisionItem Irreversible() => new(Guid.NewGuid(), DecisionKind.Approval, DecisionWeight.Irreversible, "git push?")
    {
        AssistantId = "dextra",
        MissionId = Mission,
        ThreadId = Guid.NewGuid(),
        RequestId = "r2",
        Class = ActionClass.Outward,
    };

    [Fact]
    public void An_answer_given_while_paused_is_kept_the_card_stays_open_and_it_acts_on_apply()
    {
        var queue = new DecisionQueue();
        var paused = true;
        queue.KeepsAnswers = _ => paused;
        var acted = new List<DecisionAnswerKind>();
        var card = queue.Raise(Reversible(), (_, answer) => acted.Add(answer.Kind));
        var changes = 0;
        queue.Changed += () => changes++;

        Assert.Null(queue.Answer(card.Id, DecisionAnswer.Deny("later")));
        var kept = queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Open, DecisionAnswerKind.Deny), (kept.State, kept.Kept!.Kind));
        Assert.Null(kept.Answer);
        Assert.Empty(acted);
        Assert.Equal(1, changes);
        Assert.Single(queue.Waiting);
        // A later answer replaces the kept one.
        Assert.Null(queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        Assert.Equal(DecisionAnswerKind.AllowOnce, queue.Find(card.Id)!.Kept!.Kind);

        paused = false;
        Assert.Equal(1, queue.ApplyKept(c => c.MissionId == Mission));
        Assert.Equal([DecisionAnswerKind.AllowOnce], acted);
        var answered = queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Answered, DecisionAnswerKind.AllowOnce), (answered.State, answered.Answer!.Kind));
        Assert.Null(answered.Kept);
        Assert.Equal(0, queue.ApplyKept(_ => true)); // nothing is applied twice
        Assert.Equal("It is already answered.", queue.Answer(card.Id, DecisionAnswer.Deny()));
    }

    [Fact]
    public void The_cards_own_rules_judge_an_answer_when_it_is_given_not_when_it_is_applied()
    {
        var queue = new DecisionQueue();
        queue.KeepsAnswers = _ => true;
        var card = queue.Raise(Reversible(), (_, _) => { });
        Assert.Equal("This card does not take that answer.", queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Equal("This card does not take that answer.", queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.Always)));
        Assert.Null(queue.Find(card.Id)!.Kept);
        // A question's answer that the card refuses (a pick that is not one of its options) is refused too.
        var question = queue.Raise(new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, "Which port?")
        {
            AssistantId = "dextra",
            MissionId = Mission,
            Options = new ValueList<string>(["3000", "8080"]),
        }, (_, _) => { });
        Assert.Equal("Pick one of the card's own answers.", queue.Answer(question.Id, DecisionAnswer.Pick("9999")));
        Assert.Null(queue.Answer(question.Id, DecisionAnswer.Pick("8080")));
        Assert.Equal("8080", queue.Find(question.Id)!.Kept!.Text);
    }

    [Fact]
    public void A_pause_or_stop_that_holds_the_card_drops_the_kept_answer_and_a_held_card_takes_none()
    {
        var queue = new DecisionQueue();
        var paused = true;
        queue.KeepsAnswers = _ => paused;
        var card = queue.Raise(Reversible(), (_, _) => { });
        Assert.Null(queue.Answer(card.Id, DecisionAnswer.AllowOnce));

        Assert.Single(queue.Hold(c => c.MissionId == Mission, DecisionHold.Stop));
        var held = queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Held, DecisionHold.Stop), (held.State, held.HeldBy));
        Assert.Null(held.Kept);
        Assert.Equal("Held by Stop.", queue.Answer(card.Id, DecisionAnswer.AllowOnce));
        paused = false;
        queue.Release(c => c.MissionId == Mission);
        Assert.Equal(DecisionState.Open, queue.Find(card.Id)!.State);
        Assert.Equal(0, queue.ApplyKept(_ => true));
    }

    [Fact]
    public void An_irreversible_card_approved_but_not_yet_run_is_taken_back_as_held_and_needs_a_new_tap()
    {
        var queue = new DecisionQueue();
        var acted = new List<DecisionAnswerKind>();
        var card = queue.Raise(Irreversible(), (_, answer) => acted.Add(answer.Kind));
        Assert.False(queue.Reopen(card.Id, DecisionHold.Pause, "not answered yet")); // only an answered card is taken back
        Assert.Null(queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Equal([DecisionAnswerKind.DoIt], acted);

        Assert.True(queue.Reopen(card.Id, DecisionHold.Pause, "You approved this just before the pause."));
        var back = queue.Find(card.Id)!;
        Assert.Equal((DecisionState.Held, DecisionHold.Pause), (back.State, back.HeldBy));
        Assert.Null(back.Answer);
        Assert.Null(back.Settled);
        Assert.Equal("You approved this just before the pause.", back.Note);
        Assert.Single(queue.Waiting);
        Assert.Equal("Held by Pause.", queue.Answer(card.Id, DecisionAnswer.DoIt));
        Assert.Single(queue.Release(c => c.MissionId == Mission));
        Assert.Null(queue.Answer(card.Id, DecisionAnswer.DoIt)); // the new tap
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.DoIt], acted);
        Assert.False(queue.Reopen(Guid.NewGuid(), DecisionHold.Pause, "no such card"));
    }
}
