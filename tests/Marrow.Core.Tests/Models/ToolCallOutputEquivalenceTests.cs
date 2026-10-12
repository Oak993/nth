using System.Globalization;
using System.Text;
using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models;

// ED-NEW-lean-5 guard: ToolCall.AppendOutput and SetOutput give byte-identical Output to the old code, kept here as a
// frozen copy (TrimOutput(Output + text): the UTF-8 count of the joined text against 60,000, then "…" and the last
// 30,000 Characters through a list of every Character start), for random scripts (fixed seeds) and the seams where
// a cheaper path could differ: just under, at and over the ceiling, surrogate halves meeting across an append,
// Characters that join across it, and an Output already over the ceiling when it was stored.
public sealed class ToolCallOutputEquivalenceTests
{
    private static readonly string[] Pieces =
    [
        "a", "line\n", "\r", "\n", "\r\n", "é", "e", "́", "\U0001F600", "\uD83D", "\uDE00", "\U0001F44D",
        "\U0001F3FD", "‍", "\U0001F469", "\U0001F1FA", "\U0001F1F8", "\uD83C", "\uDDF8", "ᄀ", "ᅡ",
        "ᆨ", "क", "्", "ष", "—", "…", "\t", "✓", "️",
    ];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Appended_output_matches_the_old_trim_for_a_random_script(int seed)
    {
        var random = new Random(seed * 104_729);
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run" };
        var expected = "";
        var trims = 0;
        for (var step = 0; step < 400; step++)
        {
            var text = random.Next(10) switch
            {
                0 => "",
                1 => Random(random, random.Next(20_000)),
                2 => new string('x', random.Next(1, 30_000)),
                _ => Random(random, random.Next(1, 600)),
            };
            if (random.Next(25) == 0)
            {
                call = call.SetOutput(text);
                expected = OldTrimOutput(text);
            }
            else
            {
                call = call.AppendOutput(text);
                expected = OldTrimOutput(expected + text);
            }
            if (expected.StartsWith('…')) trims++;
            Assert.Equal(expected, call.Output, StringComparer.Ordinal);
        }
        Assert.True(trims > 20, "the script reached the ceiling");
    }

    [Theory]
    [InlineData(59_999)]
    [InlineData(60_000)]
    [InlineData(60_001)]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Appends_at_the_ceiling_and_across_surrogate_and_character_seams_match_the_old_trim(int bytes)
    {
        string[] heads = ["", "\uD83D", "e", "\U0001F1FA", "\U0001F469‍", "\r", "—"];
        string[] tails = ["", "\uDE00", "́", "\U0001F1F8", "\U0001F469", "\n", "x", "\uDE00́"];
        foreach (var head in heads)
            foreach (var tail in tails)
                foreach (var shift in new[] { -6, -4, -3, -2, -1, 0, 1, 2, 3 })
                {
                    var fill = Math.Max(0, bytes + shift - Encoding.UTF8.GetByteCount(head));
                    var output = new string('a', fill) + head;
                    var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run", Output = output };
                    var appended = call.AppendOutput(tail).Output;
                    Assert.Equal(OldTrimOutput(output + tail), appended, StringComparer.Ordinal);
                    Assert.Equal(OldTrimOutput(output + tail), call.SetOutput(output + tail).Output, StringComparer.Ordinal);
                }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void Output_stored_over_the_ceiling_is_trimmed_by_the_next_append_as_before()
    {
        // 30,000 em dashes are 90,000 bytes: a trimmed non-ASCII Output stays over the ceiling (PR-RUNTIME-150).
        var stored = "…" + new string('—', 30_000);
        foreach (var text in new[] { "", "a", "́", new string('—', 5) })
        {
            var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run", Output = stored };
            Assert.Equal(OldTrimOutput(stored + text), call.AppendOutput(text).Output, StringComparer.Ordinal);
        }
        // Fewer than 30,000 Characters but over 60,000 bytes: the whole text follows the ellipsis.
        var wide = new string('—', 25_000);
        var wideCall = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run" };
        Assert.Equal(OldTrimOutput(wide), wideCall.AppendOutput(wide).Output, StringComparer.Ordinal);
        Assert.Equal("…" + wide, wideCall.AppendOutput(wide).Output, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-5")]
    public void An_append_under_the_ceiling_keeps_the_joined_string_and_an_empty_one_keeps_the_same_output()
    {
        var call = new ToolCall { ToolCallKind = ToolCall.Kind.Command, Title = "Run", Output = "abc" };
        Assert.Same(call.Output, call.AppendOutput("").Output);
        Assert.Equal("abcdef", call.AppendOutput("def").Output);
    }

    private static string Random(Random random, int pieces)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < pieces; i++) builder.Append(Pieces[random.Next(Pieces.Length)]);
        return builder.ToString();
    }

    // ---- Frozen copy of Timeline.cs:196-199 before ED-NEW-lean-5 (SwiftString.Utf8Count and SwiftString.Suffix inlined) ----

    private static string OldTrimOutput(string output)
    {
        if (Encoding.UTF8.GetByteCount(output) <= ToolCall.OutputLimit) return output;
        var starts = new List<int>();
        for (var index = 0; index < output.Length; index += StringInfo.GetNextTextElementLength(output.AsSpan(index))) starts.Add(index);
        var keep = (int)(ToolCall.OutputLimit / 2);
        return "…" + (keep >= starts.Count ? output : output[starts[starts.Count - keep]..]);
    }
}
