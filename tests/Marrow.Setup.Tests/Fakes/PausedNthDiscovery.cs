using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

// Coordinates overlapping calls entirely in memory; the first stale discovery finishes last without serialization.
internal sealed class PausedNthDiscovery(RunningNthProcess original) : IRunningNthProcesses, IDisposable
{
    private readonly ManualResetEventSlim firstRelease = new();
    private int calls;
    public TaskCompletionSource FirstEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource LaterEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<RunningNthProcess> FindInside(string installRoot)
    {
        var call = Interlocked.Increment(ref calls);
        if (call == 1)
        {
            FirstEntered.TrySetResult();
            firstRelease.Wait();
            return [];
        }
        LaterEntered.TrySetResult();
        return call == 2 ? [original] : [];
    }

    public bool IsRunning(RunningNthProcess process) => process == original;
    public ProcessEndResult End(RunningNthProcess process) => ProcessEndResult.Failed;
    public void ReleaseFirst() => firstRelease.Set();
    public void Dispose() => firstRelease.Dispose();
}
