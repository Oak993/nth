using Marrow.Core.Models;
using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

// All prices here are deliberately fictional fixture values; none is a production/provider price.
[Trait("Rule", "R-TEST-4")]
public sealed class ModelPriceCatalogTests
{
    [Theory]
    [InlineData(200L)]
    [InlineData(0L)]
    public void A_total_inconsistent_with_complete_counts_cannot_release_the_reserved_budget(long total)
    {
        var price = new ModelTokenPrice(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, DateTimeOffset.UtcNow, new Uri("https://api-docs.deepseek.com/quick_start/pricing"));
        Assert.Null(price.Estimate(new() { Total = total, Input = 60, Output = 40 }));
    }

    [Fact]
    public void A_missing_cache_breakdown_uses_the_higher_authoritative_input_rate()
    {
        var price = new ModelTokenPrice(ProviderKind.Deepseek, "fixture", 2, 4, 5, 0, 1000, 1000, DateTimeOffset.UtcNow, new Uri("https://api-docs.deepseek.com/quick_start/pricing"));
        Assert.Equal(0.0005, price.Estimate(new() { Total = 100, Input = 100, Output = 0 })!.Value, 10);
        Assert.Equal(0.0002, price.Estimate(new() { Total = 100, Input = 100, Output = 0, CachedInput = 0 })!.Value, 10);
    }

    [Theory]
    [InlineData(0L, null, null)]
    [InlineData(60L, 60L, null)]
    [InlineData(40L, null, 40L)]
    public void Empty_or_partially_derived_usage_cannot_release_the_reserved_budget(long total, long? input, long? output)
    {
        var price = new ModelTokenPrice(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, DateTimeOffset.UtcNow, new Uri("https://api-docs.deepseek.com/quick_start/pricing"));
        Assert.Null(price.Estimate(new() { Total = total, Input = input, Output = output }));
    }

    [Fact]
    public void Cache_hits_are_not_charged_twice_and_incomplete_usage_uses_a_conservative_rate()
    {
        var price = new ModelTokenPrice(ProviderKind.Deepseek, "fixture", 2, 4, 0.5, 0, 1000, 1000, DateTimeOffset.Parse("2026-10-07T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture), new Uri("https://api-docs.deepseek.com/quick_start/pricing"));
        Assert.Equal(0.00044, price.Estimate(new() { Total = 350, Input = 300, CachedInput = 240, Output = 50 })!.Value, 10);
        Assert.Equal(0.0014, price.Estimate(new() { Total = 350 })!.Value, 10);
        Assert.Null(price.Estimate(new() { Total = 10, Input = 2, CachedInput = 3, Output = 8 }));
        Assert.Equal(0.006, price.CeilingUsd!.Value, 10);
    }

    [Fact]
    public void Unfilled_expired_future_or_wrong_model_prices_fail_closed()
    {
        var now = DateTimeOffset.Parse("2026-10-07T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var catalog = ModelPriceCatalog.FromJson("{\"schemaVersion\":1,\"state\":\"unfilled\",\"prices\":[]}");
        Assert.Null(catalog.Find(ProviderKind.Deepseek, "deepseek-v4-pro", now));
        var price = new ModelTokenPrice(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, now, new Uri("https://api-docs.deepseek.com/quick_start/pricing"));
        catalog = new ModelPriceCatalog([price]);
        Assert.NotNull(catalog.Find(ProviderKind.Deepseek, "fixture", now));
        Assert.Null(catalog.Find(ProviderKind.Deepseek, "fixture", now.AddDays(2)));
        Assert.Null(catalog.Find(ProviderKind.Deepseek, "fixture", now.AddSeconds(-1)));
        Assert.Null(catalog.Find(ProviderKind.Zai, "fixture", now));
        Assert.Null(catalog.Find(ProviderKind.Deepseek, "Fixture", now));
    }
}
