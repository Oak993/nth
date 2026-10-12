using System.Reflection;
using System.Text;
using Marrow.UI.Markdown;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-lean-11 (lean pass L11): MarkdownView's extension probe (MarkdownView.swift:154-167, PR-THEME-125) now reads only the bytes it probes. This oracle
// holds it to the code it replaced, frozen below, over 20,000 random pairs (fixed seed) of ASCII, two- and three-byte characters, surrogate pairs and lone
// surrogates, at lengths on both sides of the 128-byte short case, earlier texts that are real prefixes, prefixes changed at the head, the middle, the
// probed tail or just past it, and unrelated texts.
public sealed class MarkdownExtendsOracleTests
{
    // The unchanged code (MarkdownView.cs before ED-NEW-lean-11).
    private static bool Frozen(string earlier, string to)
    {
        var text = to;
        var earlierCount = Encoding.UTF8.GetByteCount(earlier);
        var count = Encoding.UTF8.GetByteCount(text);
        if (earlierCount <= 0 || earlierCount >= count) return false;
        const int probe = 64;
        var earlierBytes = Encoding.UTF8.GetBytes(earlier);
        var bytes = Encoding.UTF8.GetBytes(text);
        if (earlierCount <= probe * 2)
        {
            return bytes.Length >= earlierBytes.Length && bytes.AsSpan(0, earlierBytes.Length).SequenceEqual(earlierBytes);
        }
        if (!bytes.AsSpan(0, probe).SequenceEqual(earlierBytes.AsSpan(0, probe))) return false;
        var earlierTail = earlierBytes.AsSpan(earlierBytes.Length - probe, probe);
        var tailStart = earlierCount - probe;
        return bytes.AsSpan(tailStart, probe).SequenceEqual(earlierTail);
    }

    private static readonly string[] Pieces = ["a", "b", " ", "\n", "é", "ß", "€", "中", "😀", "👍🏽", "\uD800", "\uDC00", "#", "`"];

    private static string Random(Random random, int length)
    {
        var text = new StringBuilder();
        while (text.Length < length) text.Append(Pieces[random.Next(random.Next(4) == 0 ? Pieces.Length : 4)]);
        return text.ToString();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-11")]
    public void The_probe_answers_as_the_code_it_replaced() => Assert.All(Cases(), pair =>
    {
        var method = typeof(MarkdownView).GetMethod("Extends", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(Frozen(pair.Earlier, pair.Text), (bool)method.Invoke(null, [pair.Earlier, pair.Text])!);
    });

    private static IEnumerable<(string Earlier, string Text)> Cases()
    {
        var random = new Random(1011);
        var trues = 0;
        for (var i = 0; i < 20_000; i++)
        {
            var earlier = Random(random, random.Next(4) switch { 0 => random.Next(0, 50), 1 => random.Next(30, 70), 2 => random.Next(60, 140), _ => random.Next(100, 600) });
            var text = earlier + Random(random, random.Next(0, 80));
            switch (random.Next(6))
            {
                case 0: break; // a real extension
                case 1 when text.Length > 0: text = Random(random, 1) + text[1..]; break; // the head changed
                case 2 when text.Length > 2: { var at = random.Next(text.Length); text = text[..at] + Random(random, 1) + text[(at + 1)..]; break; } // anywhere
                case 3 when earlier.Length > 0: { var at = Math.Max(0, earlier.Length - 1 - random.Next(Math.Min(earlier.Length, 40))); text = text[..at] + Random(random, 1) + text[(at + 1)..]; break; } // the probed tail
                case 4: text = Random(random, text.Length); break; // unrelated
                default: text = earlier[..(earlier.Length / 2)]; break; // shorter
            }
            if (Frozen(earlier, text)) trues++;
            yield return (earlier, text);
        }
        Assert.True(trues > 3_000, $"{trues} of the cases extend");
    }
}
