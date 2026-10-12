using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-14; DESIGN.md 10.1, 10.9). The 25 s rule with Pause: a call whose
// app pauses answers waiting_for_user "paused" at once (no 25 s wait), also when it was already waiting for an approval;
// pc_wait on a paused token waits its 25 s and answers "paused" again; the 60 s expiry stops while paused and starts
// afresh at the Resume; "paused" is the reason whatever else the call waits for.
[Trait("Decision", "ED-NEW-pcctl-14")]
public sealed class PcPauseWaitTests
{
    [Fact]
    public async Task A_call_whose_app_pauses_answers_paused_at_once_and_its_step_goes_on_after_the_Resume()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        var state = new PcWaitState();
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var approval = state.Waiting(PcWaitReason.Approval);
        var call = pending.Run(token => gate.Task.WaitAsync(token), state, CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(call, 50));

        var paused = state.Waiting(PcWaitReason.Paused);
        var answer = await call.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(PcStatus.WaitingForUser, answer.Status);
        Assert.Equal(PcWaitReason.Paused, answer.Reason);
        Assert.Equal(0, clock.NowMs);
        Assert.Contains("waiting_for_user: paused. token: " + answer.Token, answer.Render(), StringComparison.Ordinal);
        approval.Dispose();
        paused.Dispose();
        var resumed = pending.Resume(answer.Token!, CancellationToken.None);
        gate.SetResult(PcResult.Success(body: "done"));
        Assert.Equal("done", (await resumed)!.Body);
    }

    [Fact]
    public async Task Pc_wait_on_a_paused_token_waits_its_25_s_then_answers_paused_again()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        var state = new PcWaitState();
        using var paused = state.Waiting(PcWaitReason.Paused);
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = await pending.Run(token => gate.Task.WaitAsync(token), state, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcWaitReason.Paused, first.Reason);

        var again = pending.Resume(first.Token!, CancellationToken.None);
        Assert.True(await PcTestWait.StillWaiting(again));
        clock.Advance(PcPendingSteps.BudgetMs);
        var second = (await again)!;

        Assert.Equal(PcWaitReason.Paused, second.Reason);
        Assert.Equal(first.Token, second.Token);
        Assert.False(gate.Task.IsCompleted);
    }

    [Fact]
    public async Task The_60_s_expiry_stops_while_paused_and_starts_afresh_at_the_Resume()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        var state = new PcWaitState();
        var paused = state.Waiting(PcWaitReason.Paused);
        CancellationToken seen = default;
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = await pending.Run(token =>
        {
            seen = token;
            return gate.Task.WaitAsync(token);
        }, state, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Ten minutes paused: the token stays.
        clock.Advance(600_000);
        await Task.Delay(50);
        Assert.Equal(1, pending.Count);
        Assert.False(seen.IsCancellationRequested);

        paused.Dispose();
        await PcTestWait.Until(() => clock.Pending > 0);
        clock.Advance(PcPendingSteps.ExpiryMs - 1);
        await Task.Delay(50);
        Assert.Equal(1, pending.Count);
        clock.Advance(1);
        await PcTestWait.Until(() => pending.Count == 0);
        Assert.True(seen.IsCancellationRequested);
        Assert.Null(await pending.Resume(answer.Token!, CancellationToken.None));
    }

    [Fact]
    public async Task A_pause_during_the_expiry_stops_it_again()
    {
        var clock = new PcManualClock();
        var pending = new PcPendingSteps(clock);
        var state = new PcWaitState();
        var approval = state.Waiting(PcWaitReason.Approval);
        var gate = new TaskCompletionSource<PcResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = pending.Run(token => gate.Task.WaitAsync(token), state, CancellationToken.None);
        await PcTestWait.Until(() => clock.Pending > 0);
        clock.Advance(PcPendingSteps.BudgetMs);
        var answer = await call;
        Assert.Equal(PcWaitReason.Approval, answer.Reason);

        clock.Advance(50_000);
        var paused = state.Waiting(PcWaitReason.Paused);
        clock.Advance(30_000);
        await Task.Delay(50);
        Assert.Equal(1, pending.Count);
        paused.Dispose();
        approval.Dispose();
        await PcTestWait.Until(() => clock.Pending > 0);
        clock.Advance(PcPendingSteps.ExpiryMs);
        await PcTestWait.Until(() => pending.Count == 0);
    }

    [Fact]
    public void Paused_is_the_reason_whatever_else_the_call_waits_for()
    {
        var state = new PcWaitState();
        using var approval = state.Waiting(PcWaitReason.Approval);
        var paused = state.Waiting(PcWaitReason.Paused);
        using var confirm = state.Waiting(PcWaitReason.ConfirmCard);
        var next = state.NextPause;

        Assert.Equal(PcWaitReason.Paused, state.Reason);
        Assert.True(state.Paused);
        Assert.False(next.IsCompleted);
        paused.Dispose();
        Assert.Equal(PcWaitReason.ConfirmCard, state.Reason);
        using (state.Waiting(PcWaitReason.Paused)) Assert.True(next.IsCompleted);
    }
}
