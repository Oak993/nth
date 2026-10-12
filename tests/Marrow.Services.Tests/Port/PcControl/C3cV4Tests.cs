using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-17: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3cV4Tests
{
    [Theory]
    [InlineData(PcElementAction.Focus, false)]
    [InlineData(PcElementAction.Focus, true)]
    [InlineData(PcElementAction.Invoke, false)]
    [InlineData(PcElementAction.Invoke, true)]
    public async Task Public_Perform_passes_the_caller_token_into_resolved_lookup_and_build(PcElementAction action, bool afterBuild)
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        var builds = 0;
        var provider = new UiaPerformProvider(
            (_, _) => { if (!afterBuild) stop.Cancel(); return new object(); },
            _ => { builds++; if (afterBuild) stop.Cancel(); return new PcUiNode("1", "button", "Save"); },
            (_, _, _, _) => { calls++; return PcUiaOutcome.Ok; },
            (_, work, token) => { Assert.Equal(stop.Token, token); return Task.FromResult(work()); }, body => body());
        using var reader = new UiaReader(provider);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await reader.Perform(0x200, "1", new PcElementCommand(action), stop.Token));
        Assert.Equal(afterBuild ? 1 : 0, builds);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(PcElementAction.Focus)]
    [InlineData(PcElementAction.Invoke)]
    public async Task Public_Perform_passes_the_same_token_to_the_pattern_dispatch(PcElementAction action)
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        var starts = 0;
        using var reader = new UiaReader(new UiaPerformProvider((_, _) => new object(),
            _ => new PcUiNode("1", "button", "Save"),
            (_, _, _, token) => { Assert.Equal(stop.Token, token); calls++; return PcUiaOutcome.Ok; },
            (_, work, token) => { Assert.Equal(stop.Token, token); return Task.FromResult(work()); }, body => { starts++; body(); }));
        Assert.True((await reader.Perform(0x200, "1", new PcElementCommand(action), stop.Token)).Done);
        Assert.Equal(1, calls);
        Assert.Equal(action == PcElementAction.Invoke ? 1 : 0, starts);
    }

    [Fact]
    public void Sacrificial_checks_cancellation_at_thread_entry_before_calling_the_action()
    {
        using var stop = new CancellationTokenSource();
        var calls = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => UiaReader.Sacrificial(() => { calls++; return PcUiaOutcome.Ok; }, stop.Token,
            body => { stop.Cancel(); body(); }));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Sacrificial_converts_provider_OCE_to_task_cancellation_with_the_caller_token()
    {
        var exception = Assert.Throws<TaskCanceledException>(() => UiaReader.Sacrificial(
            () => throw new OperationCanceledException(new CancellationToken(true)), CancellationToken.None, body => body()));
        Assert.Equal(CancellationToken.None, exception.CancellationToken);
    }

    [Fact]
    public async Task Sacrificial_cancels_its_wait_while_the_action_is_still_blocked()
    {
        using var stop = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task worker = Task.CompletedTask;
        var waiting = Task.Run(() => UiaReader.Sacrificial(() =>
        {
            entered.TrySetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return PcUiaOutcome.Ok;
        }, stop.Token, body => worker = Task.Run(body)));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await stop.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(worker.IsCompleted);
        }
        finally
        {
            release.Set();
            await worker.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
