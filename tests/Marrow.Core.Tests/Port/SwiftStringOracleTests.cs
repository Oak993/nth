using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// ED-NEW-lean-1: the ASCII fast paths in SwiftString must give the Character-walking answers. Every method is run on 10,000 random cases
// (fixed seed) against the frozen pre-change code (SwiftStringFrozen): ASCII heavy with CR, LF and CRLF, Latin-1, combining marks, NFC and
// NFD pairs, KELVIN SIGN and other canonical singletons, emoji with ZWJ, skin tones and flags, a Prepend character and Hangul jamo.
// Results are compared by value, and a thrown exception by its type. Every non-ASCII character is written as a \U escape (R-STR-9).
// The one difference: Suffix(text, 0) and DropLast(text, 0) on a non-empty text threw ArgumentOutOfRangeException in the frozen code
// (it indexed one past its list of Character starts); they now give Swift's suffix(0) == "" and dropLast(0) == text.
[Trait("Decision", "ED-NEW-lean-1")]
public sealed class SwiftStringOracleTests
{
    private const int Cases = 10_000;

    private static readonly string[] AsciiTokens =
    [
        "a", "b", "c", "K", "k", "e", "n", "A", ";", ",", " ", ".", "/", "-", "0", "1", "\t",
        "\r", "\n", "\r\n", "\r\n", "\n\r", "\r\r\n", "ab", "abc", "\r\na",
    ];

    private static readonly string[] OtherTokens =
    [
        "\U000000E9", "e\U00000301", "\U000000C5", "A\U0000030A", "\U0000212B", "\U000000F1", "n\U00000303", "\U000000DF", "\U000000FC",
        "\U00000301", "\U00000308", "\U00000327", "\U0000030A",                                   // bare combining marks
        "\U0000212A", "\U0000037E", "\U00002126", "\U000003A9",                                   // KELVIN, GREEK QUESTION MARK, OHM, OMEGA
        "\U0001F600", "\U0001F468\U0000200D\U0001F469\U0000200D\U0001F467", "\U0000200D", "\U0001F44D\U0001F3FD",
        "\U0001F1FA\U0001F1F8", "\U0001F1EB", "\U00000600", "\U00001100\U00001161", "\U00002014", "\U00000085", "\U00002028",
    ];

    private static readonly string[] Separators =
    [
        "\n", "\n", "\r", "\r\n", ",", " ", "K", ";", "a", "e", "\U000000E9", "e\U00000301", "\U0000212A", "\U0000037E",
        "\U0001F600", "\U0001F1FA\U0001F1F8", "ab", "", "\U00000301",
    ];

    [Fact]
    public void Equal_matches_the_frozen_code()
    {
        var random = new Random(4101);
        for (var i = 0; i < Cases; i++)
        {
            var (left, right) = Pair(random);
            Assert.True(SwiftStringFrozen.Equal(left, right) == SwiftString.Equal(left, right), Show("Equal", left, right));
        }
    }

    [Fact]
    public void Contains_matches_the_frozen_code()
    {
        var random = new Random(4102);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var needle = Needle(random, text);
            Same(() => SwiftStringFrozen.Contains(text, needle), () => SwiftString.Contains(text, needle), Show("Contains", text, needle));
        }
    }

    [Fact]
    public void HasPrefix_matches_the_frozen_code()
    {
        var random = new Random(4103);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var prefix = random.Next(3) == 0 ? Needle(random, text) : Variant(random, text[..RuneOffset(text, random.Next(text.Length + 1))]);
            if (random.Next(4) == 0) text += Text(random);
            Same(() => SwiftStringFrozen.HasPrefix(text, prefix), () => SwiftString.HasPrefix(text, prefix), Show("HasPrefix", text, prefix));
        }
    }

    [Fact]
    public void HasSuffix_matches_the_frozen_code()
    {
        var random = new Random(4104);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var suffix = random.Next(3) == 0 ? Needle(random, text) : Variant(random, text[RuneOffset(text, random.Next(text.Length + 1))..]);
            if (random.Next(4) == 0) text = Text(random) + text;
            Same(() => SwiftStringFrozen.HasSuffix(text, suffix), () => SwiftString.HasSuffix(text, suffix), Show("HasSuffix", text, suffix));
        }
    }

    [Fact]
    public void Split_matches_the_frozen_code()
    {
        var random = new Random(4105);
        int[] splits = [int.MaxValue, int.MaxValue, int.MaxValue, 0, 1, 2, 3, -1];
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var separator = Separators[random.Next(Separators.Length)];
            var maxSplits = splits[random.Next(splits.Length)];
            var omitting = random.Next(3) != 0;
            Same(() => string.Join("\U0000E000", SwiftStringFrozen.Split(text, separator, maxSplits, omitting)) + "#" + SwiftStringFrozen.Split(text, separator, maxSplits, omitting).Count,
                () => string.Join("\U0000E000", SwiftString.Split(text, separator, maxSplits, omitting)) + "#" + SwiftString.Split(text, separator, maxSplits, omitting).Count,
                Show($"Split max {maxSplits} omitting {omitting}", text, separator));
        }
    }

    [Fact]
    public void Count_matches_the_frozen_code()
    {
        var random = new Random(4106);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            Assert.True(SwiftStringFrozen.Count(text) == SwiftString.Count(text), Show("Count", text, ""));
        }
    }

    [Fact]
    public void Suffix_matches_the_frozen_code_and_suffix_zero_is_empty()
    {
        var random = new Random(4107);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var n = random.Next(-1, SwiftStringFrozen.Count(text) + 3);
            if (n == 0 && text.Length != 0)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => SwiftStringFrozen.Suffix(text, 0));
                Assert.Equal("", SwiftString.Suffix(text, 0));
                continue;
            }
            Same(() => SwiftStringFrozen.Suffix(text, n), () => SwiftString.Suffix(text, n), Show($"Suffix {n}", text, ""));
        }
    }

    [Fact]
    public void DropLast_matches_the_frozen_code_and_drop_last_zero_is_the_text()
    {
        var random = new Random(4108);
        for (var i = 0; i < Cases; i++)
        {
            var text = Text(random);
            var k = random.Next(-1, SwiftStringFrozen.Count(text) + 3);
            if (k == 0 && text.Length != 0)
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => SwiftStringFrozen.DropLast(text, 0));
                Assert.Equal(text, SwiftString.DropLast(text, 0));
                continue;
            }
            Same(() => SwiftStringFrozen.DropLast(text, k), () => SwiftString.DropLast(text, k), Show($"DropLast {k}", text, ""));
        }
    }

    [Fact]
    public void Comparer_equality_hash_and_order_match_the_frozen_code()
    {
        var random = new Random(4109);
        var comparer = SwiftStringComparer.Instance;
        for (var i = 0; i < Cases; i++)
        {
            var (left, right) = Pair(random);
            Assert.True(SwiftStringFrozen.ComparerEquals(left, right) == comparer.Equals(left, right), Show("Comparer.Equals", left, right));
            Assert.True(SwiftStringFrozen.ComparerHash(left) == comparer.GetHashCode(left), Show("Comparer.GetHashCode", left, ""));
            Assert.True(SwiftStringFrozen.ComparerCompare(left, right) == comparer.Compare(left, right), Show("Comparer.Compare", left, right));
        }
        for (var round = 0; round < 50; round++)
        {
            var items = Enumerable.Range(0, 200).Select(_ => Pair(random).Item2).ToList();
            var expected = items.ToList();
            expected.Sort(Comparer<string>.Create(SwiftStringFrozen.ComparerCompare));
            items.Sort(comparer);
            Assert.Equal(expected, items);
        }
    }

    // ---- Generators ----

    private static string Text(Random random)
    {
        var asciiOnly = random.Next(2) == 0;
        var tokens = random.Next(0, 24);
        var text = new StringBuilder();
        for (var i = 0; i < tokens; i++)
            text.Append(asciiOnly || random.Next(4) != 0 ? AsciiTokens[random.Next(AsciiTokens.Length)] : OtherTokens[random.Next(OtherTokens.Length)]);
        return text.ToString();
    }

    private static string Needle(Random random, string text)
    {
        switch (random.Next(5))
        {
            case 0:
            case 1:
            {
                var start = RuneOffset(text, random.Next(text.Length + 1));
                var end = RuneOffset(text, start + random.Next(text.Length - start + 1));
                return Variant(random, text[start..end]);
            }
            case 2: return AsciiTokens[random.Next(AsciiTokens.Length)];
            case 3: return Variant(random, OtherTokens[random.Next(OtherTokens.Length)]);
            default: return Variant(random, Text(random));
        }
    }

    // The text itself (a new instance), its NFC or NFD form, or a canonical singleton swapped in.
    private static string Variant(Random random, string text) => random.Next(6) switch
    {
        0 => text.Normalize(NormalizationForm.FormC),
        1 => text.Normalize(NormalizationForm.FormD),
        2 => text.Replace("K", "\U0000212A", StringComparison.Ordinal).Replace(";", "\U0000037E", StringComparison.Ordinal),
        _ => new string(text.AsSpan()),
    };

    private static (string, string) Pair(Random random)
    {
        var left = Text(random);
        var right = random.Next(7) switch
        {
            0 => new string(left.AsSpan()),
            1 => left.Normalize(NormalizationForm.FormC),
            2 => left.Normalize(NormalizationForm.FormD),
            3 => left.Replace("K", "\U0000212A", StringComparison.Ordinal).Replace(";", "\U0000037E", StringComparison.Ordinal).Replace("\U000000C5", "\U0000212B", StringComparison.Ordinal),
            4 => left.Length == 0 ? "a" : left[..RuneOffset(left, random.Next(left.Length))] + "a" + left[RuneOffset(left, random.Next(left.Length))..],
            5 => left + AsciiTokens[random.Next(AsciiTokens.Length)],
            _ => Text(random),
        };
        return random.Next(2) == 0 ? (left, right) : (right, left);
    }

    // Moves an offset off the low half of a surrogate pair so no case holds a lone surrogate.
    private static int RuneOffset(string text, int offset) =>
        offset > 0 && offset < text.Length && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1]) ? offset - 1 : offset;

    // ---- Comparison ----

    private static void Same<T>(Func<T> frozen, Func<T> shipped, string label)
    {
        var expected = Outcome(frozen);
        var actual = Outcome(shipped);
        Assert.True(Equals(expected, actual), $"{label}: frozen {expected}, shipped {actual}");
    }

    private static object? Outcome<T>(Func<T> run)
    {
        try { return run(); }
        catch (Exception error) { return "threw " + error.GetType().Name; }
    }

    private static string Show(string method, string first, string second) => $"{method} [{Escape(first)}] [{Escape(second)}]";

    private static string Escape(string text)
    {
        var output = new StringBuilder();
        foreach (var unit in text) output.Append(unit is >= ' ' and <= '~' ? unit.ToString() : $"\\u{(int)unit:X4}");
        return output.ToString();
    }
}
