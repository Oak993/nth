using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 4, 12.6b; ED-NEW-dextra-as19-6): the supervised trial as the app runs it. The trial stops at every step that asks and
// every step the owner flagged, as a card on the decision queue and a line in the draft's view, and the first answer from either one counts.
// With Tactus the card for a step that cannot be undone is the irreversible card, Do it and Don't, and the step happens only after Do it; until
// Tactus lands there is no executor, so the owner follows the draft himself and says each step is done.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-6")]
public sealed class TeachControllerTrialTests : IDisposable
{
    private readonly TeachControllerRig rig;

    public TeachControllerTrialTests() => rig = new TeachControllerRig();

    public void Dispose() => rig.Dispose();

    private async Task<string> Drafted(TeachControllerRig on)
    {
        var id = await on.RecordMail();
        await on.Controller.Draft(id);
        foreach (var card in on.Queue.Waiting.ToList()) on.Queue.Withdraw(card.Id, "not part of this test");
        return id;
    }

    [Fact]
    public async Task With_no_executor_the_owner_follows_the_draft_a_step_at_a_time_and_the_step_that_sends_is_a_question_card()
    {
        var id = await Drafted(rig);
        var (trial, refusal) = rig.Controller.StartTrial(id);
        Assert.Null(refusal);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 1);
        var first = rig.Controller.Trial!.Pending!;
        Assert.Null(first.Why);
        Assert.Null(first.Card);
        Assert.Empty(rig.Queue.Waiting);
        Assert.True(rig.Controller.AnswerTrial(TrialAnswer.Continue));
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 2);
        Assert.True(rig.Controller.AnswerTrial(TrialAnswer.Continue));
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 3);
        var send = rig.Controller.Trial!.Pending!;
        Assert.Equal((TrialAsk.Guarded, true), (send.Why, send.DoneByOwner));
        Assert.Equal(["I did it", "Skip this step", "Stop the trial"], send.Options);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.Equal((DecisionKind.Question, DecisionWeight.Question, ActionClass.Outward), (card.Kind, card.Weight, card.Class!.Value));
        Assert.StartsWith("Trial, step 3 asks you: Click “Send”", card.Title, StringComparison.Ordinal);
        Assert.Null(rig.Queue.Answer(card.Id, DecisionAnswer.Pick("I did it")));
        var result = await trial!;
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Equal(DecisionState.Answered, rig.Queue.Find(card.Id)!.State);
    }

    [Fact]
    public async Task Answering_in_the_drafts_view_counts_and_takes_the_card_back()
    {
        var id = await Drafted(rig);
        var (trial, _) = rig.Controller.StartTrial(id);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 1);
        rig.Controller.AnswerTrial(TrialAnswer.Continue);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 2);
        rig.Controller.AnswerTrial(TrialAnswer.Continue);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 3);
        var card = Assert.Single(rig.Queue.Waiting);
        Assert.True(rig.Controller.AnswerTrial(TrialAnswer.Skip));
        var result = await trial!;
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Contains("(skipped)", result.Log[2], StringComparison.Ordinal);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card.Id)!.State);
        Assert.False(rig.Controller.AnswerTrial(TrialAnswer.Continue));
    }

    [Fact]
    public async Task Stop_the_trial_on_the_card_ends_it_at_that_step()
    {
        var id = await Drafted(rig);
        var (trial, _) = rig.Controller.StartTrial(id);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 1);
        rig.Controller.AnswerTrial(TrialAnswer.Continue);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 2);
        rig.Controller.AnswerTrial(TrialAnswer.Continue);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 3);
        rig.Queue.Answer(rig.Queue.Waiting.Single().Id, DecisionAnswer.Pick("Stop the trial"));
        var result = await trial!;
        Assert.Equal((TrialOutcome.Stopped, 3), (result.Outcome, result.StoppedAt));
        Assert.Contains(rig.Audited, a => a.Tool == "nth-dextra/teach-trial" && a.Result!.EndsWith("Stopped at step 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Stopping_the_trial_while_a_step_waits_ends_it_and_takes_the_card_back()
    {
        var id = await Drafted(rig);
        var (trial, _) = rig.Controller.StartTrial(id);
        for (var step = 1; step <= 2; step++)
        {
            await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == step);
            rig.Controller.AnswerTrial(TrialAnswer.Continue);
        }
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 3);
        var card = Assert.Single(rig.Queue.Waiting);
        rig.Controller.StopTrial();
        var result = await trial!;
        Assert.Equal(TrialOutcome.Stopped, result.Outcome);
        Assert.Equal(DecisionState.Withdrawn, rig.Queue.Find(card.Id)!.State);
        Assert.Null(rig.Controller.Trial!.Pending);
        Assert.NotNull(rig.Controller.Trial.Result);
        rig.Controller.ClearTrial();
        Assert.Null(rig.Controller.Trial);
    }

    [Fact]
    public async Task A_trial_cannot_start_while_recording_or_while_another_runs_and_a_recording_cannot_start_in_front_of_a_trial()
    {
        var id = await Drafted(rig);
        var (trial, _) = rig.Controller.StartTrial(id);
        await rig.Until(() => rig.Controller.Trial?.Pending is not null);
        Assert.Equal("A trial is already running.", rig.Controller.StartTrial(id).Refusal);
        Assert.Equal("A trial is in front of you. Finish it or stop it first.", rig.Controller.Start().Refusal);
        Assert.False(rig.Controller.Discard(id));
        rig.Controller.StopTrial();
        await trial!;
        Assert.True(rig.Controller.Start().Started);
        Assert.Equal("A recording is running. Stop it first.", rig.Controller.StartTrial(id).Refusal);
        await rig.Controller.Stop();
        Assert.Equal("There is no draft to try.", rig.Controller.StartTrial("t0000000000ff").Refusal);
    }

    [Fact]
    public async Task Pausing_the_trial_holds_it_before_the_next_step_until_it_is_resumed()
    {
        var id = await Drafted(rig);
        var (trial, _) = rig.Controller.StartTrial(id);
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 1);
        rig.Controller.PauseTrial();
        rig.Controller.AnswerTrial(TrialAnswer.Continue);
        await rig.Until(() => rig.Controller.Trial?.Progress?.Stage == TrialStage.Paused);
        Assert.True(rig.Controller.Trial!.Paused);
        Assert.Null(rig.Controller.Trial.Pending);
        rig.Controller.ResumeTrial();
        await rig.Until(() => rig.Controller.Trial?.Pending?.Step.Number == 2);
        rig.Controller.StopTrial();
        Assert.Equal(TrialOutcome.Stopped, (await trial!).Outcome);
    }

    [Fact]
    public async Task With_an_executor_the_step_that_sends_is_the_irreversible_card_and_happens_only_after_do_it()
    {
        using var withTactus = new TeachControllerRig(executor: true);
        var id = await Drafted(withTactus);
        var (trial, _) = withTactus.Controller.StartTrial(id);
        await withTactus.Until(() => withTactus.Controller.Trial?.Pending?.Step.Number == 3);
        Assert.Equal([1, 2], withTactus.Executor!.Performed);
        var card = Assert.Single(withTactus.Queue.Waiting);
        Assert.Equal((DecisionKind.ToolCall, DecisionWeight.Irreversible, ActionClass.Outward), (card.Kind, card.Weight, card.Class!.Value));
        Assert.Equal([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont], card.Answers);
        Assert.Equal(card.Id, withTactus.Controller.Trial!.Pending!.Card);
        Assert.False(withTactus.Controller.Trial.Pending!.DoneByOwner);
        Assert.Null(withTactus.Queue.Answer(card.Id, DecisionAnswer.DoIt));
        var result = await trial!;
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Equal([1, 2, 3], withTactus.Executor.Performed);
    }

    [Fact]
    public async Task With_an_executor_dont_ends_the_trial_and_the_step_is_never_done()
    {
        using var withTactus = new TeachControllerRig(executor: true);
        var id = await Drafted(withTactus);
        var (trial, _) = withTactus.Controller.StartTrial(id);
        await withTactus.Until(() => withTactus.Controller.Trial?.Pending?.Step.Number == 3);
        withTactus.Queue.Answer(withTactus.Queue.Waiting.Single().Id, DecisionAnswer.Dont());
        var result = await trial!;
        Assert.Equal((TrialOutcome.Stopped, 3), (result.Outcome, result.StoppedAt));
        Assert.Equal([1, 2], withTactus.Executor!.Performed);
    }

    [Fact]
    public async Task A_step_he_flagged_is_a_card_with_the_go_ahead_words_when_nth_does_the_step()
    {
        using var withTactus = new TeachControllerRig(executor: true);
        var id = await Drafted(withTactus);
        withTactus.Controller.Edit(id, d => d.Flag(1, true));
        var (trial, _) = withTactus.Controller.StartTrial(id);
        await withTactus.Until(() => withTactus.Controller.Trial?.Pending?.Step.Number == 1);
        var pending = withTactus.Controller.Trial!.Pending!;
        Assert.Equal((TrialAsk.Flagged, false), (pending.Why, pending.DoneByOwner));
        Assert.Equal(["Go ahead", "Skip this step", "Stop the trial"], pending.Options);
        Assert.Empty(withTactus.Executor!.Performed);
        withTactus.Controller.StopTrial();
        await trial!;
        Assert.Empty(withTactus.Executor.Performed);
    }
}
