namespace Marrow.App.Tests.Fixtures;

// OD-0126 and the lead's same-timestamp ruling: gen.py .xf>span is opacity .1s,
// the placeholder .15s, both CSS ease (cubic-bezier(.25,.1,.25,1)). Solve independently,
// without calling Animation.Progress or adding a comparison margin.
internal static class GapDetailFrameOracle
{
    internal static double Opacity(TimeSpan elapsed, double seconds = .1)
    {
        if (elapsed <= TimeSpan.Zero) return 1;
        var x = elapsed.TotalSeconds / seconds;
        if (x >= 1) return 0;
        static double Coordinate(double t, double a, double b) =>
            3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        double lo = 0, hi = 1;
        for (var i = 0; i < 80; i++)
        {
            var t = (lo + hi) / 2;
            if (Coordinate(t, .25, .25) < x) lo = t; else hi = t;
        }
        return 1 - Coordinate((lo + hi) / 2, .1, 1);
    }
}
