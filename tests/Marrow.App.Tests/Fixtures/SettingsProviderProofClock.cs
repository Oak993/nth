using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// R-TEST-10: observes timer requests and delegates all scheduling to the real test clock.
internal sealed class SettingsProviderProofClock(ManualTimeProvider time) : TimeProvider
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<TimeSpan> durations = new();
    public TimeSpan[] Durations => durations.ToArray();
    public override DateTimeOffset GetUtcNow() => time.GetUtcNow();
    public override long GetTimestamp() => time.GetTimestamp();
    public override long TimestampFrequency => time.TimestampFrequency;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    { durations.Enqueue(dueTime); return time.CreateTimer(callback, state, dueTime, period); }
}
