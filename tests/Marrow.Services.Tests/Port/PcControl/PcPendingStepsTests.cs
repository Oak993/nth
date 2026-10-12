using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-8; DESIGN.md 2.2, 2.12; critique C10). No tool call blocks past 25 s:
// a step still waiting for the user (an approval, a confirmation card, the user typing in its field) answers
// waiting_for_user with a token and stays queued; pc_wait until "step" resumes the same step, never a second copy; a step
// not resumed within 60 s, a cancelled call, the chat's stop and the end of the turn cancel it.
[Trait("Decision", "ED-NEW-pcctl-8")]
public sealed class PcPendingStepsTests
{
    private const string Look = "{\"window\":\"w:1a\",\"screenshot\":\"no\"}";

    private static string Resume(string? token) => "{\"window\":\"w:1a\",\"until\":\"step\",\"token\":\"" + token + "\"}";

    private static async Task<T> PastTheBudget<T>(PcManualClock clock, Task<T> call)
    {
        await PcTestWait.Until(() => clock.Pending > 0 || call.IsCompleted);
        Assert.False(call.IsCompleted);
        clock.Advance(PcPendingSteps.BudgetMs);
        return await call;
    }

    [Fact]
    public async Task Work_done_within_the_budget_answers_at_once()
    {
        var pending = new PcPendingSteps(new PcManualClock());
        var result = await pending.Run(_ => Task.FromResult(PcResult.Success(body: "done")), new PcWaitState(), CancellationToken.None);
        Assert.Equal("done", result.Body);
        Assert.Equal(0, pending.Count);
    }

    [Fact]
    public async Task Work_past_the_budget_answers_waiting_for_user_and_resumes_the_same_step()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        var state = new PcWaitState();
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starts = 0;
        using var scope = state.Waiting(PcWaitReason.Approval);
        state.Done("1. pc_set_value w:1a: ok");
        var waiting = await PastTheBudget(clock, pending.Run(token =>
        {
            starts++;
            return gate.Task.WaitAsync(token);
        }, state, CancellationToken.None));
        Assert.Equal(PcStatus.WaitingForUser, waiting.Status);
        Assert.Equal(PcWaitReason.Approval, waiting.Reason);
        Assert.StartsWith("t", waiting.Token, StringComparison.Ordinal);
        Assert.Equal("done so far:\n1. pc_set_value w:1a: ok", waiting.Body);
        Assert.Equal(1, pending.Count);
        // Still waiting after a resume: the same token again, still one step.
        var again = await PastTheBudget(clock, pending.Resume(waiting.Token!, CancellationToken.None));
        Assert.Equal(waiting.Token, again!.Token);
        Assert.Equal(1, pending.Count);
        var resumed = pending.Resume(waiting.Token!, CancellationToken.None);
        gate.SetResult(PcResult.Success(body: "finished"));
        Assert.Equal("finished", (await resumed)!.Body);
        Assert.Equal(1, starts);
        Assert.Equal(0, pending.Count);
        Assert.Null(await pending.Resume(waiting.Token!, CancellationToken.None));
    }

    [Fact]
    public async Task A_step_not_resumed_within_sixty_seconds_is_cancelled_and_a_resume_restarts_the_sixty_seconds()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        CancellationToken seen = default;
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = await PastTheBudget(clock, pending.Run(token =>
        {
            seen = token;
            return gate.Task.WaitAsync(token);
        }, new PcWaitState(), CancellationToken.None));
        clock.Advance(30_000);
        await PastTheBudget(clock, pending.Resume(waiting.Token!, CancellationToken.None));
        // The first expiry (60 s after the first answer) no longer applies.
        clock.Advance(5_000);
        await Task.Delay(50);
        Assert.Equal(1, pending.Count);
        Assert.False(seen.IsCancellationRequested);
        clock.Advance(PcPendingSteps.ExpiryMs);
        await PcTestWait.Until(() => pending.Count == 0);
        Assert.True(seen.IsCancellationRequested);
        Assert.Null(await pending.Resume(waiting.Token!, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_all_and_a_cancelled_call_cancel_the_steps_work()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        CancellationToken seen = default;
        var waiting = await PastTheBudget(clock, pending.Run(token =>
        {
            seen = token;
            return Task.Delay(Timeout.Infinite, token).ContinueWith(_ => PcResult.Success(), TaskScheduler.Default);
        }, new PcWaitState(), CancellationToken.None));
        Assert.Equal(PcStatus.WaitingForUser, waiting.Status);
        pending.CancelAll();
        Assert.True(seen.IsCancellationRequested);
        Assert.Equal(0, pending.Count);
        using var stop = new CancellationTokenSource();
        CancellationToken second = default;
        var call = pending.Run(token =>
        {
            second = token;
            return Task.Delay(Timeout.Infinite, token).ContinueWith(_ => PcResult.Success(), token, TaskContinuationOptions.None, TaskScheduler.Default);
        }, new PcWaitState(), stop.Token);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.True(second.IsCancellationRequested);
        Assert.Equal(0, pending.Count);
    }

    [Fact]
    public async Task A_held_approval_answers_waiting_for_user_and_pc_wait_step_brings_the_result()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting = await PastTheBudget(harness.WaitClock, PcEngineHarness.Call(engine, "pc_observe", Look));
        Assert.Equal(PcStatus.WaitingForUser, waiting.Status);
        Assert.Equal(PcWaitReason.Approval, waiting.Reason);
        Assert.Contains("pc_wait with until \"step\"", waiting.Render(), StringComparison.Ordinal);
        Assert.Equal(1, engine.Waiting);
        var resume = PcEngineHarness.Call(engine, "pc_wait", Resume(waiting.Token));
        harness.Approver.Hold.SetResult(PcApproval.Chat);
        var done = await resume;
        Assert.True(done.Ok, done.Render());
        Assert.Contains("[3]", done.Body, StringComparison.Ordinal);
        Assert.Single(harness.Approver.Asked);
        Assert.Equal(0, engine.Waiting);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_wait", Resume(waiting.Token))).Error);
    }

    [Fact]
    public async Task A_held_card_holds_only_its_own_app_and_a_batch_reports_the_steps_done_so_far()
    {
        var harness = new PcEngineHarness();
        var app = harness.Notepad();
        var other = harness.Notepad(0x2b, 202, "wordpad.exe");
        var engine = harness.Engine();
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        app.Root.Children[1].Name = "Send";
        app.Root.Children[1].OnInvoke = (_, _) => app.Title = "Sent";
        harness.Desktop.Bump(0x1a);
        await PcEngineHarness.Call(engine, "pc_observe", Look);
        harness.Confirmer.Hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var batch = PcEngineHarness.Call(engine, "pc_batch",
            "{\"window\":\"w:1a\",\"actions\":[{\"tool\":\"pc_set_value\",\"element\":2,\"value\":\"hello\"},{\"tool\":\"pc_click\",\"element\":3}]}");
        await PcTestWait.Until(() => harness.Confirmer.Asked.Count == 1);
        // Meanwhile another app is driven as usual.
        var meanwhile = await PcEngineHarness.Call(engine, "pc_set_value", "{\"window\":\"w:2b\",\"target\":{\"name\":\"Text editor\"},\"value\":\"meanwhile\"}");
        Assert.True(meanwhile.Ok, meanwhile.Render());
        Assert.Equal("meanwhile", other.Root.Children[0].Value);
        var waiting = await PastTheBudget(harness.WaitClock, batch);
        Assert.Equal(PcWaitReason.ConfirmCard, waiting.Reason);
        Assert.StartsWith("done so far:\n1. pc_set_value w:1a: ok", waiting.Body, StringComparison.Ordinal);
        var resume = PcEngineHarness.Call(engine, "pc_wait", Resume(waiting.Token));
        harness.Confirmer.Hold.SetResult(true);
        var done = await resume;
        Assert.True(done.Ok, done.Render());
        Assert.Equal("Sent", app.Title);
        Assert.Contains("2. pc_click w:1a: ok", done.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_or_stopped_step_is_gone_and_its_card_is_withdrawn()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await PastTheBudget(harness.WaitClock, PcEngineHarness.Call(engine, "pc_observe", Look));
        harness.WaitClock.Advance(PcPendingSteps.ExpiryMs);
        await PcTestWait.Until(() => engine.Waiting == 0);
        var expired = await PcEngineHarness.Call(engine, "pc_wait", Resume(first.Token));
        Assert.Equal(PcErrorCode.BadArgs, expired.Error);
        Assert.Contains("expired after 60 s", expired.Reason, StringComparison.Ordinal);
        var second = await PastTheBudget(harness.WaitClock, PcEngineHarness.Call(engine, "pc_observe", Look));
        Assert.Equal(PcStatus.WaitingForUser, second.Status);
        engine.StopTurn();
        Assert.Equal(0, engine.Waiting);
        Assert.Equal(PcErrorCode.BadArgs, (await PcEngineHarness.Call(engine, "pc_wait", Resume(second.Token))).Error);
    }

    [Fact]
    public async Task A_call_cancelled_by_its_client_ends_as_stopped_and_leaves_nothing_queued()
    {
        var harness = new PcEngineHarness();
        harness.Notepad();
        var engine = harness.Engine();
        harness.Approver.Hold = new TaskCompletionSource<PcApproval>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancel = new CancellationTokenSource();
        var call = PcEngineHarness.Call(engine, "pc_observe", Look, cancel.Token);
        await PcTestWait.Until(() => harness.Approver.Asked.Count == 1);
        await cancel.CancelAsync();
        var result = await call;
        Assert.Equal(PcErrorCode.StoppedByUser, result.Error);
        Assert.Equal(0, engine.Waiting);
    }
}
