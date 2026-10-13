// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.3 "Delegation and merging", 12.4 "a delegated report carries its taint up"; unit AS-16; ED-NEW-dextra-as16-8). No Swift twin.
// What an Astur is told when Dextra hands it a piece of work, and what Dextra is told when the Asturs report. The brief starts with who
// the Astur is (its name, area and style) because its mission is a fresh chat that knows nothing else. The reports come back in one message,
// each part under the name of the Astur it came from, so Dextra's merged answer labels every part; a part that read untrusted content is
// handed over fenced as data, and the message carries the union of the parts' taint, so the delegating chat is tainted by what any Astur read.
// Pure.
namespace Marrow.Core.Port.Assistant;

/// <summary>How a delegated piece ended.</summary>
public enum PartOutcome { Finished, Failed, Stopped }

/// <summary>One Astur's report on one piece of work.</summary>
public sealed record DelegatedReport(string AsturName, PartOutcome Outcome, string Report, Taint Taint);

/// <summary>The message Dextra's chat receives, and the taint it carries.</summary>
public sealed record MergedReports(string Text, Taint Taint);

public static class DelegationMerge
{
    /// <summary>The longest report kept for each part; past it the report is cut and says where to read the rest.</summary>
    public const int ReportCap = 12_000;

    /// <summary>The words for how a piece ended, in a label.</summary>
    public static string OutcomeWord(PartOutcome outcome) => outcome switch
    {
        PartOutcome.Finished => "finished",
        PartOutcome.Failed => "failed",
        _ => "stopped before it finished",
    };

    /// <summary>
    /// The brief an Astur's mission starts with: who it is, what it looks after, how it writes, where its notes are, and the task. The task
    /// is the caller's text as it is: data, fenced and labelled, when Dextra's chat had read untrusted content (12.4).
    /// </summary>
    public static string Frame(AssistantProfile astur, string brief)
    {
        ArgumentNullException.ThrowIfNull(astur);
        var lines = new List<string>
        {
            $"You work as {astur.Name}, an {AssistantNames.Astur} of {AssistantNames.Assistant}, for the owner.",
            $"Your area: {astur.Area}",
        };
        if (astur.Style.Length > 0) lines.Add($"How you write: {astur.Style}");
        lines.Add("Your notes are your own: use recall for what you know and remember for what you learn. When you are done, call report with what you did and found; "
            + AssistantNames.Assistant + " merges the reports and tells the owner.");
        lines.Add("");
        lines.Add("The task:");
        lines.Add(brief);
        return string.Join('\n', lines);
    }

    /// <summary>
    /// The message for the delegating chat once every piece is in: how many, an instruction to merge them into one answer with each part
    /// labelled by its Astur, and each part under its own label. A tainted part is fenced as data.
    /// </summary>
    public static MergedReports Compose(IReadOnlyList<DelegatedReport> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var lines = new List<string>
        {
            parts.Count == 1
                ? $"The report from {parts[0].AsturName} is in. Tell the owner, and label it with the {AssistantNames.Astur} it came from."
                : $"All {parts.Count} reports from your {AssistantNames.Asturs} are in. Merge them into one answer for the owner, and label each part with the {AssistantNames.Astur} it came from.",
        };
        foreach (var part in parts) lines.AddRange(["", .. Part(part)]);
        return new MergedReports(string.Join('\n', lines), Taint.UnionOf(parts.Select(p => p.Taint)));
    }

    /// <summary>One report that arrives after the others were merged (a stopped piece the owner resumed): under its label, alone.</summary>
    public static MergedReports Late(DelegatedReport part) =>
        new(string.Join('\n', [$"A later report from {part.AsturName} is in; the other reports were already merged. Tell the owner what it adds.", "", .. Part(part)]), part.Taint);

    private static IEnumerable<string> Part(DelegatedReport part)
    {
        var report = part.Report.Length > ReportCap ? part.Report[..ReportCap] + "\n(cut here; the whole report is on its card)" : part.Report;
        report = report.Length == 0 ? "It left no report." : report;
        // A report is the Astur's words: a line of it may not pass for a label of the merge.
        report = string.Join('\n', report.ReplaceLineEndings("\n").Split('\n').Select(line => line.StartsWith("==", StringComparison.Ordinal) ? "= " + line : line));
        yield return $"== {part.AsturName}: {OutcomeWord(part.Outcome)} ==";
        yield return TaintGates.AsData(new TaintedText(report, part.Taint));
    }
}
