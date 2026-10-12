using System.Globalization;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-1: a frozen copy of SwiftString's Character-walking code as it was before the ASCII fast paths (claude/release-integration
// 1fdf47b43), kept only as the oracle SwiftStringOracleTests compares the shipped code with. Do not change it: it is the reference.
internal static class SwiftStringFrozen
{
    public static IEnumerable<string> Characters(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(index));
            yield return text.Substring(index, length);
            index += length;
        }
    }

    public static int Count(string text) => text.Length == 0 ? 0 : new StringInfo(text).LengthInTextElements;

    public static string Suffix(string text, int maxLength)
    {
        RequireNonNegative(maxLength, "Can't take a suffix of negative length from a collection");
        var starts = Starts(text);
        return maxLength >= starts.Count ? text : text[starts[starts.Count - maxLength]..];
    }

    public static string DropLast(string text, int k = 1)
    {
        RequireNonNegative(k, "Can't drop a negative number of elements from a collection");
        var starts = Starts(text);
        return k >= starts.Count ? "" : text[..starts[starts.Count - k]];
    }

    public static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        string.Equals(Nfc(left), Nfc(right), StringComparison.Ordinal);

    public static int Compare(string left, string right)
    {
        if (ReferenceEquals(left, right)) return 0;
        var x = Nfc(left).EnumerateRunes();
        var y = Nfc(right).EnumerateRunes();
        while (true)
        {
            var hasX = x.MoveNext();
            var hasY = y.MoveNext();
            if (!hasX) return hasY ? -1 : 0;
            if (!hasY) return 1;
            var order = x.Current.Value.CompareTo(y.Current.Value);
            if (order != 0) return order;
        }
    }

    public static bool HasPrefix(string text, string prefix)
    {
        if (prefix.Length == 0) return true;
        if (Ascii.IsValid(text) && Ascii.IsValid(prefix))
            return text.StartsWith(prefix, StringComparison.Ordinal) &&
                   (prefix.Length == text.Length || !(text[prefix.Length - 1] == '\r' && text[prefix.Length] == '\n'));
        using var whole = Characters(text).GetEnumerator();
        foreach (var character in Characters(prefix))
            if (!whole.MoveNext() || !Equal(whole.Current, character)) return false;
        return true;
    }

    public static bool HasSuffix(string text, string suffix)
    {
        if (suffix.Length == 0) return true;
        if (Ascii.IsValid(text) && Ascii.IsValid(suffix))
        {
            var start = text.Length - suffix.Length;
            return text.EndsWith(suffix, StringComparison.Ordinal) &&
                   (start == 0 || !(text[start - 1] == '\r' && text[start] == '\n'));
        }
        var whole = Characters(text).ToArray();
        var tail = Characters(suffix).ToArray();
        if (tail.Length > whole.Length) return false;
        for (var i = 1; i <= tail.Length; i++)
            if (!Equal(whole[^i], tail[^i])) return false;
        return true;
    }

    public static bool Contains(string text, string needle)
    {
        if (needle.Length == 0) return true;
        var haystack = Characters(text).ToArray();
        var pattern = Characters(needle).ToArray();
        for (var start = 0; start <= haystack.Length - pattern.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < pattern.Length; i++)
            {
                if (!Equal(haystack[start + i], pattern[i])) { matched = false; break; }
            }
            if (matched) return true;
        }
        return false;
    }

    public static IReadOnlyList<string> Split(string text, string separator, int maxSplits = int.MaxValue,
        bool omittingEmptySubsequences = true)
    {
        if (Count(separator) != 1) throw new ArgumentException("A Swift Character separator is exactly one Character.", nameof(separator));
        RequireNonNegative(maxSplits, "Must take zero or more splits");
        var result = new List<string>();
        var asciiSeparator = separator.Length == 1 && separator[0] < 0x80;
        var subsequenceStart = 0;
        bool Append(int end)
        {
            if (subsequenceStart == end && omittingEmptySubsequences) return false;
            result.Add(text[subsequenceStart..end]);
            return true;
        }
        if (maxSplits == 0 || text.Length == 0)
        {
            Append(text.Length);
            return result;
        }
        var subsequenceEnd = 0;
        while (subsequenceEnd != text.Length)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(subsequenceEnd));
            var unit = text[subsequenceEnd];
            var isSeparator = asciiSeparator && length == 1 && unit < 0x80
                ? unit == separator[0]
                : Equal(text.Substring(subsequenceEnd, length), separator);
            if (isSeparator)
            {
                var didAppend = Append(subsequenceEnd);
                subsequenceEnd += length;
                subsequenceStart = subsequenceEnd;
                if (didAppend && result.Count == maxSplits) break;
                continue;
            }
            subsequenceEnd += length;
        }
        if (subsequenceStart != text.Length || !omittingEmptySubsequences)
            result.Add(text[subsequenceStart..]);
        return result;
    }

    // SwiftStringComparer as it was: canonical equality, a hash of the NFC form, scalar order.
    public static bool ComparerEquals(string? x, string? y) => x is null ? y is null : y is not null && Equal(x, y);

    public static int ComparerHash(string obj) => StringComparer.Ordinal.GetHashCode(Nfc(obj));

    public static int ComparerCompare(string? x, string? y) => x is null ? (y is null ? 0 : -1) : y is null ? 1 : Compare(x, y);

    public static string Nfc(string text) => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    private static List<int> Starts(string text)
    {
        var starts = new List<int>();
        var index = 0;
        while (index < text.Length)
        {
            starts.Add(index);
            index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
        }
        return starts;
    }

    private static void RequireNonNegative(int value, string trap)
    {
        if (value < 0) throw new SwiftTrapException(trap);
    }
}
