namespace Marrow.Testing;

/// <summary>
/// OD-0088 (ED-250): the CSS timing curves the locked accent board names (E:/Droppy/work/design/accent-build/SPEC.md,
/// "Easing names"), solved here independently of the port's own easing: the curve's parameter is found by bisection on
/// x(t) and y(t) is returned. A CSS transition of duration D and delay d at time t after its start is at
/// <c>Curve(clamp((t - d) / D))</c>.
/// </summary>
public static class CssCurve
{
    /// <summary><c>cubic-bezier(0.32, 0.72, 0, 1)</c>: slides and size changes.</summary>
    public static double Ez(double x) => Bezier(0.32, 0.72, 0, 1, x);

    /// <summary>CSS <c>ease</c>, <c>cubic-bezier(0.25, 0.1, 0.25, 1)</c>: fades, colours, hover, the fold.</summary>
    public static double Ease(double x) => Bezier(0.25, 0.1, 0.25, 1, x);

    /// <summary><c>cubic-bezier(0.2, 0.9, 0.25, 1)</c>: the theme menu growing in.</summary>
    public static double PopIn(double x) => Bezier(0.2, 0.9, 0.25, 1, x);

    /// <summary>A transition's progress <paramref name="ms"/> after it started.</summary>
    public static double At(Func<double, double> curve, double ms, double durationMs, double delayMs = 0) =>
        curve(Math.Clamp((ms - delayMs) / durationMs, 0, 1));

    public static double Bezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        static double Cubic(double u, double a, double b) => 3 * a * u * (1 - u) * (1 - u) + 3 * b * u * u * (1 - u) + u * u * u;
        double low = 0, high = 1;
        for (var i = 0; i < 80; i++)
        {
            var mid = (low + high) / 2;
            if (Cubic(mid, x1, x2) < x) low = mid; else high = mid;
        }
        return Cubic((low + high) / 2, y1, y2);
    }

    /// <summary>A browser's colour interpolation: per channel in sRGB, premultiplied with alpha; channels 0-255, alpha 0-1.</summary>
    public static (double R, double G, double B, double A) Mix((double R, double G, double B, double A) from, (double R, double G, double B, double A) to, double p)
    {
        var a = from.A + (to.A - from.A) * p;
        if (a <= 0) return (0, 0, 0, 0);
        double Channel(double c0, double c1) => (c0 * from.A + (c1 * to.A - c0 * from.A) * p) / a;
        return (Channel(from.R, to.R), Channel(from.G, to.G), Channel(from.B, to.B), a);
    }
}
