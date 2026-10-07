using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Fetch;

[Trait("Feature", "SK-16")]
[Trait("Rule", "R-TEST-4")]
[Collection("Dev data root env")]
public sealed class OrderedServiceStartsTests
{
    [Fact]
    public async Task A_delayed_first_observer_cannot_let_a_later_request_start_first()
    {
        using var rig = new FetchRig();
        var downloads = rig.Host.Downloads;
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requestingThread = 0;
        var stoppedOnce = 0;
        using var watch = downloads.Watch(() =>
        {
            if (Environment.CurrentManagedThreadId != Volatile.Read(ref requestingThread) || Interlocked.Exchange(ref stoppedOnce, 1) != 0) return;
            blocked.TrySetResult();
            release.Task.GetAwaiter().GetResult();
        });
        var first = Task.Run(() =>
        {
            Volatile.Write(ref requestingThread, Environment.CurrentManagedThreadId);
            return downloads.Install(rig.Item("S1"), new PipelineProgress([]), CancellationToken.None);
        });
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Task<string>? second = null;
        try { second = downloads.Install(rig.Item("S3"), new PipelineProgress([]), CancellationToken.None); }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Equal(["S1", "S3"], rig.Downloaded);
    }
}
