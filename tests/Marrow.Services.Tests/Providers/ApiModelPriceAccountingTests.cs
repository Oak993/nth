using Marrow.Core.Accounts.Keys;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class ApiModelPriceAccountingTests
{
    [Fact]
    public void Permission_revoked_during_encoding_releases_an_unsent_hold_without_calling_transport()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var handler = new InProcessHttpHandler { Answer = _ => throw new InvalidOperationException("No HTTP dispatch is authorized.") };
        using var transport = URLSession.Transport.Override(handler);
        var now = SwiftClock.Provider.Current.GetUtcNow();
        using var pricing = ModelPriceCatalog.Current.Override(new([new(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, now, new("https://api-docs.deepseek.com/quick_start/pricing"))]));
        var keys = new InMemoryApiKeyStore(); keys.Add(new("a", "deepseek", "A", MonthlyCapUsd: 0.006), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", keys, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new PriceAccountingSession(pool.ForThread(Guid.NewGuid())) { ExerciseDispatchBoundary = true };
        var permissions = 0;
        session.CanTransferContext = () => ++permissions < 3;
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Empty(handler.Received);
        Assert.Equal(0, spend.SpendSince("a", DateTimeOffset.MinValue));
        Assert.False(pool.State().AllOut);
        session.Stop();
    }

    [Fact]
    public void Token_only_production_requests_charge_their_selected_key_and_unknown_models_send_nothing()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var now = SwiftClock.Provider.Current.GetUtcNow();
        using var pricing = ModelPriceCatalog.Current.Override(new([new(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, now, new("https://api-docs.deepseek.com/quick_start/pricing"))]));
        var keys = new InMemoryApiKeyStore(); keys.Add(new("a", "deepseek", "A", MonthlyCapUsd: 0.01), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", keys, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new PriceAccountingSession(pool.ForThread(Guid.NewGuid()));
        runtime.Loop.Run(() => session.Start());
        var round = runtime.Loop.Run(() => session.Round());
        session.ReportUsageForTests(round);
        Assert.Single(session.Requests);
        Assert.Equal(0.00028, spend.SpendSince("a", DateTimeOffset.MinValue), 10);
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round("unpriced")));
        Assert.Single(session.Requests);
        session.Stop();
    }

    [Fact]
    public void An_unfilled_price_table_blocks_a_capped_private_head_before_transport()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var pricing = ModelPriceCatalog.Current.Override(new([]));
        var keys = new InMemoryApiKeyStore(); keys.Add(new("a", "deepseek", "A", MonthlyCapUsd: 1), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", keys, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new PriceAccountingSession(pool.ForThread(Guid.NewGuid()));
        runtime.Loop.Run(() => session.Start());
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Empty(session.Requests);
        Assert.Equal(0, spend.SpendSince("a", DateTimeOffset.MinValue));
        session.Stop();
    }

    [Fact]
    public void Capped_OpenRouter_requests_carry_the_authoritative_router_price_ceiling_and_use_reported_USD()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var fake = OpenRouterPriceSource.Fetch.Override((_, _) => Task.FromResult<string?>("{\"data\":[{\"id\":\"vendor/fixture\",\"context_length\":1000,\"top_provider\":{\"max_completion_tokens\":200},\"pricing\":{\"prompt\":\"0.000002\",\"completion\":\"0.000004\",\"request\":\"0\"}}]}"));
        var keys = new InMemoryApiKeyStore(); keys.Add(new("a", "openrouter", "A", MonthlyCapUsd: 0.005), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("openrouter", keys, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new PriceAccountingSession(pool.ForThread(Guid.NewGuid()), ProviderKind.OpenRouter, "vendor/fixture");
        session.Next = session.Next with { TokenUsage = new() { Total = 100, Input = 60, Output = 40, ReportedCostUsd = 0.0001 } };
        runtime.Loop.Run(() => session.Start());
        var round = runtime.Loop.Run(() => session.Round("vendor/fixture"));
        session.ReportUsageForTests(round);
        var payload = Assert.Single(session.Requests);
        Assert.Equal(200, payload["max_tokens"].JSONValueInt);
        Assert.Equal("2", payload["provider"]["max_price"]!["prompt"]!.JSONValueString);
        Assert.Equal("4", payload["provider"]["max_price"]!["completion"]!.JSONValueString);
        Assert.Equal(0.0001, spend.SpendSince("a", DateTimeOffset.MinValue), 10);
        session.Stop();
    }

    [Fact]
    public void A_reply_without_usage_keeps_its_durable_ceiling_and_cannot_silently_spend_again()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        var now = SwiftClock.Provider.Current.GetUtcNow();
        using var pricing = ModelPriceCatalog.Current.Override(new([new(ProviderKind.Deepseek, "fixture", 2, 4, null, 0, 1000, 1000, now, new("https://api-docs.deepseek.com/quick_start/pricing"))]));
        var keys = new InMemoryApiKeyStore(); keys.Add(new("a", "deepseek", "A", MonthlyCapUsd: 0.006), "fake-key");
        var spend = new InMemoryApiKeySpend();
        var pool = new ApiKeyPool("deepseek", keys, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current);
        var session = new PriceAccountingSession(pool.ForThread(Guid.NewGuid()));
        session.Next = session.Next with { TokenUsage = null };
        runtime.Loop.Run(() => session.Start());
        runtime.Loop.Run(() => session.Round());
        Assert.Equal(0.006, spend.SpendSince("a", DateTimeOffset.MinValue), 10);
        Assert.Throws<ProviderError>(() => runtime.Loop.Run(() => session.Round()));
        Assert.Single(session.Requests);
        session.Stop();
    }
}
