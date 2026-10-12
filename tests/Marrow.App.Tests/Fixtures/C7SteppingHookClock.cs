namespace Marrow.App.Tests.Fixtures;

internal sealed class C7SteppingHookClock : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Add(ref ticks, TimeSpan.FromMilliseconds(200).Ticks);
}
