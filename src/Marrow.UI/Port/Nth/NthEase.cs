// PORT-ONLY: OD-0136 amendment 2 (the nth motion pack, ED-NEW-nth-motion). The easing curves of
// E:\Droppy\work\design\nth-brand\nth-motion-review.html (its "How to build in Avalonia/Skia" notes), as CSS cubic-bezier
// functions solved exactly (Newton with a bisection fallback), so every primitive eases on the page's numbers. Nothing
// here allocates.
namespace Marrow.UI.Port.Nth;

/// <summary>A CSS <c>cubic-bezier(x1, y1, x2, y2)</c> timing function.</summary>
public readonly record struct NthCurve(double X1, double Y1, double X2, double Y2)
{
    /// <summary>The eased value at <paramref name="x"/> (clamped to 0..1, as a CSS animation holds its ends).</summary>
    public double Ease(double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        return Sample(Y1, Y2, Solve(x));
    }

    private static double Sample(double a1, double a2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * t * a1 + 3 * u * t * t * a2 + t * t * t;
    }

    private static double Slope(double a1, double a2, double t)
    {
        var u = 1 - t;
        return 3 * u * u * a1 + 6 * u * t * (a2 - a1) + 3 * t * t * (1 - a2);
    }

    // The bezier parameter whose x is x: Newton from x itself, then bisection when the slope is flat.
    private double Solve(double x)
    {
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var error = Sample(X1, X2, t) - x;
            if (Math.Abs(error) < 1e-9) return t;
            var slope = Slope(X1, X2, t);
            if (Math.Abs(slope) < 1e-6) break;
            t -= error / slope;
        }
        double lo = 0, hi = 1;
        t = x;
        for (var i = 0; i < 60; i++)
        {
            var value = Sample(X1, X2, t);
            if (Math.Abs(value - x) < 1e-10) return t;
            if (value < x) lo = t; else hi = t;
            t = (lo + hi) / 2;
        }
        return t;
    }
}

/// <summary>The page's named curves (nth-motion-review.html, <c>var E</c> and the build notes).</summary>
public static class NthEase
{
    /// <summary><c>cubic-bezier(.4,0,.2,1)</c>: the standard ease (spinner A, bar B unknown length, tick draw-in).</summary>
    public static readonly NthCurve Standard = new(.4, 0, .2, 1);
    /// <summary><c>cubic-bezier(.45,0,.2,1)</c>: the light's glide (text light, bar light, skeleton light, tube light).</summary>
    public static readonly NthCurve Glide = new(.45, 0, .2, 1);
    /// <summary><c>cubic-bezier(.65,0,.35,1)</c>: in-out (tile trace, thinking glyph gather).</summary>
    public static readonly NthCurve InOut = new(.65, 0, .35, 1);
    /// <summary><c>cubic-bezier(.2,.8,.2,1)</c>: the settle (enters, arrivals, word rise).</summary>
    public static readonly NthCurve Out = new(.2, .8, .2, 1);
    /// <summary><c>cubic-bezier(.3,1.3,.5,1)</c>: the done disc (about 4 % overshoot).</summary>
    public static readonly NthCurve Pop = new(.3, 1.3, .5, 1);
    /// <summary><c>cubic-bezier(.4,0,1,1)</c>: leaving (old word, rolled-out digit).</summary>
    public static readonly NthCurve Accelerate = new(.4, 0, 1, 1);
    /// <summary><c>cubic-bezier(.5,0,.75,0)</c>: the send arrow's launch.</summary>
    public static readonly NthCurve Launch = new(.5, 0, .75, 0);
    /// <summary><c>cubic-bezier(.3,1.4,.5,1)</c>: the stop square's pop-in.</summary>
    public static readonly NthCurve PopIn = new(.3, 1.4, .5, 1);
    /// <summary><c>cubic-bezier(.2,.9,.3,1.2)</c>: the send arrow dropping back.</summary>
    public static readonly NthCurve Drop = new(.2, .9, .3, 1.2);
    /// <summary>CSS <c>ease-in-out</c>, <c>cubic-bezier(.42,0,.58,1)</c>: the stop square's breath, the lens drift.</summary>
    public static readonly NthCurve EaseInOut = new(.42, 0, .58, 1);
    /// <summary>CSS <c>ease</c>, <c>cubic-bezier(.25,.1,.25,1)</c>: the status slot's cross-fades (<c>transition: opacity .2s ease</c>).</summary>
    public static readonly NthCurve Css = new(.25, .1, .25, 1);

    /// <summary>The page's <c>cl</c>: clamp to 0..1.</summary>
    public static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;

    /// <summary>The phase of <paramref name="seconds"/> in a loop of <paramref name="period"/> (0..1, never negative).</summary>
    public static double Phase(double seconds, double period) => ((seconds / period % 1) + 1) % 1;

    /// <summary>The page's calm pulse: 0.6 at the start, eased by a cosine to 1 and back every 2.4 s (step 1 "Calm").</summary>
    public static double CalmPulse(double seconds) => .6 + .4 * (.5 + .5 * Math.Cos(2 * Math.PI * seconds / 2.4));
}
