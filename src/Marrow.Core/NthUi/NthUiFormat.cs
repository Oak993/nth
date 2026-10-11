using System.Globalization;
using System.Text;

// PORT-ONLY: web surfaces W15 (DESIGN.md 7.2 `fmt(value, pattern)`; ED-NEW-web-w15-1). No Swift counterpart. The
// patterns follow the spreadsheet habit models already know: "$0" and "$0.00" for money, "0%" and "0.0%" for shares
// (the value is multiplied by 100), "#,##0" for grouped counts, "0.0a" for compact numbers (1.2k, 3.4M), and date
// patterns such as "MMM d, yyyy" for ISO dates. A currency sign or a comma turns thousands grouping on, so the
// approved canvas's "$0" reads "$1,007" while a plain "0" leaves a year as 2026.

namespace Marrow.Core.NthUi;

public static class NthUiFormat
{
    /// <summary>The longest pattern read; anything longer formats as plain display.</summary>
    public const int MaxPatternLength = 64;

    private const int MaxDecimals = 10;
    private const int MaxIntegerDigits = 20;

    /// <summary>Formats <paramref name="value"/> with <paramref name="pattern"/>. Never throws; errors show as a dash.</summary>
    public static string Format(NthUiValue value, string? pattern, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        if (value.IsError) return "\u2014";
        if (string.IsNullOrEmpty(pattern) || pattern.Length > MaxPatternLength) return value.Display(culture);
        if (value.IsText)
        {
            var text = value.AsText.Trim();
            if (LooksLikeDate(text, out var date) && HasDateTokens(pattern)) return FormatDate(date, pattern, culture);
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
                value = NthUiValue.Of(parsed);
        }
        if (!value.IsNumber) return value.Display(culture);
        return FormatNumber(value.AsNumber, pattern, culture);
    }

    /// <summary>A number with a pattern: prefix, a run of 0 # , . (the number), then a suffix. \ makes the next character literal.</summary>
    public static string FormatNumber(double number, string pattern, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.InvariantCulture;
        if (!double.IsFinite(number)) return "\u2014";
        if (!Split(pattern, out var prefix, out var spec, out var suffix, out var currency, out var scale))
            return NthUiValue.FormatNumber(number, culture);
        var compact = suffix.Length > 0 && suffix[0] == 'a' && (suffix.Length == 1 || !char.IsAsciiLetter(suffix[1]));
        if (compact) suffix = suffix[1..];
        var point = spec.IndexOf('.', StringComparison.Ordinal);
        var integerPart = point < 0 ? spec : spec[..point];
        var fraction = point < 0 ? "" : spec[(point + 1)..];
        var minDecimals = Math.Min(MaxDecimals, fraction.Count(c => c == '0'));
        var maxDecimals = Math.Min(MaxDecimals, fraction.Count(c => c is '0' or '#'));
        if (maxDecimals < minDecimals) maxDecimals = minDecimals;
        var minInteger = Math.Clamp(integerPart.Count(c => c == '0'), 1, MaxIntegerDigits);
        var grouping = integerPart.Contains(',', StringComparison.Ordinal) || currency;

        var value = number * scale;
        var unit = "";
        if (compact)
        {
            var magnitude = Math.Abs(value);
            (double div, string letter) = magnitude >= 1e12 ? (1e12, "T") : magnitude >= 1e9 ? (1e9, "B") : magnitude >= 1e6 ? (1e6, "M") : magnitude >= 1e3 ? (1e3, "k") : (1, "");
            value /= div;
            unit = letter;
            if (Math.Abs(Math.Round(value, maxDecimals, MidpointRounding.AwayFromZero)) >= 1000 && letter != "T")
            {
                (value, unit) = letter switch { "" => (value / 1e3, "k"), "k" => (value / 1e3, "M"), "M" => (value / 1e3, "B"), _ => (value / 1e3, "T") };
            }
        }
        var rounded = Math.Round(Math.Abs(value), maxDecimals, MidpointRounding.AwayFromZero);
        var negative = value < 0 && rounded != 0;
        var format = new StringBuilder();
        if (grouping) format.Append("#,");
        format.Append('0', minInteger);
        if (maxDecimals > 0)
        {
            format.Append('.');
            format.Append('0', minDecimals);
            format.Append('#', maxDecimals - minDecimals);
        }
        var digits = rounded.ToString(format.ToString(), culture);
        var sign = negative ? culture.NumberFormat.NegativeSign : "";
        return sign + prefix + digits + unit + suffix;
    }

    private static bool Split(string pattern, out string prefix, out string spec, out string suffix, out bool currency, out double scale)
    {
        var before = new StringBuilder();
        var after = new StringBuilder();
        var number = new StringBuilder();
        currency = false;
        scale = 1;
        var state = 0; // 0 prefix, 1 the number, 2 suffix
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            var next = i + 1 < pattern.Length ? pattern[i + 1] : '\0';
            if (c == '\\' && i + 1 < pattern.Length)
            {
                if (state == 1) state = 2;
                (state == 0 ? before : after).Append(next);
                i++;
                continue;
            }
            if (state == 0 && (c is '0' or '#' || (c is ',' or '.' && next is '0' or '#')))
            {
                state = 1;
                number.Append(c);
                continue;
            }
            if (state == 1)
            {
                if (c is '0' or '#' || (c is ',' or '.' && next is '0' or '#' or ',' or '.'))
                {
                    number.Append(c);
                    continue;
                }
                state = 2;
            }
            if (c == '%') scale *= 100;
            else if (c == '\u2030') scale *= 1000;
            if (char.GetUnicodeCategory(c) == UnicodeCategory.CurrencySymbol) currency = true;
            (state == 0 ? before : after).Append(c);
        }
        prefix = before.ToString();
        spec = number.ToString();
        suffix = after.ToString();
        return state > 0;
    }

    private static bool LooksLikeDate(string text, out DateTime date)
    {
        date = default;
        if (text.Length < 10 || !char.IsAsciiDigit(text[0]) || text[4] != '-' || text[7] != '-') return false;
        string[] formats = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ", "yyyy-MM-dd HH:mm"];
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out date);
    }

    private static bool HasDateTokens(string pattern)
    {
        foreach (var c in pattern)
        {
            if (c is '0' or '#') return false;
        }
        return pattern.Contains('y', StringComparison.Ordinal) || pattern.Contains('M', StringComparison.Ordinal)
            || pattern.Contains('d', StringComparison.Ordinal) || pattern.Contains('H', StringComparison.Ordinal);
    }

    private static string FormatDate(DateTime date, string pattern, CultureInfo culture)
    {
        var safe = new StringBuilder();
        foreach (var c in pattern)
        {
            // Only the date and time letters are format tokens; every other character is literal text.
            if ("yMdHhmst".Contains(c, StringComparison.Ordinal) || c == ' ') safe.Append(c);
            else if (c is '\'' or '"' or '\\' or '%') continue;
            else safe.Append('\\').Append(c);
        }
        try
        {
            return date.ToString(safe.ToString(), culture);
        }
        catch (FormatException)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
