using System.Net;
using Marrow.Core.Accounts.Keys;
using Marrow.Core.Port;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

[Trait("Rule", "R-TEST-4")]
public sealed class UncappedCallerPriceContractTests
{
    [Fact]
    public void An_uncapped_refused_key_has_no_quoted_charge_and_the_next_key_keeps_its_dispatch_price()
    {
        using var runtime = new TestRuntime();
        using var storage = new StorageScratchFolder();
        using var catalog = ModelPriceCatalog.Current.Override(new([]));
        var store = new InMemoryApiKeyStore().Add(new("a", "deepseek", "A"), "fake-a")
            .Add(new("b", "deepseek", "B"), "fake-b");
        var spend = new InMemoryApiKeySpend();
        var moves = new InMemoryKeyMovePolicy { Default = KeyMoveSetting.Allow };
        var pool = new ApiKeyPool("deepseek", store, new InMemoryApiKeyCredits(), spend, SwiftClock.Provider.Current, moves: moves);
        var session = new RequestPriceContractSession(pool.ForThread(Guid.NewGuid()), privateHead: false);
        session.KeySpendEstimator = usage => (usage.Input ?? 0) * .001 + (usage.Output ?? 0) * .002;
        var calls = 0;
        using var handler = new InProcessHttpHandler { Answer = _ =>
        {
            if (++calls == 1) return new HttpResponseMessage(HttpStatusCode.PaymentRequired) { Content = new StringContent("{}") };
            session.KeySpendEstimator = _ => .9;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "data: {\"choices\":[{\"delta\":{\"content\":\"quoted answer\"}}],\"usage\":{\"prompt_tokens\":5,\"completion_tokens\":2,\"total_tokens\":7}}\n\ndata: [DONE]\n\n") };
        } };
        using var transport = URLSession.Transport.Override(handler);
        runtime.Loop.Run(() => session.Start());
        var round = runtime.Loop.Run(() => session.Round());
        Assert.Equal("quoted answer", round.Content);
        Assert.Equal(2, handler.Received.Count);
        Assert.Equal(0, spend.SpendSince("a", DateTimeOffset.MinValue));
        Assert.Equal(.009, spend.SpendSince("b", DateTimeOffset.MinValue), 10);
        session.Stop();
    }
}
