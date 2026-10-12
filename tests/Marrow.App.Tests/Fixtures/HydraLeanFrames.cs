using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for Views/Chat/HydraDecorationLeanTests.cs (R-TEST-2: a test class holds no nested type). Not port code.
/// <summary>A frame source that allocates nothing per frame: requests go into a fixed array.</summary>
public sealed class SlotFrames : IFrameSource
{
    private readonly Action<TimeSpan>?[] waiting = new Action<TimeSpan>?[16];
    private readonly Action<TimeSpan>?[] running = new Action<TimeSpan>?[16];
    private int count;

    public void RequestFrame(Action<TimeSpan> frame) => waiting[count++] = frame;

    public void CancelFrame(Action<TimeSpan> frame)
    {
        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(waiting[i], frame)) continue;
            Array.Copy(waiting, i + 1, waiting, i, count - i - 1);
            waiting[--count] = null;
            return;
        }
    }

    public int Pending => count;

    public void Fire(TimeSpan now)
    {
        var n = count;
        Array.Copy(waiting, running, n);
        Array.Clear(waiting, 0, n);
        count = 0;
        for (var i = 0; i < n; i++) { running[i]!(now); running[i] = null; }
    }
}

/// <summary>A clock that steps by hand and allocates nothing (ManualTimeProvider's Advance queries its timers with LINQ);
/// its timers never fire.</summary>
public sealed class StepClock : TimeProvider
{
    private static readonly DateTimeOffset Origin = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
    private long ticks;

    public TimeSpan Elapsed => TimeSpan.FromTicks(ticks);

    public void Step(TimeSpan by) => ticks += by.Ticks;

    public override long GetTimestamp() => ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override DateTimeOffset GetUtcNow() => Origin + TimeSpan.FromTicks(ticks);

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Idle();

    private sealed class Idle : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
