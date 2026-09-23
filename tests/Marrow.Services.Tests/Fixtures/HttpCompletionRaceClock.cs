namespace Marrow.Services.Tests.Fixtures;

/// <summary>Holds the first timer's disposal so a cancellation callback can contend for Exchange's gate.</summary>
internal sealed class HttpCompletionRaceClock : TimeProvider
{
    private int created;
    private readonly ManualResetEventSlim entered = new();
    private readonly ManualResetEventSlim release = new();

    internal bool WaitForIdleDispose(TimeSpan guard) => entered.Wait(guard);

    internal void ReleaseIdleDispose() => release.Set();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new Timer(this, Interlocked.Increment(ref created) == 1);

    private sealed class Timer(HttpCompletionRaceClock owner, bool holdDispose) : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
            if (!holdDispose) return;
            owner.entered.Set();
            owner.release.Wait();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
