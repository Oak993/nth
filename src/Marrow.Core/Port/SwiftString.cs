using System.Globalization;
using System.Text;

namespace Marrow.Core.Port;

/// <summary>
/// PORT-ONLY. Swift <c>String</c> semantics over .NET UTF-16 strings (ED-073). Swift counts, slices and compares
/// Characters (extended grapheme clusters); <c>==</c>, <c>&lt;</c>, <c>hasPrefix</c>, <c>hasSuffix</c> and hashing
/// follow Unicode canonical equivalence. The stored text is never normalized: only comparisons see the NFC
/// form, exactly as in Swift. Source: draft A (proto-A Support/SwiftString.cs), with draft B's rule that a
/// prefix or suffix ends on a Character boundary also on the ASCII path ("\r\n" is one Character) and draft
/// B/C's newline splitter.
/// </summary>
public static class SwiftString
{
    // ---- Characters (grapheme clusters) ----

    /// <summary><c>for character in text</c>.</summary>
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

    /// <summary><c>text.count</c>: Characters, not UTF-16 units.</summary>
    public static int Count(string text) => text.Length == 0 ? 0 : new StringInfo(text).LengthInTextElements;

    /// <summary><c>text.utf8.count</c>.</summary>
    public static int Utf8Count(string text) => Encoding.UTF8.GetByteCount(text);

    /// <summary><c>String(text.prefix(n))</c>. Swift traps on a negative length.</summary>
    public static string Prefix(string text, int maxLength)
    {
        RequireNonNegative(maxLength, "Can't take a prefix of negative length from a collection");
        var end = OffsetAfter(text, maxLength);
        return end == text.Length ? text : text[..end];
    }

    /// <summary><c>String(text.dropFirst(k))</c>.</summary>
    public static string DropFirst(string text, int k = 1)
    {
        RequireNonNegative(k, "Can't drop a negative number of elements from a collection");
        return text[OffsetAfter(text, k)..];
    }

    /// <summary><c>String(text.suffix(n))</c>.</summary>
    public static string Suffix(string text, int maxLength)
    {
        RequireNonNegative(maxLength, "Can't take a suffix of negative length from a collection");
        var starts = Starts(text);
        return maxLength >= starts.Count ? text : text[starts[starts.Count - maxLength]..];
    }

    /// <summary><c>String(text.dropLast(k))</c>.</summary>
    public static string DropLast(string text, int k = 1)
    {
        RequireNonNegative(k, "Can't drop a negative number of elements from a collection");
        var starts = Starts(text);
        return k >= starts.Count ? "" : text[..starts[starts.Count - k]];
    }

    /// <summary>True when a UTF-16 offset falls between two Characters (for regex post-checks, R-RE-2).</summary>
    public static bool IsCharacterBoundary(string text, int offset)
    {
        if (offset == 0 || offset == text.Length) return true;
        var index = 0;
        while (index < offset) index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
        return index == offset;
    }

    // ---- Equality, ordering, prefix and suffix ----

    /// <summary>Swift <c>==</c>: canonical equivalence (<c>"\u{E9}" == "e\u{301}"</c>).</summary>
    public static bool Equal(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal) ||
        string.Equals(Nfc(left), Nfc(right), StringComparison.Ordinal);

    /// <summary>Swift <c>&lt;</c> as a comparison: Unicode scalar order of the NFC forms (not UTF-16 order).</summary>
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

    /// <summary>
    /// Swift <c>hasPrefix</c>: <c>starts(with:)</c> over Characters, each compared canonically, so the prefix
    /// must end on a Character boundary. On two ASCII strings that is a byte compare plus the one ASCII
    /// multi-scalar Character, "\r\n" (UNVERIFIED O-06 against Swift's NFC fast path).
    /// </summary>
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

    /// <summary>Swift <c>hasSuffix</c>: <c>reversed().starts(with: suffix.reversed())</c> over Characters.</summary>
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

    /// <summary>
    /// Swift's plain (non-localized) <c>text.contains(needle)</c>: true when some contiguous run of the
    /// receiver's Characters matches the needle's, each compared canonically (R-STR-2), the same rule
    /// <see cref="HasPrefix"/>/<see cref="HasSuffix"/> already follow. An ordinal <c>string.Contains</c> is only
    /// safe when both operands are the port's own raw values (R-STR-2); this is for haystack/needle pairs that
    /// can be outside text on either side (typed search text against a thread title or project name, R-STR-2's
    /// own "even an ASCII literal is not safe" case), where an NFC/NFD difference must still match.
    /// </summary>
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

    // ---- Splitting ----

    /// <summary>
    /// <c>text.split(separator: ch, maxSplits:, omittingEmptySubsequences:)</c>, transcribed from the stdlib's
    /// Collection.split. The separator is ONE Character, so "\r\n" is never split by "\n", and empty pieces
    /// are dropped by default.
    /// </summary>
    public static IReadOnlyList<string> Split(string text, string separator, int maxSplits = int.MaxValue,
        bool omittingEmptySubsequences = true)
    {
        if (Count(separator) != 1) throw new ArgumentException("A Swift Character separator is exactly one Character.", nameof(separator));
        RequireNonNegative(maxSplits, "Must take zero or more splits");
        var result = new List<string>();
        // ED-NEW-stutterfix-7: the Characters are walked in place (their boundaries), not each made a string. A one-unit ASCII Character
        // equals a one-unit ASCII separator only when it is that unit (NFC leaves ASCII alone); every other Character is compared as before.
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

    /// <summary>
    /// <c>text.components(separatedBy: .newlines)</c>: NSString splits on every newline UTF-16 unit, so "\r\n"
    /// gives an empty component between the two. Empty components are kept.
    /// </summary>
    public static IReadOnlyList<string> ComponentsSeparatedByNewlines(string text) =>
        text.Split(['\n', '\U0000000B', '\U0000000C', '\r', '\U00000085', '\U00002028', '\U00002029']);

    // ---- Case ----

    /// <summary>Swift <c>lowercased()</c>: full, per-scalar, context-free, locale-independent ("\u{130}" gives "i\u{307}").</summary>
    public static string Lowercased(string text) => MapScalars(text, IcuNative.CaseMap.Lower);

    /// <summary>Swift <c>uppercased()</c>: full, per-scalar ("\u{DF}" gives "SS").</summary>
    public static string Uppercased(string text) => MapScalars(text, IcuNative.CaseMap.Upper);

    /// <summary>
    /// Foundation <c>caseInsensitiveCompare</c> and <c>range(of:options:.caseInsensitive)</c> without a locale:
    /// Unicode default case folding, then scalar order (UNVERIFIED O-07 against Foundation's fold).
    /// </summary>
    public static int CaseInsensitiveCompare(string left, string right) =>
        Compare(MapScalars(left, IcuNative.CaseMap.Fold), MapScalars(right, IcuNative.CaseMap.Fold));

    /// <summary>
    /// Foundation <c>localizedCaseInsensitiveContains</c>: locale-sensitive, case-insensitive, accent-sensitive
    /// (ED-009 part 2). An empty needle is never contained (UNVERIFIED O-08); .NET's IndexOf finds it at 0.
    /// Keep each call site's own empty-needle guard as well.
    /// </summary>
    public static bool LocalizedCaseInsensitiveContains(string text, string other, CultureInfo? culture = null)
    {
        if (other.Length == 0) return false;
        return (culture ?? CultureInfo.CurrentCulture).CompareInfo.IndexOf(text, other, CompareOptions.IgnoreCase) >= 0;
    }

    private static string MapScalars(string text, IcuNative.CaseMap map)
    {
        if (Ascii.IsValid(text)) return map == IcuNative.CaseMap.Upper ? AsciiUpper(text) : AsciiLower(text);
        var output = new StringBuilder(text.Length);
        foreach (var scalar in text.EnumerateRunes())
        {
            if (scalar.IsAscii) output.Append(map == IcuNative.CaseMap.Upper ? AsciiUpper((char)scalar.Value) : AsciiLower((char)scalar.Value));
            else output.Append(IcuNative.Map(scalar, map));
        }
        return output.ToString();
    }

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 32) : c;

    private static char AsciiUpper(char c) => c is >= 'a' and <= 'z' ? (char)(c - 32) : c;

    private static string AsciiLower(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++) span[i] = AsciiLower(source[i]);
    });

    private static string AsciiUpper(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++) span[i] = AsciiUpper(source[i]);
    });

    // ---- Internals ----

    internal static string Nfc(string text) => text.IsNormalized(NormalizationForm.FormC) ? text : text.Normalize(NormalizationForm.FormC);

    private static int OffsetAfter(string text, int characters)
    {
        var index = 0;
        for (var i = 0; i < characters && index < text.Length; i++)
            index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
        return index;
    }

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

/// <summary>
/// Swift <c>String</c> as a <c>Set</c>/<c>Dictionary</c> key and in <c>sorted()</c>: canonical equality, a hash
/// of the NFC form, and scalar order. Use it for keys holding user or external text (R-STR-2).
/// </summary>
public sealed class SwiftStringComparer : IEqualityComparer<string>, IComparer<string>
{
    public static SwiftStringComparer Instance { get; } = new();
    private SwiftStringComparer() { }
    public bool Equals(string? x, string? y) => x is null ? y is null : y is not null && SwiftString.Equal(x, y);
    public int GetHashCode(string obj) => StringComparer.Ordinal.GetHashCode(SwiftString.Nfc(obj));
    public int Compare(string? x, string? y) => x is null ? (y is null ? 0 : -1) : y is null ? 1 : SwiftString.Compare(x, y);
}

/// <summary>Swift <c>Character</c> properties over one extended grapheme cluster.</summary>
public static class SwiftCharacter
{
    /// <summary><c>isNumber</c>: Numeric_Type is not None (includes U+00BD, Roman numerals, CJK numerals).</summary>
    public static bool IsNumber(string character) =>
        IcuNative.GetIntPropertyValue(First(character).Value, IcuNative.PropertyNumericType) != 0;

    /// <summary><c>isLetter</c>: the Alphabetic property (not .NET's L* categories).</summary>
    public static bool IsLetter(string character) =>
        IcuNative.HasBinaryProperty(First(character).Value, IcuNative.PropertyAlphabetic);

    /// <summary><c>isWhitespace</c>: White_Space. Not Foundation's trimming set, which adds U+200B.</summary>
    public static bool IsWhitespace(string character) =>
        IcuNative.HasBinaryProperty(First(character).Value, IcuNative.PropertyWhiteSpace);

    // Source: Swift stdlib swift-6.2-RELEASE, stdlib/public/core/CharacterProperties.swift:223-265.
    // https://github.com/swiftlang/swift/blob/swift-6.2-RELEASE/stdlib/public/core/CharacterProperties.swift#L223-L265
    // isUppercase :236-240 takes the single-scalar Uppercase fast path; otherwise _isUppercased && isCased.
    // For a cluster, isCased :260-264 is !_isUppercased || !_isLowercased, so this becomes upper-equal && lower-different.
    public static bool IsUppercase(string character)
    {
        var scalar = First(character);
        var single = scalar.Utf16SequenceLength == character.Length;
        if (single &&
            IcuNative.HasBinaryProperty(scalar.Value, IcuNative.PropertyUppercase)) return true;
        return SwiftString.Equal(character, SwiftString.Uppercased(character)) &&
            ((single && IcuNative.HasBinaryProperty(scalar.Value, IcuNative.PropertyCased)) ||
             !SwiftString.Equal(character, SwiftString.Lowercased(character))); // isCased :260-264 retains its scalar fast path.
    }

    private static Rune First(string character) =>
        Rune.DecodeFromUtf16(character, out var rune, out _) == System.Buffers.OperationStatus.Done
            ? rune : throw new SwiftTrapException("A Character is never empty.");
}
