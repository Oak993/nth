using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2; the schedule_duty tool's "weekdays 08:00"; ED-NEW-dextra-as10-1): the words an agent or the
// owner gives for a schedule become a TimeSchedule or one short reason; a schedule is said back in the same plain words.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-1")]
public sealed class ScheduleTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    private static TimeSchedule Made(string text)
    {
        var reading = ScheduleText.Read(text, Now, TimeZoneInfo.Utc);
        Assert.Null(reading.Problem);
        return Assert.IsType<TimeSchedule>(reading.Schedule);
    }

    private static string Said(string text) => ScheduleText.Describe(Made(text));

    [Theory]
    [InlineData("weekdays 08:00", "Weekdays at 08:00")]
    [InlineData("Weekdays at 9am", "Weekdays at 09:00")]
    [InlineData("daily 18:30", "Every day at 18:30")]
    [InlineData("every day at 6:30pm", "Every day at 18:30")]
    [InlineData("weekly mon,thu 09:00", "Mondays and Thursdays at 09:00")]
    [InlineData("every monday at 9", "Mondays at 09:00")]
    [InlineData("weekly mon wed fri 7:15", "Mondays, Wednesdays and Fridays at 07:15")]
    [InlineData("monthly 1 09:00", "Monthly on the 1st at 09:00")]
    [InlineData("monthly on the 22nd at 08:00", "Monthly on the 22nd at 08:00")]
    [InlineData("every 30 minutes", "Every 30 min")]
    [InlineData("every 2 hours", "Every 2 h")]
    [InlineData("every 1 day", "Every day")]
    [InlineData("once 2026-11-01 09:00", "Once on 2026-11-01 at 09:00")]
    [InlineData("tomorrow 07:00", "Once on 2026-10-14 at 07:00")]
    [InlineData("in 90 minutes", "Once on 2026-10-13 at 13:30")]
    [InlineData("cron 0 9 * * 1-5", "Cron 0 9 * * 1-5")]
    [InlineData("0 */2 * * *", "Cron 0 */2 * * *")]
    public void Plain_words_are_read_and_said_back(string text, string said) => Assert.Equal(said, Said(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("whenever")]
    [InlineData("every 4 minutes")]
    [InlineData("daily 25:00")]
    [InlineData("daily 13pm")]
    [InlineData("weekly 09:00")]
    [InlineData("monthly 32 09:00")]
    [InlineData("once someday 09:00")]
    [InlineData("cron 1 2 3")]
    [InlineData("every 9999999 days")]
    public void Words_that_are_not_a_schedule_give_a_reason_and_no_schedule(string text)
    {
        var reading = ScheduleText.Read(text, Now, TimeZoneInfo.Utc);
        Assert.Null(reading.Schedule);
        Assert.False(string.IsNullOrWhiteSpace(reading.Problem));
    }

    [Fact]
    public void An_every_schedule_under_five_minutes_says_why()
    {
        var reading = ScheduleText.Read("every 3 minutes", Now, TimeZoneInfo.Utc);
        Assert.Contains("5 or more", reading.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void In_and_tomorrow_are_read_in_the_zone_of_the_duty()
    {
        var karachi = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
        var lateEvening = new DateTimeOffset(2026, 10, 13, 20, 0, 0, TimeSpan.Zero); // 01:00 on the 14th in Karachi
        var reading = ScheduleText.Read("tomorrow 09:00", lateEvening, karachi);
        Assert.Equal(new DateTime(2026, 10, 15, 9, 0, 0), reading.Schedule!.At);
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(31, "31st")]
    public void Days_of_the_month_have_their_ordinals(int day, string said) => Assert.Equal(said, ScheduleText.Ordinal(day));
}
