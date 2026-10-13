// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1, 9.2, 15.3; unit AS-16; ED-NEW-dextra-as16-10, -11). No Swift twin.
// Dextra and its Asturs: the profiles in the store, read at start and kept in step with it. Every change (a new Astur, an edit, a
// retirement, a shared browser profile, a home thread) is checked against the team's rules first, written to disk off the caller's thread,
// and only then shown to anyone: the team a caller reads is the team on disk. One change runs at a time, so two taps on "Create" cannot make
// a seventh Astur. An Astur's day budget is a standing per-assistant cap in the reserve settings, set before its profile is written and taken
// back if the write fails (fail closed: no Astur without its slice). Nothing here acts on the owner's behalf: the owner's card calls it.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.Services.Assistant;

/// <summary>What a change to the team did: the profile after it, or the sentence for the card.</summary>
public sealed record TeamResult(AssistantProfile? Profile, string? Problem)
{
    public bool Ok => Problem is null;

    public static TeamResult Done(AssistantProfile profile) => new(profile, null);

    public static TeamResult Refused(string problem) => new(null, problem);
}

public sealed class AssistantTeamOptions
{
    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Where an Astur's day budget is kept as a standing cap (13.2); null: no cap is set (a build without cost controls).</summary>
    public ReserveSettings? Budgets { get; init; }

    /// <summary>A new assistant's id: 32 lower-case hex digits, the form a chat's AssistantID has.</summary>
    public Func<string> NewId { get; init; } = () => Guid.NewGuid().ToString("N");

    /// <summary>The most profiles read, retired ones included; past it the rest are left on disk and listed as a problem.</summary>
    public int MaxProfiles { get; init; } = 64;
}

public sealed class AssistantTeam : IDisposable
{
    /// <summary>The id of the standing cap that is an Astur's day budget.</summary>
    public static string BudgetCapId(string assistantId) => "astur:" + assistantId;

    private readonly AssistantStore store;
    private readonly AssistantTeamOptions options;
    private readonly object listGate = new();
    private readonly SemaphoreSlim one = new(1, 1);
    private readonly List<AssistantProfile> profiles = [];
    private readonly Dictionary<string, System.Text.Json.Nodes.JsonObject> extras = new(StringComparer.Ordinal);
    private readonly List<string> problems = [];
    private bool disposed;

    private AssistantTeam(AssistantStore store, AssistantTeamOptions options)
    {
        this.store = store;
        this.options = options;
    }

    /// <summary>Reads the store's profiles. A file nth cannot use is skipped and named in <see cref="Problems"/>; it is never changed.</summary>
    public static AssistantTeam Open(AssistantStore store, AssistantTeamOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        var team = new AssistantTeam(store, options ?? new AssistantTeamOptions());
        team.Load();
        return team;
    }

    private void Load()
    {
        var read = new List<ProfileDocument>();
        foreach (var id in store.AssistantIds())
        {
            ProfileRead result;
            try { result = AssistantProfileFile.Read(id, store.Profile(id)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                problems.Add($"The profile of {id} could not be read: {error.Message}");
                continue;
            }
            if (result.Document is { } document) read.Add(document);
            else if (result.Problem is { } problem) problems.Add(problem);
        }
        var ordered = read.OrderBy(d => d.Profile.Created ?? DateTimeOffset.MinValue).ThenBy(d => d.Profile.Id, StringComparer.Ordinal).ToList();
        var root = ordered.FirstOrDefault(d => d.Profile.IsRoot);
        foreach (var document in ordered)
        {
            var profile = document.Profile;
            if (profile.IsRoot && document != root) { problems.Add($"{profile.Name} is a second {AssistantNames.Assistant} profile, so it is ignored."); continue; }
            if (!profile.IsRoot && (root is null || profile.ParentId != root.Profile.Id))
            {
                problems.Add($"{profile.Name} belongs to no {AssistantNames.Assistant} nth knows, so it is left out.");
                continue;
            }
            if (profiles.Count >= options.MaxProfiles) { problems.Add($"More than {options.MaxProfiles} profiles: {profile.Name} and later ones are left out."); break; }
            profiles.Add(profile);
            extras[profile.Id] = document.Extra;
        }
    }

    // ---- reading ----

    /// <summary>Dextra first, then the Asturs in the order they were made, retired ones included (a mission of one still has its chain).</summary>
    public IReadOnlyList<AssistantProfile> Profiles
    {
        get { lock (listGate) return [.. profiles.OrderBy(p => p.IsRoot ? 0 : 1).ThenBy(p => p.Created ?? DateTimeOffset.MinValue).ThenBy(p => p.Id, StringComparer.Ordinal)]; }
    }

    /// <summary>Dextra and the Asturs that are not retired: the ones the owner sees and talks to.</summary>
    public IReadOnlyList<AssistantProfile> Active => [.. Profiles.Where(p => !p.Retired)];

    public AssistantProfile? Root => Profiles.FirstOrDefault(p => p.IsRoot);

    /// <summary>Files nth could not use, as sentences for the Team tab; empty when all were read.</summary>
    public IReadOnlyList<string> Problems
    {
        get { lock (listGate) return [.. problems]; }
    }

    public AssistantProfile? Get(string? id) => id is null ? null : Profiles.FirstOrDefault(p => p.Id == id);

    /// <summary>The active assistant a reference names: its id, or its name or another name it answers to (compared by letters, without case).</summary>
    public AssistantProfile? Find(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var active = Active;
        if (active.FirstOrDefault(p => string.Equals(p.Id, reference.Trim(), StringComparison.Ordinal)) is { } byId) return byId;
        var key = AsturRouting.Key(reference);
        if (key.Length == 0) return null;
        var named = active.Where(p => AsturTeamRules.Names(p).Contains(key)).ToList();
        return named.Count == 1 ? named[0] : null;
    }

    public event Action? Changed;

    // ---- changes ----

    /// <summary>Makes Dextra's own profile when there is none (the first run). Idempotent.</summary>
    public async Task<TeamResult> EnsureRootAsync(CancellationToken cancellationToken = default)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Root is { } existing) return TeamResult.Done(existing);
            var root = new AssistantProfile(NewId(), AssistantNames.Assistant) { Created = options.Time.GetUtcNow() };
            if (!AssistantStore.IsId(root.Id)) return TeamResult.Refused("nth could not make an id for " + AssistantNames.Assistant + ".");
            if (await Write(root, cancellationToken, createFolders: true).ConfigureAwait(false) is { } failed) return TeamResult.Refused(failed);
            Add(root);
            return TeamResult.Done(root);
        }
        finally { one.Release(); }
    }

    /// <summary>Makes an Astur from the owner's card. The draft must have no problems; a refused one changes nothing.</summary>
    public async Task<TeamResult> CreateAsync(AsturDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var team = Profiles;
            if (AsturTeamRules.Problems(draft, team) is { Count: > 0 } found) return TeamResult.Refused(found[0]);
            var root = team.First(p => p.IsRoot);
            var id = options.NewId();
            if (!AssistantStore.IsId(id) || team.Any(p => p.Id == id)) return TeamResult.Refused("nth could not make an id for it.");
            var profile = AsturTeamRules.ToProfile(draft, id, root.Id, options.Time.GetUtcNow());
            if (SetBudget(profile) is { } budgetProblem) return TeamResult.Refused(budgetProblem);
            if (await Write(profile, cancellationToken, createFolders: true).ConfigureAwait(false) is { } failed)
            {
                options.Budgets?.RemoveCap(BudgetCapId(id));
                return TeamResult.Refused(failed);
            }
            Add(profile);
            return TeamResult.Done(profile);
        }
        finally { one.Release(); }
    }

    /// <summary>Saves the owner's edits of an Astur's card. Its id, parent, home thread and shared profile stay as they were.</summary>
    public async Task<TeamResult> UpdateAsync(string id, AsturDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var team = Profiles;
            if (team.FirstOrDefault(p => p.Id == id && !p.IsRoot && !p.Retired) is not { } current) return TeamResult.Refused("That Astur is not on the team.");
            if (AsturTeamRules.Problems(draft, team, editing: id) is { Count: > 0 } found) return TeamResult.Refused(found[0]);
            var d = draft.Normalized();
            var next = current with
            {
                Name = d.Name,
                Shape = d.Shape ?? current.Shape,
                Area = d.Area,
                Aliases = new ValueList<string>(d.Aliases),
                ClassRules = d.ClassRules,
                Style = d.Style,
                Voice = d.Voice,
                Model = d.Model,
                Duties = d.Duties,
                Wants = d.Wants,
                Budget = d.Budget,
            };
            return await Replace(current, next, cancellationToken).ConfigureAwait(false);
        }
        finally { one.Release(); }
    }

    /// <summary>
    /// Retires an Astur: it is no longer listed, routed to or delegated to, its budget slice is released, and any assistant that used its
    /// browser profile goes back to its own. Its folder and notes stay until the owner deletes them. Dextra cannot be retired.
    /// </summary>
    public async Task<TeamResult> RetireAsync(string id, CancellationToken cancellationToken = default)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var team = Profiles;
            if (team.FirstOrDefault(p => p.Id == id) is not { } current) return TeamResult.Refused("That Astur is not on the team.");
            if (current.IsRoot) return TeamResult.Refused(AssistantNames.Assistant + " cannot be retired.");
            if (current.Retired) return TeamResult.Done(current);
            var result = await Replace(current, current with { Retired = true }, cancellationToken).ConfigureAwait(false);
            if (!result.Ok) return result;
            options.Budgets?.RemoveCap(BudgetCapId(id));
            foreach (var borrower in team.Where(p => p.SharesBrowserOf == id))
                await Replace(borrower, borrower with { SharesBrowserOf = null }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally { one.Release(); }
    }

    /// <summary>The owner shares another assistant's browser profile with this one (null: back to its own). The rule is AsturIsolation's.</summary>
    public async Task<TeamResult> ShareBrowserAsync(string id, string? lenderId, CancellationToken cancellationToken = default)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var team = Profiles;
            if (team.FirstOrDefault(p => p.Id == id && !p.Retired) is not { } current) return TeamResult.Refused("That assistant is not on the team.");
            if (lenderId is not null && AsturIsolation.ShareRefusal(current, team.FirstOrDefault(p => p.Id == lenderId), team) is { } refusal) return TeamResult.Refused(refusal);
            if (lenderId is not null && team.Any(p => p.SharesBrowserOf == id)) return TeamResult.Refused($"{current.Name}'s browser profile is used by another assistant, so it cannot use one on.");
            return await Replace(current, current with { SharesBrowserOf = lenderId }, cancellationToken).ConfigureAwait(false);
        }
        finally { one.Release(); }
    }

    /// <summary>Records the assistant's home thread once nth has made it. A thread already recorded is kept.</summary>
    public async Task<TeamResult> SetHomeThreadAsync(string id, Guid thread, CancellationToken cancellationToken = default)
    {
        await one.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Get(id) is not { } current) return TeamResult.Refused("That assistant is not on the team.");
            if (current.HomeThread is not null) return TeamResult.Done(current);
            return await Replace(current, current with { HomeThread = thread }, cancellationToken).ConfigureAwait(false);
        }
        finally { one.Release(); }
    }

    // ---- the one write path ----

    private async Task<TeamResult> Replace(AssistantProfile current, AssistantProfile next, CancellationToken cancellationToken)
    {
        var budgetChanged = !Equals(current.Budget, next.Budget) && !next.IsRoot;
        if (budgetChanged)
        {
            if (SetBudget(next) is { } problem)
            {
                SetBudget(current);
                return TeamResult.Refused(problem);
            }
        }
        if (await Write(next, cancellationToken).ConfigureAwait(false) is { } failed)
        {
            if (budgetChanged) SetBudget(current);
            return TeamResult.Refused(failed);
        }
        lock (listGate)
        {
            var index = profiles.FindIndex(p => p.Id == next.Id);
            if (index >= 0) profiles[index] = next;
        }
        Changed?.Invoke();
        return TeamResult.Done(next);
    }

    private void Add(AssistantProfile profile)
    {
        lock (listGate) profiles.Add(profile);
        Changed?.Invoke();
    }

    // The Astur's slice of the day, as a standing cap the cost guard enforces at the turn boundary (13.2); any earlier slice of it is
    // replaced. Null when it is set (or no cost controls are bound), else why not: a slice that cannot be set stops the change.
    private string? SetBudget(AssistantProfile profile)
    {
        if (options.Budgets is not { } settings || profile.IsRoot) return null;
        settings.RemoveCap(BudgetCapId(profile.Id));
        if (profile.Budget is not { } budget) return null;
        var cap = new CostCap(BudgetCapId(profile.Id), CostCapScope.AssistantDay) { AssistantId = profile.Id, Percent = budget.DailyPercent, Dollars = budget.DailyDollars };
        return settings.AddCap(cap) is { } problem ? $"Its daily budget could not be set: {problem}" : null;
    }

    // Writes the profile (and, for a new Astur, its folders) off the caller's thread. Null when written, else the sentence for the card.
    private async Task<string?> Write(AssistantProfile profile, CancellationToken cancellationToken, bool createFolders = false)
    {
        System.Text.Json.Nodes.JsonObject? extra;
        lock (listGate) extras.TryGetValue(profile.Id, out extra);
        try
        {
            await Task.Run(() =>
            {
                if (createFolders)
                {
                    store.AssistantFolder(profile.Id);
                    store.MemoryFolder(profile.Id);
                    store.BrowserFolder(profile.Id);
                }
                store.SaveProfile(profile.Id, AssistantProfileFile.ToJson(profile, extra));
            }, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            return $"{profile.Name} could not be saved: {error.Message}";
        }
    }

    private string NewId() => options.NewId();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        one.Dispose();
    }
}
