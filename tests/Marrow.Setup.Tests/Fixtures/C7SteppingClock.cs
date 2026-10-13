namespace Marrow.Setup.Tests.Fixtures;

internal sealed class C7SteppingClock : TimeProvider
{
    private long ticks;
    public TimeSpan Step { get; set; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Add(ref ticks, Step.Ticks);
}
