using System.Diagnostics;
using System.Text;
using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

public sealed class TextCleanupTests
{
    [Fact]
    [Trait("Req", "PR-STORE-105")]
    public void Four_long_dashes_become_hyphens_with_spacing_and_en_dash_preserved()
    {
        const string original = "works — every time; ships―sometimes; ⸺⸻; lines 10–20";
        const string expected = "works - every time; ships-sometimes; --; lines 10–20";
        Assert.Equal(expected, TextCleanup.WithoutEmDashes(original));
        Assert.Equal(expected, TextCleanup.WithoutEmDashes(TextCleanup.WithoutEmDashes(original)));
        Assert.Equal("—\u0301", TextCleanup.WithoutEmDashes("—\u0301"));
    }

    [Fact]
    [Trait("Req", "PR-STORE-106")]
    public void Dash_free_or_nonmatching_grapheme_text_is_returned_by_identity()
    {
        foreach (var text in new[] { "", "‘curly’ “quotes” … 10–20", "—\u0301" })
            Assert.Same(text, TextCleanup.WithoutEmDashes(text));
    }

    [Theory] // Expose the byte prescan so malformed/truncated byte tails can be tested.
    [Trait("Req", "PR-STORE-106")]
    [InlineData(new byte[] { 0xE2 }, false)]
    [InlineData(new byte[] { 0xE2, 0x80 }, false)]
    [InlineData(new byte[] { 0xE2, 0xB8 }, false)]
    [InlineData(new byte[] { 0xE2, 0x80, 0x94 }, true)]
    [InlineData(new byte[] { 0xE2, 0x80, 0x95 }, true)]
    [InlineData(new byte[] { 0xE2, 0xB8, 0xBA }, true)]
    [InlineData(new byte[] { 0xE2, 0xB8, 0xBB }, true)]
    [InlineData(new byte[] { 0xE2, 0x80, 0x93 }, false)]
    [InlineData(new byte[] { 0xE2, 0xE2, 0x80, 0x94 }, false)]
    public void Byte_prescan_copies_the_exact_iterator_consumption_rules(byte[] bytes, bool expected) =>
        Assert.Equal(expected, TextCleanup.MayContainEmDash(bytes));

    [Fact] // Generous infrastructure guard, not a product latency target.
    [Trait("Req", "PR-STORE-106")]
    public void Dash_free_model_prose_throughput_guard()
    {
        var text = string.Concat(Enumerable.Repeat("‘quoted’ prose… lines 10–20\n", 4096));
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 512; i++) Assert.Same(text, TextCleanup.WithoutEmDashes(text));
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Prescan took {timer.Elapsed}.");
    }

    [Theory]
    [Trait("Req", "PR-STORE-107")]
    [InlineData("\u001B[31mred\u001B[0m", "red")]
    [InlineData("A\u001B[?25lB\u001B[1;2 qC", "ABC")]
    [InlineData("\u001B]0;title\aX", "\u001B]0;title\aX")]
    [InlineData("\u001B[31", "\u001B[31")]
    [InlineData("\u001B[1:2mX", "\u001B[1:2mX")]
    [InlineData("\u001B[mX", "X")]
    public void ANSI_cleanup_removes_only_the_source_CSI_pattern(string input, string expected) =>
        Assert.Equal(expected, TextCleanup.StripANSI(input));

    [Fact]
    [Trait("Req", "PR-STORE-108")]
    public void Last_lines_strips_ANSI_trims_each_line_discards_blanks_and_defaults_to_four()
    {
        const string input = "ignored\r\n \r\n\u001B[31m one \u001B[0m\n\ttwo\vthree\f\u200Bfour\u200B\u2028 \u2029";
        Assert.Equal("one\ntwo\nthree\nfour", TextCleanup.LastLines(input));
        Assert.Equal("three\nfour", TextCleanup.LastLines(input, 2));
        Assert.Equal("", TextCleanup.LastLines(input, 0));
        Assert.Null(TextCleanup.LastLines(" \t\r\n\u200B\u001B[0m"));
    }

    [Theory]
    [Trait("Req", "PR-STORE-109")]
    [InlineData(119)]
    [InlineData(120)]
    [InlineData(121)]
    public void Summary_uses_120_graphemes_with_the_exact_single_ellipsis(int length)
    {
        var text = new string('a', length);
        Assert.Equal(length <= 120 ? text : new string('a', 119) + "…", TextCleanup.SingleLine(text));
    }

    [Fact]
    [Trait("Req", "PR-STORE-109")]
    public void Summary_skips_leading_newlines_but_does_not_skip_a_whitespace_only_first_line()
    {
        Assert.Equal("first", TextCleanup.SingleLine("\r\n\u2028\t first \u200B\r\nsecond"));
        Assert.Equal("", TextCleanup.SingleLine(" \t\nsecond"));
        Assert.Equal("", TextCleanup.SingleLine("\r\n"));
        Assert.Equal("…", TextCleanup.SingleLine("ab", 1));
    }

    [Fact]
    [Trait("Req", "PR-STORE-109")]
    public void Summary_counts_extended_graphemes_not_UTF16_units_or_Unicode_scalars()
    {
        const string grapheme = "👨‍👩‍👧‍👦";
        Assert.Equal(grapheme + "…", TextCleanup.SingleLine(grapheme + "e\u0301x", 2));
        Assert.Equal("e\u0301…", TextCleanup.SingleLine("e\u0301yz", 2));
    }
}
