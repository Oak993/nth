// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.1): the small helpers of voice-glow.js's stylesheet
// recipe (`wa`, `qa`, `ja`, `lt`, `S`, `toFixed`) with the names of the design in the comments. voice-glow 0.2.1 (Jakub Antalik, MIT; THIRD_PARTY_NOTICES).
using System.Globalization;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>An opaque colour as the package's palettes write it (<c>rgb(r, g, b)</c>).</summary>
internal readonly record struct GlowRgb(byte R, byte G, byte B);

/// <summary>The recipe's number formatting and colour parsing, as the JavaScript does them.</summary>
internal static class VoiceGlowCss
{
    /// <summary>JavaScript <c>Math.round</c>: the nearest integer, halves toward +infinity.</summary>
    public static double Round(double x)
    {
        var floor = Math.Floor(x);
        return x - floor >= 0.5 ? floor + 1 : floor;
    }

    /// <summary><c>S($)</c>: a length to 0.1.</summary>
    public static double Tenth(double x) => Round(x * 10) / 10;

    /// <summary><c>lt(n, e)</c>: <c>max(0.5, round(n * e * 100) / 100)</c>.</summary>
    public static double Lt(double n, double e = 1) => Math.Max(0.5, Round(n * e * 100) / 100);

    /// <summary>
    /// <c>x.toFixed(digits)</c> read back as a number (the stylesheet carries the rounded text). JavaScript rounds the exact value of the double, halves away from zero (.NET's
    /// own "F" format rounds an exact half to even: 0.0625 gives 0.062 where JavaScript gives 0.063), so this rounds the exact decimal expansion itself.
    /// </summary>
    public static double Fixed(double x, int digits)
    {
        if (double.IsNaN(x) || double.IsInfinity(x)) return x;
        var negative = x < 0;
        // 30 places show the exact expansion of every double of the sizes the recipe uses; a tie is an expansion that ends in a 5 right after the kept places
        var text = Math.Abs(x).ToString("F30", CultureInfo.InvariantCulture);
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var kept = text[..(point + 1 + digits)].Replace(".", "", StringComparison.Ordinal).ToCharArray();
        if (text[point + 1 + digits] >= '5')
        {
            var i = kept.Length - 1;
            while (i >= 0 && kept[i] == '9') kept[i--] = '0';
            if (i >= 0) kept[i]++;
            else kept = ['1', .. kept];
        }
        var whole = new string(kept);
        var number = double.Parse(whole[..^digits] + "." + whole[^digits..], CultureInfo.InvariantCulture);
        return negative ? -number : number;
    }

    /// <summary><c>wa(n)</c> then <c>qa(n)</c>: a hex or <c>rgb()</c> text as a colour, or null.</summary>
    public static GlowRgb? Parse(string? text)
    {
        if (text is null) return null;
        var e = text.Trim();
        if (e.Length > 0 && e[0] == '#' && (e.Length == 4 || e.Length == 7) && e.Skip(1).All(Uri.IsHexDigit))
        {
            var digits = e.Length == 4 ? string.Concat(e.Skip(1).Select(c => new string(c, 2))) : e[1..];
            return new GlowRgb(Hex(digits, 0), Hex(digits, 2), Hex(digits, 4));
        }

        var m = System.Text.RegularExpressions.Regex.Match(e, @"^rgba?\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!m.Success || !Channel(m.Groups[1].Value, out var r) || !Channel(m.Groups[2].Value, out var g) || !Channel(m.Groups[3].Value, out var b)) return null;
        return new GlowRgb(r, g, b);
    }

    private static byte Hex(string digits, int at) => byte.Parse(digits.AsSpan(at, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    // Math.round of the number, kept to a byte (the package's rgb() texts are 0 to 255)
    private static bool Channel(string text, out byte value)
    {
        value = 0;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n)) return false;
        value = (byte)Math.Clamp(Round(n), 0, 255);
        return true;
    }
}
