namespace Marrow.Core.Accounts.Pills;

public enum PillResumeReason
{
    /// <summary>The stored step still holds the stored account.</summary>
    Exact,

    /// <summary>The pill was edited: the same account is at another step, the nearest to the stored one.</summary>
    Moved,

    /// <summary>The account is no longer in the pill; the thread keeps the stored position, clamped into the pill.</summary>
    Replaced,

    /// <summary>The pill was deleted: the thread carries on without one.</summary>
    PillGone,

    /// <summary>The pill has no steps left.</summary>
    Empty,
}

public sealed record PillResume(Pill? Pill, int? StepIndex, PillResumeReason Reason);

public static class PillRelayResolver
{
    /// <summary>Where a restarted thread continues in its relay.</summary>
    public static PillResume Resolve(IReadOnlyList<Pill> pills, PillThreadState saved)
    {
        if (pills.FirstOrDefault(p => p.Id == saved.PillId) is not { } pill) return new PillResume(null, null, PillResumeReason.PillGone);
        if (pill.Steps.Count == 0) return new PillResume(pill, null, PillResumeReason.Empty);
        if (saved.StepIndex >= 0 && saved.StepIndex < pill.Steps.Count && string.Equals(pill.Steps[saved.StepIndex].AccountIdentity, saved.AccountId, StringComparison.OrdinalIgnoreCase))
            return new PillResume(pill, saved.StepIndex, PillResumeReason.Exact);

        var same = Enumerable.Range(0, pill.Steps.Count).Where(i => string.Equals(pill.Steps[i].AccountIdentity, saved.AccountId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (same.Count > 0)
        {
            // The nearest to where it was; a tie goes to the later step.
            var nearest = same.OrderBy(i => Math.Abs(i - saved.StepIndex)).ThenByDescending(i => i).First();
            return new PillResume(pill, nearest, PillResumeReason.Moved);
        }
        return new PillResume(pill, Math.Clamp(saved.StepIndex, 0, pill.Steps.Count - 1), PillResumeReason.Replaced);
    }
}
