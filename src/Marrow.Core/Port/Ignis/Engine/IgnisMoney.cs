// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-engine; PLAN unit U1). The money maths: what a turn cost, what it would have
// cost without the cache, what a cold rebuild lost, and what a keep-warm ping and a rebuild cost. Prices are US dollars
// per million tokens from the model points; a price step applies to the whole request once its prompt (uncached + read
// + written) is above the step. On a plan account the same numbers are API-equivalent dollars (what the tokens would
// cost on an API key): the plan itself is not billed per token, so Ignis reports the value of the usage it saved.
namespace Marrow.Core.Port.Ignis.Engine;

/// <summary>The prices in force for one request size (USD per million tokens). A null write price: no write premium.</summary>
public sealed record IgnisPriceTier(decimal Input, decimal Output, decimal CacheRead, decimal? CacheWrite5m, decimal? CacheWrite1h)
{
    /// <summary>
    /// The price of a write whose TTL the stream did not say. ENGINEERING DECISION: the 5-minute premium (the default
    /// TTL), else the 1-hour premium, else plain input (no premium).
    /// </summary>
    public decimal CacheWriteUnknown => CacheWrite5m ?? CacheWrite1h ?? Input;

    /// <summary>The write price for a cache lifetime: 1 h or longer uses the 1-hour premium, shorter the 5-minute one.</summary>
    public decimal CacheWriteFor(TimeSpan? ttl) =>
        ttl is { } life && life >= TimeSpan.FromHours(1) ? CacheWrite1h ?? CacheWrite5m ?? Input : CacheWrite5m ?? CacheWrite1h ?? Input;
}

public static class IgnisMoney
{
    private const decimal Million = 1_000_000m;

    /// <summary>The prices in force for a request of <paramref name="promptTokens"/>: the highest step it is above, else the base.</summary>
    public static IgnisPriceTier Tier(IgnisPrices prices, long promptTokens)
    {
        var tier = new IgnisPriceTier(prices.Input, prices.Output, prices.CacheRead, prices.CacheWrite5m, prices.CacheWrite1h);
        foreach (var step in prices.Steps.OrderBy(step => step.AboveTokens))
        {
            if (promptTokens <= step.AboveTokens) break;
            // A step replaces every price it names; a write price it leaves out keeps the base's.
            tier = new IgnisPriceTier(step.Input, step.Output, step.CacheRead, step.CacheWrite5m ?? tier.CacheWrite5m, step.CacheWrite1h ?? tier.CacheWrite1h);
        }
        return tier;
    }

    /// <summary>What the turn cost: each bucket at its own price, in the tier of the turn's whole prompt.</summary>
    public static decimal Cost(IgnisModelPoints points, IgnisTurnUsage usage)
    {
        var tier = Tier(points.Prices, usage.TotalInput);
        return (usage.UncachedInput * tier.Input
            + usage.CacheRead * tier.CacheRead
            + usage.CacheWrite5m * (tier.CacheWrite5m ?? tier.CacheWrite1h ?? tier.Input)
            + usage.CacheWrite1h * (tier.CacheWrite1h ?? tier.CacheWrite5m ?? tier.Input)
            + usage.CacheWriteUnknownTtl * tier.CacheWriteUnknown
            + usage.Output * tier.Output) / Million;
    }

    /// <summary>What the same turn would have cost with no cache at all: the whole prompt at the input price.</summary>
    public static decimal CostWithoutCache(IgnisModelPoints points, IgnisTurnUsage usage)
    {
        var tier = Tier(points.Prices, usage.TotalInput);
        return (usage.TotalInput * tier.Input + usage.Output * tier.Output) / Million;
    }

    /// <summary>
    /// "Reused quick memory": what the cache saved on this turn, the cost without the cache minus the real cost (write
    /// premiums included), never below 0. 0 when the provider reported no cache numbers (PLAN: unreported, no savings).
    /// </summary>
    public static decimal ReuseSaving(IgnisModelPoints points, IgnisTurnUsage usage) =>
        usage.Reported ? Math.Max(0m, CostWithoutCache(points, usage) - Cost(points, usage)) : 0m;

    /// <summary>
    /// What a rebuild after the cache went cold lost: the tokens that would have been read warm (at most
    /// <paramref name="warmPrefixTokens"/>, the prompt the cache held before the break) were paid at their write price
    /// (or, where the provider reports no writes, at the input price) instead of the cache-read price. 0 unless the
    /// break was longer than the TTL, and 0 when not reported.
    /// </summary>
    public static decimal ColdLoss(IgnisModelPoints points, IgnisTurnUsage usage, TimeSpan gap, TimeSpan ttl, long warmPrefixTokens)
    {
        if (!usage.Reported || gap <= ttl || warmPrefixTokens <= 0) return 0m;
        var tier = Tier(points.Prices, usage.TotalInput);
        var left = warmPrefixTokens;
        var paid = 0m;
        // The rebuilt prefix is taken from the write buckets first (dearest first), then from plain input.
        foreach (var (tokens, price) in new[]
                 {
                     (usage.CacheWrite1h, tier.CacheWrite1h ?? tier.CacheWrite5m ?? tier.Input),
                     (usage.CacheWrite5m, tier.CacheWrite5m ?? tier.CacheWrite1h ?? tier.Input),
                     (usage.CacheWriteUnknownTtl, tier.CacheWriteUnknown),
                     (usage.UncachedInput, tier.Input),
                 })
        {
            var take = Math.Min(left, tokens);
            paid += take * (price - tier.CacheRead);
            left -= take;
            if (left == 0) break;
        }
        return Math.Max(0m, paid / Million);
    }

    /// <summary>
    /// What one keep-warm ping costs: the cached prefix read once, plus the ping's own small message at the input
    /// price and its reply at the output price, in the prefix's tier.
    /// </summary>
    public static decimal PingCost(IgnisModelPoints points, long prefixTokens, long pingInputTokens = IgnisKeepWarm.PingInputTokens, long pingOutputTokens = IgnisKeepWarm.PingOutputTokens)
    {
        var tier = Tier(points.Prices, prefixTokens + pingInputTokens);
        return (prefixTokens * tier.CacheRead + pingInputTokens * tier.Input + pingOutputTokens * tier.Output) / Million;
    }

    /// <summary>What letting the cache go cold costs on the next message: the prefix written again instead of read.</summary>
    public static decimal RebuildPremium(IgnisModelPoints points, long prefixTokens, TimeSpan? ttl)
    {
        var tier = Tier(points.Prices, prefixTokens);
        return prefixTokens * (tier.CacheWriteFor(ttl) - tier.CacheRead) / Million;
    }

    /// <summary>Share of the prompt read from cache (0...1); null when not reported or empty.</summary>
    public static double? ReuseRate(IgnisTurnUsage usage) =>
        usage.Reported && usage.TotalInput > 0 ? (double)usage.CacheRead / usage.TotalInput : null;

    /// <summary>
    /// A "Smart helpers" saving: the helper's tokens priced on the main model minus their price on the helper model.
    /// </summary>
    public static decimal HelperSaving(IgnisModelPoints main, IgnisModelPoints helper, IgnisTurnUsage helperUsage) =>
        Math.Max(0m, Cost(main, helperUsage) - Cost(helper, helperUsage));
}
