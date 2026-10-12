using System.Text;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Markdown.Frozen;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-13 guard: the span-based mention scan returns exactly the ranges and values of the frozen pre-lean
// scan (Frozen/MentionScannerFrozen.cs) on 5,000 random paragraphs (fixed seed). Names and text mix ASCII with
// the cases where Swift compares whole Characters canonically: NFC against NFD, KELVIN SIGN against 'K',
// combining marks after a name, digits and letters beside a name, emoji and ZWJ sequences, CRLF, empty names
// and lone surrogates.
public sealed class MentionScannerOracleTests
{
    private static readonly string[] Names =
    [
        "Hank", "Hank 2", "Nova", "Zo\u00EB", "Zoe\u0308", "Kai", "\u212Aai", "R2", "Jos\u00E9", "\u674E\u96F7", "\U0001F44D", "a\r",
        "x\r\n", "", "An", "Ann", "e", "\u00C5sa", "A\u030Asa", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "Hank\u0301",
        "\u212B", "\u00C5", "\r", "\n", "2",
    ];

    private static readonly string[] Pieces =
    [
        " ", " ", " ", ", ", ".", "\n", "\r\n", "\r", "2", "s", "x", "\u0301", "\u0308", "\u00E9", "\u212A", "K", "-", "'", "\u00A0",
        "\u0663", "\u00BD", "\U0001F44D", "\u200D", "word", "and", "Hankering", "Novak", "Zo", "\u674E", "A",
        "\u030A", "e", "\u212B",
    ];

    [Fact]
    [Trait("Decision", "ED-NEW-lean-13")]
    public void Five_thousand_random_paragraphs_scan_exactly_as_the_frozen_scanner()
    {
        var random = new Random(1313);
        var compared = 0;
        var matched = 0;
        for (var n = 0; n < 5_000; n++)
        {
            var text = RandomParagraph(random);
            var candidates = Enumerable.Range(0, random.Next(0, 7)).Select(_ => Names[random.Next(Names.Length)]).ToList();
            if (random.Next(20) == 0) candidates.Insert(random.Next(candidates.Count + 1), "\uD83D"); // a lone surrogate name
            var expected = Outcome(() => MentionScannerFrozen.Matches(text, candidates, name => name));
            var actual = Outcome(() => MentionScanner.Matches(text, candidates, name => name));
            Assert.True(expected.Error == actual.Error,
                $"Exception {actual.Error?.Name ?? "none"} instead of {expected.Error?.Name ?? "none"} for \"{Escape(text)}\" with [{string.Join(", ", candidates.Select(Escape))}].");
            if (expected.Value is null || actual.Value is null) continue;
            Assert.True(expected.Value.Equals(actual.Value),
                $"Matches differ for \"{Escape(text)}\" with [{string.Join(", ", candidates.Select(Escape))}]: " +
                $"expected {string.Join(" ", expected.Value)}, got {string.Join(" ", actual.Value)}.");
            compared++;
            matched += expected.Value.Count;
        }
        // The frozen scan throws when a lone surrogate reaches NFC or a Character property; most paragraphs scan.
        Assert.True(compared > 4_300, $"Only {compared} paragraphs scanned without an exception.");
        Assert.True(matched > 1_800, $"Only {matched} mentions were found and compared.");
    }

    private static (T? Value, Type? Error) Outcome<T>(Func<T> scan) where T : class
    {
        try { return (scan(), null); }
        catch (Exception error) { return (null, error.GetType()); }
    }

    private static string RandomParagraph(Random random)
    {
        var builder = new StringBuilder();
        var count = random.Next(0, 30);
        for (var i = 0; i < count; i++)
        {
            if (random.Next(3) != 0) { builder.Append(Pieces[random.Next(Pieces.Length)]); continue; }
            if (random.Next(2) == 0) builder.Append(' ');
            builder.Append(Names[random.Next(Names.Length)]);
            if (random.Next(2) == 0) builder.Append(' ');
        }
        if (random.Next(12) == 0) builder.Insert(random.Next(builder.Length + 1), random.Next(2) == 0 ? "\uD83D" : "\uDE00"); // a lone surrogate
        return builder.ToString();
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text) builder.Append(c is >= ' ' and < '\u007F' ? c.ToString() : $"\\u{(int)c:X4}");
        return builder.ToString();
    }
}
