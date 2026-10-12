using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.PcControl.Native;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

// ED-NEW-pcctl-cx-7: the trait names the owner decision, as a two-word ED id is not a well-formed trait id (ED-NEW-pcctl-35).
[Trait("Decision", "OD-0144")]
public sealed class C3A7Tests
{
    [Fact]
    public async Task Hook_failure_cancels_deferred_focus_before_restoring_the_user()
    {
        var rig = new PcBorrowRig().ProveAll();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> Focus(CancellationToken token)
        {
            try
            {
                await release.Task;
                token.ThrowIfCancellationRequested();
                rig.Host.SetForeground(PcBorrowRig.Target);
                return true;
            }
            finally { finished.TrySetResult(); }
        }
        var borrow = rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41) with { Focus = Focus }, CancellationToken.None);
        await PcTestWait.Until(() => rig.Host.Foreground == PcBorrowRig.Target);
        rig.Monitor.Installed(false);
        try
        {
            Assert.Equal(PcBorrowStatus.NeedsRealInput, (await borrow.WaitAsync(TimeSpan.FromSeconds(5))).Status);
            Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        }
        finally { release.TrySetResult(); }
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        Assert.Empty(rig.Host.Sent);
    }

    [Fact]
    public async Task Hook_failure_abandons_hung_focus_without_waiting_for_the_cap()
    {
        var rig = new PcBorrowRig().ProveAll();
        var focus = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var borrow = rig.Lease.Borrow(rig.Keys(PcBorrowRig.Paint, 0x41) with { Focus = _ => focus.Task }, CancellationToken.None);
        await PcTestWait.Until(() => rig.Host.Foreground == PcBorrowRig.Target);
        var at = rig.Clock.NowMs;
        rig.Monitor.Installed(false);
        try
        {
            var result = await borrow.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(PcBorrowStatus.NeedsRealInput, result.Status);
            Assert.Equal(PcRealInputReason.MonitorUnhealthy, result.Reason);
            Assert.Equal(at, rig.Clock.NowMs);
            Assert.Empty(rig.Host.Sent);
            Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
            Assert.False(rig.Hold.Swallowing);
        }
        finally
        {
            focus.TrySetResult(true);
            rig.Clock.Advance(1000);
            await borrow.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Hook_failure_during_a_drag_ends_it_and_releases_the_button()
    {
        var rig = new PcBorrowRig().ProveAll();
        var worker = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Lease.ActionStarted = task => worker.TrySetResult(task);
        var (arrays, gaps) = PcBorrowRung.DragArrays([new PcPoint(10, 10), new PcPoint(50, 50)], [], 100,
            rig.Host.Desktop(), rig.Host.ScanCode);
        var request = rig.Click() with { Chunks = arrays, GapsMs = gaps, HoldCapMs = RealInputLease.DragCapMs };
        var borrowing = rig.Lease.Borrow(request, CancellationToken.None);
        var act = await worker.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(rig.Host.Sent);
        rig.Monitor.Installed(false);
        var result = await borrowing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PcBorrowStatus.NeedsRealInput, result.Status);
        Assert.Equal(PcRealInputReason.MonitorUnhealthy, result.Reason);
        Assert.Equal(2, rig.Host.Sent.Count);
        Assert.Equal(PcInputPlan.LEFTUP, Assert.Single(rig.Host.Sent[1].Events).Flags);
        Assert.Equal(PcBorrowRig.User, rig.Host.Foreground);
        Assert.False(rig.Hold.Swallowing);
        rig.Clock.Advance(1000);
        try { await act.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { }
        Assert.True(act.IsCompleted);
        Assert.Equal(2, rig.Host.Sent.Count);
    }
}
