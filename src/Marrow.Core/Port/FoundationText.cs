namespace Marrow.Core.Port;

// PORT-ONLY. The existing E:\Droppy\port\src\Marrow.Core\Support\FoundationText.cs (T-M2-023), unchanged in
// behaviour, moved to Port/ and made public so Services can port trimmingCharacters too (ED-085). Character
// sets used by Foundation trimming, from CFUniChar.c. In particular, U+200B belongs to whitespace here even
// though it is Unicode category Cf. String.Trim() uses a different set and must not stand in for it.
public static class FoundationText
{
    /// <summary>A member of <c>CharacterSet.whitespaces</c>.</summary>
    public static bool IsWhitespace(char value) => value is
        '\U00000009' or '\U00000020' or '\U000000A0' or '\U00001680' or
        >= '\U00002000' and <= '\U0000200B' or '\U0000202F' or '\U0000205F' or '\U00003000';

    /// <summary>A member of <c>CharacterSet.newlines</c>.</summary>
    public static bool IsNewline(char value) => value is
        >= '\U0000000A' and <= '\U0000000D' or '\U00000085' or '\U00002028' or '\U00002029';

    /// <summary><c>trimmingCharacters(in: .whitespaces)</c>, or <c>.whitespacesAndNewlines</c> with includeNewlines.</summary>
    public static ReadOnlySpan<char> Trim(ReadOnlySpan<char> text, bool includeNewlines = false)
    {
        var start = 0;
        var end = text.Length;
        while (start < end && (IsWhitespace(text[start]) || includeNewlines && IsNewline(text[start]))) start++;
        while (end > start && (IsWhitespace(text[end - 1]) || includeNewlines && IsNewline(text[end - 1]))) end--;
        return text[start..end];
    }

    public static string Trim(string text, bool includeNewlines = false)
    {
        var trimmed = Trim(text.AsSpan(), includeNewlines);
        return trimmed.Length == text.Length ? text : trimmed.ToString();
    }
}
