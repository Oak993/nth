using Marrow.App.Assistant;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-14: the usage-aware router over the reserve rig. Three accounts: Claude 20x and Codex A (calibrated: 100,000 tokens to
/// the point, every model of them having ten 100,000-token turns on record) and Claude B (never calibrated). Claude has a light, a
/// standard and a strong model, Codex a standard and a strong one. The pill order is the order of <see cref="Accounts"/> unless a
/// test sets it. Nothing here touches the app, the Ignis points or a provider.
/// </summary>
internal sealed class RouterRig : IDisposable
{
    public static readonly Guid ClaudeId = Guid.Parse("c0000000-0000-4000-8000-0000000000c1");

    public static readonly Guid CodexId = Guid.Parse("c0000000-0000-4000-8000-0000000000c2");

    public static readonly Guid ClaudeBId = Guid.Parse("c0000000-0000-4000-8000-0000000000c3");

    public static readonly ReserveAccountRef ClaudeB = new("claude:b", "Claude B", "claude");

    public static readonly RouteModel[] ClaudeModels = [new("haiku", ModelTier.Light), new("sonnet", ModelTier.Standard), new("opus", ModelTier.Strong)];

    public static readonly RouteModel[] CodexModels = [new("sol", ModelTier.Standard), new("astra", ModelTier.Strong)];

    public RouterRig(string root, bool claudeB = false)
    {
        Accounts = [new RouterAccount(ClaudeId, ReserveRig.Claude20, ProviderKind.Claude, true, 0), new RouterAccount(CodexId, ReserveRig.CodexA, ProviderKind.Codex, true, 1)];
        if (claudeB) Accounts.Add(new RouterAccount(ClaudeBId, ClaudeB, ProviderKind.Claude, true, 2));
        Setup = new RouterSetup
        {
            Accounts = () => Accounts,
            Models = provider =>
            {
                ModelListings++;
                return ModelsOf.GetValueOrDefault(provider) ?? [];
            },
            Refusal = provider => Refused.GetValueOrDefault(provider),
        };
        Reserve = new ReserveRig(root, movers: (missions, clock) => Router = new AssistantRouter(Setup, clock, missions.Runner, missions.Agents));
        Router.Bind(Reserve.Guard);
        Reserve.Missions.Runner.ReviewerRouter = Router;
        Reserve.Pinned = record => record.ModelPinned;
        Reserve.AccountById[ClaudeId] = ReserveRig.Claude20;
        Reserve.AccountById[CodexId] = ReserveRig.CodexA;
        Reserve.AccountById[ClaudeBId] = ClaudeB;
        foreach (var model in ClaudeModels.Concat(CodexModels))
        {
            for (var i = 0; i < 10; i++) Reserve.Turns.Record(model.Id, 100_000, ReserveRig.T0);
        }
    }

    public ReserveRig Reserve { get; }

    public AssistantRouter Router { get; private set; } = null!;

    public RouterSetup Setup { get; }

    public List<RouterAccount> Accounts { get; }

    /// <summary>How many times the router asked for a provider's models (it should be once per provider for one question).</summary>
    public int ModelListings { get; private set; }

    public Dictionary<ProviderKind, string> Refused { get; } = [];

    /// <summary>The models each provider lists; a test may change what a provider offers.</summary>
    public Dictionary<ProviderKind, RouteModel[]> ModelsOf { get; } = new() { [ProviderKind.Claude] = ClaudeModels, [ProviderKind.Codex] = CodexModels };

    public ManualTimeProvider Clock => Reserve.Clock;

    public MissionRig Missions => Reserve.Missions;

    public void Dispose() => Reserve.Dispose();

    /// <summary>Changes an account's row (its pill place, whether it can be used).</summary>
    public void Set(Guid id, Func<RouterAccount, RouterAccount> change)
    {
        var at = Accounts.FindIndex(a => a.Id == id);
        Accounts[at] = change(Accounts[at]);
    }

    /// <summary>A poll of the account now: its 5-hour window resets in <paramref name="fiveHourHours"/> hours, its weekly one (when read) in <paramref name="weeklyHours"/>.</summary>
    public void Read(ReserveAccountRef account, double fiveHour, double fiveHourHours = 3, double? weekly = null, double weeklyHours = 72)
    {
        var now = Clock.GetUtcNow();
        var windows = new List<ReserveWindow>
        {
            new("five", ReserveWindowKind.FiveHour, fiveHour, now, now.AddHours(fiveHourHours)) { Title = "5-hour limit" },
        };
        if (weekly is { } w) windows.Add(new ReserveWindow("week", ReserveWindowKind.Weekly, w, now, now.AddHours(weeklyHours)) { Title = "Weekly limit" });
        Reserve.Guard.OnReadings(account, windows);
    }

    public RouteRequest Request(StepWork work, params string[] pinned) => new(new RouteStep(work) { Pinned = new ValueList<string>(pinned) }, "dextra");

    public static string[] Order(RouteDecision decision) => [.. decision.Ranked.Select(r => r.Option.AccountKey + "/" + r.Option.Model)];

    public static RouteExcluded[] Left(RouteDecision decision, string account) => [.. decision.Excluded.Where(e => e.Option.AccountKey == account)];
}
