using System.Net;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class RequestPriceContractTests
{
    [Fact]
    public void Caller_price_reserves_the_declared_request_bound_and_settles_the_original_estimator()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("quoted", "deepseek", "Quoted", MonthlyCapUsd: .09), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead: true);
        var quotes = new List<OpenAICompatibleSession.ReportedTokenUsage>();
        session.KeySpendEstimator = usage => { quotes.Add(usage); return (usage.Input ?? 0) * .001 + (usage.Output ?? 0) * .002; };
        using var handler = new InProcessHttpHandler { Answer = _ =>
        {
            Assert.Equal(.09, spend.SpendSince("quoted", DateTimeOffset.MinValue), 10);
            session.KeySpendEstimator = _ => .9; // A later price change must not reprice the submitted request.
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "data: {\"choices\":[{\"delta\":{\"content\":\"quoted answer\"}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":2,\"total_tokens\":7}}\n\ndata: [DONE]\n\n") };
        } };
        using var transport = URLSession.Transport.Override(handler);
        runtime.Loop.Run(() => session.Start());
        var round = runtime.Loop.Run(() => session.Round());
        Assert.Equal("quoted answer", round.Content);
        Assert.Single(handler.Received);
        Assert.Contains(quotes, usage => usage.Input == 50 && usage.Output == 20 && usage.Total == 70);
        Assert.Equal(.009, spend.SpendSince("quoted", DateTimeOffset.MinValue), 10);
        session.Stop();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Missing_price_stops_actual_HTTP_including_a_private_head(bool privateHead)
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("unpriced", "deepseek", "Unpriced", MonthlyCapUsd: 1), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No request may reach HTTP.") };
        using var transport = URLSession.Transport.Override(handler);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead);
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Empty(handler.Received);
        Assert.Equal(0, spend.SpendSince("unpriced", DateTimeOffset.MinValue));
        session.Stop();
    }

    [Fact]
    public void A_valid_caller_quote_that_exceeds_the_live_cap_is_refused_before_HTTP()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("small", "deepseek", "Small", MonthlyCapUsd: .08), "fake-key");
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), new InMemoryApiKeySpend(), SwiftClock.Provider.Current);
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No request may reach HTTP.") };
        using var transport = URLSession.Transport.Override(handler);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead: true);
        session.KeySpendEstimator = usage => (usage.Input ?? 0) * .001 + (usage.Output ?? 0) * .002;
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Empty(handler.Received);
        session.Stop();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-.1)]
    public void Invalid_caller_quotes_never_authorize_HTTP(double? price)
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("invalid", "deepseek", "Invalid", MonthlyCapUsd: 1), "fake-key");
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), new InMemoryApiKeySpend(), SwiftClock.Provider.Current);
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No request may reach HTTP.") };
        using var transport = URLSession.Transport.Override(handler);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead: true);
        session.KeySpendEstimator = _ => price;
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Empty(handler.Received);
        session.Stop();
    }

    [Fact]
    public void A_caller_price_is_bound_to_the_model_for_which_it_was_installed()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("bound", "deepseek", "Bound", MonthlyCapUsd: 1), "fake-key");
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), new InMemoryApiKeySpend(), SwiftClock.Provider.Current);
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No request may reach HTTP.") };
        using var transport = URLSession.Transport.Override(handler);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead: true);
        session.KeySpendEstimator = _ => .1;
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round("another-model")));
        Assert.Empty(handler.Received);
        session.Stop();
    }
}
