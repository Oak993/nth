// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.3 "Broad-trigger warning", 7.1 "Silent unless something changed"; unit AS-10;
// ED-NEW-dextra-as10-9, -10). No Swift twin.
// How often a duty is likely to run in a day, so a trigger that fires more than 20 times a day (a watch on all of Downloads)
// shows a warning and a cost estimate before it can be saved; and the structured result a duty's mission ends with, which makes
// a quiet duty quiet. Pure.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

/// <summary>How often a duty runs in a day, whether that is broad (more than 20), and the words for the card.</summary>
public sealed record DutyForecast(double RunsPerDay, bool Broad, string Words)
{
    /// <summary>More runs a day than this is broad (7.3).</summary>
    public const double BroadAbove = 20;

    /// <summary>
    /// The forecast. <paramref name="observedPerDay"/> is what a folder or an app event did over the last day, when nth could
    /// measure it; <paramref name="runUsd"/> is the cost of one run when it is known (the budget, or what the last runs cost).
    /// </summary>
    public static DutyForecast Of(Duty duty, DateTimeOffset now, double? observedPerDay = null, decimal? runUsd = null)
    {
        ArgumentNullException.ThrowIfNull(duty);
        var perDay = duty.Trigger switch
        {
            TimeTrigger time => TimedRuns(time, now, duty.Since),
            FolderChangeTrigger folder => observedPerDay ?? (folder.Pattern is "*" or "*.*" ? BroadAbove + 5 : 5),
            PageWatchTrigger page => page.Condition == PageCondition.Changed ? 1440.0 / Math.Max(page.EveryMinutes, 1) : Math.Min(1440.0 / Math.Max(page.EveryMinutes, 1), 1),
            AppEventTrigger app => observedPerDay ?? app.Event switch
            {
                AppEventKind.ChatFinished => BroadAbove + 10,
                AppEventKind.MissionFinished => 10,
                AppEventKind.WindowReset => 5,
                _ => 3,
            },
            _ => 0,
        };
        var broad = perDay > BroadAbove;
        var words = new List<string>();
        if (perDay > 0) words.Add($"About {Whole(perDay)} a day");
        if (perDay > 0 && (runUsd ?? duty.BudgetUsd) is { } each) words.Add($"up to ${(each * (decimal)Math.Ceiling(perDay)).ToString("0.00", CultureInfo.InvariantCulture)} a day at ${each.ToString("0.00", CultureInfo.InvariantCulture)} a run");
        if (broad) words.Add("more than 20 a day: narrow the trigger, or expect a high bill");
        return new DutyForecast(perDay, broad, words.Count == 0 ? "Only when you run it" : string.Join(" · ", words));
    }

    private static string Whole(double value) => Math.Max(1, (int)Math.Round(value)).ToString(CultureInfo.InvariantCulture);

    // Counted on the schedule itself over the next 24 hours, so a cron line is exact.
    private static double TimedRuns(TimeTrigger time, DateTimeOffset now, DateTimeOffset since)
    {
        if (time.Schedule.Kind == ScheduleKind.Every) return 1440.0 / Math.Max(time.Schedule.EveryMinutes, 1);
        if (DutyZones.Find(time.TimeZoneId) is not { } zone) return 0;
        var count = 0;
        var at = now;
        var end = now.AddHours(24);
        while (count < 2_000 && ScheduleMath.Next(time.Schedule, zone, at, since) is { } next && next <= end)
        {
            count++;
            at = next;
        }
        return count;
    }
}

/// <summary>
/// The structured result a duty's mission ends with (7.1): its report carries a line "RESULT: nothing_changed" when there was
/// nothing to report, and then nothing reaches the board or the digest beyond one quiet feed line.
/// </summary>
public static class DutyResult
{
    public const string NothingChangedLine = "RESULT: nothing_changed";

    /// <summary>What the brief of every duty run ends with, so the agent ends the way the scheduler reads.</summary>
    public const string Instruction =
        "When you finish, if there was nothing to do or nothing changed since the last run, make the last line of your report exactly \""
        + NothingChangedLine + "\". Otherwise report what you did and found, and do not write that line.";

    /// <summary>Whether a report ends with the nothing-changed line (any case, trailing blanks allowed).</summary>
    public static bool NothingChanged(string? report)
    {
        if (string.IsNullOrWhiteSpace(report)) return false;
        var lines = report.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            return string.Equals(line, NothingChangedLine, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
