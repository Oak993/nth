using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1 "active hours"; ED-NEW-dextra-as10-3): the hours a duty may run in, read in its zone. A run
// that comes due outside them waits for the next opening. From after To runs overnight on the days it opens on.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-3")]
public sealed class ActiveHoursTests
{
    private static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Working_hours_on_weekdays_contain_a_tuesday_morning_in_the_zone_and_not_a_saturday()
    {
        var hours = ActiveHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0));
        Assert.True(hours.Contains(At("2026-10-13T10:00:00+05:00"), Karachi));
        Assert.True(hours.Contains(At("2026-10-13T09:00:00+05:00"), Karachi));
        Assert.False(hours.Contains(At("2026-10-13T17:00:00+05:00"), Karachi));
        Assert.False(hours.Contains(At("2026-10-13T08:59:00+05:00"), Karachi));
        Assert.False(hours.Contains(At("2026-10-17T10:00:00+05:00"), Karachi));
        Assert.True(hours.Contains(At("2026-10-13T05:30:00+00:00"), Karachi)); // 10:30 in Karachi
    }

    [Fact]
    public void The_next_opening_is_today_tomorrow_or_after_the_weekend()
    {
        var hours = ActiveHours.Weekdays(new TimeOnly(9, 0), new TimeOnly(17, 0));
        Assert.Equal(At("2026-10-13T09:00:00+05:00"), hours.NextOpen(At("2026-10-13T06:00:00+05:00"), Karachi));
        Assert.Equal(At("2026-10-14T09:00:00+05:00"), hours.NextOpen(At("2026-10-13T18:00:00+05:00"), Karachi));
        Assert.Equal(At("2026-10-19T09:00:00+05:00"), hours.NextOpen(At("2026-10-16T18:00:00+05:00"), Karachi)); // Friday evening -> Monday
        Assert.Equal(At("2026-10-13T12:00:00+05:00"), hours.NextOpen(At("2026-10-13T12:00:00+05:00"), Karachi)); // already inside
    }

    [Fact]
    public void Overnight_hours_belong_to_the_day_they_open_on()
    {
        var night = new ActiveHours(new TimeOnly(22, 0), new TimeOnly(6, 0), WeekDays.Friday);
        Assert.True(night.Contains(At("2026-10-16T23:00:00+05:00"), Karachi)); // Friday night
        Assert.True(night.Contains(At("2026-10-17T05:00:00+05:00"), Karachi)); // still that night, Saturday morning
        Assert.False(night.Contains(At("2026-10-17T23:00:00+05:00"), Karachi)); // Saturday night: it opens on Fridays
        Assert.False(night.Contains(At("2026-10-16T05:00:00+05:00"), Karachi)); // Friday morning: Thursday night did not open
    }

    [Fact]
    public void Equal_ends_mean_the_whole_day_and_no_day_means_never()
    {
        var all = new ActiveHours(new TimeOnly(0, 0), new TimeOnly(0, 0), WeekDays.All);
        Assert.True(all.Contains(At("2026-10-17T03:00:00+05:00"), Karachi));
        Assert.Equal("All day", all.ToString());
        var none = new ActiveHours(new TimeOnly(9, 0), new TimeOnly(17, 0), WeekDays.None);
        Assert.False(none.Contains(At("2026-10-13T10:00:00+05:00"), Karachi));
        Assert.Null(none.NextOpen(At("2026-10-13T10:00:00+05:00"), Karachi));
    }
}
