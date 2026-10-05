namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// One transition sampled on the frame clock (CZ-T40): the value at each 1/60 s frame from the action, held to the named CSS timing function (solved here, not by the port) within 0.02 of the travel,
/// monotonic, never stepping further in one frame than the curve itself does (and never more than 15 % of the travel on a curve that can keep to that), and landed on the target at its end.
/// </summary>
internal static class FrameCurves
{
    internal const double FrameMs = 1000.0 / 60;

    internal static readonly (double, double, double, double) Ez = (.32, .72, 0, 1), Ease = (.25, .1, .25, 1), EaseInOut = (.42, 0, .58, 1);

    /// <summary>The CSS cubic-bezier's y at progress x.</summary>
    internal static double Bezier((double X1, double Y1, double X2, double Y2) c, double x)
    {
        static double At(double a, double b, double t) => 3 * a * t * (1 - t) * (1 - t) + 3 * b * t * t * (1 - t) + t * t * t;
        double lo = 0, hi = 1, t = x;
        for (var i = 0; i < 40; i++)
        {
            t = (lo + hi) / 2;
            if (At(c.X1, c.X2, t) < x) lo = t; else hi = t;
        }
        return At(c.Y1, c.Y2, t);
    }

    /// <summary>The values <paramref name="read"/> gives at the action (frame 0) and after each of the next <paramref name="frames"/> frames.</summary>
    internal static List<double> Sample(CustomizationPageRig rig, Func<double> read, int frames)
    {
        var seen = new List<double> { rig.Get(read) };
        for (var k = 1; k <= frames; k++)
        {
            rig.Advance(FrameMs, settle: false);
            seen.Add(rig.Get(read));
        }
        return seen;
    }

    internal static void Check(string name, IReadOnlyList<double> seen, double from, double to, double delay, double duration, (double, double, double, double) curve)
    {
        var travel = Math.Abs(to - from);
        Assert.True(travel > 0, name + ": nothing moves");
        var previousRef = from;
        for (var k = 0; k < seen.Count; k++)
        {
            var t = k * FrameMs / 1000;
            var progress = t <= delay ? 0 : Math.Min(1, (t - delay) / duration);
            var expected = from + (to - from) * Bezier(curve, progress);
            Assert.True(Math.Abs(seen[k] - expected) <= 0.02 * travel + 1e-6, $"{name} frame {k} ({k * FrameMs:F0} ms): {seen[k]:F4}, the curve {expected:F4}");
            if (k == 0) { previousRef = expected; continue; }
            var step = Math.Abs(seen[k] - seen[k - 1]);
            var curveStep = Math.Abs(expected - previousRef);
            Assert.True((to > from ? seen[k] >= seen[k - 1] - 1e-9 : seen[k] <= seen[k - 1] + 1e-9), $"{name} frame {k}: not monotonic");
            Assert.True(step <= Math.Max(0.15 * travel, curveStep + 0.02 * travel) + 1e-6, $"{name} frame {k}: a step of {step / travel:P0} of the travel");
            previousRef = expected;
        }
        Assert.True(Math.Abs(seen[^1] - to) <= 1e-6 * Math.Max(1, travel), $"{name}: ends at {seen[^1]:F4}, not {to:F4}");
    }
}
