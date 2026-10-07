namespace Marrow.App.Views.Composer;

// E:70-105, SPEC K-001/K-003..K-006/K-174. Eight value segments and integrated clocks, owned by the render thread.
public sealed class StrikeFastClock
{
    private readonly StrikeFastSegment[] segments = new StrikeFastSegment[8];
    private int count = 1;

    public StrikeFastClock() : this(true) { }
    public StrikeFastClock(bool initiallyFast) => segments[0] = new(0, initiallyFast ? 1 : 0, initiallyFast ? 1 : 0, 0, 0, 0);

    private StrikeFastSegment Segment(double time)
    {
        var result = segments[0]; for (var i = 0; i < count; i++) if (segments[i].At <= time) result = segments[i]; return result;
    }
    public double FastAt(double time)
    {
        var s = Segment(time); if (!(s.Duration > 0)) return time >= s.At ? s.To : s.From;
        return s.From + (s.To - s.From) * Strike.Ss(0, 1, (time - s.At) / s.Duration);
    }
    public void SetFast(bool on, double time, bool reduceMotion = false)
    {
        var f = FastAt(time); var to = on ? 1 : 0; var next = new StrikeFastSegment(time, f, to, reduceMotion ? 0 : 0.5 * Math.Abs(to - f), ClockB(time), ClockC(time));
        if (count == 8) { Array.Copy(segments, 1, segments, 0, 7); count--; } segments[count++] = next;
    }
    private static double Integral(StrikeFastSegment s, double time)
    {
        var d = time - s.At; if (d <= 0) return s.From * d; if (!(s.Duration > 0)) return s.To * d;
        var u = d / s.Duration;
        if (u <= 1) return s.Duration * (s.From * u + (s.To - s.From) * (u * u * u - u * u * u * u / 2));
        return s.Duration * (s.From + (s.To - s.From) * 0.5) + s.To * (d - s.Duration);
    }
    public double ClockB(double time) { var s = Segment(time); return s.B0 + (time - s.At) + 0.75 * Integral(s, time); }
    public double ClockC(double time) { var s = Segment(time); return s.C0 + (time - s.At) + 0.5 * Integral(s, time); }
}
