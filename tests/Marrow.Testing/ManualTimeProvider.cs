namespace Marrow.Testing;

/// <summary>
/// A TimeProvider whose clocks move only when a test calls <see cref="Advance"/> (ED-080). Wall clock and monotonic
/// clock move together. Timers fire in due order (ties in creation order), on the advancing thread, each under the
/// ExecutionContext captured when it was created, so per-flow overrides reach timer callbacks. No package: the
/// BCL TimeProvider is enough (Microsoft.Extensions.TimeProvider.Testing would add one and does not pump a main
/// loop between timers). Source: draft B (proto-B ManualTimeProvider).
/// </summary>
public sealed class ManualTimeProvider(DateTimeOffset start) : TimeProvider
{
    private readonly Lock gate = new();
    private readonly List<ManualTimer> timers = [];
    private DateTimeOffset now = start;
    private long timestamp;
    private long sequence;

    public ManualTimeProvider() : this(new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero)) { }

    public override DateTimeOffset GetUtcNow()
    {
        lock (gate) return now;
    }

    public override long GetTimestamp()
    {
        lock (gate) return timestamp;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public TimeSpan Elapsed
    {
        get { lock (gate) return TimeSpan.FromTicks(timestamp); }
    }

    public int ActiveTimers
    {
        get { lock (gate) return timers.Count(t => t.Due is not null); }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state, ExecutionContext.Capture());
        lock (gate) timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves time forward, firing each timer that falls due, then calls <paramref name="afterEach"/>.</summary>
    public void Advance(TimeSpan by, Action? afterEach = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        long target;
        lock (gate) target = timestamp + by.Ticks;
        while (true)
        {
            ManualTimer? next;
            lock (gate)
            {
                next = timers.Where(t => t.Due is { } due && due <= target)
                    .OrderBy(t => t.Due).ThenBy(t => t.Sequence).FirstOrDefault();
                if (next is null)
                {
                    now += TimeSpan.FromTicks(target - timestamp);
                    timestamp = target;
                    break;
                }
                var dueTicks = next.Due!.Value;
                now += TimeSpan.FromTicks(dueTicks - timestamp);
                timestamp = dueTicks;
                next.Due = next.Period is { } period && period > TimeSpan.Zero ? dueTicks + period.Ticks : null;
            }
            next.Fire();
            afterEach?.Invoke();
        }
        afterEach?.Invoke();
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state, ExecutionContext? context) : ITimer
    {
        public long? Due { get; set; }

        public TimeSpan? Period { get; private set; }

        public long Sequence { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.gate)
            {
                Sequence = ++owner.sequence;
                Period = period == Timeout.InfiniteTimeSpan ? null : period;
                Due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.timestamp + Math.Max(0, dueTime.Ticks);
            }
            return true;
        }

        public void Fire()
        {
            if (context is null) callback(state);
            else ExecutionContext.Run(context, _ => callback(state), null);
        }

        public void Dispose()
        {
            lock (owner.gate)
            {
                Due = null;
                owner.timers.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
