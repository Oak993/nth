// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.1 "active hours"; unit AS-10; ED-NEW-dextra-as10-3). No Swift twin.
// The hours of the day, on some days, in the duty's zone, in which a duty may run. A run that comes due outside them waits
// for the next opening and then runs once. From equal to To is the whole day; From after To runs overnight, and its days are
// the days it opens on.
namespace Marrow.Core.Port.Assistant;

public sealed record ActiveHours(TimeOnly From, TimeOnly To, WeekDays Days)
{
    public static ActiveHours Weekdays(TimeOnly from, TimeOnly to) => new(from, to, WeekDays.Weekdays);

    /// <summary>Whether <paramref name="moment"/> is inside the hours, read in <paramref name="zone"/>.</summary>
    public bool Contains(DateTimeOffset moment, TimeZoneInfo zone)
    {
        if (Days == WeekDays.None) return false;
        var local = TimeZoneInfo.ConvertTime(moment, zone).DateTime;
        var time = TimeOnly.FromDateTime(local);
        if (From == To) return Days.Has(local.DayOfWeek);
        if (From < To) return Days.Has(local.DayOfWeek) && time >= From && time < To;
        if (time >= From) return Days.Has(local.DayOfWeek);
        return time < To && Days.Has(local.AddDays(-1).DayOfWeek);
    }

    /// <summary>The first moment at or after <paramref name="after"/> inside the hours; null when no day is chosen.</summary>
    public DateTimeOffset? NextOpen(DateTimeOffset after, TimeZoneInfo zone)
    {
        if (Days == WeekDays.None) return null;
        if (Contains(after, zone)) return after;
        var start = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        for (var offset = 0; offset <= 8; offset++)
        {
            var date = start.AddDays(offset);
            if (!Days.Has(date.DayOfWeek)) continue;
            var opens = ScheduleMath.Resolve(date.ToDateTime(From), zone);
            if (opens > after) return opens;
        }
        return null;
    }

    public override string ToString() =>
        From == To ? "All day" : $"{From.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)} to {To.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)}";
}
