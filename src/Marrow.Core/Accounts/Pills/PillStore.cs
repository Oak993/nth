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
    private readonly PillStorage? storage;
    private List<Pill> pills = [];
    private Guid? useForNewThreads;

    public PillStore(PillStorage? storage = null)
    {
        this.storage = storage;
        if (storage is null) return;
        var load = storage.Load();
        pills = [.. load.Pills];
        useForNewThreads = load.UseForNewThreads;
        SaveBlocked = load.Newer || load.Problem is not null;
    }

    public IReadOnlyList<Pill> Pills => pills;

    public Guid? UseForNewThreads => useForNewThreads;

    /// <summary>The last save was refused (the stored text is from a newer app or unreadable); the change is kept in memory.</summary>
    public bool SaveBlocked { get; private set; }

    public event EventHandler<PillStoreChange>? Changed;

    public Pill? Find(Guid id) => pills.FirstOrDefault(p => p.Id == id);

    public PillNameProblem NameProblem(string name, Guid? except = null)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return PillNameProblem.Empty;
        return pills.Any(p => p.Id != except && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)) ? PillNameProblem.Taken : PillNameProblem.None;
    }

    public Pill Create(string name, IEnumerable<PillStep>? steps = null)
    {
        CheckName(name, null);
        var list = (steps ?? []).ToList();
        foreach (var step in list) CheckStep(step);
        var pill = new Pill(name.Trim(), list, Id: Guid.NewGuid());
        pills.Add(pill);
        Commit(PillChangeKind.Created, pill.Id);
        return pill;
    }

    public void Rename(Guid id, string name)
    {
        var pill = Get(id);
        CheckName(name, id);
        Replace(pill, pill with { Name = name.Trim() }, PillChangeKind.Renamed);
    }

    public Pill Duplicate(Guid id)
    {
        var pill = Get(id);
        var name = $"{pill.Name} copy";
        for (var n = 2; NameProblem(name) != PillNameProblem.None; n++) name = $"{pill.Name} copy {n}";
        var copy = pill with { Name = name, Id = Guid.NewGuid(), Steps = [.. pill.Steps] };
        pills.Insert(pills.IndexOf(pill) + 1, copy);
        Commit(PillChangeKind.Duplicated, copy.Id);
        return copy;
    }

    public bool Delete(Guid id)
    {
        if (Find(id) is not { } pill) return false;
        pills.Remove(pill);
        if (useForNewThreads == id) useForNewThreads = null;
        Commit(PillChangeKind.Deleted, id);
        return true;
    }

    public void AddStep(Guid id, PillStep step, int? at = null)
    {
        CheckStep(step);
        var pill = Get(id);
        var steps = pill.Steps.ToList();
        var index = at ?? steps.Count;
        if (index < 0 || index > steps.Count) throw new ArgumentOutOfRangeException(nameof(at));
        steps.Insert(index, step);
        Replace(pill, pill with { Steps = steps }, PillChangeKind.StepsChanged);
    }

    public void RemoveStep(Guid id, int index)
    {
        var pill = Get(id);
        if (index < 0 || index >= pill.Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var steps = pill.Steps.ToList();
        steps.RemoveAt(index);
        Replace(pill, pill with { Steps = steps }, PillChangeKind.StepsChanged);
    }

    public void UpdateStep(Guid id, int index, Func<PillStep, PillStep> change)
    {
        var pill = Get(id);
        if (index < 0 || index >= pill.Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var step = change(pill.Steps[index]);
        CheckStep(step);
        var steps = pill.Steps.ToList();
        steps[index] = step;
        Replace(pill, pill with { Steps = steps }, PillChangeKind.StepsChanged);
    }

    public void MoveStep(Guid id, int from, int to)
    {
        var pill = Get(id);
        if (from < 0 || from >= pill.Steps.Count) throw new ArgumentOutOfRangeException(nameof(from));
        if (to < 0 || to >= pill.Steps.Count) throw new ArgumentOutOfRangeException(nameof(to));
        if (from == to) return;
        var steps = pill.Steps.ToList();
        var step = steps[from];
        steps.RemoveAt(from);
        steps.Insert(to, step);
        Replace(pill, pill with { Steps = steps }, PillChangeKind.StepsChanged);
    }

    /// <summary>The pill's own "when an earlier account is back" value (go back, stay or ask), or null to follow Settings.</summary>
    public void SetEarlierAccount(Guid id, string? value)
    {
        var pill = Get(id);
        if (value is not null && value is not (DecisionValues.GoBack or DecisionValues.Stay or DecisionValues.Ask))
            throw new ArgumentException($"'{value}' is not a value of the earlier-account rule.", nameof(value));
        Replace(pill, pill with { EarlierAccount = value }, PillChangeKind.RuleChanged);
    }

    public void SetHandshakeMode(Guid id, PillHandshakeMode mode, HandshakePoint? same = null)
    {
        var pill = Get(id);
        Replace(pill, pill with { HandshakeMode = mode, SameHandshake = mode == PillHandshakeMode.SameForEvery ? same ?? HandshakePoint.Smart : null }, PillChangeKind.RuleChanged);
    }

    public void SetUseForNewThreads(Guid? id)
    {
        if (id is { } pill) Get(pill);
        if (useForNewThreads == id) return;
        useForNewThreads = id;
        Commit(PillChangeKind.UseForNewThreadsChanged, id);
    }

    /// <summary>A new thread starts on the "Use for new threads" pill, at its first usable step; null when none is chosen or no step is usable.</summary>
    public PillThreadState? NewThreadRelay(Func<PillStep, bool> usable)
    {
        if (useForNewThreads is not { } id || Find(id) is not { } pill) return null;
        for (var i = 0; i < pill.Steps.Count; i++)
            if (usable(pill.Steps[i])) return new PillThreadState(pill.Id, i, pill.Steps[i].AccountIdentity);
        return null;
    }

    private Pill Get(Guid id) => Find(id) ?? throw new KeyNotFoundException($"No pill {id}.");

    private void CheckName(string name, Guid? except)
    {
        switch (NameProblem(name, except))
        {
            case PillNameProblem.Empty: throw new ArgumentException("A pill needs a name.", nameof(name));
            case PillNameProblem.Taken: throw new ArgumentException($"There is already a pill called '{name.Trim()}'.", nameof(name));
        }
    }

    private static void CheckStep(PillStep step)
    {
        if (string.IsNullOrWhiteSpace(step.AccountId)) throw new ArgumentException("A step needs an account.", nameof(step));
    }

    private void Replace(Pill old, Pill replacement, PillChangeKind kind)
    {
        pills[pills.IndexOf(old)] = replacement;
        Commit(kind, replacement.Id);
    }

    private void Commit(PillChangeKind kind, Guid? id)
    {
        if (storage is not null) SaveBlocked = !storage.Save(pills, useForNewThreads);
        Changed?.Invoke(this, new PillStoreChange(kind, id));
    }
}
