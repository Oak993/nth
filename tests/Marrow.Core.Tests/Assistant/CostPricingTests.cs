using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Ignis;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-15 (DESIGN.md rev 5, 13.2 "Caps", 11.2; ED-NEW-dextra-as15-3): a cap in dollars needs a price for every token it counts. A model's
// price is its per-million input, output and cached-input price; a model with no known price is counted at a deliberately dear one.
[Trait("Feature", "AS-15")]
[Trait("Decision", "ED-NEW-dextra-as15-3")]
public sealed class CostPricingTests
{
    private static readonly IgnisPrices Prices = new(5m, 25m, 0.5m, 6.25m, 10m, ValueList<IgnisPriceStep>.Empty);

    [Fact]
    public void With_the_input_and_output_the_cached_part_of_the_input_is_read_at_the_cache_price()
    {
        // 600,000 input of which 400,000 cached, 100,000 output: 200,000 x 5 + 400,000 x 0.5 + 100,000 x 25 = 1 + 0.2 + 2.5 = 3.7 dollars
        Assert.Equal(3.7m, CostPricing.Dollars(Prices, 700_000, 600_000, 100_000, 400_000));
        Assert.Equal(5.5m, CostPricing.Dollars(Prices, 700_000, 600_000, 100_000));      // none cached: 600,000 x 5 + 100,000 x 25
    }

    [Fact]
    public void With_only_the_total_it_is_read_at_three_parts_input_to_one_part_output()
    {
        // 5 x 0.75 + 25 x 0.25 = 10 dollars per million
        Assert.Equal(10m, CostPricing.Blended(Prices));
        Assert.Equal(2m, CostPricing.Dollars(Prices, 200_000));
        Assert.Equal(2m, CostPricing.Dollars(Prices, 200_000, input: 50_000));               // an output count is missing: the total is used
    }

    [Fact]
    public void Cached_input_is_never_more_than_the_input_and_nothing_is_negative()
    {
        Assert.Equal(0.5m, CostPricing.Dollars(Prices, 1_000_000, 1_000_000, 0, 5_000_000));   // all of it cached, at 0.5 a million
        Assert.Equal(0m, CostPricing.Dollars(Prices, -10));
        Assert.Equal(0m, CostPricing.Conservative(-10));
    }

    [Fact]
    public void A_model_with_no_known_price_is_counted_at_a_dear_one()
    {
        Assert.Equal(15m, CostPricing.ConservativePerMillion);
        Assert.Equal(1.5m, CostPricing.Conservative(100_000));
        Assert.True(CostPricing.Conservative(1_000_000) >= CostPricing.Blended(Prices));      // never counted cheaper than a strong model
    }
}
