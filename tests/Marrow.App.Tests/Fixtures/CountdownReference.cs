using Marrow.App.Port.Voice.Countdown;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

// VT-17 test fixture: the OD-0106 curves written out as a second derivation. Colourless from 0:30 to 0:15; a glide to the theme's warning colour starting at 0:15 and one to
// its error colour starting at 0:05, each 0.8 s (0.2 s under Reduce Motion) on cubic-bezier(.42, 0, .58, 1) through OKLCH; a 0.4 s sin-squared brightening that starts as the
// colour has arrived (none under Reduce Motion), raising OKLCH lightness by up to 0.05.
internal static class CountdownReference
{
    internal const double Lift = 0.05;

    internal static double Ease(double x) => CssCurve.Bezier(0.42, 0, 0.58, 1, x);

    internal static (double Progress, double ArrivedFor) Stage(double start, double remaining, double glide) =>
        (Math.Clamp((start - remaining) / glide, 0, 1), start - remaining - glide);

    internal static (CountdownColour Colour, double Bright) Expected(double remaining, bool reduce, CountdownPalette palette)
    {
        var glide = reduce ? 0.2 : 0.8;
        if (remaining > 15) return (palette.Neutral, 0);
        CountdownColour from, to;
        double start;
        if (remaining > 5) (from, to, start) = (palette.Neutral, palette.Warning, 15);
        else (from, to, start) = (palette.Warning, palette.Danger, 5);
        var (progress, arrived) = Stage(start, remaining, glide);
        var eased = Ease(progress);
        var a = OklchReference.FromSrgb(from.R, from.G, from.B);
        var b = OklchReference.FromSrgb(to.R, to.G, to.B);
        var mixed = OklchReference.Mix(a, b, eased);
        var bright = !reduce && arrived is >= 0 and < 0.4 ? Math.Pow(Math.Sin(Math.PI * arrived / 0.4), 2) : 0;
        var rgb = OklchReference.ToSrgb(mixed.L + Lift * bright, mixed.C, mixed.H);
        return (new CountdownColour(rgb.R, rgb.G, rgb.B, from.A + (to.A - from.A) * eased), bright);
    }
}
