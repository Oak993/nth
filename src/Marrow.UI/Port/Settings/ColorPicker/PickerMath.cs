// OD-0099 Look Picks, Q-CZ-3 B. SOURCE FACT: every formula here is lp-pickers.js's (variant B and the colour helpers above it),
// and is checked against a node run of that file (tests/fixtures/customization/picker/gen-picker-fixture.mjs, picker.json).
// JavaScript's Math.round rounds halves up; .NET's default rounds them to even, so every rounding goes through JsRound.
using System.Globalization;

namespace Marrow.UI.Port.Settings.ColorPicker;

/// <summary>The picker's colour maths, hex parsing and pointer-to-value mapping.</summary>
public static class PickerMath
{
    /// <summary>The thumb of a strip is 16 px wide and travels the strip's width less its own (wireStrip: x - 8, w - 16).</summary>
    public const double StripThumb = 16;

    /// <summary>The square's round thumb is 18 px (render: c.s * w - 9).</summary>
    public const double SquareThumb = 18;

    /// <summary>The hue strip reaches 359.9, not 360, so its far end is not the near end (wireStrip: u * 359.9).</summary>
    public const double HueMax = 359.9;

    public static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

    /// <summary>JavaScript's Math.round: halves go up.</summary>
    public static double JsRound(double x) => Math.Floor(x + 0.5);

    /// <summary>"#RRGGBB" (the leading # is skipped, as hexRgb does with slice(i, i + 2)) to its three channels 0..255.</summary>
    public static double[] HexRgb(string hex) =>
        new[] { 1, 3, 5 }.Select(i => (double)int.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToArray();

    /// <summary>Three channels to "#RRGGBB" in upper case, each rounded and clamped to 0..255.</summary>
    public static string RgbHex(double r, double g, double b) =>
        "#" + string.Concat(new[] { r, g, b }.Select(v => ((int)Clamp(JsRound(v), 0, 255)).ToString("X2", CultureInfo.InvariantCulture)));

    public static string RgbHex(double[] c) => RgbHex(c[0], c[1], c[2]);

    /// <summary>rgbHsv: channels 0..255 to hue (0..360), saturation and value.</summary>
    public static (double H, double S, double V) RgbHsv(double r, double g, double b)
    {
        r /= 255; g /= 255; b /= 255;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), d = mx - mn;
        double h = 0;
        if (d > 0) h = mx == r ? ((g - b) / d) % 6 : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h = (h * 60 + 360) % 360;
        return (h, mx != 0 ? d / mx : 0, mx);
    }

    /// <summary>hsvRgb: hue, saturation and value to channels 0..255 (not rounded).</summary>
    public static double[] HsvRgb(double h, double s, double v)
    {
        double F(double n)
        {
            var k = (n + h / 60) % 6;
            return v - v * s * Math.Max(0, Math.Min(Math.Min(k, 4 - k), 1));
        }
        return [F(5) * 255, F(3) * 255, F(1) * 255];
    }

    /// <summary>hsvHex: the colour's "#RRGGBB" without its alpha.</summary>
    public static string HsvHex(PickerColor c) => RgbHex(HsvRgb(c.H, c.S, c.V));

    /// <summary>fromHex: "#RRGGBB" with an alpha (1 when none).</summary>
    public static PickerColor FromHex(string hex, double? alpha = null)
    {
        var rgb = HexRgb(hex);
        var (h, s, v) = RgbHsv(rgb[0], rgb[1], rgb[2]);
        return new PickerColor(h, s, v, alpha ?? 1);
    }

    /// <summary>The form a custom colour is stored in: "#RRGGBBAA" (alpha as a byte).</summary>
    public static string Stored(PickerColor c) =>
        HsvHex(c) + ((int)Clamp(JsRound(c.A * 255), 0, 255)).ToString("X2", CultureInfo.InvariantCulture);

    /// <summary>A stored "#RRGGBBAA" or "#RRGGBB" (opaque) as a picker colour; null when it is neither.</summary>
    public static PickerColor? FromStored(string? text) =>
        Marrow.Core.Models.ComposerLights.UiverseAuraOptions.NormalizedHex(text) is { } hex
            ? FromHex(Marrow.Core.Models.ComposerLights.UiverseAuraOptions.OpaqueHex(hex), Marrow.Core.Models.ComposerLights.UiverseAuraOptions.AlphaOf(hex))
            : null;

    /// <summary>
    /// What the hex field accepts. The design's field (B) takes six hex digits; this field also takes eight, the last pair the alpha as a
    /// byte / 255, exactly as the design's own components field does (pickerA: /^[0-9a-f]{6}([0-9a-f]{2})?$/i). A leading # is skipped and
    /// the text trimmed. Null for anything else (the field then shows the current colour again).
    /// </summary>
    public static (string Rgb, double? Alpha)? ParseHexEntry(string? text)
    {
        var v = (text ?? "").Trim();
        if (v.StartsWith('#')) v = v[1..];
        if (v.Length is not (6 or 8)) return null;
        foreach (var ch in v) if (!Uri.IsHexDigit(ch)) return null;
        var alpha = v.Length == 8 ? int.Parse(v.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0 : (double?)null;
        return ("#" + v[..6].ToUpperInvariant(), alpha);
    }

    // ---- the square: saturation across, value up (drag(sv): s = x / w, v = 1 - y / h)
    public static (double S, double V) SquareAt(double x, double y, double w, double h) => (Clamp(x / w, 0, 1), Clamp(1 - y / h, 0, 1));

    /// <summary>Where the 18 px thumb's top-left sits (render: s * w - 9, (1 - v) * h - 9).</summary>
    public static (double X, double Y) SquareThumbAt(double s, double v, double w, double h) => (s * w - SquareThumb / 2, (1 - v) * h - SquareThumb / 2);

    // ---- the strips
    /// <summary>The share along a strip for a pointer at x: clamp((x - 8) / (w - 16), 0, 1).</summary>
    public static double StripShare(double x, double w) => Clamp((x - StripThumb / 2) / (w - StripThumb), 0, 1);

    public static double HueAt(double x, double w) => StripShare(x, w) * HueMax;

    /// <summary>The opacity strip steps by a hundredth: round(u * 100) / 100.</summary>
    public static double AlphaAt(double x, double w) => JsRound(StripShare(x, w) * 100) / 100;

    /// <summary>The thumb's left edge for a share u: u * (w - 16).</summary>
    public static double StripThumbAt(double u, double w) => u * (w - StripThumb);
}
