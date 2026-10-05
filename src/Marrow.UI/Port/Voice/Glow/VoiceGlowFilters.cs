// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2): the CSS filter functions the recipe uses
// (`hue-rotate()`, `brightness()`, `saturate()`: Filter Effects Module Level 1, their colour matrices) as Skia colour filters, composed in the order the stylesheet
// writes them (each clamps, as a chain of CSS filter functions does).
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

internal static class VoiceGlowFilters
{
    /// <summary>The 4 x 5 matrix of <c>hue-rotate(degrees)</c> (row major, offsets 0).</summary>
    public static void HueRotate(Span<float> m, double degrees)
    {
        var rad = degrees * Math.PI / 180;
        double c = Math.Cos(rad), s = Math.Sin(rad);
        Set(m,
            0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928,
            0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283,
            0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072);
    }

    /// <summary>The matrix of <c>brightness(amount)</c>.</summary>
    public static void Brightness(Span<float> m, double amount) =>
        Set(m, amount, 0, 0, 0, amount, 0, 0, 0, amount);

    /// <summary>The matrix of <c>saturate(amount)</c>.</summary>
    public static void Saturate(Span<float> m, double s) =>
        Set(m,
            0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
            0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
            0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s);

    private static void Set(Span<float> m, double a, double b, double c, double d, double e, double f, double g, double h, double i)
    {
        m.Clear();
        m[0] = (float)a; m[1] = (float)b; m[2] = (float)c;
        m[5] = (float)d; m[6] = (float)e; m[7] = (float)f;
        m[10] = (float)g; m[11] = (float)h; m[12] = (float)i;
        m[18] = 1;
    }

    /// <summary><c>brightness(b) saturate(s)</c> as one colour filter: the part of the chain that does not change from frame to frame (the caller disposes it).</summary>
    public static SKColorFilter BrightnessSaturate(double brightness, double saturation)
    {
        Span<float> m = stackalloc float[20];
        Brightness(m, brightness);
        using var bright = SKColorFilter.CreateColorMatrix(m);
        Saturate(m, saturation);
        using var sat = SKColorFilter.CreateColorMatrix(m);
        return SKColorFilter.CreateCompose(sat, bright);
    }

    /// <summary><c>hue-rotate(h)</c> followed by <paramref name="brightnessSaturate"/> (the caller disposes the result, not the argument).</summary>
    public static SKColorFilter Chain(double hueDegrees, SKColorFilter brightnessSaturate)
    {
        Span<float> m = stackalloc float[20];
        HueRotate(m, hueDegrees);
        using var hue = SKColorFilter.CreateColorMatrix(m);
        return SKColorFilter.CreateCompose(brightnessSaturate, hue);
    }

    /// <summary><c>hue-rotate(h) brightness(b) saturate(s)</c> as one colour filter (the caller disposes it).</summary>
    public static SKColorFilter Chain(double hueDegrees, double brightness, double saturation)
    {
        Span<float> m = stackalloc float[20];
        HueRotate(m, hueDegrees);
        using var hue = SKColorFilter.CreateColorMatrix(m);
        Brightness(m, brightness);
        using var bright = SKColorFilter.CreateColorMatrix(m);
        Saturate(m, saturation);
        using var sat = SKColorFilter.CreateColorMatrix(m);
        using var first = SKColorFilter.CreateCompose(bright, hue);
        return SKColorFilter.CreateCompose(sat, first);
    }
}
