// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.5; unit AS-13; ED-NEW-dextra-as13-9). No Swift twin.
// The reserve guard: it follows every account a mission uses through the plan-limit snapshots and the ledger's token spend,
// keeps the current percent between polls, and before each agent turn is sent checks the projection (estimate + this turn's
// p90 + margin) against the floor. A projection that crosses pauses that mission at the turn boundary, never mid-turn, and puts
// four options in the decision queue (ReserveGuard.Holds.cs). Everything here runs on the app's main thread; the guard's own
// state sits behind one lock, and nothing outside it (the runner, the queue, the router) is called while the lock is held.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Port.Usage;

namespace Marrow.App.Assistant;

public sealed partial class ReserveGuard : IReserveGate, IDisposable
{
    private sealed class AccountUse(ReserveAccountRef account)
    {
        public ReserveAccountRef Account { get; } = account;
        public double Tokens { get; set; }
        public Dictionary<ReserveWindowKind, double> Points { get; } = [];
    }

    private sealed class Flight(Guid mission, string accountKey, string? model)
    {
        public Guid Mission { get; } = mission;
        public string AccountKey { get; } = accountKey;
        public string? Model { get; } = model;
        public double SpentTokens { get; set; }
    }

    private sealed class MissionReserve(Guid id, string title, string assistantId, DateTimeOffset began)
    {
        public Guid Id { get; } = id;
        public string Title { get; set; } = title;
        public string AssistantId { get; } = assistantId;
        public DateTimeOffset Began { get; } = began;
        public List<ReserveRule> TaskRules { get; } = [];
        public Dictionary<(string Account, string Window), double> Baseline { get; } = [];
        public Dictionary<(string Account, string Window), double> Allowance { get; } = [];
        public Dictionary<string, AccountUse> Used { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, (double P50, double P90)> Planned { get; } = new(StringComparer.Ordinal);
        public ReservePlan? Plan { get; set; }
        public HoldState? Hold { get; set; }
    }

    private readonly ReserveGuardOptions o;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<string, ReserveAccount> accounts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ReserveAccountRef> refs = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, MissionReserve> missions = [];
    private readonly Dictionary<Guid, Flight> flights = [];
    private readonly Dictionary<Guid, ReserveAfterReport> reports = [];
    private readonly Queue<Guid> reportOrder = new();
    private bool disposed;

    /// <summary>The after-reports kept for the board (the oldest go first); a report is small, but a list nothing trims would grow for ever.</summary>
    public const int ReportsKept = 200;

    public ReserveGuard(ReserveGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        o = options;
        clock = o.Time;
        o.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>The owner's rules and switches the guard reads (the Reserves view edits them).</summary>
    public ReserveSettings Settings => o.Settings;

    /// <summary>
    /// The owner's caps (AS-15, ED-NEW-dextra-as15-4): the gate that comes after this one. A turn is asked of the reserve and then of the
    /// caps, a task a cap holds is not asked again (so it never raises a second card), and the caps hear of everything the reserve does.
    /// </summary>
    public CostGuard? Costs { get; set; }

    /// <summary>A task was paused for its reserve and its card raised (the toast and the board listen).</summary>
    public event Action<ReserveHold>? Held;

    /// <summary>A task was re-planned: a window reset, or spend ran 25% over the estimate. The line is for the task's live status.</summary>
    public event Action<Guid, string>? Replanned;

    /// <summary>A task ended and its after-report was made.</summary>
    public event Action<ReserveAfterReport>? Reported;

    /// <summary>Anything the Reserves view shows changed.</summary>
    public event Action? Changed;

    // ---- the feeds ----

    /// <summary>MultiAccountRouting: an account's plan-limit windows were polled (once a minute; the guard does not poll faster, 11.5).</summary>
    public void OnSnapshot(AccountPlanLimitsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Limits?.Windows is not { Count: > 0 } windows) return;
        var readAt = snapshot.ReadStartedAt;
        OnReadings(o.AccountOfSnapshot(snapshot.Account), [.. windows.Select(w =>
            new ReserveWindow(w.Id, ReserveWindowKinds.Of(w.DurationMinutes, w.Title), w.Percent, readAt, w.ResetsAt) { Title = w.Title })]);
    }

    /// <summary>An account's windows as a poll (or an API key's own spend) read them. A new reading replaces the estimate.</summary>
    public void OnReadings(ReserveAccountRef account, IReadOnlyList<ReserveWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(windows);
        var notes = new List<(Guid Mission, string Line)>();
        lock (gate)
        {
            if (disposed) return;
            var state = StateOf(account);
            foreach (var change in state.Observe(windows))
            {
                // An interval with outside use in it says nothing about nth's own rate (11.2).
                o.Calibrator.Observe(account.Key, change.Window, change.OutsidePoints > 0);
                if (!change.Reset) continue;
                // The window reset: "of what is left" measures from the new window, and the freed budget goes back into the plan.
                foreach (var m in missions.Values)
                {
                    m.Baseline[(account.Key, change.Window.Id)] = change.Window.Percent;
                    m.Allowance.Remove((account.Key, change.Window.Id));
                    if (m.Used.ContainsKey(account.Key))
                        notes.Add((m.Id, $"{account.Name}'s {ReserveWindowKinds.Words(change.Window.Kind)} reset · re-planned: the freed budget is back in the plan"));
                }
            }
        }
        foreach (var (mission, line) in notes) Replanned?.Invoke(mission, line);
        ReleaseWaiting(account.Key);
        Reevaluate(account.Key);
        Changed?.Invoke();
    }

    /// <summary>The ledger saw tokens spent by a chat. Every chat on the account counts: they all move the window.</summary>
    public void OnSpend(Guid thread, string? model, long tokens)
    {
        if (tokens <= 0) return;
        if (o.AccountOf(thread) is not { } account) return;
        Guid? mission;
        lock (gate) mission = flights.TryGetValue(thread, out var flight) ? flight.Mission : null;
        mission ??= o.Runner.MissionOf(thread)?.Id;
        var now = clock.GetUtcNow();
        string? replan = null;
        Guid replanned = default;
        lock (gate)
        {
            if (disposed) return;
            var state = StateOf(account);
            var inFlight = flights.TryGetValue(thread, out var f);
            state.Spend(now, tokens, model, inFlight ? thread : null);
            o.Calibrator.Spend(account.Key, model, tokens);
            if (inFlight) f!.SpentTokens += tokens;
            if (mission is { } id && missions.TryGetValue(id, out var m))
            {
                Attribute(m, account, state, model, tokens);
                replan = CheckOverEstimate(m, account, state, now);
                replanned = m.Id;
            }
        }
        if (replan is not null) Replanned?.Invoke(replanned, replan);
        Reevaluate(account.Key);
        Changed?.Invoke();
    }

    private void Attribute(MissionReserve m, ReserveAccountRef account, ReserveAccount state, string? model, long tokens)
    {
        if (!m.Used.TryGetValue(account.Key, out var use)) m.Used[account.Key] = use = new AccountUse(account);
        use.Tokens += tokens;
        foreach (var window in state.Windows)
        {
            var rate = o.Calibrator.RateFor(account.Key, account.ProviderId, model, window.Kind);
            use.Points[window.Kind] = use.Points.GetValueOrDefault(window.Kind) + tokens / rate.TokensPerPercent;
        }
    }

    // ---- turns ----

    public void TurnStarted(Guid mission, Guid thread, string? model)
    {
        StartTurn(mission, thread, model);
        Costs?.TurnStarted(mission, thread, model);
    }

    private void StartTurn(Guid mission, Guid thread, string? model)
    {
        if (o.AccountOf(thread) is not { } account) return;
        var p90 = o.Turns.P90(model);
        lock (gate)
        {
            if (disposed) return;
            var state = StateOf(account);
            if (flights.TryGetValue(thread, out var existing))
            {
                if (existing.AccountKey == account.Key) return; // already counted: the runner and the provider tap both report it
                accounts[existing.AccountKey].TurnEnded(thread);
            }
            flights[thread] = new Flight(mission, account.Key, model);
            state.TurnStarted(thread, model, p90);
            if (missions.TryGetValue(mission, out var m) && !m.Used.ContainsKey(account.Key)) m.Used[account.Key] = new AccountUse(account);
        }
    }

    public void TurnEnded(Guid thread)
    {
        EndTurn(thread);
        Costs?.TurnEnded(thread);
    }

    private void EndTurn(Guid thread)
    {
        double spent;
        string? model;
        lock (gate)
        {
            if (!flights.Remove(thread, out var flight)) return;
            if (accounts.TryGetValue(flight.AccountKey, out var state)) state.TurnEnded(thread);
            spent = flight.SpentTokens;
            model = flight.Model;
        }
        // What the turn cost is what the next turn like it is expected to cost.
        if (spent > 0) o.Turns.Record(model, (long)Math.Round(spent), clock.GetUtcNow());
    }

    // ---- the gate ----

    /// <summary>
    /// Before each turn is sent (11.5): the projection of the account the turn runs on, against the floor. True: send it. False:
    /// the mission is held; if its reserve is the reason, it is Paused (a turn running elsewhere in it finishes first) and the
    /// owner's card is in the queue. Then the owner's caps are asked (AS-15): a task a cap holds is not asked of the reserve again.
    /// </summary>
    public bool BeforeTurn(MissionRecord mission, Guid? thread, ProviderKind provider, string? model, string purpose, ReserveAccountRef? onAccount = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (Costs is not { } costs) return BeforeTurnReserve(mission, thread, model, purpose, onAccount);
        if (costs.IsHeld(mission.Id)) return false;
        return BeforeTurnReserve(mission, thread, model, purpose, onAccount) && costs.BeforeTurn(mission, thread, provider, model, purpose, onAccount);
    }

    private bool BeforeTurnReserve(MissionRecord mission, Guid? thread, string? model, string purpose, ReserveAccountRef? onAccount)
    {
        var account = (thread is { } t ? o.AccountOf(t) : null) ?? onAccount ?? o.AccountForNew(mission);
        var now = clock.GetUtcNow();
        ReserveCheck check;
        double p90;
        lock (gate)
        {
            if (disposed) return true;
            var m = MissionOf(mission);
            if (m.Hold is not null) return false; // already held: the owner's answer lets it go
            if (account is null || !accounts.TryGetValue(account.Key, out var state)) return true; // nothing known of this account: nothing to keep
            p90 = o.Turns.P90(model);
            check = state.Check(window => FloorFor(m, account, state, window, now), model, p90, now);
        }
        if (check.Go) return true;
        RaiseHold(mission, account!, check, p90, model, purpose, note: null);
        return false;
    }

    // ---- the pause check between turns ----

    // A running mission whose account has no room even for what its turns have left is paused at its turn boundary (11.5: "re-plans
    // when a window resets or an estimate is off, and pauses at a turn boundary before any agent would cross a floor").
    private void Reevaluate(string accountKey)
    {
        var due = new List<(Guid Mission, ReserveAccountRef Account, ReserveCheck Check, string? Model)>();
        var now = clock.GetUtcNow();
        lock (gate)
        {
            if (disposed || !accounts.TryGetValue(accountKey, out var state)) return;
            foreach (var group in flights.Values.Where(f => f.AccountKey == accountKey).GroupBy(f => f.Mission))
            {
                if (!missions.TryGetValue(group.Key, out var m) || m.Hold is not null) continue;
                var account = refs[accountKey];
                var model = group.First().Model;
                var check = state.Check(window => FloorFor(m, account, state, window, now), model, 0, now);
                if (!check.Go) due.Add((m.Id, account, check, model));
            }
        }
        foreach (var (mission, account, check, model) in due)
        {
            if (o.Runner.Find(mission) is not { } record || record.Mission.PausedBy is not null) continue;
            if (record.Mission.State is not (MissionState.Working or MissionState.Reviewing or MissionState.Planning)) continue;
            RaiseHold(record, account, check, o.Turns.P90(model), model, "running", note: null);
        }
    }

    private void OnSettingsChanged()
    {
        List<string> keys;
        lock (gate) keys = [.. accounts.Keys];
        foreach (var key in keys) Reevaluate(key);
        Changed?.Invoke();
    }

    // ---- floors ----

    private ReserveFloor? FloorFor(MissionReserve m, ReserveAccountRef account, ReserveAccount state, ReserveWindow window, DateTimeOffset now, bool withAllowance = true)
    {
        var subject = new ReserveSubject(account.Key, account.Name, account.ProviderId, m.AssistantId);
        var inputs = new List<ReserveFloorInput>();
        double? start = null;
        foreach (var rule in o.Settings.Rules.Concat(m.TaskRules))
        {
            if (!rule.Scope.Covers(subject)) continue;
            start ??= BaselineOf(m, account.Key, window, state, now);
            inputs.Add(new ReserveFloorInput(rule, start.Value));
        }
        var allowance = withAllowance ? m.Allowance.GetValueOrDefault((account.Key, window.Id)) : 0;
        return ReserveMath.EffectiveFloor(inputs, window, now, o.Settings.HoldReserves, allowance);
    }

    // "Of what is left" measures from the window as the mission first met it.
    private static double BaselineOf(MissionReserve m, string accountKey, ReserveWindow window, ReserveAccount state, DateTimeOffset now)
    {
        if (!m.Baseline.TryGetValue((accountKey, window.Id), out var start)) m.Baseline[(accountKey, window.Id)] = start = state.Estimate(window.Id, now);
        return start;
    }

    // ---- bookkeeping ----

    private ReserveAccount StateOf(ReserveAccountRef account)
    {
        if (accounts.TryGetValue(account.Key, out var state))
        {
            refs[account.Key] = account; // the owner may have renamed it
            return state;
        }
        refs[account.Key] = account;
        return accounts[account.Key] = new ReserveAccount(account.Key, account.Name, account.ProviderId,
            (model, kind) => o.Calibrator.RateFor(account.Key, account.ProviderId, model, kind));
    }

    private MissionReserve MissionOf(MissionRecord record)
    {
        if (missions.TryGetValue(record.Id, out var m)) return m;
        m = new MissionReserve(record.Id, record.Mission.Title, record.Mission.AssistantId, clock.GetUtcNow());
        missions[record.Id] = m;
        return m;
    }

    /// <summary>
    /// The task rules the owner spoke for this task ("keep 30% of what's left"), read by the plan and the pause rule. Task rules
    /// are exact: they are never eased, and they end with the task.
    /// </summary>
    public void SetTaskRules(MissionRecord mission, IReadOnlyList<ReserveRule> rules)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(rules);
        lock (gate)
        {
            var m = MissionOf(mission);
            m.TaskRules.Clear();
            m.TaskRules.AddRange(rules.Where(r => r.Lifetime == ReserveLifetime.Task).Select(r => r.Normalized()));
        }
    }

    public void MissionBegan(MissionRecord mission, ReserveStart? start)
    {
        BeginMission(mission, start);
        Costs?.MissionBegan(mission, start);
    }

    private void BeginMission(MissionRecord mission, ReserveStart? start)
    {
        ArgumentNullException.ThrowIfNull(mission);
        lock (gate)
        {
            if (disposed) return;
            MissionOf(mission);
        }
        if (start is null) return;
        if (start.Plan is { } plan) Begin(mission, plan, start.Steps, start.TaskRules);
        else SetTaskRules(mission, start.TaskRules);
    }

    public void MissionStopped(Guid mission)
    {
        lock (gate)
        {
            if (missions.TryGetValue(mission, out var m) && m.Hold is { } hold) CancelWait(hold);
        }
        Costs?.MissionStopped(mission);
    }

    public void MissionEnded(Guid mission)
    {
        EndMission(mission);
        Costs?.MissionEnded(mission);
    }

    private void EndMission(Guid mission)
    {
        MissionReserve? m;
        Guid? card = null;
        ReserveAfterReport report;
        lock (gate)
        {
            if (!missions.Remove(mission, out m)) return;
            if (m.Hold is { } hold)
            {
                CancelWait(hold);
                card = hold.Card;
            }
            report = BuildReport(m, clock.GetUtcNow());
            reports[mission] = report;
            reportOrder.Enqueue(mission);
            while (reportOrder.Count > ReportsKept) reports.Remove(reportOrder.Dequeue());
            foreach (var thread in flights.Where(f => f.Value.Mission == mission).Select(f => f.Key).ToList())
            {
                if (flights.Remove(thread, out var flight) && accounts.TryGetValue(flight.AccountKey, out var state)) state.TurnEnded(thread);
            }
        }
        if (card is { } id) o.Queue.Withdraw(id, "The task ended.");
        Reported?.Invoke(report);
        Changed?.Invoke();
    }

    /// <summary>The after-report of an ended task, if the guard made one.</summary>
    public ReserveAfterReport? ReportOf(Guid mission)
    {
        lock (gate) return reports.GetValueOrDefault(mission);
    }

    public void Dispose()
    {
        List<HoldState> waiting;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            waiting = [.. missions.Values.Select(m => m.Hold).OfType<HoldState>()];
        }
        o.Settings.Changed -= OnSettingsChanged;
        foreach (var hold in waiting) lock (gate) CancelWait(hold);
    }
}
