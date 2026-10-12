using Marrow.Setup.Platform;
using Marrow.Testing;

namespace Marrow.Setup.Tests.Platform;

// C4b signature gate: the owner requires bounded verification and no installation on an unknown answer.
public sealed class SignatureWaitTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_signature_check_that_never_answers_stays_unknown_after_a_bounded_wait()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<SignatureResult>();
        var wait = Wait(pending.Task, clock);
        Assert.False(wait.IsCompleted);
        Assert.Single(clock.TimerDeadlines);
        Assert.InRange(clock.TimerDeadlines[0], TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(60));
        clock.Advance(clock.TimerDeadlines[0]);
        Assert.Null(await wait);
        pending.SetResult(new SignatureResult(SignatureState.Signed, "Publisher", 0));
        Assert.Null(await wait); // a late answer cannot turn a timed-out check into permission to install
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task An_answer_before_the_deadline_is_kept_and_the_timer_is_disposed()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<SignatureResult>();
        var wait = Wait(pending.Task, clock);
        var answer = new SignatureResult(SignatureState.Unsigned, null, 0);
        pending.SetResult(answer);
        Assert.Same(answer, await wait);
        Assert.Equal(0, clock.ActiveTimers);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task Closing_the_window_cancels_the_wait_and_releases_its_timer()
    {
        var clock = new ManualTimeProvider();
        var pending = new TaskCompletionSource<SignatureResult>();
        using var closed = new CancellationTokenSource();
        var wait = Wait(pending.Task, clock, closed.Token);
        closed.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await wait);
        Assert.Equal(0, clock.ActiveTimers);
    }

    private static Task<SignatureResult?> Wait(Task<SignatureResult> check, TimeProvider clock, CancellationToken closed = default)
    {
        // Reflection keeps this acceptance test compilable on the reviewed head, before the production seam exists.
        var method = typeof(SignatureCheck).GetMethod("WaitForAnswerAsync", [typeof(Task<SignatureResult>), typeof(TimeProvider), typeof(CancellationToken)]);
        Assert.NotNull(method);
        return (Task<SignatureResult?>)method.Invoke(null, [check, clock, closed])!;
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_fault_or_unknown_answer_cannot_authorize_installation()
    {
        Assert.Null(await Wait(Task.FromException<SignatureResult>(new IOException("check refused")), new ManualTimeProvider()));
        Assert.Null(await Wait(Task.FromResult(SignatureResult.Pending), new ManualTimeProvider()));
    }
}
