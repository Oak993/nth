using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>An offer to move the rest of a task, and what moving it does (option 1 of the pause card).</summary>
internal sealed class FakeMover : IReserveMover
{
    public ReserveMoveOffer? Offering { get; set; }

    public string? Refusal { get; set; }

    public List<ReserveMoveOffer> Moved { get; } = [];

    public List<IReadOnlyList<ReserveCandidate>> Seen { get; } = [];

    public ReserveMoveOffer? Offer(Marrow.Services.Assistant.MissionRecord mission, IReadOnlyList<ReserveCandidate> candidates)
    {
        Seen.Add(candidates);
        return Offering;
    }

    public string? Move(Marrow.Services.Assistant.MissionRecord mission, ReserveMoveOffer offer)
    {
        Moved.Add(offer);
        return Refusal;
    }
}

/// <summary>
/// Dextra AS-13: a mission runner over fake chats with the usage reserve bound to it. One account, "Claude 20x", whose
/// rate is 100,000 tokens per percent with no spread (so a token count reads straight off as points), and a ten-turn history
/// of 100,000-token turns (so a new turn's p90 is 131,623 tokens, 1.3162 points, and counted at 1.5 times that).
/// The clock is the test's.
/// </summary>
internal sealed class ReserveRig : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    public static readonly ReserveAccountRef Claude20 = new("claude:20x", "Claude 20x", "claude");

    public static readonly ReserveAccountRef CodexA = new("codex:a", "Codex A", "codex");

    public const string Model = "model-a";

    /// <summary>A new 100,000-token turn's p90 in points: 100,000 x (1 + 1/sqrt(10)) / 100,000, then x 1.5 for its tail.</summary>
    public static readonly double TurnPoints = (1 + 1 / Math.Sqrt(10)) * 1.5;

    /// <param name="movers">A mover made over the rig's runner and clock (AS-14: the router), used when no fake mover is given.</param>
    public ReserveRig(string root, FakeMover? mover = null, MissionLimits? limits = null, Func<MissionRig, ManualTimeProvider, IReserveMover?>? movers = null)
    {
        Mover = mover;
        Missions = new MissionRig(root, limits: limits);
        Clock = new ManualTimeProvider(T0);
        Calibrator = new UsageCalibrator(Clock);
        Turns = new TurnCostHistory(Clock);
        foreach (var account in new[] { Claude20, CodexA }) Calibrate(account);
        for (var i = 0; i < 10; i++) Turns.Record(Model, 100_000, T0);
        Guard = new ReserveGuard(new ReserveGuardOptions
        {
            Runner = Missions.Runner,
            Queue = Missions.Queue,
            Settings = Settings,
            Calibrator = Calibrator,
            Turns = Turns,
            AccountOf = thread => AccountOfThread.TryGetValue(thread, out var account) ? account : DefaultAccount,
            AccountForNew = record => record.AccountId is { } chosen && AccountById.TryGetValue(chosen, out var routed) ? routed : DefaultAccount,
            AccountOfSnapshot = key => key.AccountId == "20x" ? Claude20 : CodexA,
            Mover = (IReserveMover?)Mover ?? movers?.Invoke(Missions, Clock),
            IsPinned = record => Pinned(record),
            Refresh = account =>
            {
                lock (Refreshed) Refreshed.Add(account);
                return Task.CompletedTask;
            },
            Audit = entry =>
            {
                if (AuditFails) throw new IOException("The activity log's disk is full.");
                lock (Audit) Audit.Add(entry);
            },
            Time = Clock,
        });
        Missions.Runner.Reserve = Guard;
        Guard.Held += Holds.Add;
        Guard.Replanned += (mission, line) => Replans.Add((mission, line));
        Guard.Reported += Reports.Add;
        Missions.Runner.Faulted += Faults.Add;
    }

    public MissionRig Missions { get; }

    public ManualTimeProvider Clock { get; }

    public ReserveSettings Settings { get; } = new();

    public UsageCalibrator Calibrator { get; }

    public TurnCostHistory Turns { get; }

    public ReserveGuard Guard { get; }

    public FakeMover? Mover { get; }

    public ReserveAccountRef? DefaultAccount { get; set; } = Claude20;

    public Dictionary<Guid, ReserveAccountRef> AccountOfThread { get; } = [];

    /// <summary>The accounts a mission the router moved or placed runs on, by the account store's id (AS-14).</summary>
    public Dictionary<Guid, ReserveAccountRef> AccountById { get; } = [];

    public List<ReserveHold> Holds { get; } = [];

    public List<(Guid Mission, string Line)> Replans { get; } = [];

    public List<ReserveAfterReport> Reports { get; } = [];

    public List<string> Faults { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    public bool AuditFails { get; set; }

    public Func<MissionRecord, bool> Pinned { get; set; } = _ => false;

    public List<ReserveAccountRef> Refreshed { get; } = [];

    public void Dispose() => Guard.Dispose();

    /// <summary>Five qualifying intervals, so the account is calibrated at exactly 100,000 tokens per percent.</summary>
    public void Calibrate(ReserveAccountRef account)
    {
        var window = "cal-" + account.Key;
        var percent = 10.0;
        var at = T0.AddHours(-2);
        Calibrator.Observe(account.Key, new ReserveWindow(window, ReserveWindowKind.FiveHour, percent, at, T0.AddHours(3)), false);
        for (var i = 0; i < 5; i++)
        {
            Calibrator.Spend(account.Key, Model, 300_000);
            percent += 3;
            at = at.AddMinutes(10);
            Calibrator.Observe(account.Key, new ReserveWindow(window, ReserveWindowKind.FiveHour, percent, at, T0.AddHours(3)), false);
        }
    }

    public ReserveWindow Window(double percent, ReserveWindowKind kind = ReserveWindowKind.FiveHour, DateTimeOffset? resets = null, DateTimeOffset? at = null) =>
        new(kind == ReserveWindowKind.FiveHour ? "five" : "week", kind, percent, at ?? Clock.GetUtcNow(), resets ?? Clock.GetUtcNow().AddHours(kind == ReserveWindowKind.FiveHour ? 3 : 72))
        { Title = kind == ReserveWindowKind.FiveHour ? "5-hour limit" : "Weekly limit" };

    /// <summary>A poll of the account now.</summary>
    public void Read(double fiveHour, double? weekly = null, ReserveAccountRef? account = null, DateTimeOffset? resets = null)
    {
        var windows = new List<ReserveWindow> { Window(fiveHour, resets: resets) };
        if (weekly is { } w) windows.Add(Window(w, ReserveWindowKind.Weekly));
        Guard.OnReadings(account ?? Claude20, windows);
    }

    /// <summary>The owner's standing rule: always keep this much of the whole window on Claude 20x.</summary>
    public void Always(double keep, string account = "Claude 20x", ReserveWindows windows = ReserveWindows.Both) =>
        Assert.Null(Settings.Add(new ReserveRule("", ReserveScope.Named(account), ReserveLifetime.Standing) { KeepPercent = keep, Windows = windows }));

    public MissionRecord Started(string title = "Fix the login timeout") => Missions.Started(Missions.Request(title));

    public DecisionItem? Card(MissionRecord record) => Missions.Queue.Of(record.Id).FirstOrDefault(c => c.Kind == DecisionKind.Reserve);

    public string? Pick(DecisionItem card, ReserveOptionKind kind)
    {
        var hold = Guard.HoldOf(card.MissionId!.Value) ?? throw new InvalidOperationException("no hold");
        return Missions.Queue.Answer(card.Id, DecisionAnswer.Pick(hold.Options.Single(o => o.Kind == kind).Text));
    }

    public static string Clock24(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
