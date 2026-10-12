namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// ED-NEW-lean-9: another clock (a test's ManualTimeProvider), read through, counting without allocating the reads made on
/// one thread and on any other. Installed as SwiftClock's provider before a dot field is shown, it is the clock the field
/// captures, so its counts show which thread runs the field's render-server work (headless: the test thread, inside
/// ForceRenderTimerTick), and that a per-thread allocation count on that thread sees the work.
/// </summary>
public sealed class ThreadCountingTimeProvider(TimeProvider inner, int thread) : TimeProvider
{
    private int on, off;

    public override long GetTimestamp()
    {
        if (Environment.CurrentManagedThreadId == thread) Interlocked.Increment(ref on);
        else Interlocked.Increment(ref off);
        return inner.GetTimestamp();
    }

    public override long TimestampFrequency => inner.TimestampFrequency;
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);

    /// <summary>Reads made on the thread given at construction.</summary>
    public int OnThread => Volatile.Read(ref on);

    /// <summary>Reads made on any other thread.</summary>
    public int OffThread => Volatile.Read(ref off);
}
