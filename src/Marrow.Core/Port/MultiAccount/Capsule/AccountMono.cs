// PORT-ONLY: multi-account step 3a. The account's monogram, the page's mono(): "Claude Max 2" is M2, "Claude Pro" is P, "Codex 1" is C1; a name the user gave takes its initials ("Work laptop" is WL, "Team 3" is T3).
using System.Globalization;

namespace Marrow.Core.Port.MultiAccount.Capsule;

public static class AccountMono
{
    public static string Of(string name, bool custom)
    {
        var words = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "";
        var digits = TrailingDigits(name.Trim());
        string initial(string word) => First(word).ToUpperInvariant();
        if (!custom)
        {
            if (digits.Length > 0) return initial(words.Length > 2 ? words[^2] : words[0]) + digits;
            return initial(words.Length > 1 ? words[^1] : words[0]);
        }
        if (digits.Length > 0 && words.Length > 1) return initial(words[0]) + digits;
        return words.Length > 1 ? initial(words[0]) + initial(words[1]) : initial(words[0]);
    }

    private static string TrailingDigits(string text)
    {
        var end = text.Length;
        var start = end;
        while (start > 0 && text[start - 1] is >= '0' and <= '9') start--;
        return text[start..end];
    }

    private static string First(string word)
    {
        var elements = StringInfo.GetTextElementEnumerator(word);
        return elements.MoveNext() ? (string)elements.Current : "";
    }
}
