// PORT-ONLY: web surfaces W16b (DESIGN.md 7.2 "chart (line, bar, area, pie, scatter)", 7.5 "tabular numerals"; ED-NEW-web-w16b-1).
// No Swift counterpart. The numbers a chart's axes are made of: round tick values at 1, 2, 2.5 and 5 times a power of ten, the
// domain that holds them, and the shapes of a pie's slices. Pure maths on doubles, so every rule can be tested without a window.
namespace Marrow.UI.NthUi.Charts;

/// <summary>Axis maths: nice steps, ticks and domains.</summary>
public static class NthUiChartMath
{
    /// <summary>The ticks a Y axis aims for across the plot.</summary>
    public const int TargetTicks = 5;

    /// <summary>The smallest of 1, 2, 2.5, 5 and 10 times a power of ten that is at least <paramref name="raw"/> (1 for nothing to go on).
    /// Rounding up keeps an axis to about five lines, and the 2.5 keeps the range from growing by half again (a top of 105 gets
    /// lines every 25 and ends at 125, not every 50 and 150).</summary>
    public static double NiceStep(double raw)
    {
        if (!double.IsFinite(raw) || raw <= 0) return 1;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var fraction = raw / power;
        var nice = fraction <= 1.0000001 ? 1 : fraction <= 2.0000001 ? 2 : fraction <= 2.5000001 ? 2.5 : fraction <= 5.0000001 ? 5 : 10;
        return nice * power;
    }

    /// <summary>The round values from <paramref name="lo"/> to <paramref name="hi"/> that a gridline sits on, at most about
    /// <paramref name="target"/> of them.</summary>
    public static double[] Ticks(double lo, double hi, int target = TargetTicks)
    {
        if (!double.IsFinite(lo) || !double.IsFinite(hi) || hi <= lo) return [];
        var step = NiceStep((hi - lo) / Math.Max(1, target));
        return TicksAt(lo, hi, step);
    }

    /// <summary>The multiples of <paramref name="step"/> inside the closed range.</summary>
    public static double[] TicksAt(double lo, double hi, double step)
    {
        if (!(step > 0) || !double.IsFinite(step) || hi < lo) return [];
        var first = Math.Ceiling(lo / step - 1e-9);
        var last = Math.Floor(hi / step + 1e-9);
        var count = (int)Math.Min(200, Math.Max(0, last - first + 1));
        var ticks = new double[count];
        for (var i = 0; i < count; i++) ticks[i] = Round((first + i) * step, step);
        return ticks;
    }

    /// <summary>The range from <paramref name="lo"/> to <paramref name="hi"/> widened outward to whole steps, except an edge
    /// the author fixed (<paramref name="fixedLo"/>, <paramref name="fixedHi"/>). A range of no width opens to a unit around it.
    /// With <paramref name="breathe"/>, an edge that lands exactly on the data takes one more step, so a scatter's outermost
    /// points do not sit on the axis.</summary>
    public static (double Lo, double Hi, double Step) Domain(double lo, double hi, bool fixedLo = false, bool fixedHi = false, int target = TargetTicks, bool breathe = false)
    {
        if (!double.IsFinite(lo)) lo = 0;
        if (!double.IsFinite(hi)) hi = lo + 1;
        if (hi < lo) (lo, hi) = (hi, lo);
        if (hi - lo < 1e-12)
        {
            var pad = Math.Max(Math.Abs(lo) * .1, 1);
            if (!fixedLo) lo -= pad;
            if (!fixedHi) hi += pad;
            if (hi - lo < 1e-12) hi = lo + 1;
        }
        var dataLo = lo;
        var dataHi = hi;
        var step = NiceStep((hi - lo) / Math.Max(1, target));
        if (!fixedLo) lo = Round(Math.Floor(lo / step + 1e-9) * step, step);
        if (!fixedHi) hi = Round(Math.Ceiling(hi / step - 1e-9) * step, step);
        if (breathe)
        {
            if (!fixedLo && Math.Abs(lo - dataLo) < 1e-9 * Math.Max(1, Math.Abs(lo))) lo = Round(lo - step, step);
            if (!fixedHi && Math.Abs(hi - dataHi) < 1e-9 * Math.Max(1, Math.Abs(hi))) hi = Round(hi + step, step);
        }
        return (lo, hi, step);
    }

    /// <summary>Removes the floating point dust of a multiple of <paramref name="step"/> (0.30000000000000004 to 0.3).</summary>
    public static double Round(double value, double step)
    {
        var digits = Math.Min(10, Decimals(step) + 2);
        return Math.Round(value, digits, MidpointRounding.AwayFromZero);
    }

    /// <summary>The decimals every tick label of an axis with ticks <paramref name="step"/> apart shows: as few as show the step exactly.</summary>
    public static int Decimals(double step)
    {
        if (!(step > 0) || !double.IsFinite(step)) return 0;
        for (var digits = 0; digits < 8; digits++)
        {
            if (Math.Abs(Math.Round(step, digits) - step) < 1e-9 * Math.Max(1, step)) return digits;
        }
        return 8;
    }

    /// <summary>A tick's label: the value with the axis's decimals, so a column of labels reads 3.8, 4.0, 4.2 and not 3.8, 4, 4.2.</summary>
    public static string TickLabel(double value, double step, System.Globalization.CultureInfo culture)
    {
        var rounded = Round(value, step);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("F" + Decimals(step), culture);
    }

    /// <summary>Where <paramref name="value"/> falls in the domain: 0 at <paramref name="lo"/>, 1 at <paramref name="hi"/>.</summary>
    public static double Fraction(double value, double lo, double hi) => hi > lo ? (value - lo) / (hi - lo) : 0;

    /// <summary>The share of the whole each of <paramref name="values"/> is (negative and missing values count as nothing).</summary>
    public static double[] Shares(IReadOnlyList<double?> values)
    {
        var amounts = values.Select(v => v is double d && double.IsFinite(d) && d > 0 ? d : 0).ToArray();
        var total = amounts.Sum();
        return total > 0 ? amounts.Select(a => a / total).ToArray() : new double[amounts.Length];
    }

    /// <summary>The whole degrees, 0 to 100, shares round to so that they sum to 100 (largest remainders).</summary>
    public static int[] Percentages(IReadOnlyList<double> shares)
    {
        var floors = shares.Select(s => (int)Math.Floor(s * 100 + 1e-9)).ToArray();
        var missing = shares.Sum() > 0 ? 100 - floors.Sum() : 0;
        var order = Enumerable.Range(0, shares.Count).OrderByDescending(i => shares[i] * 100 - floors[i]).ThenBy(i => i).ToArray();
        for (var i = 0; i < missing && i < order.Length; i++) floors[order[i]]++;
        return floors;
    }
}
