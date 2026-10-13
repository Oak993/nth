// PORT-ONLY: Dextra (DESIGN.md rev 5, 13.2 "Caps", 11.2 "API keys have money, not windows"; unit AS-15; ED-NEW-dextra-as15-3). No Swift twin.
// What a turn on an API key cost in dollars, from the model's price per million tokens. A cap in dollars needs a price for every
// token it counts, so a model whose price is not known is counted at a deliberately dear one: a cap may stop work early, never late.
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Port.Assistant;

public static class CostPricing
{
    /// <summary>
    /// UNVERIFIED: US dollars per million tokens for a model whose price is not known. About the price of a strong model's output,
    /// so an unknown model is never counted as cheap; the points file (Ignis) names the price of every model it knows.
    /// </summary>
    public const decimal ConservativePerMillion = 15m;

    private const decimal Million = 1_000_000m;

    /// <summary>
    /// The dollars of a spend with the model's prices. With the input and output counts, the cached part of the input is read at the
    /// cache price; with only the total, the total is read at three parts input to one part output.
    /// </summary>
    public static decimal Dollars(IgnisPrices prices, long total, long? input = null, long? output = null, long? cachedInput = null)
    {
        ArgumentNullException.ThrowIfNull(prices);
        if (total < 0) total = 0;
        if (input is { } inputTokens && output is { } outputTokens && inputTokens >= 0 && outputTokens >= 0)
        {
            var cached = Math.Clamp(cachedInput ?? 0, 0, inputTokens);
            return ((inputTokens - cached) * prices.Input + cached * prices.CacheRead + outputTokens * prices.Output) / Million;
        }
        return total * Blended(prices) / Million;
    }

    /// <summary>The price of a typical turn, three parts input to one part output, in dollars per million tokens.</summary>
    public static decimal Blended(IgnisPrices prices) => prices.Input * 0.75m + prices.Output * 0.25m;

    /// <summary>The dollars of a spend whose model has no known price.</summary>
    public static decimal Conservative(long total) => Math.Max(0, total) * ConservativePerMillion / Million;
}
