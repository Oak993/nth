// PORT-ONLY: Dextra (DESIGN.md rev 5, 11.6; unit AS-14; ED-NEW-dextra-as14-2). No Swift twin.
// Which of a provider's models are cheap and which strong. No provider publishes a strength, so it is read from the price the Ignis
// points know (USD per million output tokens, from the provider's own pricing page): a model priced at most 15% of its provider's
// dearest is Light, at least 60% is Strong, between is Standard, and a model with no known price is Standard (UNVERIFIED: price is
// a proxy for strength, and the thresholds are a first guess; ED-NEW-dextra-as14-2).
namespace Marrow.Core.Port.Assistant;

/// <summary>A model as its provider lists it, with the price the Ignis points know for it (USD per million output tokens).</summary>
public sealed record PricedModel(string Id, decimal? OutputPrice, bool IsDefault = false);

public static class ModelTiers
{
    /// <summary>A model priced at most this share of its provider's dearest model is Light.</summary>
    public const decimal LightShare = 0.15m;

    /// <summary>A model priced at least this share of its provider's dearest model is Strong.</summary>
    public const decimal StrongShare = 0.6m;

    /// <summary>
    /// A provider's models with their tiers, in the provider's order; a model listed twice (ids are compared without case) counts
    /// once. A price of nothing or zero is no price: with no price at all in the list every model is Standard.
    /// </summary>
    public static IReadOnlyList<RouteModel> Classify(IEnumerable<PricedModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var distinct = new List<PricedModel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var model in models)
        {
            if (seen.Add(model.Id)) distinct.Add(model);
        }
        var top = distinct.Select(m => m.OutputPrice).Where(price => price is > 0).DefaultIfEmpty(0).Max() ?? 0;
        return [.. distinct.Select(m => new RouteModel(m.Id, TierOf(m.OutputPrice, top), m.IsDefault, m.OutputPrice))];
    }

    private static ModelTier TierOf(decimal? price, decimal top)
    {
        if (price is not > 0 || top <= 0) return ModelTier.Standard;
        if (price <= top * LightShare) return ModelTier.Light;
        return price >= top * StrongShare ? ModelTier.Strong : ModelTier.Standard;
    }

    /// <summary>
    /// The models worth trying, so a provider with a long catalogue is not scored model by model: one of each tier, the provider's
    /// default if it is in the tier, else the cheapest priced, else the first listed. Light, Standard, Strong; a tier with no model is left out.
    /// </summary>
    public static IReadOnlyList<RouteModel> Representatives(IReadOnlyList<RouteModel> models)
    {
        ArgumentNullException.ThrowIfNull(models);
        var chosen = new List<RouteModel>();
        foreach (var tier in new[] { ModelTier.Light, ModelTier.Standard, ModelTier.Strong })
        {
            var inTier = models.Where(m => m.Tier == tier).ToList();
            if (inTier.Count == 0) continue;
            chosen.Add(inTier.FirstOrDefault(m => m.IsDefault)
                ?? inTier.Where(m => m.OutputPrice is > 0).OrderBy(m => m.OutputPrice).FirstOrDefault()
                ?? inTier[0]);
        }
        return chosen;
    }
}
