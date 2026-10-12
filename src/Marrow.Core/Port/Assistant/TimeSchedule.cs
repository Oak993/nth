// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2 "Time"; unit AS-10; ED-NEW-dextra-as10-1). No Swift twin.
// When a time duty runs: once, every N minutes (at least 5), daily, weekdays, weekly, monthly, or an advanced cron line.
// Pure data. ScheduleMath turns it into the next instant on a clock the caller passes, in the duty's time zone.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public enum ScheduleKind { Once, Every, Daily, Weekdays, Weekly, Monthly, Cron }

/// <summary>The days of a week a weekly schedule or active hours cover (a set; Sunday is bit 0, like <see cref="DayOfWeek"/>).</summary>
[Flags]
public enum WeekDays : byte
{
    None = 0,
    Sunday = 1,
    Monday = 2,
    Tuesday = 4,
    Wednesday = 8,
    Thursday = 16,
    Friday = 32,
    Saturday = 64,
    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    All = 127,
}

public static class WeekDaySets
{
    public static WeekDays Of(DayOfWeek day) => (WeekDays)(1 << (int)day);

    public static bool Has(this WeekDays set, DayOfWeek day) => (set & Of(day)) != WeekDays.None;
}

/// <summary>
/// A time trigger's schedule. Calendar kinds (Daily, Weekdays, Weekly, Monthly, Cron) read their times as the wall clock of the
/// duty's zone, so "09:00" stays 09:00 across a clock change. <see cref="ScheduleKind.Every"/> counts real minutes from the
/// duty's anchor, and <see cref="ScheduleKind.Once"/> names one wall-clock moment.
/// </summary>
public sealed record TimeSchedule(ScheduleKind Kind)
{
    /// <summary>The fewest minutes between two runs of an Every schedule (7.2).</summary>
    public const int MinimumEveryMinutes = 5;

    /// <summary>The longest Every schedule: a year.</summary>
    public const int MaximumEveryMinutes = 366 * 24 * 60;

    /// <summary>Once: the wall-clock moment, in the duty's zone.</summary>
    public DateTime? At { get; init; }

    /// <summary>Every: the minutes between two runs.</summary>
    public int EveryMinutes { get; init; }

    /// <summary>Daily, Weekdays, Weekly, Monthly: the wall-clock time of day.</summary>
    public TimeOnly TimeOfDay { get; init; }

    /// <summary>Weekly: the days it runs on.</summary>
    public WeekDays Days { get; init; }

    /// <summary>Monthly: the day of the month, 1 to 31. A month without that day runs on its last day.</summary>
    public int DayOfMonth { get; init; } = 1;

    /// <summary>Cron: five fields, minute hour day-of-month month day-of-week.</summary>
    public string? Cron { get; init; }

    public static TimeSchedule Once(DateTime wallClock) => new(ScheduleKind.Once) { At = DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified) };

    public static TimeSchedule Every(int minutes) => new(ScheduleKind.Every) { EveryMinutes = minutes };

    public static TimeSchedule Daily(TimeOnly time) => new(ScheduleKind.Daily) { TimeOfDay = time };

    public static TimeSchedule OnWeekdays(TimeOnly time) => new(ScheduleKind.Weekdays) { TimeOfDay = time };

    public static TimeSchedule Weekly(WeekDays days, TimeOnly time) => new(ScheduleKind.Weekly) { Days = days, TimeOfDay = time };

    public static TimeSchedule Monthly(int day, TimeOnly time) => new(ScheduleKind.Monthly) { DayOfMonth = day, TimeOfDay = time };

    public static TimeSchedule FromCron(string line) => new(ScheduleKind.Cron) { Cron = line };

    /// <summary>Null when the schedule can run; else why not (shown on the card before the duty is saved).</summary>
    public string? Problem()
    {
        switch (Kind)
        {
            case ScheduleKind.Once:
                return At is null ? "Say when it runs." : null;
            case ScheduleKind.Every:
                if (EveryMinutes < MinimumEveryMinutes) return $"Every N minutes needs {MinimumEveryMinutes} or more, so a duty cannot run away with the allowance.";
                return EveryMinutes > MaximumEveryMinutes ? "Every N minutes is a year at most." : null;
            case ScheduleKind.Weekly:
                return Days == WeekDays.None ? "Pick at least one day." : null;
            case ScheduleKind.Monthly:
                return DayOfMonth is < 1 or > 31 ? "The day of the month is 1 to 31." : null;
            case ScheduleKind.Cron:
                return CronLine.Parse(Cron).Problem;
            default:
                return null;
        }
    }
}
