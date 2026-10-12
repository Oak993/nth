using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-cx-6")]
public sealed class C3A6Tests
{
    [Fact]
    public void A_cancelled_invocation_at_thread_entry_never_dispatches()
    {
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var actions = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => UiaReader.PerformAction(
            () => { actions++; return new PcUiaOutcome(null, null); }, stop.Token));
        Assert.Equal(0, actions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelling_during_lookup_or_cache_build_prevents_dispatch(bool duringBuild)
    {
        using var stop = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var actions = 0;
        var work = Task.Run(() => UiaReader.PerformResolved(
            () => { if (!duringBuild) { entered.Set(); release.Wait(); } return new object(); },
            _ => { if (duringBuild) { entered.Set(); release.Wait(); } return new PcUiNode("1", "button", "Delete"); },
            (_, _) => { Interlocked.Increment(ref actions); return new PcUiaOutcome(null, null); }, stop.Token));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            await stop.CancelAsync();
        }
        finally { release.Set(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, actions);
    }
}
