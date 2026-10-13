// PORT-ONLY: Dextra (DESIGN.md rev 5, 7.3, 10 step 4; unit AS-19; ED-NEW-dextra-as19-5). No Swift twin.
// The dry run of a draft: the steps read out in "would" words, each marked "on its own" or "would ask you", and nothing done. It is a
// pure walk over the draft, so it cannot write, click, send or even read anything: there is no executor in it to call. (A duty's dry run
// does real reads because that is how it shows what it would find; a taught skill's steps are the owner's own, already shown to him,
// so reading them out changes nothing and costs nothing.) The banner says so.
using System.Globalization;

namespace Marrow.Core.Port.Assistant;

public static class TeachDryRun
{
    /// <summary>The line above the steps.</summary>
    public const string Banner = "Dry run: nothing is written, sent or clicked, and nothing is read either. These are the steps as they would run.";

    /// <summary>The steps of a draft as a dry run lists them. Steps that ask the owner are marked "would ask you"; a step he does himself, "would be refused".</summary>
    public static IReadOnlyList<DryStep> Walk(TeachDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var steps = new List<DryStep>();
        foreach (var step in draft.Steps.Take(DryRunLog.StepLimit))
        {
            var verdict = step.Guard is { OwnerDoesIt: true } ? DryVerdict.WouldBeRefused
                : step.AsksYou ? DryVerdict.WouldAskYou
                : DryVerdict.OnItsOwn;
            steps.Add(new DryStep(steps.Count + 1, Would(step), verdict, RealRead: false));
        }
        return steps;
    }

    /// <summary>The report the owner reads: the banner, then the steps, then how many would ask.</summary>
    public static string Report(TeachDraft draft)
    {
        var steps = Walk(draft);
        var lines = new List<string> { Banner };
        lines.AddRange(steps.Select(s => $"{s.Number.ToString(CultureInfo.InvariantCulture)}. {s.Text} ({s.VerdictWords})"));
        var asks = steps.Count(s => s.Verdict != DryVerdict.OnItsOwn);
        lines.Add(asks == 0 ? "No step would ask you." : $"{asks.ToString(CultureInfo.InvariantCulture)} of {steps.Count.ToString(CultureInfo.InvariantCulture)} steps would ask you.");
        return string.Join("\n", lines);
    }

    /// <summary>"Would click “Send” (button) in Outlook": the step's own words with the verb in the would form.</summary>
    public static string Would(TeachStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var text = step.Text.Trim();
        if (text.Length == 0) return "Would do a step";
        return "Would " + char.ToLowerInvariant(text[0]) + text[1..];
    }
}
