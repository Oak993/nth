using System.Text;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Markdown.Frozen;

namespace Marrow.UI.Tests.Markdown;

// ED-NEW-lean-13 guard: the span-based parser gives exactly the blocks, Stable and Restart of the frozen
// pre-lean parser (Frozen/MarkdownParserFrozen.cs) on 5,000 random documents (fixed seed), whole and streamed.
// The documents mix every block marker with the text that makes Swift's Character rules matter: combining
// marks after markers and spaces, NFC and NFD letters, KELVIN SIGN and GREEK VARIA (canonical twins of 'K'
// and '`'), non-ASCII digits, emoji and ZWJ sequences, CRLF, Unicode spaces and lone surrogates.
public sealed class MarkdownParserOracleTests
{
    private static readonly string[] Leads =
    [
        "", "", "", " ", "  ", "   ", "    ", "\t", " \t", "\u00A0", "\u3000", "\u2003", " \u0301",
    ];

    private static readonly string[] Markers =
    [
        "", "", "", "", "#", "# ", "## ", "###### ", "####### ", "#\u0301 ", "#x", "# \u0301", "- ", "-", "-\u0301 ", "- \u0301", "* ",
        "+ ", "+\t", "-  ", "1. ", "1.", "2) ", "10. ", "0. ", "1234567890. ", "123456789. ", "3.x", "1.\u0301", "1. \u0301",
        "\u0663. ", "\u00BD. ", "\u4E00) ", "7\u0301. ", "> ", ">", ">> ", ">\u0301 ", "> \u0301", "```", "```python ", "~~~",
        "~~~~ ", "``", "\u1FEF\u1FEF\u1FEF", "`\u0301``", "---", "- - -", "***", "___", "**", "-*-", "-\u0301--", "| a | b |",
        "|---|:-:|", "| - | - |", "|", "[ ] ", "[x] ", "[X] ", "- [ ] ", "- [x] ", "- [X] ", "\uFF0D ", "\uFF11. ",
    ];

    private static readonly string[] Words =
    [
        "word", "Hank", "e\u0301", "\u00E9", "\u212A", "K", "\u1FEF", "`", "|", "-", ":", " ", "  ", "\t", "\u0301", "\u00BD", "\u0663",
        "\u4E00", "\U0001F44D", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\uD83D", "\uDE00", "\r", "#", "*", "_", ">", "1", "2.",
        "~", "\u00A0", "\u2028", "x", "a b", "[link](https://e.x)", "```", "~~~",
    ];

    [Fact]
    [Trait("Decision", "ED-NEW-lean-13")]
    public void Five_thousand_random_documents_parse_exactly_as_the_frozen_parser_whole_and_streamed()
    {
        var random = new Random(1313);
        var checkedDocuments = 0;
        var checkedStreams = 0;
        for (var n = 0; n < 5_000; n++)
        {
            var text = RandomDocument(random);
            var frozen = Outcome(() => MarkdownParserFrozen.MarkdownParserParse(text, null));
            var lean = Outcome(() => MarkdownParser.MarkdownParserParse(text, null));
            AssertSame(frozen, lean, text);
            if (frozen.Error is not null) continue;

            // Streamed: three growing cuts at any UTF-16 offset (a cut can split a surrogate pair or a CRLF).
            var cuts = new[] { random.Next(text.Length + 1), random.Next(text.Length + 1), random.Next(text.Length + 1), text.Length };
            Array.Sort(cuts);
            (string Text, MarkdownParser.Parse Parse)? previous = null;
            (string Text, MarkdownParserFrozen.Parse Parse)? previousFrozen = null;
            foreach (var cut in cuts)
            {
                var part = text[..cut];
                var leanPrevious = previous;
                var frozenPrevious = previousFrozen;
                var parse = Outcome(() => MarkdownParser.MarkdownParserParse(part, leanPrevious));
                var parseFrozen = Outcome(() => MarkdownParserFrozen.MarkdownParserParse(part, frozenPrevious));
                AssertSame(parseFrozen, parse, part);
                if (parse.Value is null || parseFrozen.Value is null) break;
                previous = (part, parse.Value);
                previousFrozen = (part, parseFrozen.Value);
                checkedStreams++;
            }
            checkedDocuments++;
        }
        // The frozen parser throws on some lone surrogates (NFC) and on a GREEK VARIA fence; most documents parse.
        Assert.True(checkedDocuments > 4_500, $"Only {checkedDocuments} documents parsed without an exception.");
        Assert.True(checkedStreams > 17_000, $"Only {checkedStreams} streamed steps were compared.");
    }

    private static (T? Value, Type? Error) Outcome<T>(Func<T> parse) where T : class
    {
        try { return (parse(), null); }
        catch (Exception error) { return (null, error.GetType()); }
    }

    private static void AssertSame((MarkdownParserFrozen.Parse? Value, Type? Error) expected, (MarkdownParser.Parse? Value, Type? Error) actual, string text)
    {
        Assert.True(expected.Error == actual.Error, $"Exception {actual.Error?.Name ?? "none"} instead of {expected.Error?.Name ?? "none"} for \"{Escape(text)}\".");
        if (expected.Value is null || actual.Value is null) return;
        AssertSame(expected.Value, actual.Value, text);
    }

    private static void AssertSame(MarkdownParserFrozen.Parse expected, MarkdownParser.Parse actual, string text)
    {
        Assert.True(expected.Blocks.Equals(actual.Blocks), $"Blocks differ for \"{Escape(text)}\".");
        Assert.True(expected.Stable == actual.Stable, $"Stable {actual.Stable} instead of {expected.Stable} for \"{Escape(text)}\".");
        Assert.True(expected.Restart == actual.Restart, $"Restart {actual.Restart} instead of {expected.Restart} for \"{Escape(text)}\".");
    }

    private static string RandomDocument(Random random)
    {
        var builder = new StringBuilder();
        var lines = random.Next(1, 25);
        for (var i = 0; i < lines; i++)
        {
            if (i > 0) builder.Append(random.Next(6) == 0 ? "\r\n" : "\n");
            if (random.Next(5) == 0) continue; // a blank line
            builder.Append(Leads[random.Next(Leads.Length)]);
            if (random.Next(4) == 0) builder.Append(Leads[random.Next(Leads.Length)]);
            builder.Append(Markers[random.Next(Markers.Length)]);
            var words = random.Next(0, 8);
            for (var w = 0; w < words; w++)
            {
                builder.Append(Words[random.Next(Words.Length)]);
                if (random.Next(2) == 0) builder.Append(' ');
            }
            if (random.Next(8) == 0) builder.Append(Leads[random.Next(Leads.Length)]);
        }
        return builder.ToString();
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder();
        foreach (var c in text) builder.Append(c is >= ' ' and < '\u007F' ? c.ToString() : $"\\u{(int)c:X4}");
        return builder.ToString();
    }
}
