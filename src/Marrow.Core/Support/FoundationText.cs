namespace Marrow.Core.Support;

// Character sets used by Foundation trimming, from CFUniChar.c. In particular,
// U+200B belongs to whitespace here even though it is Unicode category Cf.
internal static class FoundationText
{
    internal static bool IsWhitespace(char value) => value is
        '\u0009' or '\u0020' or '\u00A0' or '\u1680' or
        >= '\u2000' and <= '\u200B' or '\u202F' or '\u205F' or '\u3000';

    internal static bool IsNewline(char value) => value is
        >= '\u000A' and <= '\u000D' or '\u0085' or '\u2028' or '\u2029';

    internal static ReadOnlySpan<char> Trim(ReadOnlySpan<char> text, bool includeNewlines = false)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && (IsWhitespace(text[start]) || includeNewlines && IsNewline(text[start]))) start++;
        while (end > start && (IsWhitespace(text[end - 1]) || includeNewlines && IsNewline(text[end - 1]))) end--;
        return text[start..end];
    }

    internal static string Trim(string text, bool includeNewlines = false)
    {
        var trimmed = Trim(text.AsSpan(), includeNewlines);
        return trimmed.Length == text.Length ? text : trimmed.ToString();
    }
}
