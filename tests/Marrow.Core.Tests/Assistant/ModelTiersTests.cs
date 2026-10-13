using Marrow.Core.Port.Assistant;

namespace Marrow.Core.Tests.Assistant;

// Dextra AS-14 (DESIGN.md rev 5, 11.6; ED-NEW-dextra-as14-2): which of a provider's models are cheap and which strong. No provider
// publishes a strength, so the tier is read from the price the Ignis points know: at most 15% of the provider's dearest model is
// Light, at least 60% is Strong, between is Standard, and a model with no known price is Standard. The router tries one model of
// each tier per provider: the default, else the cheapest priced, else the first listed.
[Trait("Decision", "ED-NEW-dextra-as14-2")]
[Trait("Feature", "AS-14")]
public sealed class ModelTiersTests
{
    private static Dictionary<string, ModelTier> Tiers(params PricedModel[] models) => ModelTiers.Classify(models).ToDictionary(m => m.Id, m => m.Tier);

    [Fact]
    public void A_provider_s_models_are_tiered_by_their_price_against_its_dearest()
    {
        var withTop = Tiers(new("haiku", 0.5m), new("sonnet", 10m), new("opus", 20m), new("fable", 50m));
        Assert.Equal(ModelTier.Light, withTop["haiku"]);
        Assert.Equal(ModelTier.Standard, withTop["sonnet"]);
        Assert.Equal(ModelTier.Standard, withTop["opus"]);
        Assert.Equal(ModelTier.Strong, withTop["fable"]);

        var without = Tiers(new("haiku", 0.5m), new("sonnet", 10m), new("opus", 20m));
        Assert.Equal(ModelTier.Light, without["haiku"]);
        Assert.Equal(ModelTier.Standard, without["sonnet"]);
        Assert.Equal(ModelTier.Strong, without["opus"]);
    }

    [Fact]
    public void The_boundaries_belong_to_the_outer_tiers_and_an_unpriced_model_is_standard()
    {
        var tiers = Tiers(new("top", 100m), new("light", 15m), new("strong", 60m), new("just-over", 15.01m), new("just-under", 59.99m), new("unknown", null));
        Assert.Equal(ModelTier.Strong, tiers["top"]);
        Assert.Equal(ModelTier.Light, tiers["light"]);
        Assert.Equal(ModelTier.Strong, tiers["strong"]);
        Assert.Equal(ModelTier.Standard, tiers["just-over"]);
        Assert.Equal(ModelTier.Standard, tiers["just-under"]);
        Assert.Equal(ModelTier.Standard, tiers["unknown"]);
    }

    [Fact]
    public void A_provider_with_no_prices_or_a_free_one_has_only_standard_models()
    {
        Assert.All(ModelTiers.Classify([new("a", null), new("b", null)]), m => Assert.Equal(ModelTier.Standard, m.Tier));
        Assert.All(ModelTiers.Classify([new("a", 0m), new("b", 0m)]), m => Assert.Equal(ModelTier.Standard, m.Tier));
        Assert.Empty(ModelTiers.Classify([]));
        Assert.Equal(ModelTier.Strong, Assert.Single(ModelTiers.Classify([new("only", 3m)])).Tier);
    }

    [Fact]
    public void Classifying_keeps_the_provider_s_order_the_default_flag_and_the_price()
    {
        var models = ModelTiers.Classify([new("b", 2m), new("a", 1m, IsDefault: true)]);
        Assert.Equal(["b", "a"], models.Select(m => m.Id));
        Assert.Equal([false, true], models.Select(m => m.IsDefault));
        Assert.Equal([2m, 1m], models.Select(m => m.OutputPrice));
    }

    [Fact]
    public void One_model_of_each_tier_is_tried_the_default_else_the_cheapest_priced_else_the_first_listed()
    {
        var models = ModelTiers.Classify([
            new("big-b", 50m), new("big-a", 45m), new("small-a", 1m), new("small-default", 2m, IsDefault: true), new("mid-unpriced", null), new("mid-x", 25m),
        ]);
        // big-b 50 -> Strong, big-a 45 -> Strong, small-a 1 -> Light, small-default 2 -> Light, mid-unpriced -> Standard, mid-x 25 -> Standard.
        Assert.Equal(["small-default", "mid-x", "big-a"], ModelTiers.Representatives(models).Select(m => m.Id));

        var noDefault = ModelTiers.Classify([new("s2", 2m), new("s1", 1m), new("u1", null), new("u2", null), new("top", 100m)]);
        Assert.Equal(["s1", "u1", "top"], ModelTiers.Representatives(noDefault).Select(m => m.Id));
    }

    [Fact]
    public void A_tier_with_no_model_is_left_out_a_repeated_id_counts_once_and_none_listed_gives_none()
    {
        var onlyStandard = ModelTiers.Classify([new("x", null), new("X", null), new("y", null)]);
        Assert.Equal(["x", "y"], onlyStandard.Select(m => m.Id));
        Assert.Equal(["x"], ModelTiers.Representatives(onlyStandard).Select(m => m.Id));
        Assert.Empty(ModelTiers.Representatives([]));
    }
}
