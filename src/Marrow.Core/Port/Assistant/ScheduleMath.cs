// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2 "Time zone stored; DST-safe; next run shown", 7.1 missed runs; unit AS-10;
// ED-NEW-dextra-as10-2). No Swift twin.
// The next instant a time schedule runs after a moment, in a zone, with no clock of its own (the caller passes "now"), so a
// manual clock proves it. Calendar schedules are wall-clock times of the zone: a time the clocks skip (spring forward) runs
// at once in the same offset the day started in (02:30 becomes 03:30); a time they repeat (fall back) runs on its first
// occurrence only. Every-N schedules count real minutes on a grid anchored at the duty's start.
namespace Marrow.Core.Port.Assistant;

public static class ScheduleMath
{
    private const int SearchDays = 366 * 6;

    /// <summary>
    /// The first run strictly after <paramref name="after"/>, or null when there is none (a Once in the past, a cron line that
    /// names a day that never comes). <paramref name="anchor"/> is where an Every schedule's grid starts.
    /// </summary>
    public static DateTimeOffset? Next(TimeSchedule schedule, TimeZoneInfo zone, DateTimeOffset after, DateTimeOffset anchor)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(zone);
        switch (schedule.Kind)
        {
            case ScheduleKind.Once:
            {
                if (schedule.At is not { } at) return null;
                var moment = Resolve(at, zone);
                return moment > after ? moment : null;
            }
            case ScheduleKind.Every:
            {
                var minutes = Math.Max(schedule.EveryMinutes, 1);
                var step = TimeSpan.FromMinutes(minutes);
                if (after < anchor) return anchor + step;
                var passed = (after - anchor).Ticks / step.Ticks;
                return anchor + (step * (passed + 1));
            }
            case ScheduleKind.Cron:
            {
                var cron = CronLine.Parse(schedule.Cron);
                return cron.IsValid ? NextByDay(zone, after, date => cron.MatchesDay(date) ? cron.TimesOfDay() : []) : null;
            }
            default:
                return NextByDay(zone, after, date => Matches(schedule, date) ? [schedule.TimeOfDay] : []);
        }
    }

    /// <summary>
    /// Whether a run is owed: the first scheduled moment after <paramref name="lastHandled"/> is not after <paramref name="now"/>.
    /// Several missed moments are one (the catch-up runs once, 7.1).
    /// </summary>
    public static bool Missed(TimeSchedule schedule, TimeZoneInfo zone, DateTimeOffset lastHandled, DateTimeOffset now, DateTimeOffset anchor) =>
        Next(schedule, zone, lastHandled, anchor) is { } due && due <= now;

    /// <summary>A wall-clock moment as an instant in the zone: see the class comment for the skipped and the repeated hour.</summary>
    public static DateTimeOffset Resolve(DateTime wallClock, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
        TimeSpan offset;
        if (zone.IsInvalidTime(local)) offset = zone.GetUtcOffset(local.AddDays(-1));
        else if (zone.IsAmbiguousTime(local)) offset = zone.GetAmbiguousTimeOffsets(local).Max();
        else offset = zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static bool Matches(TimeSchedule schedule, DateOnly date) => schedule.Kind switch
    {
        ScheduleKind.Daily => true,
        ScheduleKind.Weekdays => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
        ScheduleKind.Weekly => schedule.Days.Has(date.DayOfWeek),
        ScheduleKind.Monthly => date.Day == Math.Min(Math.Clamp(schedule.DayOfMonth, 1, 31), DateTime.DaysInMonth(date.Year, date.Month)),
        _ => false,
    };

    // The earliest instant after `after` among the times each matching day offers. Days are visited in order, so the first
    // day that offers a moment after `after` holds the answer (a skipped hour only moves a time later within its own day).
    private static DateTimeOffset? NextByDay(TimeZoneInfo zone, DateTimeOffset after, Func<DateOnly, IEnumerable<TimeOnly>> timesOf)
    {
        var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime).AddDays(-1);
        for (var offset = 0; offset < SearchDays; offset++)
        {
            var date = start.AddDays(offset);
            DateTimeOffset? best = null;
            foreach (var time in timesOf(date))
            {
                var moment = Resolve(date.ToDateTime(time), zone);
                if (moment > after && (best is null || moment < best)) best = moment;
            }
            if (best is not null) return best;
        }
        return null;
    }
}
