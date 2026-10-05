namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// The two CSS curves of the voice design (voice-composer.html), solved the way a browser solves them (WebKit's UnitBezier: Newton's method, then bisection), not the way the port does,
/// so a test compares the port's motion with the design's curve and not with itself. <c>ease</c> is cubic-bezier(.25,.1,.25,1); <c>ez</c> is cubic-bezier(.32,.72,0,1).
/// </summary>
internal static class CssCurves
{
    public static double Ease(double x) => Solve(0.25, 0.1, 0.25, 1, x);

    public static double Ez(double x) => Solve(0.32, 0.72, 0, 1, x);

    /// <summary>The curve at <paramref name="seconds"/> of a transition lasting <paramref name="duration"/>: 0 before it starts, 1 after it ends.</summary>
    public static double At(Func<double, double> curve, double seconds, double duration) =>
        seconds <= 0 ? 0 : seconds >= duration ? 1 : curve(seconds / duration);

    private static double Solve(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx;
        double cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
        double X(double t) => ((ax * t + bx) * t + cx) * t;
        double dX(double t) => (3 * ax * t + 2 * bx) * t + cx;
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = X(t) - x;
            if (Math.Abs(error) < 1e-12) return ((ay * t + by) * t + cy) * t;
            var slope = dX(t);
            if (Math.Abs(slope) < 1e-9) break;
            t -= error / slope;
        }
        double low = 0, high = 1;
        t = x;
        while (low < high)
        {
            var error = X(t);
            if (Math.Abs(error - x) < 1e-12) break;
            if (x > error) low = t;
            else high = t;
            t = (high - low) / 2 + low;
            if (high - low < 1e-14) break;
        }
        return ((ay * t + by) * t + cy) * t;
    }
}
