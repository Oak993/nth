namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// PORT-ONLY: a TimeProvider that forwards everything to another one (the harness's manual clock) and remembers the
/// due time of every timer made on it, so a test can tell a retry sleep (SwiftTask.sleep, Git.swift:223-224) from the
/// 300-second timeout timer of a running process, and advance the clock exactly when the sleep it waits for exists.
/// </summary>
public sealed class RecordingTimeProvider(TimeProvider inner) : TimeProvider
{
    private readonly List<TimeSpan> created = [];

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (created) created.Add(dueTime);
        return inner.CreateTimer(callback, state, dueTime, period);
    }

    /// <summary>How many timers were made with exactly this due time.</summary>
    public int Created(TimeSpan dueTime)
    {
        lock (created) return created.Count(due => due == dueTime);
    }
}
