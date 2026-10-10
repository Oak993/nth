// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The "Save report" text for one chat (design
// Stage.report): fills IgnisReport from the chat's status and the savings ledger; IgnisWords.Report writes the lines.
namespace Marrow.Core.Port.Ignis.Engine;

public static class IgnisReportText
{
    /// <summary>The report for one chat at <paramref name="now"/>, with days and the clock in <paramref name="zone"/> (default: local).</summary>
    public static string Build(IgnisChatStatus status, ISavingsLedger ledger, DateTimeOffset now, TimeZoneInfo? zone = null) =>
        IgnisWords.Report(Fill(status, ledger, now, zone));

    public static IgnisReport Fill(IgnisChatStatus status, ISavingsLedger ledger, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local);
        var today = DateOnly.FromDateTime(local.DateTime);
        var chatToday = ledger.ChatDay(status.ThreadID, today);
        var week = ledger.Days(today.AddDays(-6), today).Sum(day => day.Saved);
        return new IgnisReport(
            local,
            status.ModelDisplayName ?? status.Model ?? IgnisWords.NotReported,
            status.Mode,
            status.Auto,
            chatToday.Saved,
            new Dictionary<IgnisSource, decimal>
            {
                [IgnisSource.Reuse] = chatToday.Reuse,
                [IgnisSource.KeptWarm] = chatToday.KeptWarm,
                [IgnisSource.TidyUp] = chatToday.TidyUp,
                [IgnisSource.SmartHelpers] = chatToday.SmartHelpers,
                [IgnisSource.LongerMemory] = chatToday.LongerMemory,
            },
            chatToday.Lost,
            status.CacheReported ? status.ReuseRate : null,
            status.ContextFill,
            AccountLine(status, zone),
            week,
            ledger.AllTimeSaved) { CacheLines = CacheLines(status) };
    }

    /// <summary>
    /// The report's cache lines (ED-NEW-ignis-runtime): "Paid credits: 5-minute cache", today's helper rebuilds, and the
    /// two cache-lifetime verdicts of the chat's last Claude launch. Empty when none applies.
    /// </summary>
    public static IReadOnlyList<string> CacheLines(IgnisChatStatus status)
    {
        var lines = new List<string>();
        if (status.PaidCredits) lines.Add(IgnisWords.PaidCreditsCache);
        if (status.MissedToday > 0m) lines.Add(IgnisWords.HelpersRebuilt(status.MissedToday));
        if (status.CacheLifetimes is { } choice)
        {
            lines.Add(IgnisWords.LifetimeLine(choice.Main));
            lines.Add(IgnisWords.LifetimeLine(choice.Helpers));
        }
        return lines;
    }

    /// <summary>Plan: "This chat used 9% of your week." plus the weekly-limit sentence when known; API key: its cost line.</summary>
    public static string AccountLine(IgnisChatStatus status, TimeZoneInfo? zone = null)
    {
        if (status.Account == IgnisAccountKind.ApiKey) return IgnisWords.ReportApiLine(status.ChatCost ?? 0m);
        var parts = new List<string>();
        if (status.WeekShare is { } share) parts.Add(IgnisWords.WeekShare(share));
        if (status.WeeklyLimitAt is { } at) parts.Add(IgnisWords.WeeklyLimitAt(IgnisWords.DayPart(TimeZoneInfo.ConvertTime(at, zone ?? TimeZoneInfo.Local))));
        return string.Join(" ", parts);
    }
}
