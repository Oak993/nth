using Marrow.Core.Models;

namespace Marrow.Core.Tests.Models.Panes;

// DESIGN 3.10 and the approved canvas's task cards (web surfaces W5; ED-NEW-web-w5-1): the words of a card, as the canvas says
// them ("12m 05s", "165.4k tokens", "132 tool uses", "62 %", "Done 4 min ago", "3.1 of 5.0 GB").
[Trait("Decision", "ED-NEW-web-w5-1")]
public sealed class BackgroundJobTextTests
{
    [Theory]
    [InlineData(0, "0m 00s")]
    [InlineData(44, "0m 44s")]
    [InlineData(61, "1m 01s")]
    [InlineData(725, "12m 05s")]
    [InlineData(3599, "59m 59s")]
    [InlineData(3600, "1h 00m")]
    [InlineData(3840, "1h 04m")]
    [InlineData(90000, "25h 00m")]
    [InlineData(-5, "0m 00s")]
    public void Elapsed_time_reads_as_the_canvas_writes_it(double seconds, string expected) =>
        Assert.Equal(expected, BackgroundJobText.Elapsed(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_fraction_of_a_second_never_rounds_a_figure_up()
    {
        Assert.Equal("0m 59s", BackgroundJobText.Elapsed(TimeSpan.FromSeconds(59.999)));
        Assert.Equal("59m 59s", BackgroundJobText.Elapsed(TimeSpan.FromSeconds(3599.9)));
    }

    [Theory]
    [InlineData(0, "0 tokens")]
    [InlineData(812, "812 tokens")]
    [InlineData(999, "999 tokens")]
    [InlineData(1000, "1.0k tokens")]
    [InlineData(12_800, "12.8k tokens")]
    [InlineData(165_400, "165.4k tokens")]
    [InlineData(999_949, "999.9k tokens")]
    [InlineData(999_950, "1.0M tokens")]
    [InlineData(1_234_567, "1.2M tokens")]
    [InlineData(-3, "0 tokens")]
    public void Tokens_read_as_k_and_M(long count, string expected) => Assert.Equal(expected, BackgroundJobText.Tokens(count));

    [Theory]
    [InlineData(0, "0 tool uses")]
    [InlineData(1, "1 tool use")]
    [InlineData(2, "2 tool uses")]
    [InlineData(132, "132 tool uses")]
    public void Tool_uses_are_counted_in_the_singular_and_the_plural(long count, string expected) => Assert.Equal(expected, BackgroundJobText.ToolUses(count));

    [Theory]
    [InlineData(0, "0 %")]
    [InlineData(0.62, "62 %")]
    [InlineData(0.999, "99 %")]
    [InlineData(1, "100 %")]
    [InlineData(1.5, "100 %")]
    [InlineData(-1, "0 %")]
    [InlineData(double.NaN, "0 %")]
    public void A_download_percent_never_rounds_up_to_done(double progress, string expected) => Assert.Equal(expected, BackgroundJobText.Percent(progress));

    [Theory]
    [InlineData(BackgroundJobState.Done, 20, "Done just now")]
    [InlineData(BackgroundJobState.Done, 60, "Done 1 min ago")]
    [InlineData(BackgroundJobState.Done, 240, "Done 4 min ago")]
    [InlineData(BackgroundJobState.Failed, 540, "Failed 9 min ago")]
    [InlineData(BackgroundJobState.Stopped, 3700, "Stopped 1 h ago")]
    public void The_Done_line_says_how_long_ago(BackgroundJobState state, double seconds, string expected) =>
        Assert.Equal(expected, BackgroundJobText.EndedAgo(state, TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(BackgroundJobState.Running)]
    [InlineData(BackgroundJobState.Paused)]
    [InlineData(BackgroundJobState.Pausing)]
    [InlineData(BackgroundJobState.Stopping)]
    public void A_live_job_has_no_Done_line(BackgroundJobState state) => Assert.Null(BackgroundJobText.EndedAgo(state, TimeSpan.FromMinutes(2)));

    [Theory]
    [InlineData(3_100_000_000L, 5_000_000_000L, "3.1 of 5.0 GB")]
    [InlineData(0L, 5_000_000_000L, "0.0 of 5.0 GB")]
    [InlineData(500_000_000L, 750_000_000L, "500 of 750 MB")]
    [InlineData(1_500_000L, 4_000_000L, "1.5 of 4.0 MB")]
    [InlineData(2_000L, 8_000L, "2 of 8 KB")]
    [InlineData(50L, 100L, "50 of 100 B")]
    [InlineData(9_000_000_000L, 5_000_000_000L, "5.0 of 5.0 GB")]
    public void Bytes_share_the_unit_of_the_total(long done, long total, string expected) => Assert.Equal(expected, BackgroundJobText.BytesOf(done, total));

    [Fact]
    public void A_title_is_one_line_cut_with_an_ellipsis()
    {
        Assert.Equal("Fix the build", BackgroundJobText.OneLine("  Fix\n the\tbuild \r\n"));
        Assert.Equal("", BackgroundJobText.OneLine("   "));
        Assert.Equal("", BackgroundJobText.OneLine(null));
        var cut = BackgroundJobText.OneLine(new string('a', 200), 40);
        Assert.Equal(40, cut.Length);
        Assert.EndsWith("…", cut, StringComparison.Ordinal);
        Assert.Equal("abcde", BackgroundJobText.OneLine("abcde", 5));
    }

    [Fact]
    public void The_reserve_strings_are_at_least_as_wide_as_anything_the_formats_write()
    {
        Assert.True(BackgroundJobText.ElapsedReserve.Length >= BackgroundJobText.Elapsed(TimeSpan.FromHours(99).Add(TimeSpan.FromMinutes(59))).Length);
        Assert.True(BackgroundJobText.TokensReserve.Length >= BackgroundJobText.Tokens(999_949).Length);
        Assert.True(BackgroundJobText.ToolUsesReserve.Length >= BackgroundJobText.ToolUses(9_999).Length);
        Assert.True(BackgroundJobText.PercentReserve.Length >= BackgroundJobText.Percent(1).Length);
    }

    [Fact]
    public void The_ticker_is_4_Hz_and_a_finished_card_lives_ten_minutes()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), BackgroundJobText.TickInterval);
        Assert.Equal(TimeSpan.FromMinutes(10), BackgroundJobText.DoneLifetime);
    }
}
