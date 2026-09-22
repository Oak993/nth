namespace Marrow.Core.Port;

/// <summary>A <c>ContinuousClock.Instant</c>: monotonic, in TimeSpan ticks, from TimeProvider timestamps.</summary>
public readonly record struct ContinuousInstant(long Ticks) : IComparable<ContinuousInstant>
{
    public static TimeSpan operator -(ContinuousInstant a, ContinuousInstant b) => TimeSpan.FromTicks(a.Ticks - b.Ticks);
    public static ContinuousInstant operator -(ContinuousInstant a, TimeSpan b) => new(a.Ticks - b.Ticks);
    public static ContinuousInstant operator +(ContinuousInstant a, TimeSpan b) => new(a.Ticks + b.Ticks);
    public static bool operator <(ContinuousInstant a, ContinuousInstant b) => a.Ticks < b.Ticks;
    public static bool operator >(ContinuousInstant a, ContinuousInstant b) => a.Ticks > b.Ticks;
    public static bool operator <=(ContinuousInstant a, ContinuousInstant b) => a.Ticks <= b.Ticks;
    public static bool operator >=(ContinuousInstant a, ContinuousInstant b) => a.Ticks >= b.Ticks;
    public int CompareTo(ContinuousInstant other) => Ticks.CompareTo(other.Ticks);
}

/// <summary>
/// PORT-ONLY. Every clock read and every wait in the port goes through one BCL TimeProvider, so a test drives
/// every timer and every stamp at once (ED-068). Production is <see cref="TimeProvider.System"/>. Wall clock
/// (<c>Date()</c>) is <see cref="SwiftDate.Now"/>; the monotonic clock is <see cref="Now"/>. Source: draft B
/// (proto-B SwiftClock.cs) with draft C's Ambient seam.
/// </summary>
public static class SwiftClock
{
    public static Ambient<TimeProvider> Provider { get; } = new(TimeProvider.System);

    /// <summary><c>ContinuousClock.now</c>. Never DateTime.Now for intervals: it jumps with clock changes.</summary>
    public static ContinuousInstant Now
    {
        get
        {
            var provider = Provider.Current;
            var timestamp = provider.GetTimestamp();
            var frequency = provider.TimestampFrequency;
            return new ContinuousInstant(frequency == TimeSpan.TicksPerSecond
                ? timestamp
                : (long)((Int128)timestamp * TimeSpan.TicksPerSecond / frequency));
        }
    }
}
