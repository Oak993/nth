// PORT-ONLY: web surfaces W16c (DESIGN.md 7.2 sanitised SVG; 10 "text at least 4.5:1"; ED-NEW-web-w16c-1). No Swift counterpart.
// Colours of an SVG, read by nth's own reader (hex in three, four, six or eight digits, rgb() and rgba(), hsl() and hsla(), the
// named colours, transparent), and the one adaptation a model's line art gets: a stroke or a text colour that would be lost on
// the surface it is drawn on (black on a dark thread) is moved along its own hue toward the readable end, so line art written for
// a white page still reads in dark and a diagram written in light still reads in light.
using System.Globalization;
using Avalonia.Media;

namespace Marrow.UI.NthUi.Diagram;

public static class NthSvgColor
{
    /// <summary>Reads a CSS colour. False for anything else (a url, a keyword, a function nth does not know).</summary>
    public static bool TryParse(string? text, out Color color)
    {
        color = default;
        if (text is null) return false;
        var value = text.Trim();
        if (value.Length == 0 || value.Length > 64) return false;
        if (value[0] == '#') return TryHex(value.AsSpan(1), out color);
        var open = value.IndexOf('(', StringComparison.Ordinal);
        if (open > 0 && value[^1] == ')')
        {
            var name = value[..open].Trim().ToLowerInvariant();
            var arguments = value[(open + 1)..^1];
            return name switch
            {
                "rgb" or "rgba" => TryRgb(arguments, out color),
                "hsl" or "hsla" => TryHsl(arguments, out color),
                _ => false,
            };
        }
        var lower = value.ToLowerInvariant();
        if (lower == "transparent")
        {
            color = Color.FromArgb(0, 0, 0, 0);
            return true;
        }
        if (lower.Length > 24 || !lower.All(char.IsAsciiLetter)) return false;
        return Color.TryParse(lower, out color);
    }

    private static bool TryHex(ReadOnlySpan<char> digits, out Color color)
    {
        color = default;
        foreach (var c in digits)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }
        int Hex(char c) => Convert.ToInt32(c.ToString(), 16);
        switch (digits.Length)
        {
            case 3:
            case 4:
                color = Color.FromArgb(digits.Length == 4 ? (byte)(Hex(digits[3]) * 17) : (byte)255, (byte)(Hex(digits[0]) * 17), (byte)(Hex(digits[1]) * 17), (byte)(Hex(digits[2]) * 17));
                return true;
            case 6:
            case 8:
                color = Color.FromArgb(digits.Length == 8 ? (byte)(Hex(digits[6]) * 16 + Hex(digits[7])) : (byte)255,
                    (byte)(Hex(digits[0]) * 16 + Hex(digits[1])), (byte)(Hex(digits[2]) * 16 + Hex(digits[3])), (byte)(Hex(digits[4]) * 16 + Hex(digits[5])));
                return true;
            default:
                return false;
        }
    }

    private static List<string> Split(string arguments)
    {
        // "12, 34, 56", "12 34 56", "12 34 56 / 50%", "12,34,56,.5"
        var parts = new List<string>();
        foreach (var piece in arguments.Replace('/', ' ').Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) parts.Add(piece);
        return parts;
    }

    private static bool TryRgb(string arguments, out Color color)
    {
        color = default;
        var parts = Split(arguments);
        if (parts.Count is not (3 or 4)) return false;
        var channels = new byte[3];
        for (var i = 0; i < 3; i++)
        {
            if (!TryNumber(parts[i], out var number, out var percent)) return false;
            var scaled = percent ? number / 100 * 255 : number;
            channels[i] = (byte)Math.Clamp(Math.Round(scaled, MidpointRounding.AwayFromZero), 0, 255);
        }
        var alpha = 1.0;
        if (parts.Count == 4 && !TryAlpha(parts[3], out alpha)) return false;
        color = Color.FromArgb((byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero), channels[0], channels[1], channels[2]);
        return true;
    }

    private static bool TryHsl(string arguments, out Color color)
    {
        color = default;
        var parts = Split(arguments);
        if (parts.Count is not (3 or 4)) return false;
        var hueText = parts[0].EndsWith("deg", StringComparison.Ordinal) ? parts[0][..^3] : parts[0];
        if (!TryNumber(hueText, out var hue, out _) || !TryNumber(parts[1], out var saturation, out _) || !TryNumber(parts[2], out var lightness, out _)) return false;
        var alpha = 1.0;
        if (parts.Count == 4 && !TryAlpha(parts[3], out alpha)) return false;
        var (r, g, b) = FromHsl(((hue % 360) + 360) % 360, Math.Clamp(saturation / 100, 0, 1), Math.Clamp(lightness / 100, 0, 1));
        color = Color.FromArgb((byte)Math.Round(alpha * 255, MidpointRounding.AwayFromZero), r, g, b);
        return true;
    }

    private static bool TryNumber(string text, out double value, out bool percent)
    {
        percent = text.EndsWith('%');
        var digits = percent ? text[..^1] : text;
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    private static bool TryAlpha(string text, out double alpha)
    {
        alpha = 1;
        if (!TryNumber(text, out var value, out var percent)) return false;
        alpha = Math.Clamp(percent ? value / 100 : value, 0, 1);
        return true;
    }

    /// <summary>The colour as markup text: #rrggbb, or rgba() when it is see-through.</summary>
    public static string ToText(Color color) => color.A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}")
        : string.Create(CultureInfo.InvariantCulture, $"rgba({color.R},{color.G},{color.B},{color.A / 255.0:0.###})");

    // ---- adaptation ----------------------------------------------------------------------------------------------------

    /// <summary>Hue (0 to 360), saturation and lightness (0 to 1) of an opaque colour.</summary>
    public static (double Hue, double Saturation, double Lightness) ToHsl(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max - min < 1e-9) return (0, 0, lightness);
        var delta = max - min;
        var saturation = lightness > .5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r) hue = ((g - b) / delta + (g < b ? 6 : 0)) * 60;
        else if (max == g) hue = ((b - r) / delta + 2) * 60;
        else hue = ((r - g) / delta + 4) * 60;
        return (hue, saturation, lightness);
    }

    public static (byte R, byte G, byte B) FromHsl(double hue, double saturation, double lightness)
    {
        if (saturation <= 0)
        {
            var grey = (byte)Math.Round(lightness * 255);
            return (grey, grey, grey);
        }
        var q = lightness < .5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        static double Channel(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < .5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        var h = hue / 360;
        return ((byte)Math.Round(Channel(p, q, h + 1.0 / 3) * 255), (byte)Math.Round(Channel(p, q, h) * 255), (byte)Math.Round(Channel(p, q, h - 1.0 / 3) * 255));
    }

    /// <summary><paramref name="top"/> over an opaque <paramref name="bottom"/>.</summary>
    public static Color Over(Color top, Color bottom) => NthUiTokens.Over(top, bottom);

    /// <summary>
    /// The colour to draw <paramref name="color"/> with on <paramref name="backdrop"/> so it keeps at least
    /// <paramref name="minimum"/>:1 against it. A colour that already does is returned as it is. Otherwise the lightness is
    /// moved, hue and saturation kept, toward the end that contrasts with the backdrop, by the least that reaches the
    /// minimum; when no lightness of that hue does, black or white.
    /// </summary>
    public static Color Adapt(Color color, Color backdrop, double minimum)
    {
        if (color.A == 0) return color;
        var seen = Over(color, backdrop);
        if (NthUiTokens.Contrast(seen, backdrop) >= minimum) return color;
        var opaque = Color.FromRgb(color.R, color.G, color.B);
        var (hue, saturation, lightness) = ToHsl(opaque);
        var toLight = NthUiTokens.Luminance(backdrop) < .18;
        double low = toLight ? lightness : 0, high = toLight ? 1 : lightness;
        Color Make(double l)
        {
            var (r, g, b) = FromHsl(hue, saturation, l);
            return Color.FromArgb(color.A, r, g, b);
        }
        var far = Make(toLight ? 1 : 0);
        if (NthUiTokens.Contrast(Over(far, backdrop), backdrop) < minimum)
        {
            // Not reachable along this hue (a mid-grey backdrop): the stronger of black and white.
            var white = Color.FromArgb(color.A, 255, 255, 255);
            var black = Color.FromArgb(color.A, 0, 0, 0);
            return NthUiTokens.Contrast(Over(white, backdrop), backdrop) >= NthUiTokens.Contrast(Over(black, backdrop), backdrop) ? white : black;
        }
        for (var i = 0; i < 18; i++)
        {
            var middle = (low + high) / 2;
            var reaches = NthUiTokens.Contrast(Over(Make(middle), backdrop), backdrop) >= minimum;
            if (toLight == reaches) high = middle;
            else low = middle;
        }
        return Make(toLight ? high : low);
    }
}
