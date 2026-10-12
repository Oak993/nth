namespace Marrow.App.Tests.Fixtures;

// Stops the pump's first read while the test publishes. Reads 1/2 drain the ready batch;
// read 3 tests the wait deadline; a second wait sample (old code) crosses it by 2 ms.
public sealed class C6cPumpBoundaryTimeProvider(DateTimeOffset due) : TimeProvider, IDisposable
{
    private readonly ManualResetEventSlim release = new();
    private int reads;
    public TaskCompletionSource FirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource TimerRequested { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TimeSpan? FirstDelay { get; private set; }

    public override DateTimeOffset GetUtcNow()
    {
        var read = Interlocked.Increment(ref reads);
        if (read == 1)
        {
            FirstRead.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("First clock read was not released.");
        }
        return read <= 3 ? due.AddTicks(-1) : due.AddMilliseconds(2);
    }

    public void ReleaseFirstRead() => release.Set();
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        FirstDelay ??= dueTime;
        TimerRequested.TrySetResult();
        return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
    public void Dispose() { release.Set(); release.Dispose(); }
}
