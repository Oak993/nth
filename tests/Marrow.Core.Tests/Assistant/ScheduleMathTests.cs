using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.1, 7.2; ED-NEW-dextra-as10-2): the next run of a time schedule, from a moment the test passes
// in. Daily, weekdays, weekly and monthly are wall-clock times of the duty's zone; Every counts real minutes on a grid anchored
// at the duty's start; Once is one moment; a run owed several times over is owed once.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-2")]
public sealed class ScheduleMathTests
{
    private static readonly TimeZoneInfo Karachi = TimeZoneInfo.FindSystemTimeZoneById("Pakistan Standard Time");
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? Next(TimeSchedule schedule, string after, TimeZoneInfo? zone = null, string anchor = "2026-01-01T00:00:00+00:00") =>
        ScheduleMath.Next(schedule, zone ?? Utc, At(after), At(anchor));

    [Fact]
    public void A_daily_run_is_the_next_time_of_day_strictly_after_the_moment()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(8, 0));
        Assert.Equal(At("2026-10-13T08:00:00+00:00"), Next(daily, "2026-10-13T07:59:59+00:00"));
        Assert.Equal(At("2026-10-14T08:00:00+00:00"), Next(daily, "2026-10-13T08:00:00+00:00"));
        Assert.Equal(At("2026-10-14T08:00:00+00:00"), Next(daily, "2026-10-13T20:00:00+00:00"));
    }

    [Fact]
    public void A_daily_run_is_read_in_the_duty_zone_not_in_utc()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(8, 0));
        // Karachi is UTC+5 all year: 08:00 there is 03:00 UTC.
        Assert.Equal(At("2026-10-13T03:00:00+00:00"), Next(daily, "2026-10-13T02:00:00+00:00", Karachi));
        Assert.Equal(At("2026-10-14T03:00:00+00:00"), Next(daily, "2026-10-13T03:00:00+00:00", Karachi));
    }

    [Fact]
    public void Weekdays_skip_saturday_and_sunday()
    {
        var weekdays = TimeSchedule.OnWeekdays(new TimeOnly(9, 0));
        // 2026-10-16 is a Friday.
        Assert.Equal(At("2026-10-19T09:00:00+00:00"), Next(weekdays, "2026-10-16T09:00:00+00:00")); // Monday
        Assert.Equal(At("2026-10-19T09:00:00+00:00"), Next(weekdays, "2026-10-17T12:00:00+00:00")); // from a Saturday
        Assert.Equal(At("2026-10-16T09:00:00+00:00"), Next(weekdays, "2026-10-16T08:00:00+00:00"));
    }

    [Fact]
    public void A_weekly_run_picks_the_chosen_days()
    {
        var weekly = TimeSchedule.Weekly(WeekDays.Monday | WeekDays.Thursday, new TimeOnly(18, 30));
        Assert.Equal(At("2026-10-15T18:30:00+00:00"), Next(weekly, "2026-10-13T00:00:00+00:00")); // Tuesday -> Thursday
        Assert.Equal(At("2026-10-19T18:30:00+00:00"), Next(weekly, "2026-10-15T18:30:00+00:00")); // Thursday -> Monday
    }

    [Fact]
    public void A_monthly_run_on_a_day_a_month_lacks_runs_on_that_months_last_day()
    {
        var monthly = TimeSchedule.Monthly(31, new TimeOnly(9, 0));
        Assert.Equal(At("2026-10-31T09:00:00+00:00"), Next(monthly, "2026-10-13T00:00:00+00:00"));
        Assert.Equal(At("2026-11-30T09:00:00+00:00"), Next(monthly, "2026-10-31T09:00:00+00:00"));
        Assert.Equal(At("2027-02-28T09:00:00+00:00"), Next(monthly, "2027-01-31T09:00:00+00:00"));
        Assert.Equal(At("2028-02-29T09:00:00+00:00"), Next(monthly, "2028-01-31T09:00:00+00:00")); // a leap year
    }

    [Fact]
    public void An_every_schedule_counts_real_minutes_on_a_grid_from_its_anchor()
    {
        var every = TimeSchedule.Every(30);
        var anchor = "2026-10-13T10:07:00+00:00";
        Assert.Equal(At("2026-10-13T10:37:00+00:00"), Next(every, "2026-10-13T10:07:00+00:00", anchor: anchor)); // the first run is one step after the start
        Assert.Equal(At("2026-10-13T11:07:00+00:00"), Next(every, "2026-10-13T10:37:00+00:00", anchor: anchor));
        Assert.Equal(At("2026-10-13T12:07:00+00:00"), Next(every, "2026-10-13T11:40:00+00:00", anchor: anchor)); // between two grid points: the later one
        Assert.Equal(At("2026-10-13T10:37:00+00:00"), Next(every, "2026-10-13T09:00:00+00:00", anchor: anchor)); // before the anchor
    }

    [Fact]
    public void A_once_schedule_is_one_moment_and_nothing_after_it()
    {
        var once = TimeSchedule.Once(new DateTime(2026, 11, 1, 9, 0, 0));
        Assert.Equal(At("2026-11-01T09:00:00+00:00"), Next(once, "2026-10-13T00:00:00+00:00"));
        Assert.Null(Next(once, "2026-11-01T09:00:00+00:00"));
        Assert.Null(Next(once, "2026-12-01T00:00:00+00:00"));
    }

    [Fact]
    public void A_cron_line_runs_at_the_minutes_it_names()
    {
        var cron = TimeSchedule.FromCron("0 9 * * 1-5");
        Assert.Equal(At("2026-10-19T09:00:00+00:00"), Next(cron, "2026-10-16T09:00:00+00:00"));
        var quarter = TimeSchedule.FromCron("*/15 * * * *");
        Assert.Equal(At("2026-10-13T10:15:00+00:00"), Next(quarter, "2026-10-13T10:07:00+00:00"));
        Assert.Equal(At("2026-10-13T11:00:00+00:00"), Next(quarter, "2026-10-13T10:45:00+00:00"));
    }

    [Fact]
    public void A_cron_line_that_names_a_day_that_never_comes_has_no_next_run()
    {
        Assert.Null(Next(TimeSchedule.FromCron("0 0 31 2 *"), "2026-10-13T00:00:00+00:00"));
        Assert.Null(Next(TimeSchedule.FromCron("not a line"), "2026-10-13T00:00:00+00:00"));
    }

    [Fact]
    public void Several_missed_runs_are_owed_as_one()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(8, 0));
        var anchor = At("2026-10-01T00:00:00+00:00");
        // nth was closed from the 10th to the 13th: four 08:00s passed, one run is owed.
        Assert.True(ScheduleMath.Missed(daily, Utc, At("2026-10-10T08:00:30+00:00"), At("2026-10-13T09:00:00+00:00"), anchor));
        Assert.False(ScheduleMath.Missed(daily, Utc, At("2026-10-13T08:00:30+00:00"), At("2026-10-13T09:00:00+00:00"), anchor));
        Assert.False(ScheduleMath.Missed(daily, Utc, At("2026-10-13T08:00:30+00:00"), At("2026-10-14T07:59:59+00:00"), anchor));
    }

    [Fact]
    public void A_schedule_states_what_is_wrong_with_it()
    {
        Assert.NotNull(TimeSchedule.Every(4).Problem());
        Assert.Null(TimeSchedule.Every(5).Problem());
        Assert.NotNull(TimeSchedule.Weekly(WeekDays.None, new TimeOnly(9, 0)).Problem());
        Assert.NotNull(TimeSchedule.Monthly(32, new TimeOnly(9, 0)).Problem());
        Assert.NotNull(TimeSchedule.FromCron("1 2 3").Problem());
        Assert.Null(TimeSchedule.Daily(new TimeOnly(0, 0)).Problem());
    }
}
