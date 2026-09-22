using System.Globalization;
using System.Numerics;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Swift numeric printing, parsing, rounding and conversion (ED-073). Every number that becomes
/// text or comes from text goes through here or through CultureInfo.InvariantCulture, never through a .NET
/// default. Source: draft A (proto-A Support/SwiftNumber.cs) and draft C (the exact Fixed).
/// </summary>
public static class SwiftNumber
{
    /// <summary>
    /// SwiftDtoa switches to exponential form above 2^54 (<c>fabs(d) &gt; 0x1.0p54</c>) and when the decimal
    /// exponent is below -3. The upper threshold is UNVERIFIED (O-03): draft A used 2^53, draft B 1e16. It is
    /// one constant so the oracle's answer is a one-line change.
    /// </summary>
    public const double ExponentialAbove = 18014398509481984d; // 2^54

    /// <summary>
    /// <c>String(double)</c>, <c>"\(double)"</c>, <c>double.description</c>: the shortest round-trip digits;
    /// "1.0", "0.0001", "1e-05", "-0.0", "inf", "nan".
    /// </summary>
    public static string Description(double value)
    {
        // .NET's default NaN has its sign bit set on x64; Swift prints .nan as "nan".
        if (double.IsNaN(value)) return "nan";
        if (double.IsInfinity(value)) return value > 0 ? "inf" : "-inf";
        var sign = double.IsNegative(value) ? "-" : "";
        if (value == 0) return sign + "0.0";
        var (digits, exponent) = ShortestDigits(value);
        if (exponent < -3 || Math.Abs(value) > ExponentialAbove)
        {
            var mantissa = digits.Length == 1 ? digits : digits[..1] + "." + digits[1..];
            var power = exponent - 1;
            return sign + mantissa + "e" + (power < 0 ? "-" : "+") + Math.Abs(power).ToString("00", CultureInfo.InvariantCulture);
        }
        if (exponent <= 0) return sign + "0." + new string('0', -exponent) + digits;
        if (exponent < digits.Length) return sign + digits[..exponent] + "." + digits[exponent..];
        return sign + digits + new string('0', exponent - digits.Length) + ".0";
    }

    /// <summary>
    /// A Double inside JSONEncoder output: <c>description</c> with a trailing ".0" removed ("1", "1e-05").
    /// A non-finite value is an EncodingError under the default strategy.
    /// </summary>
    public static string JsonText(double value)
    {
        if (!double.IsFinite(value)) throw new EncodingError($"Unable to encode Double.{Description(value)} directly in JSON.");
        var text = Description(value);
        return text.EndsWith(".0", StringComparison.Ordinal) ? text[..^2] : text;
    }

    /// <summary>Digits (no leading or trailing zeros) and E with value = 0.digits x 10^E. .NET "R" is shortest round-trip.</summary>
    internal static (string Digits, int Exponent) ShortestDigits(double value)
    {
        var text = Math.Abs(value).ToString("R", CultureInfo.InvariantCulture);
        var exponent = 0;
        var e = text.IndexOf('E', StringComparison.Ordinal);
        if (e >= 0)
        {
            exponent = int.Parse(text[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            text = text[..e];
        }
        var point = text.IndexOf('.', StringComparison.Ordinal);
        var integerDigits = point < 0 ? text.Length : point;
        var all = point < 0 ? text : text.Remove(point, 1);
        var leading = 0;
        while (leading < all.Length - 1 && all[leading] == '0') leading++;
        var digits = all[leading..].TrimEnd('0');
        return (digits, integerDigits - leading + exponent);
    }

    /// <summary><c>x.rounded()</c>: to nearest, ties AWAY from zero. Math.Round(x) alone is ties to even.</summary>
    public static double Rounded(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary><c>Int(x)</c>: truncates toward zero; Swift TRAPS on NaN, infinity or out of range.</summary>
    public static long TruncatingToInt(double value)
    {
        if (!double.IsFinite(value) || value < -9223372036854775808d || value >= 9223372036854775808d)
            throw new SwiftTrapException("Double value cannot be converted to Int because it is either infinite, NaN or outside the representable range.");
        return (long)Math.Truncate(value);
    }

    /// <summary><c>Int(text)</c>: one optional leading + or -, then ASCII digits, nothing else; nil on overflow.</summary>
    public static long? ParseInt(string text)
    {
        if (text.Length == 0) return null;
        var start = text[0] is '+' or '-' ? 1 : 0;
        if (start == text.Length) return null;
        for (var i = start; i < text.Length; i++) if (text[i] is < '0' or > '9') return null;
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    /// <summary>
    /// <c>Double(text)</c>: decimal with optional exponent, "inf"/"infinity"/"nan" in any case, and hexadecimal
    /// floats; no surrounding whitespace, no grouping, no culture.
    /// </summary>
    public static double? ParseDouble(string text)
    {
        if (text.Length == 0) return null;
        var sign = 1.0;
        var body = text;
        if (body[0] is '+' or '-') { sign = body[0] == '-' ? -1 : 1; body = body[1..]; }
        if (body.Length == 0) return null;
        if (body.Equals("inf", StringComparison.OrdinalIgnoreCase) || body.Equals("infinity", StringComparison.OrdinalIgnoreCase))
            return sign * double.PositiveInfinity;
        if (body.Equals("nan", StringComparison.OrdinalIgnoreCase)) return double.NaN;
        if (body.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return ParseHex(body[2..]) is { } hex ? sign * hex : null;
        var sawDigit = false;
        var i = 0;
        while (i < body.Length && char.IsAsciiDigit(body[i])) { i++; sawDigit = true; }
        if (i < body.Length && body[i] == '.') { i++; while (i < body.Length && char.IsAsciiDigit(body[i])) { i++; sawDigit = true; } }
        if (!sawDigit) return null;
        if (i < body.Length && body[i] is 'e' or 'E')
        {
            i++;
            if (i < body.Length && body[i] is '+' or '-') i++;
            var exponentDigits = 0;
            while (i < body.Length && char.IsAsciiDigit(body[i])) { i++; exponentDigits++; }
            if (exponentDigits == 0) return null;
        }
        if (i != body.Length) return null;
        return sign * double.Parse(body, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);
    }

    private static double? ParseHex(string body)
    {
        BigInteger mantissa = 0;
        var binaryExponent = 0;
        var sawDigit = false;
        var i = 0;
        for (; i < body.Length && char.IsAsciiHexDigit(body[i]); i++) { mantissa = mantissa * 16 + HexValue(body[i]); sawDigit = true; }
        if (i < body.Length && body[i] == '.')
            for (i++; i < body.Length && char.IsAsciiHexDigit(body[i]); i++) { mantissa = mantissa * 16 + HexValue(body[i]); binaryExponent -= 4; sawDigit = true; }
        if (!sawDigit) return null;
        if (i < body.Length && body[i] is 'p' or 'P')
        {
            var exponent = ParseInt(body[(i + 1)..]);
            if (exponent is null) return null;
            binaryExponent += (int)Math.Clamp(exponent.Value, -100_000, 100_000);
            i = body.Length;
        }
        if (i != body.Length) return null;
        // Exact for mantissas of 53 bits or fewer; longer ones round once here (UNVERIFIED tie cases).
        return Math.ScaleB((double)mantissa, binaryExponent);
    }

    private static int HexValue(char c) => c <= '9' ? c - '0' : (c | 0x20) - 'a' + 10;

    /// <summary>
    /// <c>String(format: "%.Nf", x)</c> in the POSIX locale: the exact binary value rounded to N digits, ties to
    /// even, "." always, and printf's "nan"/"inf". (.NET 10's "F" agrees for finite values, draft B measured
    /// 188,048 comparisons; the helper stays the single entry point for the non-finite spellings.)
    /// </summary>
    public static string Fixed(double value, int digits)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(digits);
        if (!double.IsFinite(value)) return double.IsNaN(value) ? "nan" : value > 0 ? "inf" : "-inf";
        var bits = BitConverter.DoubleToInt64Bits(value);
        var negative = bits < 0;
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0) exponent++; else mantissa |= 1L << 52;
        exponent -= 1075; // value = mantissa * 2^exponent exactly
        BigInteger scaled;
        var numerator = new BigInteger(mantissa) * BigInteger.Pow(10, digits);
        if (exponent >= 0) scaled = numerator << exponent;
        else
        {
            var denominator = BigInteger.One << -exponent;
            scaled = BigInteger.DivRem(numerator, denominator, out var remainder);
            var twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !scaled.IsEven)) scaled += 1;
        }
        var text = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
        var result = digits == 0 ? text : text[..^digits] + "." + text[^digits..];
        return negative ? "-" + result : result;
    }
}

/// <summary>Foundation formatters the source uses with a FIXED locale, where .NET has no one-call equivalent.</summary>
public static class SwiftFormat
{
    /// <summary>
    /// <c>NumberFormatter</c> with <c>.decimal</c> style and <c>maximumFractionDigits = 0</c> (ZaiAPI.swift:213-218):
    /// the locale's grouping and HALF-EVEN rounding (the NumberFormatter default mode). User-locale formatting
    /// (<c>Date.formatted</c>, relative dates, <c>.spellOut</c>) is not here: it goes through ICU (R-FMT-2).
    /// </summary>
    public static string DecimalNoFraction(double value, CultureInfo locale) =>
        Math.Round(value, MidpointRounding.ToEven).ToString("N0", locale);
}

/// <summary>Foundation <c>Scanner</c> as the source uses it.</summary>
public static class SwiftScanner
{
    /// <summary>
    /// <c>Scanner(string:).scanHexInt64(&amp;value)</c> (AppTheme.swift:242): skips leading whitespace and newlines,
    /// accepts an optional 0x/0X, then reads hex digits up to the first non-hex character. No digit: false, and
    /// the value stays 0 (so <c>Color(hex: "zz")</c> is black). Overflow saturates (UNVERIFIED O-16).
    /// </summary>
    public static bool ScanHexInt64(string text, out ulong value)
    {
        value = 0;
        var i = 0;
        while (i < text.Length && (FoundationText.IsWhitespace(text[i]) || FoundationText.IsNewline(text[i]))) i++;
        if (i + 1 < text.Length && text[i] == '0' && text[i + 1] is 'x' or 'X') i += 2;
        var start = i;
        ulong result = 0;
        var overflow = false;
        for (; i < text.Length && char.IsAsciiHexDigit(text[i]); i++)
        {
            var digit = (ulong)(text[i] <= '9' ? text[i] - '0' : (text[i] | 0x20) - 'a' + 10);
            if (result > (ulong.MaxValue - digit) / 16) overflow = true;
            else result = result * 16 + digit;
        }
        if (i == start) return false;
        value = overflow ? ulong.MaxValue : result;
        return true;
    }
}

/// <summary>String and Data conversions whose failure behaviour differs between Swift and .NET.</summary>
public static class SwiftText
{
    private static readonly System.Text.UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary><c>String(data: d, encoding: .utf8)</c>: nil on invalid UTF-8. A BOM is kept (UNVERIFIED O-15).</summary>
    public static string? DecodeUtf8OrNil(ReadOnlySpan<byte> data)
    {
        try { return Strict.GetString(data); }
        catch (System.Text.DecoderFallbackException) { return null; }
    }

    /// <summary><c>String(decoding: d, as: UTF8.self)</c>: never fails; invalid sequences become U+FFFD.</summary>
    public static string DecodeUtf8Repairing(ReadOnlySpan<byte> data) => System.Text.Encoding.UTF8.GetString(data);
}
