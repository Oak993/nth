using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "advanced cron"; ED-NEW-dextra-as10-1): a five-field cron line, with lists, ranges, steps,
// names, 7 as Sunday and the @ shortcuts; both day fields restricted means either one matches (the usual cron rule); a line that
// does not fit says what is wrong.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-1")]
public sealed class CronLineTests
{
    private static IEnumerable<string> Times(string line) => CronLine.Parse(line).TimesOfDay().Select(t => t.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Lists_ranges_and_steps_name_the_minutes_and_hours()
    {
        Assert.Equal(["09:00", "09:30", "17:00", "17:30"], Times("0,30 9,17 * * *"));
        Assert.Equal(["08:00", "10:00", "12:00"], Times("0 8-12/2 * * *"));
        Assert.Equal(24 * 4, Times("*/15 * * * *").Count());
        Assert.Equal(["00:00"], Times("@daily"));
    }

    [Fact]
    public void Names_and_seven_as_sunday_are_read()
    {
        var weekly = CronLine.Parse("0 6 * jan-mar mon,sun");
        Assert.True(weekly.IsValid);
        Assert.True(weekly.MatchesDay(new DateOnly(2026, 1, 5)));  // a Monday in January
        Assert.True(weekly.MatchesDay(new DateOnly(2026, 2, 1)));  // a Sunday in February
        Assert.False(weekly.MatchesDay(new DateOnly(2026, 4, 6))); // a Monday in April
        Assert.False(weekly.MatchesDay(new DateOnly(2026, 1, 6))); // a Tuesday
        Assert.True(CronLine.Parse("0 6 * * 7").MatchesDay(new DateOnly(2026, 2, 1)));
    }

    [Fact]
    public void When_both_day_fields_are_restricted_either_one_matches()
    {
        var line = CronLine.Parse("0 0 13 * fri");
        Assert.True(line.MatchesDay(new DateOnly(2026, 10, 13)));  // the 13th (a Tuesday)
        Assert.True(line.MatchesDay(new DateOnly(2026, 10, 16)));  // a Friday
        Assert.False(line.MatchesDay(new DateOnly(2026, 10, 14))); // neither
    }

    [Theory]
    [InlineData("")]
    [InlineData("* * * *")]
    [InlineData("* * * * * *")]
    [InlineData("60 * * * *")]
    [InlineData("* 24 * * *")]
    [InlineData("* * 0 * *")]
    [InlineData("* * * 13 *")]
    [InlineData("* * * * 8")]
    [InlineData("*/0 * * * *")]
    [InlineData("5-1 * * * *")]
    [InlineData("a * * * *")]
    public void A_line_that_does_not_fit_is_invalid_and_says_so(string line)
    {
        var cron = CronLine.Parse(line);
        Assert.False(cron.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(cron.Problem));
        Assert.Empty(cron.TimesOfDay());
        Assert.False(cron.MatchesDay(new DateOnly(2026, 10, 13)));
    }
}
