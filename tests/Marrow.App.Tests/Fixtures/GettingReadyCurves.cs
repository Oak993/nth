namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-2: the CSS curves of the locked page, solved here by bisection, independently of the port's easing.</summary>
public static class GettingReadyCurves
{
    /// <summary>cubic-bezier(x1, y1, x2, y2) at time fraction <paramref name="x"/> (CSS transition-timing-function).</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
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

    /// <summary>CSS "ease".</summary>
    public static double Ease(double x) => CubicBezier(0.25, 0.1, 0.25, 1, x);

    /// <summary>cubic-bezier(.32,.72,0,1): the page's curve for a mark, the bar and a swapped word's rise.</summary>
    public static double Settle(double x) => CubicBezier(0.32, 0.72, 0, 1, x);

    /// <summary>The page's hand-over easing (gr-page.js ease): cubic in-out.</summary>
    public static double HandOver(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x < 0.5 ? 4 * x * x * x : 1 - Math.Pow(-2 * x + 2, 3) / 2;
}
