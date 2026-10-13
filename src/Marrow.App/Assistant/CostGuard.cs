// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps"; unit AS-15; ED-NEW-dextra-as15-4). No Swift twin.
// The cost guard: it follows what each mission and each assistant spends (points of every account window, and dollars on API
// keys) and, before each agent turn is sent, checks it against the owner's caps: this task's, the assistant's for the day, and the
// API keys' dollars for the day. A turn that would pass a cap pauses its mission at the turn boundary (never mid-turn) and puts the
// options in the decision queue (CostGuard.Holds.cs), the way the usage reserve does. The runner has one gate, the reserve, and the
// reserve asks this guard once it has said yes (ReserveGuard.Costs), so a mission is asked the reserve and then its caps. Everything
// here runs on the app's main thread; the guard's own state
// sits behind one lock, and nothing outside it (the runner, the queue, the ledger's events) is called while the lock is held.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class CostGuard : IDisposable
{
    private sealed class Flight(Guid mission, ReserveAccountRef account, string? model)
    {
        public Guid Mission { get; } = mission;
        public ReserveAccountRef Account { get; } = account;
        public string? Model { get; } = model;
    }

    /// <summary>What the owner agreed to over one cap this once: points per account window and dollars, for the day it was agreed on.</summary>
    private sealed class Allow(DateOnly day)
    {
        public DateOnly Day { get; } = day;
        public Dictionary<(string Account, ReserveWindowKind Kind), double> Points { get; } = [];
        public decimal Dollars { get; set; }
    }

    private sealed class MissionCost(Guid id, string title, string assistantId)
    {
        public Guid Id { get; } = id;
        public string Title { get; set; } = title;
        public string AssistantId { get; } = assistantId;
        public List<CostCap> TaskCaps { get; } = [];

        /// <summary>The mission's own dollar budget (start_mission's budget_usd, journaled with it): a cap in dollars for this task.</summary>
        public CostCap? Budget { get; set; }

        public CapUse Use { get; } = new();
        public Dictionary<string, Allow> Allowed { get; } = new(StringComparer.Ordinal);
        public HoldState? Hold { get; set; }
    }

    private readonly CostGuardOptions o;
    private readonly TimeProvider clock;
    private readonly object gate = new();
    private readonly Dictionary<Guid, MissionCost> missions = [];
    private readonly Dictionary<Guid, Flight> flights = [];
    private bool disposed;

    public CostGuard(CostGuardOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        o = options;
        clock = o.Time;
        o.Settings.Changed += OnSettingsChanged;
    }

    /// <summary>The owner's standing caps (the Reserves view edits them).</summary>
    public ReserveSettings Settings => o.Settings;

    /// <summary>A task was paused at a cap and its card raised (the toast and the board listen).</summary>
    public event Action<CapHold>? Held;

    /// <summary>Anything the Reserves view shows changed.</summary>
    public event Action? Changed;

    // ---- the feeds ----

    /// <summary>The ledger saw a chat spend tokens (ProviderEvent.TokenSpend); the guard prices them. Every chat counts, a mission's or the assistant's own.</summary>
    public void OnSpend(Guid thread, TokenSpend spend)
    {
        ArgumentNullException.ThrowIfNull(spend);
        decimal? dollars = null;
        if (o.AccountOf(thread) is { } account && IsApiKey(account) && o.Prices?.Invoke(account, spend.Model) is { } prices)
            dollars = CostPricing.Dollars(prices, spend.TotalTokens, spend.InputTokens, spend.OutputTokens, spend.CachedInputTokens);
        OnSpend(thread, spend.Model, spend.TotalTokens, dollars);
    }

    /// <summary>
    /// A chat spent <paramref name="tokens"/>. On a subscription account that is points of each of its windows (the calibrated rate,
    /// as the reserve counts them); on an API key it is <paramref name="dollars"/> or, when the caller has no figure, the model's price
    /// or, when that is not known either, a deliberately dear one.
    /// </summary>
    public void OnSpend(Guid thread, string? model, long tokens, decimal? dollars = null)
    {
        if (tokens <= 0 || o.AccountOf(thread) is not { } account) return;
        var record = o.Runner.MissionOf(thread);
        var assistant = record?.Mission.AssistantId ?? o.AssistantOf?.Invoke(thread);
        if (assistant is null) return;
        var api = IsApiKey(account);
        var points = new List<(ReserveWindowKind Kind, double Points)>();
        if (!api)
            foreach (var kind in Kinds(account))
                points.Add((kind, o.Calibrator.RateFor(account.Key, account.ProviderId, model, kind).Percent(tokens)));
        var money = api ? dollars ?? tokens * PerMillion(account, model) / 1_000_000m : 0m;

        foreach (var (kind, amount) in points) o.Ledger.AddPoints(assistant, account.Key, account.Name, api, kind, amount);
        if (money > 0) o.Ledger.AddDollars(assistant, account.Key, account.Name, money);
        if (record is not null)
        {
            lock (gate)
            {
                if (disposed) return;
                var m = MissionOf(record);
                foreach (var (kind, amount) in points) m.Use.Add(account.Key, kind, amount, account.Name);
                m.Use.AddDollars(money);
            }
        }
        Reevaluate();
        Changed?.Invoke();
    }

    // ---- turns ----

    public void TurnStarted(Guid mission, Guid thread, string? model)
    {
        if (o.AccountOf(thread) is not { } account) return;
        lock (gate)
        {
            if (disposed) return;
            flights[thread] = new Flight(mission, account, model);
        }
    }

    public void TurnEnded(Guid thread)
    {
        lock (gate) flights.Remove(thread);
    }

    // ---- the gate ----

    /// <summary>
    /// Before each turn is sent: the use of every cap that covers the account, plus this turn's p90, against the cap. True: send
    /// it. False: the mission is held (paused at its cap with the owner's card in the queue, or his own pause stands).
    /// <paramref name="onAccount"/> is the account the turn will run on when that is not the mission's (a reviewer on another
    /// account). The reserve calls this once it has said yes (ReserveGuard.Costs); the guard is not itself the runner's gate.
    /// </summary>
    public bool BeforeTurn(MissionRecord mission, Guid? thread, ProviderKind provider, string? model, string purpose, ReserveAccountRef? onAccount = null)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var account = (thread is { } t ? o.AccountOf(t) : null) ?? onAccount ?? o.AccountForNew(mission);
        Crossing? crossed;
        lock (gate)
        {
            if (disposed) return true;
            var m = MissionOf(mission);
            if (m.Hold is not null) return false; // already held: the owner's answer lets it go
            if (account is null) return true;     // nothing known of this account: nothing to count it against
            crossed = Evaluate(m, account, model, withTurn: true);
        }
        if (crossed is not { } hit) return true;
        RaiseHold(mission, hit.Cap, hit.Reach, hit.Account, model, purpose, note: null);
        return false;
    }

    /// <summary>Whether a cap holds the mission now: the reserve asks this before it asks itself, so a held task never raises a second card.</summary>
    public bool IsHeld(Guid mission)
    {
        lock (gate) return missions.TryGetValue(mission, out var m) && m.Hold is not null;
    }

    /// <summary>The cap that holds the work, where, and the account the turn runs on.</summary>
    private sealed record Crossing(CostCap Cap, CapReach Reach, ReserveAccountRef Account);

    // The tightest cap the next turn would pass (or, between turns, that the use has reached), or null. Under the lock.
    private Crossing? Evaluate(MissionCost m, ReserveAccountRef account, string? model, bool withTurn)
    {
        var api = IsApiKey(account);
        var subject = new CapSubject(m.AssistantId, account.Key, account.Name, api);
        var today = o.Ledger.Today;
        CapTurn? turn = withTurn ? TurnOf(account, model, api) : null;
        (CostCap Cap, CapVerdict Verdict)? worst = null;
        var caps = new List<CostCap>(m.TaskCaps);
        if (m.Budget is not null) caps.Add(m.Budget);
        caps.AddRange(o.Settings.Caps);
        foreach (var cap in caps)
        {
            if (!cap.Limits || !cap.Covers(subject)) continue;
            // A task's cap counts the task; a cap that names no assistant limits each on its own, so it counts only this assistant's day.
            var use = cap.Scope == CostCapScope.Task ? m.Use : o.Ledger.UseOf(cap, today, cap.Scope == CostCapScope.AssistantDay ? m.AssistantId : null);
            // Between turns the cap is asked of the account the turn in flight runs on (ED-NEW-dextra-as15-11).
            var verdict = CostCapMath.Check(cap, use, turn, AllowanceOf(m, cap, today), turn is null ? new CapAccount(account.Key, api) : null);
            if (!verdict.Crossed || verdict.Reach is not { } reach) continue;
            if (worst is null || reach.Room(withTurn) < worst.Value.Verdict.Reach!.Room(withTurn)) worst = (cap, verdict);
        }
        return worst is { } w ? new Crossing(w.Cap, w.Verdict.Reach!, account) : null;
    }

    private CapTurn TurnOf(ReserveAccountRef account, string? model, bool api)
    {
        var tokens = o.Turns.P90(model);
        var points = new Dictionary<ReserveWindowKind, double>();
        if (!api)
            foreach (var kind in Kinds(account))
                points[kind] = o.Calibrator.RateFor(account.Key, account.ProviderId, model, kind).PercentP90(tokens);
        var dollars = api ? (decimal)tokens * PerMillion(account, model) / 1_000_000m : 0m;
        return new CapTurn(account.Key, account.Name, points, dollars);
    }

    // What a million tokens of this model cost: its price, or the dear one counted for a model whose price is not known.
    private decimal PerMillion(ReserveAccountRef account, string? model) =>
        o.Prices?.Invoke(account, model) is { } prices ? CostPricing.Blended(prices) : CostPricing.ConservativePerMillion;

    // What the next turn on this account is expected to use of the window (or of the money) a reach is about; 0 for another account's.
    private double NeedsFor(ReserveAccountRef account, string? model, CapReach reach)
    {
        if (reach.AccountKey != account.Key && !reach.IsDollars) return 0;
        var turn = TurnOf(account, model, IsApiKey(account));
        return reach.Window is { } kind ? turn.Points.GetValueOrDefault(kind) : (double)turn.Dollars;
    }

    private static CapAllowance AllowanceOf(MissionCost m, CostCap cap, DateOnly today)
    {
        if (!m.Allowed.TryGetValue(cap.Id, out var allow) || (cap.IsDaily && allow.Day != today)) return CapAllowance.None;
        return new CapAllowance(allow.Points, allow.Dollars);
    }

    // ---- the check between turns ----

    // A running mission whose caps are already reached is paused at its turn boundary: its running turn finishes, nothing is cut.
    private void Reevaluate()
    {
        var due = new List<(Guid Mission, Crossing Hit, string? Model)>();
        lock (gate)
        {
            if (disposed) return;
            foreach (var flight in flights.Values)
            {
                if (!missions.TryGetValue(flight.Mission, out var m) || m.Hold is not null || due.Any(d => d.Mission == m.Id)) continue;
                if (Evaluate(m, flight.Account, flight.Model, withTurn: false) is { } hit) due.Add((m.Id, hit, flight.Model));
            }
        }
        foreach (var (mission, hit, model) in due)
        {
            if (o.Runner.Find(mission) is not { } record) continue;
            // Between turns the cap is reached; the card also says what the next step needs, so "allow it once" covers that step too.
            var reach = hit.Reach with { Needs = NeedsFor(hit.Account, model, hit.Reach) };
            RaiseHold(record, hit.Cap, reach, hit.Account, model, "running", note: null);
        }
    }

    private void OnSettingsChanged()
    {
        Reevaluate();
        Changed?.Invoke();
    }

    // ---- bookkeeping ----

    private MissionCost MissionOf(MissionRecord record)
    {
        if (missions.TryGetValue(record.Id, out var m)) return m;
        m = new MissionCost(record.Id, record.Mission.Title, record.Mission.AssistantId);
        // A mission that was started with a budget in dollars (13.2 "Per mission: ... dollars") carries it in its record, so a mission that
        // comes back after a restart is held to it again; what it had spent before the restart is not known, so the count starts at 0.
        if (record.BudgetUsd is > 0) m.Budget = new CostCap("budget", CostCapScope.Task) { Dollars = record.BudgetUsd };
        missions[record.Id] = m;
        return m;
    }

    /// <summary>The caps the owner set for this task; they bind from now on and end with it.</summary>
    public void SetTaskCaps(MissionRecord mission, IReadOnlyList<CostCap> caps)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(caps);
        lock (gate)
        {
            var m = MissionOf(mission);
            m.TaskCaps.Clear();
            foreach (var cap in caps.Where(c => c.Scope == CostCapScope.Task && c.Limits).Take(ReserveSettings.MaxCaps))
                m.TaskCaps.Add(cap.Normalized() with { Id = string.IsNullOrWhiteSpace(cap.Id) ? Guid.NewGuid().ToString("N") : cap.Id });
        }
    }

    public void MissionBegan(MissionRecord mission, ReserveStart? start)
    {
        ArgumentNullException.ThrowIfNull(mission);
        lock (gate)
        {
            if (disposed) return;
            MissionOf(mission);
        }
        if (start is { TaskCaps.Count: > 0 }) SetTaskCaps(mission, start.TaskCaps);
    }

    public void MissionStopped(Guid mission)
    {
        lock (gate)
        {
            if (missions.TryGetValue(mission, out var m) && m.Hold is { } hold) CancelWait(hold);
        }
    }

    public void MissionEnded(Guid mission)
    {
        Guid? card = null;
        lock (gate)
        {
            if (!missions.Remove(mission, out var m)) return;
            if (m.Hold is { } hold)
            {
                CancelWait(hold);
                card = hold.Card == Guid.Empty ? null : hold.Card;
            }
            foreach (var thread in flights.Where(f => f.Value.Mission == mission).Select(f => f.Key).ToList()) flights.Remove(thread);
        }
        if (card is { } id) o.Queue.Withdraw(id, "The task ended.");
        Changed?.Invoke();
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

    // ---- helpers ----

    /// <summary>An API key has money, not windows (11.2): a percent cap does not count it, a dollar cap does.</summary>
    public static bool IsApiKey(ReserveAccountRef account) =>
        SwiftEnum<ProviderKind>.FromRawValue(account.ProviderId) is { } kind && SwarmPlaces.Of(kind) == SwarmPlace.Api;

    private IReadOnlyList<ReserveWindowKind> Kinds(ReserveAccountRef account) =>
        o.WindowsOf?.Invoke(account.Key) is { Count: > 0 } known ? known : [ReserveWindowKind.FiveHour, ReserveWindowKind.Weekly];

    /// <summary>The board's line for a task a cap holds: "Paused at your cap of 5% for this task".</summary>
    public static string LineOf(CostCap cap) =>
        $"Paused at your cap of {CostCapWords.Amount(cap.Normalized())}{(cap.IsDaily ? " a day" : " for this task")}";
}
