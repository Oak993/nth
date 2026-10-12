using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Support;

public sealed class C7ConnectionDeadlineTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_throwing_abort_does_not_skip_another_phase_reservation_cleanup()
    {
        using var stream = new ControlReviewStream([.. NthControl.Frame(NthControlRequest.QuitIfIdle.ToJson()), NthControl.AckByte]);
        var cleaned = 0;
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok(),
            PrepareCommit: _ => Task.FromResult(new NthControlAnswer(ScriptedControlHost.Ok(), Abort: () => cleaned++)),
            Abort: () => throw new InvalidOperationException("fake cleanup failure")));
        await NthControlConnection.ServeAsync(stream, host, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), default, default);
        Assert.True(stream.Disposed);
        Assert.Equal(1, cleaned);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task A_late_throwing_cleanup_is_attempted_after_the_disconnected_server_task_has_ended()
    {
        using var stream = new ControlReviewStream(NthControl.Frame(NthControlRequest.Status.ToJson()));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok(), Abort: () =>
        {
            aborted.TrySetResult();
            throw new InvalidOperationException("fake late cleanup failure");
        })) { Gate = async (_, _) => { entered.SetResult(); await release.Task; } };
        using var gone = new CancellationTokenSource();
        var serving = NthControlConnection.ServeAsync(stream, host, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), default, gone.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            gone.Cancel();
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
            release.TrySetResult();
            await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stream.Disposed);
        }
        finally { release.TrySetResult(); gone.Cancel(); await serving; }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task The_deadline_ends_the_connection_even_if_the_host_ignores_cancellation_and_aborts_its_late_answer()
    {
        using var stream = new ControlReviewStream(NthControl.Frame(NthControlRequest.Status.ToJson()));
        using var serverStop = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok(), Abort: () => aborted.TrySetResult()))
        {
            Gate = async (_, _) => { entered.SetResult(); await release.Task; },
        };
        var serving = NthControlConnection.ServeAsync(stream, host, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(100), serverStop.Token, default);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await serving.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(stream.Disposed);
            Assert.False(serverStop.IsCancellationRequested);
            Assert.False(aborted.Task.IsCompleted);
            release.SetResult();
            await aborted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { release.TrySetResult(); await serving; }
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public async Task EOF_before_the_acknowledgement_aborts_the_reservation_and_never_runs_commit()
    {
        using var stream = new ControlReviewStream(NthControl.Frame(NthControlRequest.QuitIfIdle.ToJson()));
        var aborted = 0; var committed = 0;
        var host = new ScriptedControlHost(_ => new NthControlAnswer(ScriptedControlHost.Ok(),
            Abort: () => aborted++, Commit: _ => { committed++; return Task.FromResult<NthControlResponse?>(null); }));
        await NthControlConnection.ServeAsync(stream, host, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), default, default);
        Assert.True(stream.Disposed);
        Assert.Equal(1, aborted);
        Assert.Equal(0, committed);
    }
}
