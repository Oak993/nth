using System.Text.RegularExpressions;
using Marrow.Core.Port;

namespace Marrow.Services.Providers;

// Source: DroppyCode/Services/Providers/UsageLimitSignal.swift (B2 1fbf81e), :1-54.
public static partial class UsageLimitSignal
{
    // DEVIATION OD-0114 / ED-406: attribution is supplied by the session owner, not parsed from text.
    public sealed record AccountLimit(UsageAccountKey Account, DateTimeOffset? ResetsAt);

    public static AccountLimit? Detect(string text, UsageAccountKey account, DateTimeOffset? now = null) =>
        Matches(text) ? new(account, ResetTime(text, now)) : null;

    // :6-10
    public static bool Matches(string text)
    {
        var lower = SwiftString.Lowercased(text);
        return lower.Contains("usage limit", StringComparison.Ordinal)
            || lower.Contains("hit your limit", StringComparison.Ordinal)
            || lower.Contains("usage_limit", StringComparison.Ordinal)
            || lower.Contains("limit reached", StringComparison.Ordinal)
            || lower.Contains("out of extra usage", StringComparison.Ordinal);
    }

    // :16 the bar-then-digits timestamp pattern.
    [GeneratedRegex(@"\|\s*(\d{9,})", RegexOptions.CultureInvariant)]
    private static partial Regex TimestampPattern();

    // :23 the case-insensitive "resets 3pm" / "resets at 3:30 pm" clock pattern.
    [GeneratedRegex(@"resets?(?:\s+at)?\s+(\d{1,2})(?::(\d{2}))?\s*(am|pm)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ClockPattern();

    // :32 the parenthesised Area/Location IANA identifier, searched over the whole message.
    [GeneratedRegex(@"\(([A-Za-z_]+/[A-Za-z_]+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex ZonePattern();

    // :15-40
    public static DateTimeOffset? ResetTime(string text, DateTimeOffset? now = null)
    {
        var effectiveNow = now ?? SwiftDate.Now;

        // :16-22 the timestamp path is tried first and wins when both patterns are present.
        var timestamp = TimestampPattern().Match(text);
        if (timestamp.Success)
        {
            var digits = timestamp.Groups[1].Value;
            if (SwiftNumber.ParseDouble(digits) is { } seconds)
            {
                // :19-20 some providers write milliseconds.
                return SwiftDate.From1970Seconds(seconds > 1e11 ? seconds / 1_000 : seconds);
            }
        }

        // :23-38 the clock-time path.
        var clock = ClockPattern().Match(text);
        if (clock.Success)
        {
            if (SwiftNumber.ParseInt(clock.Groups[1].Value) is not { } hourValue) return null;
            var hour = (int)hourValue;
            var minute = clock.Groups[2].Success ? (int)(SwiftNumber.ParseInt(clock.Groups[2].Value) ?? 0) : 0;
            var isPM = SwiftString.Lowercased(clock.Groups[3].Value) == "pm";
            if (isPM && hour < 12) hour += 12; // :29
            if (!isPM && hour == 12) hour = 0; // :30

            // :31-35 Calendar.current's own zone, unless a parenthesised IANA identifier overrides it (ED-034: an
            // unrecognized identifier falls through to the local zone, exactly as the source's `if let` does).
            var zone = SwiftFormatters.CurrentTimeZone.Current;
            var zoneMatch = ZonePattern().Match(text);
            if (zoneMatch.Success && TimeZoneInfo.TryFindSystemTimeZoneById(zoneMatch.Groups[1].Value, out var found))
            {
                zone = found;
            }

            return NextTime(effectiveNow, hour, minute, zone); // :36-37
        }

        return null; // :39
    }

    /// <summary>
    /// calendar.nextDate(after: now, matching: DateComponents(hour:minute:), matchingPolicy: .nextTime) (:37): the
    /// next wall-clock hour:minute in <paramref name="zone"/> strictly after <paramref name="now"/>. A day boundary
    /// (midnight to midnight in the zone) is the same in every calendar identifier Foundation supports, so this
    /// needs only the zone, not Calendar.current's identifier (ED-094 names CultureInfo.CurrentCulture.Calendar for
    /// where the identifier itself is observable, which it is not here).
    /// </summary>
    private static DateTimeOffset NextTime(DateTimeOffset now, int hour, int minute, TimeZoneInfo zone)
    {
        var localToday = TimeZoneInfo.ConvertTime(now, zone).Date;
        var candidate = Candidate(localToday);
        return candidate > now ? candidate : Candidate(localToday.AddDays(1));

        DateTimeOffset Candidate(DateTime day)
        {
            var wall = DateTime.SpecifyKind(day, DateTimeKind.Unspecified).AddHours(hour).AddMinutes(minute);
            if (zone.IsInvalidTime(wall))
            {
                // :37 .nextTime selects the first existing instant after a gap, without preserving
                // the requested minute. Gaps need not last one hour or end on a minute boundary.
                var lower = wall;
                var upper = wall.AddHours(1);
                while (zone.IsInvalidTime(upper))
                {
                    lower = upper;
                    upper = upper.AddHours(1);
                }

                while (upper.Ticks - lower.Ticks > 1)
                {
                    var middle = new DateTime(lower.Ticks + (upper.Ticks - lower.Ticks) / 2, DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(middle)) lower = middle;
                    else upper = middle;
                }

                wall = upper;
            }

            return new DateTimeOffset(wall, OffsetOf(zone, wall));
        }
    }

    // nextDate's repeatedTimePolicy defaults to .first: a wall time that occurs twice at the end of daylight saving is its first
    // occurrence, the one with the larger (daylight) offset; TimeZoneInfo.GetUtcOffset would pick the standard offset.
    private static TimeSpan OffsetOf(TimeZoneInfo zone, DateTime wall)
    {
        return zone.IsAmbiguousTime(wall) ? zone.GetAmbiguousTimeOffsets(wall).Max() : zone.GetUtcOffset(wall);
    }

    // :42-52 "3:31 PM" today, "tomorrow at 9:00 AM" or "Thursday at 9:00 AM" further out.
    public static string Format(DateTimeOffset date)
    {
        var zone = SwiftFormatters.CurrentTimeZone.Current;
        var time = SwiftFormatters.Formatted(date, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened)); // :45
        var now = SwiftDate.Now;
        if (SameLocalDay(date, now, zone)) return time; // :46
        if (SameLocalDay(date, AddCalendarDays(now, 1, zone), zone)) return "tomorrow at " + time; // :47
        var week = AddCalendarDays(now, 6, zone); // :48
        if (date < week)
        {
            var weekday = SwiftFormatters.Formatted(date, DateFormatStyle.DateTime.Weekday(DateFormatStyle.Symbol.Weekday.Wide));
            return weekday + " at " + time; // :49
        }
        return SwiftFormatters.Formatted(date, DateFormatStyle.Of(DateFormatStyle.DateStyle.Abbreviated, DateFormatStyle.TimeStyle.Shortened)); // :51
    }

    /// <summary>calendar.isDateInToday / isDateInTomorrow (:46-47): same Year/Month/Day in the zone's local wall
    /// clock. Calendar-identifier independent for the reason <see cref="NextTime"/> gives.</summary>
    private static bool SameLocalDay(DateTimeOffset a, DateTimeOffset b, TimeZoneInfo zone)
    {
        var la = TimeZoneInfo.ConvertTime(a, zone);
        var lb = TimeZoneInfo.ConvertTime(b, zone);
        return la.Year == lb.Year && la.Month == lb.Month && la.Day == lb.Day;
    }

    /// <summary>calendar.date(byAdding: .day, value:, to:) (:48): a wall-clock day add in the zone, keeping the
    /// same time of day. Calendar-identifier independent for the reason <see cref="NextTime"/> gives.</summary>
    private static DateTimeOffset AddCalendarDays(DateTimeOffset instant, int days, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(instant, zone).DateTime, DateTimeKind.Unspecified);
        var wall = local.AddDays(days);
        return new DateTimeOffset(wall, zone.GetUtcOffset(wall));
    }
}
