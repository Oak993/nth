using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Assistant;

// Dextra AS-19 (DESIGN.md rev 5, 10 step 4, 12.6b; ED-NEW-dextra-as19-6): the supervised trial of a draft. It stops at every step that asks the owner and at
// every step he flagged, and does such a step only after he says so; "Don't" ends the trial with that step not done. A plain step is done at once
// by the executor (Tactus) or, with no executor, handed to the owner to do. A step that is his alone is never done by the executor. It can be
// paused before a step, resumed, and stopped, even while a step waits for his answer.
[Trait("Feature", "AS-19")]
[Trait("Decision", "ED-NEW-dextra-as19-6")]
public sealed class TeachTrialTests
{
    private static readonly TeachElement Mail = new("outlook", "New message", "edit", "To");

    // 1 Open (plain), 2 Type To (plain), 3 the password (his alone), 4 Send (asks), 5 Close (plain, flagged by the owner)
    private static TeachDraft Draft() => TeachDraftBuilder.Skeleton(TeachSamples.Info(),
    [
        TeachSamples.Click(1, 1, TeachSamples.Button("Open")),
        TeachSamples.Type(2, 5, Mail, "sam@example.com"),
        TeachSamples.Type(3, 9, TeachSamples.Field("Password", "outlook", "Sign in", password: true), null, 9, secret: true),
        TeachSamples.Click(4, 12, TeachSamples.Button("Send")),
        TeachSamples.Click(5, 16, TeachSamples.Button("Close")),
    ], []).Flag(5, true);

    private static async Task<TrialResult> Run(TeachDraft draft, FakeTrialHost host, ITeachStepExecutor? executor, CancellationToken cancel = default) =>
        await new TeachTrial(draft, host, executor).RunAsync(cancel);

    [Fact]
    public async Task With_an_executor_a_plain_step_is_done_at_once_and_a_step_that_asks_is_done_only_after_the_owner_says_so()
    {
        var host = new FakeTrialHost();
        var result = await Run(Draft(), host, new FakeExecutor(host));
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Equal(["do:1", "do:2", "ask:3:HisAlone:owner", "ask:4:Guarded:nth", "do:4", "ask:5:Flagged:nth", "do:5"], host.Order);
    }

    [Fact]
    public async Task A_step_that_asks_is_not_done_while_the_owner_has_not_answered()
    {
        var host = new FakeTrialHost();
        var hold = host.Holds[4] = new TaskCompletionSource<TrialAnswer>();
        var executor = new FakeExecutor(host);
        var run = Run(Draft(), host, executor);
        await host.Asked(4);
        Assert.DoesNotContain(4, executor.Performed);
        Assert.False(run.IsCompleted);
        hold.SetResult(TrialAnswer.Continue);
        await run;
        Assert.Contains(4, executor.Performed);
        Assert.True(host.Order.IndexOf("ask:4:Guarded:nth") < host.Order.IndexOf("do:4"));
    }

    [Fact]
    public async Task Saying_no_to_a_step_that_asks_ends_the_trial_and_the_step_is_never_done()
    {
        var host = new FakeTrialHost { Answers = { [4] = TrialAnswer.Stop } };
        var executor = new FakeExecutor(host);
        var result = await Run(Draft(), host, executor);
        Assert.Equal((TrialOutcome.Stopped, 4), (result.Outcome, result.StoppedAt));
        Assert.Equal([1, 2], executor.Performed);
        Assert.EndsWith("(stopped)", result.Log[^1], StringComparison.Ordinal);
        Assert.DoesNotContain("ask:5:Flagged:nth", host.Order);
    }

    [Fact]
    public async Task Skipping_a_step_goes_on_without_doing_it()
    {
        var host = new FakeTrialHost { Answers = { [4] = TrialAnswer.Skip } };
        var executor = new FakeExecutor(host);
        var result = await Run(Draft(), host, executor);
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Equal([1, 2, 5], executor.Performed);
        Assert.Contains("4. Click “Send” (button) in Outlook (skipped)", result.Log);
    }

    [Fact]
    public async Task A_step_the_owner_flagged_is_asked_though_nothing_in_it_asks()
    {
        var host = new FakeTrialHost();
        var plain = TeachDraftBuilder.Skeleton(TeachSamples.Info(), [TeachSamples.Click(1, 1, TeachSamples.Button("Open")), TeachSamples.Click(2, 3, TeachSamples.Button("Close"))], []);
        await Run(plain.Flag(1, true), host, new FakeExecutor(host));
        Assert.Equal(["ask:1:Flagged:nth", "do:1", "do:2"], host.Order);
    }

    [Fact]
    public async Task A_step_that_is_his_alone_is_never_done_by_the_executor()
    {
        var host = new FakeTrialHost();
        var executor = new FakeExecutor(host);
        await Run(Draft(), host, executor);
        Assert.DoesNotContain(3, executor.Performed);
        Assert.Contains("ask:3:HisAlone:owner", host.Order);
    }

    [Fact]
    public async Task With_no_executor_every_plain_step_is_handed_to_the_owner_and_a_step_that_asks_is_asked_once_for_him_to_do_it()
    {
        var host = new FakeTrialHost();
        var result = await Run(Draft(), host, executor: null);
        Assert.Equal(TrialOutcome.Finished, result.Outcome);
        Assert.Equal(["hand:1", "hand:2", "ask:3:HisAlone:owner", "ask:4:Guarded:owner", "ask:5:Flagged:owner"], host.Order);
        Assert.Equal(["1. Click “Open” (button) in Outlook (done)", "2. Type “sam@example.com” into “To” (edit) (done)"], result.Log.Take(2));
        Assert.EndsWith("(asked, then done)", result.Log[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_step_the_executor_cannot_do_fails_the_trial_with_the_reason_and_the_rest_is_not_tried()
    {
        var host = new FakeTrialHost();
        var executor = new FakeExecutor(host) { Fail = { [2] = "the field is not on the screen" } };
        var result = await Run(Draft(), host, executor);
        Assert.Equal((TrialOutcome.Failed, 2, "the field is not on the screen"), (result.Outcome, result.StoppedAt, result.Reason));
        Assert.Equal([1, 2], executor.Performed);
        Assert.Contains(host.Progresses, p => p is { Step: 2, Stage: TrialStage.Failed });
    }

    [Fact]
    public async Task Pausing_makes_the_trial_wait_before_the_next_step_and_resuming_goes_on()
    {
        var host = new FakeTrialHost();
        var executor = new FakeExecutor(host);
        var trial = new TeachTrial(Draft(), host, executor);
        var reached = new TaskCompletionSource();
        host.OnProgress = p =>
        {
            if (p is { Step: 1, Stage: TrialStage.Done }) trial.Pause();
            if (p.Stage == TrialStage.Paused) reached.TrySetResult();
        };
        var run = trial.RunAsync(CancellationToken.None);
        await reached.Task;
        Assert.True(trial.Paused);
        Assert.Equal([1], executor.Performed);
        Assert.False(run.IsCompleted);
        host.OnProgress = null;
        trial.Resume();
        Assert.False(trial.Paused);
        Assert.Equal(TrialOutcome.Finished, (await run).Outcome);
        Assert.Equal([1, 2, 4, 5], executor.Performed);
    }

    [Fact]
    public async Task Stopping_while_a_step_waits_for_the_owner_ends_the_trial_there()
    {
        var host = new FakeTrialHost();
        host.Holds[4] = new TaskCompletionSource<TrialAnswer>();
        var executor = new FakeExecutor(host);
        using var stop = new CancellationTokenSource();
        var run = Run(Draft(), host, executor, stop.Token);
        await host.Asked(4);
        await stop.CancelAsync();
        var result = await run;
        Assert.Equal((TrialOutcome.Stopped, 4), (result.Outcome, result.StoppedAt));
        Assert.DoesNotContain(4, executor.Performed);
    }

    [Fact]
    public async Task Stopping_while_paused_ends_the_trial_at_the_next_step()
    {
        var host = new FakeTrialHost();
        var trial = new TeachTrial(Draft(), host, new FakeExecutor(host));
        using var stop = new CancellationTokenSource();
        var reached = new TaskCompletionSource();
        host.OnProgress = p =>
        {
            if (p is { Step: 1, Stage: TrialStage.Done }) trial.Pause();
            if (p.Stage == TrialStage.Paused) reached.TrySetResult();
        };
        var run = trial.RunAsync(stop.Token);
        await reached.Task;
        await stop.CancelAsync();
        var result = await run;
        Assert.Equal((TrialOutcome.Stopped, 2), (result.Outcome, result.StoppedAt));
    }
}
