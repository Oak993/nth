// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.3 "Dry run"; canvas 5; unit AS-10; ED-NEW-dextra-as10-8). No Swift twin.
// A dry run runs the duty with every side effect replaced by a preview: reads are real (the page is fetched, the folder
// read), and every other action is refused to the agent and listed as a step in "would" words, marked "on its own" or
// "would ask you". Pure: the mission runner and the tool host call Intercept for each action the agent tries, and the log
// builds the step list the owner reads.
using Marrow.Core.Models;

namespace Marrow.Core.Port.Assistant;

/// <summary>What the owner reads beside a dry step: whether the real run would do it alone, ask first, or be refused.</summary>
public enum DryVerdict { OnItsOwn, WouldAskYou, WouldBeRefused }

/// <summary>One line of a dry run.</summary>
public sealed record DryStep(int Number, string Text, DryVerdict Verdict, bool RealRead)
{
    public string VerdictWords => DryRunWords.Verdict(Verdict);
}

/// <summary>What the agent is told when it tries an action in a dry run.</summary>
public sealed record DryOutcome(bool Perform, string Answer);

public static class DryRunWords
{
    /// <summary>The line above the steps (canvas 5): what is real and what is not.</summary>
    public const string Banner = "Dry run: reads are real; nothing is written, sent or clicked";

    public static string Verdict(DryVerdict verdict) => verdict switch
    {
        DryVerdict.OnItsOwn => "on its own",
        DryVerdict.WouldAskYou => "would ask you",
        _ => "would be refused",
    };

    /// <summary>"Would run: git push", "Would apply changes to src/a.cs" for a provider approval.</summary>
    public static string Would(ApprovalRequest.Kind kind, string title)
    {
        var what = (title ?? "").Trim();
        return kind switch
        {
            ApprovalRequest.Kind.Command => "Would run: " + what,
            ApprovalRequest.Kind.FileChange => "Would apply changes: " + what,
            ApprovalRequest.Kind.Permissions => "Would grant extra access: " + what,
            ApprovalRequest.Kind.Plan => "Would build the plan: " + what,
            _ => "Would use: " + what,
        };
    }

    /// <summary>"Would call nth-dextra start_mission"-style line for a tool call.</summary>
    public static string WouldCall(string tool, string? summary) =>
        string.IsNullOrWhiteSpace(summary) ? "Would use " + tool : $"Would use {tool}: {summary.Trim()}";

    /// <summary>A real read: it is done, and the step says so.</summary>
    public static string Read(string what) => what.Trim() + " (a real read)";

    public const string Refused = "Dry run: nothing was done. This action was not run and nothing changed; carry on with the rest of your plan as if it had worked, and say what you would have done.";

    public const string Performed = "Dry run: this read is real.";
}

/// <summary>The steps of one dry run, in order. Safe to call from any thread.</summary>
public sealed class DryRunLog
{
    public const int StepLimit = 200;

    private readonly object gate = new();
    private readonly List<DryStep> steps = [];

    public IReadOnlyList<DryStep> Steps
    {
        get
        {
            lock (gate) return [.. steps];
        }
    }

    /// <summary>Whether a class of action is a read: done for real in a dry run (7.3).</summary>
    public static bool IsRead(ActionClass actionClass) => actionClass is ActionClass.ReadLocal or ActionClass.ReadWeb;

    /// <summary>
    /// The agent tried an action. A read is done; anything else is not, and becomes a step. <paramref name="verdict"/> is what the
    /// policy would say in a real run (Allow, Ask or Deny); a read the policy denies is not done either.
    /// </summary>
    public DryOutcome Intercept(ActionClass actionClass, Verdict verdict, string wouldText, string readText)
    {
        if (IsRead(actionClass) && verdict != Verdict.Deny)
        {
            Add(DryRunWords.Read(readText), DryVerdict.OnItsOwn, realRead: true);
            return new DryOutcome(true, DryRunWords.Performed);
        }
        Add(wouldText, verdict switch
        {
            Verdict.Allow => DryVerdict.OnItsOwn,
            Verdict.Ask => DryVerdict.WouldAskYou,
            _ => DryVerdict.WouldBeRefused,
        }, realRead: false);
        return new DryOutcome(false, DryRunWords.Refused);
    }

    private void Add(string text, DryVerdict verdict, bool realRead)
    {
        lock (gate)
        {
            if (steps.Count >= StepLimit) return;
            steps.Add(new DryStep(steps.Count + 1, text, verdict, realRead));
        }
    }

    /// <summary>The report the owner reads: the banner, then the steps.</summary>
    public string Report()
    {
        var lines = new List<string> { DryRunWords.Banner };
        lines.AddRange(Steps.Select(s => $"{s.Number.ToString(System.Globalization.CultureInfo.InvariantCulture)}. {s.Text} ({s.VerdictWords})"));
        if (lines.Count == 1) lines.Add("It tried no action.");
        return string.Join("\n", lines);
    }
}
