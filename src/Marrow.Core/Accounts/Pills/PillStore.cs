namespace Marrow.Core.Accounts.Pills;

// PORT-ONLY (OD-0114, MA-7): pills as data. Source for every rule is porting/first-release/multi-account-NOTES.md ("Pills", "Pills loop", "Effort and fast mode",
// "Handshake point everywhere", "Pills get their own Settings category") and the MA-7 brief; no Swift twin.

public enum PillNameProblem
{
    None,
    Empty,
    Taken,
}

public enum PillChangeKind
{
    Loaded,
    Created,
    Renamed,
    Duplicated,
    Deleted,
    StepsChanged,
    RuleChanged,
    UseForNewThreadsChanged,
}

public sealed record PillStoreChange(PillChangeKind Kind, Guid? PillId);

/// <summary>Where a thread is in its pill: persisted on the thread so a restart resumes the relay where it was.</summary>
public sealed record PillThreadState(Guid PillId, int StepIndex, string AccountId);

public sealed class PillStore
{
    public PillStore(PillStorage? storage = null) => throw new NotImplementedException();

    public IReadOnlyList<Pill> Pills => throw new NotImplementedException();

    public Guid? UseForNewThreads => throw new NotImplementedException();

    /// <summary>The last save was refused (the stored text is from a newer app or unreadable); the change is kept in memory.</summary>
    public bool SaveBlocked => throw new NotImplementedException();

    public event EventHandler<PillStoreChange>? Changed;

    public Pill? Find(Guid id) => throw new NotImplementedException();

    public PillNameProblem NameProblem(string name, Guid? except = null) => throw new NotImplementedException();

    public Pill Create(string name, IEnumerable<PillStep>? steps = null) => throw new NotImplementedException();

    public void Rename(Guid id, string name) => throw new NotImplementedException();

    public Pill Duplicate(Guid id) => throw new NotImplementedException();

    public bool Delete(Guid id) => throw new NotImplementedException();

    public void AddStep(Guid id, PillStep step, int? at = null) => throw new NotImplementedException();

    public void RemoveStep(Guid id, int index) => throw new NotImplementedException();

    public void UpdateStep(Guid id, int index, Func<PillStep, PillStep> change) => throw new NotImplementedException();

    public void MoveStep(Guid id, int from, int to) => throw new NotImplementedException();

    /// <summary>The pill's own "when an earlier account is back" value (go back, stay or ask), or null to follow Settings.</summary>
    public void SetEarlierAccount(Guid id, string? value) => throw new NotImplementedException();

    public void SetHandshakeMode(Guid id, PillHandshakeMode mode, HandshakePoint? same = null) => throw new NotImplementedException();

    public void SetUseForNewThreads(Guid? id) => throw new NotImplementedException();

    /// <summary>A new thread starts on the "Use for new threads" pill, at its first usable step; null when none is chosen or no step is usable.</summary>
    public PillThreadState? NewThreadRelay(Func<PillStep, bool> usable) => throw new NotImplementedException();

    internal void Unused() => Changed?.Invoke(this, null!);
}
