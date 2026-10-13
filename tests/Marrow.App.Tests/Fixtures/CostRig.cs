using Marrow.App.Assistant;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-15: a mission runner over fake chats with the cost guard bound to it. "Claude 20x" and "Codex A" are subscription accounts whose
/// 5-hour rate is 100,000 tokens per percent with no spread, and "OpenRouter" is an API key priced at $10 a million tokens (input and output
/// alike). A ten-turn history of 100,000-token turns gives a new turn a p90 of 131,623 tokens: 1.3162 points on a subscription, $1.32 on the key.
/// The clock is the test's; local time is UTC.
/// </summary>
internal sealed class CostRig : IDisposable
{
    public static readonly DateTimeOffset T0 = new(2026, 10, 13, 9, 0, 0, TimeSpan.Zero);

    public static readonly ReserveAccountRef Claude20 = new("claude:20x", "Claude 20x", "claude");

    public static readonly ReserveAccountRef CodexA = new("codex:a", "Codex A", "codex");

    public static readonly ReserveAccountRef Key = new("openrouter:key", "OpenRouter", "openrouter");

    /// <summary>An API key whose model has no known price: its tokens are counted at a dear one.</summary>
    public static readonly ReserveAccountRef Unpriced = new("deepseek:key", "DeepSeek", "deepseek");

    public const string Model = "model-a";

    /// <summary>A new 100,000-token turn's p90 in points: 100,000 x (1 + 1/sqrt(10)) / 100,000.</summary>
    public static readonly double TurnPoints = 1 + 1 / Math.Sqrt(10);

    public static readonly IgnisPrices TenDollars = new(10m, 10m, 1m, null, null, ValueList<IgnisPriceStep>.Empty);

    public CostRig(string root, MissionLimits? limits = null)
    {
        Missions = new MissionRig(root, limits: limits);
        Clock = new ManualTimeProvider(T0);
        Calibrator = new UsageCalibrator(Clock);
        Turns = new TurnCostHistory(Clock);
        Ledger = new CostLedger(Clock);
        foreach (var account in new[] { Claude20, CodexA }) Calibrate(account);
        for (var i = 0; i < 10; i++) Turns.Record(Model, 100_000, T0);
        Guard = new CostGuard(new CostGuardOptions
        {
            Runner = Missions.Runner,
            Queue = Missions.Queue,
            Settings = Settings,
            Ledger = Ledger,
            Calibrator = Calibrator,
            Turns = Turns,
            AccountOf = thread => AccountOfThread.TryGetValue(thread, out var account) ? account : DefaultAccount,
            AccountForNew = _ => DefaultAccount,
            WindowsOf = _ => [ReserveWindowKind.FiveHour],
            Prices = (account, _) => account.Key == Key.Key ? TenDollars : null,
            AssistantOf = thread => HomeThreads.GetValueOrDefault(thread),
            AssistantName = id => id == "channel-a" ? "Channel A" : null,
            Audit = entry =>
            {
                if (AuditFails) throw new IOException("The activity log's disk is full.");
                lock (Audit) Audit.Add(entry);
            },
            Time = Clock,
        });
        Missions.Runner.Reserve = new CostGuardGate(Guard);
        Guard.Held += Holds.Add;
        Missions.Runner.Faulted += Faults.Add;
    }

    public MissionRig Missions { get; }

    public ManualTimeProvider Clock { get; }

    public ReserveSettings Settings { get; } = new();

    public UsageCalibrator Calibrator { get; }

    public TurnCostHistory Turns { get; }

    public CostLedger Ledger { get; }

    public CostGuard Guard { get; }

    public ReserveAccountRef? DefaultAccount { get; set; } = Claude20;

    public Dictionary<Guid, ReserveAccountRef> AccountOfThread { get; } = [];

    /// <summary>The chats that belong to an assistant but to no mission (its home thread): their spend counts toward its day.</summary>
    public Dictionary<Guid, string> HomeThreads { get; } = [];

    public List<CapHold> Holds { get; } = [];

    public List<string> Faults { get; } = [];

    public List<AuditEntry> Audit { get; } = [];

    public bool AuditFails { get; set; }

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

    /// <summary>The owner's standing cap on an assistant's day.</summary>
    public void DailyCap(double percent, string? assistant = "dextra", decimal? dollars = null) =>
        Assert.Null(Settings.AddCap(new CostCap("", CostCapScope.AssistantDay) { Percent = percent, Dollars = dollars, AssistantId = assistant }));

    /// <summary>The owner's standing cap on the API keys' dollars for the day.</summary>
    public void KeyCap(decimal dollars) => Assert.Null(Settings.AddCap(new CostCap("", CostCapScope.ApiDay) { Dollars = dollars }));

    public MissionRequest WithTaskCap(CostCap cap, string title = "Fix the login timeout", string assistant = "dextra") =>
        Missions.Request(title) with { AssistantId = assistant, Reserve = new ReserveStart([], null, []) { TaskCaps = [cap] } };

    public static CostCap TaskPercent(double percent) => new("", CostCapScope.Task) { Percent = percent };

    public static CostCap TaskDollars(decimal dollars) => new("", CostCapScope.Task) { Dollars = dollars };

    public MissionRecord Started(MissionRequest? request = null) => Missions.Started(request);

    public DecisionItem? Card(MissionRecord record) => Missions.Queue.Of(record.Id).FirstOrDefault(c => c.Kind == DecisionKind.Cap);

    public string? Pick(DecisionItem card, CapOptionKind kind)
    {
        var hold = Guard.HoldOf(card.MissionId!.Value) ?? throw new InvalidOperationException("no hold");
        return Missions.Queue.Answer(card.Id, DecisionAnswer.Pick(hold.Options.Single(o => o.Kind == kind).Text));
    }

    public Guid LeadOf(MissionRecord record) => Missions.Agents.Lead(record).Id;

    /// <summary>The lead's turn ends (the runner learns it, and a pausing mission becomes paused).</summary>
    public void EndTurn(Guid thread, string? answer = "done for now")
    {
        Missions.Agents.Finish(thread, answer);
        Missions.Runner.OnTurnFinished(thread, Marrow.Core.Models.TurnStatus.Completed, continues: false);
    }
}
