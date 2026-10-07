using Marrow.Services.Providers;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class OpenRouterPriceSourceTests
{
    [Fact]
    public async Task A_public_pricing_fetch_binds_exact_model_rates_and_declared_limits_without_credentials()
    {
        var urls = new List<Uri>();
        using var fake = OpenRouterPriceSource.Fetch.Override((url, _) =>
        {
            urls.Add(url);
            return Task.FromResult<string?>("{\"data\":[{\"id\":\"vendor/fixture\",\"context_length\":1000,\"top_provider\":{\"max_completion_tokens\":200},\"pricing\":{\"prompt\":\"0.000002\",\"completion\":\"0.000004\",\"request\":\"0\"}}]}");
        });
        var price = await OpenRouterPriceSource.ResolveAsync("vendor/fixture", CancellationToken.None);
        Assert.NotNull(price);
        Assert.Equal(2, price.InputPerMillion);
        Assert.Equal(4, price.OutputPerMillion);
        Assert.Equal(0.0028, price.CeilingUsd!.Value, 10);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/models"), Assert.Single(urls));
        Assert.Null(await OpenRouterPriceSource.ResolveAsync("vendor/missing", CancellationToken.None));
        Assert.Null(await OpenRouterPriceSource.ResolveAsync("openrouter/auto", CancellationToken.None));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("NaN")]
    [InlineData("unknown")]
    public async Task Unknown_dynamic_or_malformed_prices_never_become_zero_prices(string rate)
    {
        using var fake = OpenRouterPriceSource.Fetch.Override((_, _) => Task.FromResult<string?>("{\"data\":[{\"id\":\"v/m\",\"pricing\":{\"prompt\":\"" + rate + "\",\"completion\":\"0\"}}]}"));
        Assert.Null(await OpenRouterPriceSource.ResolveAsync("v/m", CancellationToken.None));
    }

    [Theory]
    [InlineData("\"discount\":-0.5")]
    [InlineData("\"discount\":2")]
    [InlineData("\"discount\":\"unknown\"")]
    [InlineData("\"overrides\":[{\"prompt\":\"0.001\"}]")]
    [InlineData("\"internal_reasoning\":\"0.001\"")]
    public async Task Conditional_or_additional_charges_cannot_authorize_the_flat_token_ceiling(string extra)
    {
        using var fake = OpenRouterPriceSource.Fetch.Override((_, _) => Task.FromResult<string?>("{\"data\":[{\"id\":\"v/m\",\"context_length\":1000,\"top_provider\":{\"max_completion_tokens\":200},\"pricing\":{\"prompt\":\"0.000002\",\"completion\":\"0.000004\"," + extra + "}}]}"));
        Assert.Null(await OpenRouterPriceSource.ResolveAsync("v/m", CancellationToken.None));
    }
}
