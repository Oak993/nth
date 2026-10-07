using Marrow.Services.Port.ModelsPalette;
using Marrow.Testing;
namespace Marrow.Services.Tests.Port.ModelsPalette;
[Trait("Decision","ED-NEW-modelspalette-11")]
public sealed class PalettePrimeBudgetTests
{
    [Fact]
    public async Task Cold_disk_misses_the_exact_fifty_millisecond_budget_without_cancelling_the_warm_snapshot()
    {
        var time=new ManualTimeProvider();var disk=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting=PalettePrimeBudget.BeforeWindow(disk.Task,time);Assert.False(waiting.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(49));Assert.False(waiting.IsCompleted);
        time.Advance(TimeSpan.FromMilliseconds(1));Assert.False(await waiting);Assert.False(disk.Task.IsCompleted);
        disk.SetResult();await disk.Task;Assert.True(await PalettePrimeBudget.BeforeWindow(disk.Task,time));Assert.Equal(0,time.ActiveTimers);
    }
    [Fact]
    public async Task A_verified_warm_snapshot_finishes_before_the_budget_and_removes_the_deadline()
    {
        var time=new ManualTimeProvider();var disk=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiting=PalettePrimeBudget.BeforeWindow(disk.Task,time);time.Advance(TimeSpan.FromMilliseconds(37));disk.SetResult();Assert.True(await waiting);
        Assert.Equal(0,time.ActiveTimers);
    }
}
