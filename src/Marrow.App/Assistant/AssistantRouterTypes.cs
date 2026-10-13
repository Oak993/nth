// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.6; unit AS-14; ED-NEW-dextra-as14-5). No Swift twin.
// What the usage-aware router reads and what it answers. The router itself is AssistantRouter; the rules that score an option are
// pure and live in Marrow.Core (RouteScoring), so this file is only the shapes between the app and them.
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Ignis;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>
/// An account the router may use, as the app knows it: the account store's id, the name the reserve calls it, its provider, whether
/// it can be used now (enabled, connected, not out: CanUseAccount), and its place in the owner's pill order (0 first).
/// </summary>
public sealed record RouterAccount(Guid Id, ReserveAccountRef Ref, ProviderKind Provider, bool Usable, int Rank);

/// <summary>What the router reads. The app supplies it from its account store, its providers and the Ignis prices; a test supplies fakes.</summary>
public sealed class RouterSetup
{
    /// <summary>The accounts the owner has connected and enabled.</summary>
    public required Func<IReadOnlyList<RouterAccount>> Accounts { get; init; }

    /// <summary>A provider's models, in its own order, each with its tier. Empty while its catalogue is not loaded.</summary>
    public required Func<ProviderKind, IReadOnlyList<RouteModel>> Models { get; init; }

    /// <summary>Why nth cannot run mission work on a provider (12.3 point 3); null when it can.</summary>
    public Func<ProviderKind, string?> Refusal { get; init; } = SealedProviderConfig.Refusal;
}

/// <summary>What a step asks of the router.</summary>
public sealed record RouteRequest(RouteStep Step, string AssistantId)
{
    /// <summary>The step's p90 in tokens; 0: each model's own typical turn.</summary>
    public double TokensP90 { get; init; }

    /// <summary>The rules the owner spoke for this task, when it has none yet registered with the reserve.</summary>
    public IReadOnlyList<ReserveRule> TaskRules { get; init; } = [];

    /// <summary>The mission the step belongs to, when it exists: its floors, baselines and allowance bind the options.</summary>
    public MissionRecord? Mission { get; init; }

    /// <summary>Only accounts of this provider (a mission has one provider); null: every provider.</summary>
    public ProviderKind? Provider { get; init; }

    /// <summary>Only this account (a mission's heads run on its lead's account); null: every account.</summary>
    public Guid? AccountId { get; init; }
}

/// <summary>The router's pick for a step: the account, the provider and model, and the scoring that chose it.</summary>
public sealed record RoutePick(Guid AccountId, ReserveAccountRef Account, ProviderKind Provider, string? Model, RouteRanked Ranked)
{
    /// <summary>The step as the plan shows it (11.4): the account, the model and what it costs in tokens.</summary>
    public PlanStep ToPlanStep(double tokensP50, double tokensP90) => new(Account, Model, tokensP50, tokensP90);
}

public static class RouterModels
{
    /// <summary>
    /// A provider's models with their tiers (ED-NEW-dextra-as14-2): the output price the Ignis points know for each, against its
    /// provider's dearest. With no points, or a model they do not know, the model is Standard.
    /// </summary>
    public static IReadOnlyList<RouteModel> Of(ProviderKind provider, IEnumerable<ModelOption> models, IIgnisPoints? points)
    {
        ArgumentNullException.ThrowIfNull(models);
        return ModelTiers.Classify(models.Select(model => new PricedModel(model.Id, points?.Find(provider, model.Id)?.Prices.Output, model.IsDefault)));
    }
}
