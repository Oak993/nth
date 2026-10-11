using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// ED-NEW-lean-3: the early-outs must give the exact answers of the frozen pre-change code (TextCleanupFrozen). Fixed-seed random texts
// drawn from an alphabet built around both patterns: ESC and every CSI parameter, intermediate and final class edge, canonical
// equivalents that NFC turns into pattern characters (GREEK QUESTION MARK -> ';', KELVIN SIGN), combining marks that glue onto ESC,
// '[', a digit or a dash, the four long dashes and their neighbours, CRLF, emoji, ZWJ sequences, Hangul jamo and plain prose. Half the
// texts never contain ESC (the StripANSI early-out) and half never contain a long dash (the WithoutEmDashes early-out). Results are
// compared by value and by identity (the input returned as is), and a thrown exception by its type.
[Trait("Decision", "ED-NEW-lean-3")]
public sealed class TextCleanupLeanEquivalenceTests
{
    private const int Cases = 200_000;

    private static readonly string[] Alphabet =
    [
        "\u001B", "[", "]", "0", "5", "9", ";", "?", ":", " ", "!", "/", "@", "m", "K", "~", "\u007F", "a", "Z", "\r\n", "\n", "\r", "\t",
        "\u0301", "\u0300", "\u037E", "\u212A", "\u00C5", "A\u030A", "\u00E9", "e\u0301", "\u2014", "\u2015", "\u2E3A", "\u2E3B", "\u2013", "-", "\u2018", "\u2026", "\u65E5\u672C",
        "\U0001F680", "\U0001F468\u200D\U0001F469\u200D\U0001F467", "\U0001F1F3\U0001F1F1", "\u200D", "\uFE0F", "\u1100\u1161", "\uFFFD", "\u0085", "\u2028", "\u001B[", "\u001B[31m", "\u001B[0m",
        "\u001B[1;2 q", "\u001B[?25l", "\u001B]0;t\a", "\u2014\u0301", "\u001B\u0301", "[\u0301", "1\u0301",
    ];

    [Fact]
    public void StripANSI_matches_the_frozen_code_on_random_texts()
    {
        var mismatches = new List<string>();
        foreach (var text in Texts(5303, allowEscape: true, allowDash: true, loneSurrogates: false))
            Compare(text, TextCleanupFrozen.StripANSI, TextCleanup.StripANSI, mismatches);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void WithoutEmDashes_matches_the_frozen_code_on_random_texts_including_lone_surrogates()
    {
        var mismatches = new List<string>();
        foreach (var text in Texts(5304, allowEscape: true, allowDash: true, loneSurrogates: true))
            Compare(text, TextCleanupFrozen.WithoutEmDashes, TextCleanup.WithoutEmDashes, mismatches);
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    [Fact]
    public void StripANSI_with_a_lone_surrogate_matches_the_frozen_code_when_an_escape_is_present_and_returns_the_input_otherwise()
    {
        // The frozen code NFC-normalises every grapheme and .NET's normaliser throws on a lone surrogate, which a Swift String can
        // never hold (Swift has no such input and never traps here). Escape-free text now returns the input untouched; text with an
        // escape still takes the unchanged walk, so it behaves exactly as before.
        var mismatches = new List<string>();
        var escapeFree = 0;
        foreach (var text in Texts(5305, allowEscape: true, allowDash: true, loneSurrogates: true))
        {
            if (text.Contains('\u001B', StringComparison.Ordinal)) { Compare(text, TextCleanupFrozen.StripANSI, TextCleanup.StripANSI, mismatches); continue; }
            escapeFree++;
            var result = TextCleanup.StripANSI(text);
            if (!ReferenceEquals(result, text)) mismatches.Add($"escape-free {Escape(text)}: not returned as is");
        }
        Assert.True(escapeFree > Cases / 3, $"only {escapeFree} escape-free texts");
        Assert.True(mismatches.Count == 0, string.Join("\n", mismatches.Take(20)));
    }

    private static IEnumerable<string> Texts(int seed, bool allowEscape, bool allowDash, bool loneSurrogates)
    {
        var random = new Random(seed);
        var builder = new StringBuilder();
        string[] edges = ["", "\u001B", "\u001B[", "\u001B[m", "—", "x", "\u0301", "\r\n"];
        foreach (var edge in edges) yield return edge;
        for (var i = 0; i < Cases; i++)
        {
            var noEscape = !allowEscape || random.Next(2) == 0;
            var noDash = !allowDash || random.Next(2) == 0;
            builder.Clear();
            var length = random.Next(random.Next(4) == 0 ? 200 : 24);
            while (builder.Length < length)
            {
                if (loneSurrogates && random.Next(40) == 0) { builder.Append(random.Next(2) == 0 ? '\uD83D' : '\uDE80'); continue; }
                var piece = random.Next(5) == 0 ? ((char)(0x20 + random.Next(0x5F))).ToString() : Alphabet[random.Next(Alphabet.Length)];
                if (noEscape && piece.Contains('\u001B', StringComparison.Ordinal)) continue;
                if (noDash && piece.AsSpan().IndexOfAny("—―⸺⸻") >= 0) continue;
                builder.Append(piece);
            }
            yield return builder.ToString();
        }
    }

    private static void Compare(string text, Func<string, string> frozen, Func<string, string> shipped, List<string> mismatches)
    {
        var (expected, expectedError) = Run(frozen, text);
        var (actual, actualError) = Run(shipped, text);
        if (expectedError != actualError) mismatches.Add($"{Escape(text)}: threw {expectedError?.Name ?? "nothing"} before, {actualError?.Name ?? "nothing"} now");
        else if (expected != actual) mismatches.Add($"{Escape(text)}: {Escape(expected ?? "")} before, {Escape(actual ?? "")} now");
        else if (ReferenceEquals(expected, text) != ReferenceEquals(actual, text)) mismatches.Add($"{Escape(text)}: identity differs");
    }

    private static (string? Result, Type? Error) Run(Func<string, string> cleanup, string text)
    {
        try { return (cleanup(text), null); }
        catch (Exception error) { return (null, error.GetType()); }
    }

    private static string Escape(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in text) builder.Append(c is >= ' ' and <= '~' ? c.ToString() : $"\\u{(int)c:X4}");
        return builder.Append('"').ToString();
    }
}
