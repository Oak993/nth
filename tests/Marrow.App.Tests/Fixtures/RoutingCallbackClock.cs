namespace Marrow.App.Tests.Fixtures;

// Retains the callback already queued by a repeating timer, even after its ITimer is disposed.
public sealed class RoutingCallbackClock(TimeProvider inner) : TimeProvider
{
    public List<Action> RepeatingCallbacks { get; } = [];
    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();
    public override long GetTimestamp() => inner.GetTimestamp();
    public override long TimestampFrequency => inner.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (period == TimeSpan.FromMinutes(1)) RepeatingCallbacks.Add(() => callback(state));
        return inner.CreateTimer(callback, state, dueTime, period);
    }
}
