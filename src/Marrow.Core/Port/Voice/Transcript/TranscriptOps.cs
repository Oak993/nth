// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): TranscriptOps (voice-SPEC V-7.5, V-7.6, V-8.7).
namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>The final pass's operations with their times, and how long Processing lasts (V-7.6).</summary>
public sealed record FinalPlan(IReadOnlyList<ScheduledOp> Ops, TimeSpan ScheduleEnd)
{
    /// <summary>
    /// <c>max(0.75 s, scheduleEnd + 0.3 s)</c> from the click (V-7.6), where the schedule starts <see cref="TranscriptOps.FinalStart"/>
    /// after the result and the result came <paramref name="resultAfterClick"/> after the click.
    /// </summary>
    public TimeSpan ProcessingDuration(TimeSpan resultAfterClick)
    {
        var wanted = resultAfterClick + ScheduleEnd + TranscriptOps.FinalTail;
        return wanted > TranscriptOps.FinalMinimum ? wanted : TranscriptOps.FinalMinimum;
    }
}

/// <summary>The difference between the words shown and the words wanted, as operations, and the timings of V-7.5 and V-7.6.</summary>
public static class TranscriptOps
{
    /// <summary>Successive adds at least this far apart (V-7.5): 60 ms.</summary>
    public static readonly TimeSpan MinAddSpacing = TimeSpan.FromMilliseconds(60);

    /// <summary>The final schedule starts this long after the result (V-7.6): 0.18 s.</summary>
    public static readonly TimeSpan FinalStart = TimeSpan.FromSeconds(0.18);

    /// <summary>Adds in the final schedule are this far apart (V-7.6): 0.07 s.</summary>
    public static readonly TimeSpan FinalAddSpacing = TimeSpan.FromSeconds(0.07);

    /// <summary>Revisions in the final schedule are this far apart (V-7.6): 0.12 s.</summary>
    public static readonly TimeSpan FinalReviseSpacing = TimeSpan.FromSeconds(0.12);

    /// <summary>Processing lasts at least this long (V-7.6 <c>finalMin</c>): 0.75 s.</summary>
    public static readonly TimeSpan FinalMinimum = TimeSpan.FromSeconds(0.75);

    /// <summary>Processing lasts the schedule's end plus this (V-7.6): 0.3 s.</summary>
    public static readonly TimeSpan FinalTail = TimeSpan.FromSeconds(0.3);

    /// <summary>Words leave this far apart when a session is cancelled, from the end (V-8.6): 30 ms.</summary>
    public static readonly TimeSpan WithdrawSpacing = TimeSpan.FromMilliseconds(30);

    /// <summary>Tentative words ink to solid this far apart at Landing (V-8.7): 25 ms.</summary>
    public static readonly TimeSpan InkSpacing = TimeSpan.FromMilliseconds(25);

    /// <summary>The operations that turn <paramref name="shown"/> into <paramref name="wanted"/> (V-7.5).</summary>
    public static IReadOnlyList<WordOp> Diff(IReadOnlyList<TranscriptWord> shown, IReadOnlyList<TranscriptWord> wanted)
    {
        var ops = new List<WordOp>();
        var common = Math.Min(shown.Count, wanted.Count);
        for (var i = 0; i < common; i++)
        {
            if (!string.Equals(shown[i].Text, wanted[i].Text, StringComparison.Ordinal)) ops.Add(WordOp.Revise(i, wanted[i].Text));
            if (shown[i].Tentative && !wanted[i].Tentative) ops.Add(WordOp.Confirm(i));
        }
        for (var i = shown.Count - 1; i >= wanted.Count; i--) ops.Add(WordOp.Remove(i));
        for (var i = shown.Count; i < wanted.Count; i++) ops.Add(WordOp.Add(i, wanted[i].Text, wanted[i].Tentative));
        return ops;
    }

    /// <summary>
    /// Times for a live batch: operations other than Add are due at once and Adds at least <see cref="MinAddSpacing"/> apart, the
    /// first no sooner than that after <paramref name="sinceLastAdd"/> (null: no add yet).
    /// </summary>
    public static IReadOnlyList<ScheduledOp> PaceLive(IReadOnlyList<WordOp> ops, TimeSpan? sinceLastAdd)
    {
        var scheduled = new List<ScheduledOp>(ops.Count);
        var nextAdd = sinceLastAdd is { } since && since < MinAddSpacing ? MinAddSpacing - since : TimeSpan.Zero;
        foreach (var op in ops)
        {
            if (op.Kind == WordOpKind.Add)
            {
                scheduled.Add(new ScheduledOp(nextAdd, op));
                nextAdd += MinAddSpacing;
            }
            else scheduled.Add(new ScheduledOp(TimeSpan.Zero, op));
        }
        return scheduled;
    }

    /// <summary>
    /// The final pass's schedule (V-7.6): the operations that turn <paramref name="shown"/> into <paramref name="finalWords"/> (all of
    /// them tentative until Landing inks them), the first <see cref="FinalStart"/> after the result and each later one after the one
    /// before by its own kind's spacing; Confirm operations are not part of it.
    /// </summary>
    public static FinalPlan PlanFinal(IReadOnlyList<TranscriptWord> shown, IReadOnlyList<TranscriptWord> finalWords)
    {
        var scheduled = new List<ScheduledOp>();
        var at = TimeSpan.Zero;
        foreach (var op in Diff(shown, finalWords))
        {
            if (op.Kind == WordOpKind.Confirm) continue; // Landing inks the words (V-8.7)
            at = scheduled.Count == 0 ? FinalStart : at + (op.Kind == WordOpKind.Add ? FinalAddSpacing : FinalReviseSpacing);
            scheduled.Add(new ScheduledOp(at, op));
        }
        return new FinalPlan(scheduled, scheduled.Count == 0 ? TimeSpan.Zero : at);
    }

    /// <summary>The withdraw on cancel (V-8.6): Remove for every word, last first, <see cref="WithdrawSpacing"/> apart from 0.</summary>
    public static IReadOnlyList<ScheduledOp> PlanWithdraw(int wordCount)
    {
        var scheduled = new List<ScheduledOp>(wordCount);
        for (var k = 0; k < wordCount; k++) scheduled.Add(new ScheduledOp(TimeSpan.FromTicks(WithdrawSpacing.Ticks * k), WordOp.Remove(wordCount - 1 - k)));
        return scheduled;
    }

    /// <summary>Landing (V-8.7): Confirm for every tentative word, <see cref="InkSpacing"/> apart from the first at 0.</summary>
    public static IReadOnlyList<ScheduledOp> PlanLanding(IReadOnlyList<TranscriptWord> shown)
    {
        var scheduled = new List<ScheduledOp>();
        for (var i = 0; i < shown.Count; i++)
            if (shown[i].Tentative) scheduled.Add(new ScheduledOp(TimeSpan.FromTicks(InkSpacing.Ticks * scheduled.Count), WordOp.Confirm(i)));
        return scheduled;
    }
}
