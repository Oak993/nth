using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pages;

[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-17")]
[Trait("Rule", "R-TEST-9")]
public sealed class W3_ManualPlacementTests
{
    [Fact]
    public async Task Check_again_returns_and_shows_checking_before_held_placement_finishes()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, LibraryFixtures.Pin, "fixture failure")], [], [], []);
        using var release = new ManualResetEventSlim();
        using var callerAlive = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var returned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callerThread = 0;
        var placementThread = 0;
        rig.Backend.ManualWork = _ =>
        {
            placementThread = Environment.CurrentManagedThreadId;
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Placement release gate was not opened.");
            return 1;
        };
        Task? work = null;
        var caller = Task.Run(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            work = rig.Page.CheckAgain("S1");
            returned.TrySetResult();
            if (!callerAlive.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Caller release gate was not opened.");
        });
        try
        {
            await returned.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotEqual(callerThread, placementThread);
            Assert.Equal(ItemState.Checking, rig.Row("S1").State);
            Assert.False(work!.IsCompleted);
            Assert.Empty(rig.Install.Calls);
            await rig.Page.CheckAgain("S1").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(["place S1"], rig.Backend.Calls.Where(c => c.StartsWith("place", StringComparison.Ordinal)));
        }
        finally
        {
            release.Set();
            callerAlive.Set();
            await caller.WaitAsync(TimeSpan.FromSeconds(20));
            if (work is not null) await work.WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Equal(["S1"], rig.Install.Calls);
        Assert.Equal(ItemState.Installed, rig.Row("S1").State);
    }

    [Fact]
    public async Task A_placement_failure_keeps_its_original_message_and_never_starts_installation()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, LibraryFixtures.Pin, "fixture failure")], [], [], []);
        rig.Backend.ManualWork = _ => throw new IOException("fixture placement failure");
        await rig.Page.CheckAgain("S1").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ItemState.Failed, rig.Row("S1").State);
        Assert.Equal("fixture placement failure", rig.Row("S1").Failure);
        Assert.Empty(rig.Install.Calls);
    }

    [Fact]
    public async Task Cancelling_held_manual_placement_never_starts_installation_after_it_finishes()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Failed, null, LibraryFixtures.Pin, "fixture failure")], [], [], []);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Backend.ManualWork = _ =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Placement release gate was not opened.");
            return 1;
        };
        var work = Task.Run(() => rig.Page.CheckAgain("S1"));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            rig.Page.CancelItem("S1");
        }
        finally
        {
            release.Set();
            await work.WaitAsync(TimeSpan.FromSeconds(20));
        }
        Assert.Empty(rig.Install.Calls);
        Assert.Equal(ItemState.Failed, rig.Row("S1").State);
    }
}
