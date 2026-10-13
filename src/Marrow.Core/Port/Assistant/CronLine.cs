// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2 "advanced cron"; unit AS-10; ED-NEW-dextra-as10-1). No Swift twin.
// A five-field cron line: minute hour day-of-month month day-of-week. Lists (1,15), ranges (1-5), steps (*/10, 8-18/2),
// month and weekday names, 7 as Sunday, and the @hourly @daily @weekly @monthly shortcuts. When both the day-of-month and
// the day-of-week field are restricted a day matches if either does (the usual cron rule). Read as the wall clock of the
// duty's zone; ScheduleMath resolves a time that a clock change skipped or repeated.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public sealed record CronLine
{
    private CronLine(bool[] minutes, bool[] hours, bool[] daysOfMonth, bool[] months, bool[] daysOfWeek, bool anyDayOfMonth, bool anyDayOfWeek)
    {
        Minutes = minutes;
        Hours = hours;
        DaysOfMonth = daysOfMonth;
        Months = months;
        DaysOfWeek = daysOfWeek;
        AnyDayOfMonth = anyDayOfMonth;
        AnyDayOfWeek = anyDayOfWeek;
    }

    private bool[] Minutes { get; }

    private bool[] Hours { get; }

    private bool[] DaysOfMonth { get; }

    private bool[] Months { get; }

    private bool[] DaysOfWeek { get; }

    private bool AnyDayOfMonth { get; }

    private bool AnyDayOfWeek { get; }

    /// <summary>The line's problem, in the owner's words; null when it parsed.</summary>
    public string? Problem { get; private init; }

    public bool IsValid => Problem is null;

    private static readonly string[] MonthNames = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];
    private static readonly string[] DayNames = ["sun", "mon", "tue", "wed", "thu", "fri", "sat"];

    private static CronLine Failed(string problem) =>
        new([], [], [], [], [], false, false) { Problem = problem };

    public static CronLine Parse(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return Failed("A cron line has five fields: minute hour day-of-month month day-of-week.");
        var text = line.Trim();
        if (text.Length > 200) return Failed("That cron line is too long.");
        text = text.ToLowerInvariant() switch
        {
            "@hourly" => "0 * * * *",
            "@daily" or "@midnight" => "0 0 * * *",
            "@weekly" => "0 0 * * 0",
            "@monthly" => "0 0 1 * *",
            _ => text,
        };
        var fields = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5) return Failed("A cron line has five fields: minute hour day-of-month month day-of-week.");
        var minutes = Field(fields[0], 0, 59, null, "minute", out var problem);
        if (problem is not null) return Failed(problem);
        var hours = Field(fields[1], 0, 23, null, "hour", out problem);
        if (problem is not null) return Failed(problem);
        var dom = Field(fields[2], 1, 31, null, "day of the month", out problem);
        if (problem is not null) return Failed(problem);
        var months = Field(fields[3], 1, 12, MonthNames, "month", out problem);
        if (problem is not null) return Failed(problem);
        var dow = Field(fields[4], 0, 7, DayNames, "day of the week", out problem);
        if (problem is not null) return Failed(problem);
        if (dow[7]) dow[0] = true;
        return new CronLine(minutes, hours, dom, months, dow[..7], IsAny(fields[2]), IsAny(fields[4]));
    }

    private static bool IsAny(string field) => field.StartsWith('*') && !field.Contains(',', StringComparison.Ordinal);

    // The set a field names, indexed by value. `names` maps the 1-based (months) or 0-based (weekdays) words.
    private static bool[] Field(string field, int low, int high, string[]? names, string what, out string? problem)
    {
        problem = null;
        var set = new bool[high + 1];
        foreach (var part in field.Split(','))
        {
            var step = 1;
            var range = part;
            var slash = part.IndexOf('/', StringComparison.Ordinal);
            if (slash >= 0)
            {
                if (!int.TryParse(part[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out step) || step < 1)
                {
                    problem = $"The step in the {what} field is not a number.";
                    return set;
                }
                range = part[..slash];
            }
            int from;
            int to;
            if (range == "*")
            {
                from = low;
                to = names == DayNames ? 6 : high;
            }
            else
            {
                var dash = range.IndexOf('-', StringComparison.Ordinal);
                if (dash < 0)
                {
                    if (!Value(range, low, high, names, out from)) { problem = $"“{range}” is not a valid {what}."; return set; }
                    to = slash >= 0 ? high : from;
                }
                else if (!Value(range[..dash], low, high, names, out from) || !Value(range[(dash + 1)..], low, high, names, out to) || to < from)
                {
                    problem = $"“{range}” is not a valid {what} range.";
                    return set;
                }
            }
            for (var value = from; value <= to; value += step) set[value] = true;
        }
        return set;
    }

    private static bool Value(string text, int low, int high, string[]? names, out int value)
    {
        if (names is not null)
        {
            var index = Array.IndexOf(names, text.ToLowerInvariant());
            if (index >= 0)
            {
                value = names == MonthNames ? index + 1 : index;
                return true;
            }
        }
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= low && value <= high;
    }

    /// <summary>Whether the wall-clock day matches the day-of-month, month and day-of-week fields.</summary>
    public bool MatchesDay(DateOnly date)
    {
        if (!IsValid || !Months[date.Month]) return false;
        var domMatch = DaysOfMonth[date.Day];
        var dowMatch = DaysOfWeek[(int)date.DayOfWeek];
        if (AnyDayOfMonth && AnyDayOfWeek) return true;
        if (AnyDayOfMonth) return dowMatch;
        if (AnyDayOfWeek) return domMatch;
        return domMatch || dowMatch;
    }

    /// <summary>The times of one matching day, in order.</summary>
    public IEnumerable<TimeOnly> TimesOfDay()
    {
        if (!IsValid) yield break;
        for (var hour = 0; hour < 24; hour++)
        {
            if (!Hours[hour]) continue;
            for (var minute = 0; minute < 60; minute++)
                if (Minutes[minute]) yield return new TimeOnly(hour, minute);
        }
    }
}
