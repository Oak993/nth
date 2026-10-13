// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.2; the schedule_duty tool's "weekdays 08:00"; unit AS-10; ED-NEW-dextra-as10-1). No Swift twin.
// Reads the words an agent or the owner gives for a schedule ("weekdays 08:00", "every 30 minutes", "weekly mon,wed 18:00",
// "monthly 15 09:00", "once 2026-11-01 09:00", "in 2 hours", "cron 0 9 * * 1-5") into a TimeSchedule, and says a schedule back
// in the same plain words for the card ("Every day at 08:00"). Pure.
using System.Globalization;
using System.Text.RegularExpressions;

namespace Marrow.Core.Port.Assistant;

public sealed record ScheduleReading(TimeSchedule? Schedule, string? Problem)
{
    public static ScheduleReading Made(TimeSchedule schedule) => schedule.Problem() is { } problem ? new(null, problem) : new(schedule, null);

    public static ScheduleReading Unreadable(string problem) => new(null, problem);
}

public static partial class ScheduleText
{
    private const string Hint = "Say it like “weekdays 08:00”, “daily 18:30”, “weekly mon,thu 09:00”, “monthly 1 09:00”, “every 30 minutes”, “once 2026-11-01 09:00” or “cron 0 9 * * 1-5”.";

    [GeneratedRegex(@"^(?<h>\d{1,2})(?::(?<m>\d{2}))?\s*(?<ap>am|pm)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockPattern();

    [GeneratedRegex(@"^(?:every|each)\s+(?<n>\d{1,6})\s*(?<u>m|min|mins|minute|minutes|h|hr|hrs|hour|hours|d|day|days)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EveryPattern();

    [GeneratedRegex(@"^in\s+(?<n>\d{1,6})\s*(?<u>m|min|mins|minute|minutes|h|hr|hrs|hour|hours)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InPattern();

    /// <summary>Reads <paramref name="text"/>. <paramref name="now"/> and <paramref name="zone"/> are for "in 2 hours" and "tomorrow".</summary>
    public static ScheduleReading Read(string? text, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (string.IsNullOrWhiteSpace(text)) return ScheduleReading.Unreadable("Say when it runs. " + Hint);
        var words = Regex.Replace(text.Trim(), @"\s+", " ");
        if (words.Length > 200) return ScheduleReading.Unreadable("That is too long for a schedule. " + Hint);
        var lower = words.ToLowerInvariant();

        if (lower.StartsWith("cron", StringComparison.Ordinal) || lower.StartsWith('@') || LooksLikeCron(lower))
        {
            var line = lower.StartsWith("cron", StringComparison.Ordinal) ? words[4..].TrimStart(':', ' ') : words;
            return ScheduleReading.Made(TimeSchedule.FromCron(line));
        }
        if (EveryPattern().Match(lower) is { Success: true } every)
        {
            var count = int.Parse(every.Groups["n"].Value, CultureInfo.InvariantCulture);
            var unit = every.Groups["u"].Value[0];
            long minutes = unit switch { 'h' => count * 60L, 'd' => count * 1440L, _ => count };
            return minutes > TimeSchedule.MaximumEveryMinutes ? ScheduleReading.Unreadable("Every N minutes is a year at most.") : ScheduleReading.Made(TimeSchedule.Every((int)minutes));
        }
        if (InPattern().Match(lower) is { Success: true } soon)
        {
            var count = int.Parse(soon.Groups["n"].Value, CultureInfo.InvariantCulture);
            var minutes = soon.Groups["u"].Value[0] == 'h' ? count * 60L : count;
            var local = TimeZoneInfo.ConvertTime(now, zone).DateTime.AddMinutes(minutes);
            return ScheduleReading.Made(TimeSchedule.Once(new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Unspecified)));
        }

        var parts = lower.Replace(" at ", " ", StringComparison.Ordinal).Replace("every ", "", StringComparison.Ordinal).Split(' ');
        var head = parts[0];
        switch (head)
        {
            case "daily" or "day":
            case "weekday" or "weekdays":
                return Timed(head.StartsWith('w') ? ScheduleKind.Weekdays : ScheduleKind.Daily, string.Join(' ', parts.Skip(1)));
            case "once" or "on":
                return OnceReading(parts.Skip(1).ToArray(), now, zone);
            case "weekly" or "week":
                return Weekly(parts.Skip(1).ToArray());
            case "monthly" or "month":
                return Monthly(parts.Skip(1).ToArray());
        }
        if (Days(head) != WeekDays.None) return Weekly(parts);
        if (head is "tomorrow" or "today") return OnceReading(parts, now, zone);
        return ScheduleReading.Unreadable($"“{words}” is not a schedule nth reads. " + Hint);
    }

    private static bool LooksLikeCron(string lower)
    {
        var fields = lower.Split(' ');
        return fields.Length == 5 && fields.All(f => f.Length > 0 && f.All(c => char.IsAsciiDigit(c) || c is '*' or '/' or ',' or '-'));
    }

    private static ScheduleReading Timed(ScheduleKind kind, string clock)
    {
        if (!Clock(clock, out var time)) return ScheduleReading.Unreadable("Give the time of day, like 08:00 or 6:30pm. " + Hint);
        return ScheduleReading.Made(kind == ScheduleKind.Weekdays ? TimeSchedule.OnWeekdays(time) : TimeSchedule.Daily(time));
    }

    private static ScheduleReading Weekly(string[] parts)
    {
        if (parts.Length == 0) return ScheduleReading.Unreadable("Name the days, like “weekly mon,thu 09:00”.");
        var days = WeekDays.None;
        var rest = new List<string>();
        foreach (var part in parts)
        {
            var named = Days(part);
            if (named == WeekDays.None) rest.Add(part);
            else days |= named;
        }
        if (days == WeekDays.None) return ScheduleReading.Unreadable("Name the days, like “weekly mon,thu 09:00”.");
        if (!Clock(string.Join(' ', rest), out var time)) return ScheduleReading.Unreadable("Give the time of day, like 08:00 or 6:30pm. " + Hint);
        return ScheduleReading.Made(TimeSchedule.Weekly(days, time));
    }

    private static ScheduleReading Monthly(string[] parts)
    {
        var rest = parts.Where(p => p is not ("on" or "the" or "day")).ToArray();
        if (rest.Length == 0) return ScheduleReading.Unreadable("Name the day of the month, like “monthly 15 09:00”.");
        var dayText = Regex.Replace(rest[0], "(st|nd|rd|th)$", "", RegexOptions.CultureInvariant);
        if (!int.TryParse(dayText, NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day is < 1 or > 31)
            return ScheduleReading.Unreadable("The day of the month is 1 to 31.");
        if (!Clock(string.Join(' ', rest.Skip(1)), out var time)) return ScheduleReading.Unreadable("Give the time of day, like 08:00 or 6:30pm. " + Hint);
        return ScheduleReading.Made(TimeSchedule.Monthly(day, time));
    }

    private static ScheduleReading OnceReading(string[] parts, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (parts.Length == 0) return ScheduleReading.Unreadable("Say when, like “once 2026-11-01 09:00”.");
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        DateOnly date;
        if (parts[0] == "today") date = today;
        else if (parts[0] == "tomorrow") date = today.AddDays(1);
        else if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return ScheduleReading.Unreadable("Say the date as 2026-11-01, or use today or tomorrow.");
        if (!Clock(string.Join(' ', parts.Skip(1)), out var time)) return ScheduleReading.Unreadable("Give the time of day, like 08:00 or 6:30pm. " + Hint);
        return ScheduleReading.Made(TimeSchedule.Once(date.ToDateTime(time)));
    }

    /// <summary>08:00, 8:30, 8am, 6:30pm, 18, 18h.</summary>
    private static bool Clock(string text, out TimeOnly time)
    {
        time = default;
        var match = ClockPattern().Match(text.Trim());
        if (!match.Success) return false;
        var hour = int.Parse(match.Groups["h"].Value, CultureInfo.InvariantCulture);
        var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        if (match.Groups["ap"].Success)
        {
            if (hour is < 1 or > 12) return false;
            hour = hour % 12 + (match.Groups["ap"].Value.Equals("pm", StringComparison.OrdinalIgnoreCase) ? 12 : 0);
        }
        if (hour > 23 || minute > 59) return false;
        time = new TimeOnly(hour, minute);
        return true;
    }

    private static WeekDays Days(string word)
    {
        var days = WeekDays.None;
        foreach (var piece in word.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var day = piece.TrimEnd('s') switch
            {
                "sun" or "sunday" => WeekDays.Sunday,
                "mon" or "monday" => WeekDays.Monday,
                "tue" or "tues" or "tuesday" => WeekDays.Tuesday,
                "wed" or "wednesday" => WeekDays.Wednesday,
                "thu" or "thur" or "thurs" or "thursday" => WeekDays.Thursday,
                "fri" or "friday" => WeekDays.Friday,
                "sat" or "saturday" => WeekDays.Saturday,
                _ => WeekDays.None,
            };
            if (day == WeekDays.None) return WeekDays.None;
            days |= day;
        }
        return days;
    }

    // ---- saying a schedule back ----

    /// <summary>The schedule in plain words: "Every day at 08:00", "Weekdays at 09:00", "Every 2 h".</summary>
    public static string Describe(TimeSchedule schedule)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        var at = schedule.TimeOfDay.ToString("HH:mm", CultureInfo.InvariantCulture);
        return schedule.Kind switch
        {
            ScheduleKind.Once => schedule.At is { } moment ? $"Once on {moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} at {moment.ToString("HH:mm", CultureInfo.InvariantCulture)}" : "Once",
            ScheduleKind.Every => EveryWords(schedule.EveryMinutes),
            ScheduleKind.Daily => $"Every day at {at}",
            ScheduleKind.Weekdays => $"Weekdays at {at}",
            ScheduleKind.Weekly => $"{DayWords(schedule.Days)} at {at}",
            ScheduleKind.Monthly => $"Monthly on the {Ordinal(schedule.DayOfMonth)} at {at}",
            ScheduleKind.Cron => "Cron " + schedule.Cron,
            _ => "",
        };
    }

    private static string EveryWords(int minutes)
    {
        if (minutes % 1440 == 0) return minutes == 1440 ? "Every day" : $"Every {(minutes / 1440).ToString(CultureInfo.InvariantCulture)} days";
        if (minutes % 60 == 0) return $"Every {(minutes / 60).ToString(CultureInfo.InvariantCulture)} h";
        return $"Every {minutes.ToString(CultureInfo.InvariantCulture)} min";
    }

    private static string DayWords(WeekDays days)
    {
        if (days == WeekDays.Weekdays) return "Weekdays";
        if (days == WeekDays.All) return "Every day";
        var names = new[] { "Sundays", "Mondays", "Tuesdays", "Wednesdays", "Thursdays", "Fridays", "Saturdays" };
        var set = Enumerable.Range(0, 7).Where(i => days.Has((DayOfWeek)i)).Select(i => names[i]).ToList();
        return set.Count switch
        {
            1 => set[0],
            2 => set[0] + " and " + set[1],
            _ => string.Join(", ", set.Take(set.Count - 1)) + " and " + set[^1],
        };
    }

    public static string Ordinal(int day)
    {
        var suffix = day % 100 is >= 11 and <= 13 ? "th" : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day.ToString(CultureInfo.InvariantCulture) + suffix;
    }
}
