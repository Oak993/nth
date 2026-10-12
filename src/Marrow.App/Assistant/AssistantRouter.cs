// PORT-ONLY: Dextra (DESIGN.md rev 5, 3.4, 11.5, 11.6, 15.4; unit AS-14; ED-NEW-dextra-as14-5 to -8). No Swift twin.
// The usage-aware router. For a step it lists every account the owner could run it on, each with the models worth trying, asks the
// reserve guard where each stands (the room under its floors after the step, the same projection that gates a turn), and lets
// RouteScoring choose: eligibility, room (allowance that resets within 12 hours counts double), model fit, the owner's pill order.
// It picks a mission's lead, fills a Hydra team's head profiles, chooses the reviewer's model and account, and offers (and makes)
// the pause card's first option, "Move the rest to ...". Everything here runs on the app's main thread.
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class AssistantRouter(RouterSetup setup, TimeProvider time, MissionRunner runner, IMissionAgents agents) : IReserveMover, IReviewerRouter
{
    /// <summary>A team's profiles are worked out again after this long: the accounts' room changes as the work goes on.</summary>
    public static readonly TimeSpan TeamKept = TimeSpan.FromSeconds(5);

    /// <summary>The teams kept at most; the oldest go first.</summary>
    public const int TeamsKept = 64;

    private ReserveGuard? guard;
    private readonly Dictionary<Guid, (DateTimeOffset At, HydraPair? Pair)> teams = [];

    /// <summary>The reserve guard that says where each account stands; without one every account is unread and the pill order decides.</summary>
    public void Bind(ReserveGuard reserve) => guard = reserve;

    // ---- the decision ----

    /// <summary>Every option for the step, ranked or left out with the reason (11.6).</summary>
    public RouteDecision Decide(RouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Decide(request, setup.Accounts());
    }

    private RouteDecision Decide(RouteRequest request, IReadOnlyList<RouterAccount> accounts)
    {
        var options = new List<RouteOption>();
        foreach (var account in accounts)
        {
            if (request.Provider is { } only && account.Provider != only) continue;
            if (request.AccountId is { } mine && account.Id != mine) continue;
            var refusal = setup.Refusal(account.Provider);
            foreach (var model in Candidates(account.Provider, request.Step))
            {
                var standing = guard?.Standing(account.Ref, request.AssistantId, request.Mission, request.TaskRules, model?.Id, request.TokensP90);
                options.Add(new RouteOption(account.Ref.Key, account.Ref.Name, account.Provider, model?.Id, model?.Tier ?? ModelTier.Standard)
                {
                    Windows = standing?.Windows ?? [],
                    Rank = account.Rank,
                    Usable = account.Usable,
                    Refusal = refusal,
                    Rough = standing?.Rough ?? true,
                });
            }
        }
        return RouteScoring.Decide(options, request.Step, time.GetUtcNow());
    }

    // One model of each tier per provider, so a provider with a long catalogue is not scored model by model; the owner's pinned
    // models are always the ones asked for, whatever their tier. A provider that lists no model yet is tried with its own default.
    private IReadOnlyList<RouteModel?> Candidates(ProviderKind provider, RouteStep step)
    {
        var models = setup.Models(provider);
        if (step.Pinned.Count > 0)
            return [.. models.Where(m => step.Pinned.Any(p => string.Equals(p, m.Id, StringComparison.OrdinalIgnoreCase)))];
        return models.Count == 0 ? [null] : [.. ModelTiers.Representatives(models)];
    }

    /// <summary>The best option for the step, or null when none can run it (every account out of room, or none can be sealed).</summary>
    public RoutePick? Pick(RouteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var accounts = setup.Accounts();
        if (Decide(request, accounts).Best is not { } best) return null;
        var account = accounts.First(a => a.Ref.Key == best.Option.AccountKey);
        return new RoutePick(account.Id, account.Ref, account.Provider, best.Option.Model, best);
    }

    /// <summary>The account, provider and model a mission's lead starts on: the strongest model that fits, where the reserve leaves room.</summary>
    public RoutePick? PickLead(string assistantId, IReadOnlyList<ReserveRule>? taskRules = null) =>
        Pick(new RouteRequest(new RouteStep(StepWork.Edit), assistantId) { TaskRules = taskRules ?? [] });

    // ---- the reviewer (3.4) ----

    /// <summary>The reviewer: a model other than the workers', on the account of the mission's provider with the most room.</summary>
    public ReviewerPick? Pick(MissionRecord mission, IReadOnlyList<string> models)
    {
        ArgumentNullException.ThrowIfNull(mission);
        var step = new RouteStep(StepWork.Review) { AvoidModel = mission.Model };
        var pick = Pick(new RouteRequest(step, mission.Mission.AssistantId) { Mission = mission, Provider = mission.Provider });
        return pick is null ? null : new ReviewerPick(pick.Model, pick.AccountId, pick.Account);
    }

    // ---- a Hydra team (15.4) ----

    /// <summary>
    /// The pair a mission's team runs on (15.4: "the router chooses HydraHeadProfiles"): the model its plain heads edit with, and a
    /// "Reading" profile on a cheaper model for the heads that only search and read. The heads run on the lead's provider and account,
    /// so only the model is chosen (ED-NEW-dextra-as14-7). Null when the owner pinned the mission's model (its heads follow the
    /// lead) or nothing can be chosen.
    /// </summary>
    public HydraPair? TeamPair(MissionRecord mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        if (mission.ModelPinned) return null;
        var now = time.GetUtcNow();
        if (teams.TryGetValue(mission.Id, out var kept) && now - kept.At < TeamKept) return kept.Pair;
        var pair = BuildTeam(mission);
        foreach (var stale in teams.Where(t => now - t.Value.At >= TeamKept).Select(t => t.Key).ToList()) teams.Remove(stale);
        while (teams.Count >= TeamsKept) teams.Remove(teams.OrderBy(t => t.Value.At).First().Key);
        teams[mission.Id] = (now, pair);
        return pair;
    }

    private HydraPair? BuildTeam(MissionRecord mission)
    {
        RoutePick? PickFor(StepWork work) => Pick(new RouteRequest(new RouteStep(work), mission.Mission.AssistantId)
        {
            Mission = mission,
            Provider = mission.Provider,
            AccountId = mission.AccountId,
        });
        var edit = PickFor(StepWork.Edit);
        var read = PickFor(StepWork.Read);
        if (edit is null && read is null) return null;
        var worker = edit?.Model ?? mission.Model;
        var profiles = new List<HydraHeadProfile>();
        if (read?.Model is { } readModel && !string.Equals(readModel, worker, StringComparison.OrdinalIgnoreCase))
            profiles.Add(new HydraHeadProfile("Reading") { Id = StableId(mission.Id, "Reading"), Model = readModel });
        return new HydraPair(mission.Provider)
        {
            Id = StableId(mission.Id, "team"),
            OrchestratorModel = mission.Model,
            WorkerModel = worker,
            HeadProfiles = new ValueList<HydraHeadProfile>(profiles),
        };
    }

    // The same ids every time for the same mission, so a profile is the same profile whenever it is asked for.
    private static Guid StableId(Guid mission, string name) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(mission.ToString("N") + ":" + name)).AsSpan(0, 16));

    // ---- "Move the rest to ..." (11.5 option 1) ----

    /// <summary>
    /// The account to offer when a task is held at its reserve: the best-placed of those with room, of the task's own provider and
    /// with the task's model (a chat changes account, not model). Null when the owner pinned the model: it never switches silently.
    /// </summary>
    public ReserveMoveOffer? Offer(MissionRecord mission, IReadOnlyList<ReserveCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(candidates);
        if (mission.ModelPinned) return null;
        var step = new RouteStep(StepWork.Edit) { Pinned = mission.Model is { } model ? new ValueList<string>([model]) : ValueList<string>.Empty };
        var decision = Decide(new RouteRequest(step, mission.Mission.AssistantId) { Mission = mission, Provider = mission.Provider });
        foreach (var ranked in decision.Ranked)
        {
            if (candidates.FirstOrDefault(c => c.Account.Key == ranked.Option.AccountKey && c.Account.ProviderId == mission.Provider.Id) is { } candidate)
                return new ReserveMoveOffer(candidate.Account, candidate.CostPercent);
        }
        return null;
    }

    /// <summary>
    /// Makes the move the owner chose: the task's account becomes the target (every chat it starts runs there), and a lead already
    /// running is changed to it at its next safe point with nth's own summary, so the account at its reserve is not asked anything.
    /// Null when it is done; else why not, and the task is back where it was.
    /// </summary>
    public bool MovesAsync => true;

    public async Task<string?> MoveAsync(MissionRecord mission, ReserveMoveOffer offer)
    {
        ArgumentNullException.ThrowIfNull(mission);
        ArgumentNullException.ThrowIfNull(offer);
        if (mission.ModelPinned) return "its model was chosen for it, so it stays where it is";
        var accounts = setup.Accounts();
        var target = accounts.FirstOrDefault(a => a.Ref.Key == offer.Target.Key);
        if (target is null || !target.Usable || target.Provider != mission.Provider) return "that account is not available now";
        if (runner.Find(mission.Id) is not { } record) return "the task is gone";
        var before = record.AccountId;
        runner.MoveAccount(mission.Id, target.Id, target.Ref.Key);
        if (record.LeadThread is not { } lead) return null;
        bool moved;
        try { moved = await agents.MoveAccount(lead, target.Id); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            runner.ReportFault($"The chat could not be moved to {target.Ref.Name} ({error.GetType().Name}).");
            moved = false;
        }
        if (moved) return null;
        var back = before is { } id ? accounts.FirstOrDefault(a => a.Id == id)?.Ref.Key ?? "" : "";
        runner.Post(() => runner.MoveAccount(mission.Id, before, back));
        return $"its chat could not change to {target.Ref.Name}";
    }
}
