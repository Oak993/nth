// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 13.1 "Duty opt-in", 7.1 "a report style ... Notify me"; ED-NEW-dextra-as9-9). No Swift twin.
// What a duty with the report style "Notify me" tells the owner when a run found something: one toast with the duty's name and the first line of
// its report. A duty's report is not a decision, so this is not a DecisionNotice and carries no button that answers anything. Pure: no I/O, no clock.
// The report is data that a page or a file can put words in, so the toast shows one masked, cut line of it, never the whole report.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>A run of a "Notify me" duty that found something: the mission that did it, the duty's name and the report it ended with.</summary>
public sealed record DutyFinding(Guid Mission, string Duty, string? Report);

/// <summary>
/// One toast about duty runs: its words and the missions it is about. <see cref="Target"/> is the mission a click opens (the first of
/// <see cref="Missions"/>); a toast about several runs says so and opens the missions list.
/// </summary>
public sealed record DutyNotice(Guid Target, ValueList<Guid> Missions, string Title, string Body)
{
    /// <summary>The toast speaks for several runs.</summary>
    public bool IsSummary => Missions.Count > 1;
}

public static class DutyNotices
{
    /// <summary>The longest duty name in a title: the banner is one line, and a name is the owner's own word.</summary>
    public const int NameChars = 60;

    private const string Fallback = "Has something for you.";

    /// <summary>
    /// Whether a run that ended tells the owner (13.1): the duty chose "Notify me", the run is a real one that finished, and its report does not end
    /// with the nothing-changed line. A failed or stopped run is the board's to say, and three failures in a row are a card (13.2).
    /// </summary>
    public static bool Notifies(Duty duty, DutyRunOutcome outcome, string? report, bool dryRun)
    {
        ArgumentNullException.ThrowIfNull(duty);
        return !dryRun && duty.Report == ReportStyle.NotifyMe && outcome == DutyRunOutcome.Done && !DutyResult.NothingChanged(report);
    }

    /// <summary>The toast for one run: the duty's name over the first line of its report.</summary>
    public static DutyNotice For(DutyFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var line = FeedShield.Excerpt(FirstLine(finding.Report), DecisionNotices.BodyChars);
        return new DutyNotice(finding.Mission, new ValueList<Guid>([finding.Mission]), Name(finding.Duty), line.Length > 0 ? line : Fallback);
    }

    /// <summary>The toast for runs that ended together: one is its own toast, several are one summary that lists the duties, three at most.</summary>
    public static DutyNotice? ForMany(IReadOnlyList<DutyFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        if (findings.Count == 0) return null;
        if (findings.Count == 1) return For(findings[0]);
        var names = findings.Select(finding => Name(finding.Duty)).Distinct(StringComparer.Ordinal).ToList();
        var listed = names.Count <= DecisionNotices.NamesListed
            ? names
            : [.. names.Take(DecisionNotices.NamesListed), (names.Count - DecisionNotices.NamesListed).ToString(System.Globalization.CultureInfo.InvariantCulture) + " more"];
        return new DutyNotice(findings[0].Mission, new ValueList<Guid>(findings.Select(finding => finding.Mission)), DecisionNoticeWords.DutyMany(findings.Count),
            $"{ControlWords.AndList(listed)} {(names.Count == 1 ? "has" : "have")} something for you.");
    }

    private static string Name(string duty)
    {
        var name = FeedShield.Excerpt(duty, NameChars);
        return name.Length > 0 ? name : AssistantNames.Assistant;
    }

    // The first line of the report that says something: not blank, not the structured result, with the Markdown marks of a heading, bullet or quote taken off.
    private static string FirstLine(string? report)
    {
        if (string.IsNullOrWhiteSpace(report)) return "";
        foreach (var raw in report.Split('\n'))
        {
            var line = Unmark(raw.Trim());
            if (line.Length == 0 || line.StartsWith("RESULT:", StringComparison.OrdinalIgnoreCase)) continue;
            return line;
        }
        return "";
    }

    private static string Unmark(string line)
    {
        while (true)
        {
            var rest = line.TrimStart('#');
            if (rest.Length != line.Length && rest.StartsWith(' ')) { line = rest.TrimStart(); continue; }
            if (line.Length > 1 && (line[0] is '-' or '*' or '>') && line[1] == ' ') { line = line[2..].TrimStart(); continue; }
            return line;
        }
    }
}
