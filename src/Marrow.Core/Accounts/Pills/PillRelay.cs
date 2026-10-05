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
    public static PillResume Resolve(IReadOnlyList<Pill> pills, PillThreadState saved) => throw new NotImplementedException();
}
