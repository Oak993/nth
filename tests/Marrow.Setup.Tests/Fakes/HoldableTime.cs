namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// System time, until <see cref="HoldAfter"/>: then that many more timers run and every later one never fires (VELOPACK_CHECK.md
/// finding 14, ED-NEW-inst-fix-9). A handshake's wait can then be stopped while no poll is in flight, on every run.
/// </summary>
public sealed class HoldableTime : TimeProvider
{
    private int allowed = int.MaxValue;

    public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void HoldAfter(int timers) => Volatile.Write(ref allowed, timers);

    public override long GetTimestamp() => System.GetTimestamp();

    public override long TimestampFrequency => System.TimestampFrequency;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (Interlocked.Decrement(ref allowed) >= 0) return System.CreateTimer(callback, state, dueTime, period);
        Held.TrySetResult();
        return new NeverTimer();
    }
}

/// <summary>A timer that never fires.</summary>
public sealed class NeverTimer : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period) => true;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
