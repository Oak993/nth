using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-10 (DESIGN.md rev 5, 7.2 "DST-safe"; ED-NEW-dextra-as10-2): across a clock change a daily run keeps its wall-clock
// meaning. The skipped hour of spring forward (2026-03-08, 02:00 to 03:00 in New York) runs the same day at once in the offset
// the day started in; the repeated hour of fall back (2026-11-01) runs on its first occurrence only; an Every schedule is real
// minutes and ignores the change.
[Trait("Feature", "AS-10")]
[Trait("Decision", "ED-NEW-dextra-as10-2")]
public sealed class ScheduleMathDstTests
{
    private static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    private static DateTimeOffset At(string iso) => DateTimeOffset.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    private static DateTimeOffset? Next(TimeSchedule schedule, string after) =>
        ScheduleMath.Next(schedule, NewYork, At(after), At("2026-01-01T00:00:00+00:00"));

    [Fact]
    public void A_daily_run_before_the_change_and_after_it_stays_at_the_same_wall_clock_time()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(9, 0));
        // 09:00 EST (UTC-5) on the 7th; 09:00 EDT (UTC-4) on the 9th: the hours in UTC differ, the wall clock does not.
        Assert.Equal(At("2026-03-07T09:00:00-05:00"), Next(daily, "2026-03-06T12:00:00-05:00"));
        Assert.Equal(At("2026-03-09T09:00:00-04:00"), Next(daily, "2026-03-08T12:00:00-04:00"));
    }

    [Fact]
    public void A_time_the_spring_forward_skips_runs_the_same_day_in_the_offset_the_day_started_in()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(2, 30));
        var next = Next(daily, "2026-03-07T12:00:00-05:00");
        // 02:30 does not exist on 2026-03-08. It runs once that day, at 03:30 EDT (07:30 UTC), not the next day and not twice.
        Assert.Equal(At("2026-03-08T07:30:00+00:00"), next);
        Assert.Equal(At("2026-03-09T02:30:00-04:00"), Next(daily, "2026-03-08T07:30:00+00:00"));
    }

    [Fact]
    public void A_time_the_fall_back_repeats_runs_on_its_first_occurrence_only()
    {
        var daily = TimeSchedule.Daily(new TimeOnly(1, 30));
        // 01:30 happens twice on 2026-11-01: 05:30 UTC (EDT) and 06:30 UTC (EST). The first one runs; the second does not.
        var first = Next(daily, "2026-10-31T12:00:00-04:00");
        Assert.Equal(At("2026-11-01T05:30:00+00:00"), first);
        Assert.Equal(At("2026-11-02T01:30:00-05:00"), Next(daily, "2026-11-01T05:30:00+00:00"));
    }

    [Fact]
    public void An_hourly_cron_line_runs_once_in_the_repeated_hour()
    {
        var hourly = TimeSchedule.FromCron("30 * * * *");
        var runs = new List<DateTimeOffset>();
        var at = At("2026-11-01T04:00:00+00:00"); // 00:00 EDT
        while (Next(hourly, at.ToString("o", System.Globalization.CultureInfo.InvariantCulture)) is { } next && next < At("2026-11-01T09:00:00+00:00"))
        {
            runs.Add(next);
            at = next;
        }
        // 00:30, 01:30 (first), 02:30 EST, 03:30 EST: the repeated 01:30 does not run twice.
        Assert.Equal([At("2026-11-01T04:30:00+00:00"), At("2026-11-01T05:30:00+00:00"), At("2026-11-01T07:30:00+00:00"), At("2026-11-01T08:30:00+00:00")], runs);
    }

    [Fact]
    public void An_every_schedule_counts_real_minutes_through_both_changes()
    {
        var every = TimeSchedule.Every(60);
        var anchor = At("2026-03-08T00:00:00+00:00");
        Assert.Equal(At("2026-03-08T07:00:00+00:00"), ScheduleMath.Next(every, NewYork, At("2026-03-08T06:30:00+00:00"), anchor)); // 02:00 EST jumps to 03:00 EDT: the hour is an hour of the clock
        var fall = At("2026-11-01T00:00:00+00:00");
        Assert.Equal(At("2026-11-01T06:00:00+00:00"), ScheduleMath.Next(every, NewYork, At("2026-11-01T05:30:00+00:00"), fall));
    }

    [Fact]
    public void A_once_run_in_the_skipped_hour_runs_at_the_shifted_time()
    {
        var once = TimeSchedule.Once(new DateTime(2026, 3, 8, 2, 15, 0));
        Assert.Equal(At("2026-03-08T07:15:00+00:00"), Next(once, "2026-03-08T00:00:00+00:00"));
    }
}
