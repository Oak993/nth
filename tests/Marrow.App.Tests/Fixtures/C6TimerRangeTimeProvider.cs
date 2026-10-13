namespace Marrow.App.Tests.Fixtures;

// Records the requested delay; fires immediately so a far-future wait stays deterministic and bounded.
public sealed class C6TimerRangeTimeProvider : TimeProvider
{
    public TimeSpan? LastDelay { get; private set; }
    public override DateTimeOffset GetUtcNow() => new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        LastDelay = dueTime;
        return TimeProvider.System.CreateTimer(callback, state, TimeSpan.Zero, period);
    }
}
