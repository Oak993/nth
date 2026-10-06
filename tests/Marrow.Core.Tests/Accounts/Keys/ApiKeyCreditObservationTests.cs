using Marrow.Core.Accounts.Keys;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Accounts.Keys;

[Trait("Rule", "R-TEST-4")]
public sealed class ApiKeyCreditObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_undated_or_stale_positive_credit_report_cannot_erase_a_later_real_credit_refusal(bool dated)
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryApiKeyStore().Add(new("a", "s", "A"), "offline-a").Add(new("b", "s", "B"), "offline-b");
        var credits = new ObservedApiCredits();
        credits.Set("a", 10, dated ? clock.GetUtcNow().AddMinutes(-1) : null);
        var moves = new InMemoryKeyMovePolicy { Default = KeyMoveSetting.Allow };
        var pool = new ApiKeyPool("s", store, credits, new InMemoryApiKeySpend(), clock, moves: moves);
        var request = pool.ForThread(Guid.NewGuid(), "a");
        Assert.Equal("a", request.Current()!.Id);
        Assert.Equal("b", request.OnFailure(402, null)!.Id);
        Assert.Equal(1, pool.State().Available);
        Assert.Equal("b", pool.ForThread(Guid.NewGuid()).Current()!.Id);
        clock.Advance(TimeSpan.FromTicks(1));
        credits.Set("a", 10, clock.GetUtcNow());
        Assert.Equal(2, pool.State().Available);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, false)]
    public void A_reported_empty_or_unusable_key_is_skipped_before_sending_without_a_fabricated_reset(double usd, bool usable)
    {
        var clock = new ManualTimeProvider();
        var store = new InMemoryApiKeyStore().Add(new("a", "s", "A"), "offline-a").Add(new("b", "s", "B"), "offline-b");
        var credits = new ObservedApiCredits();
        credits.Set("a", usd, clock.GetUtcNow(), usable);
        var pool = new ApiKeyPool("s", store, credits, new InMemoryApiKeySpend(), clock);
        var events = new List<ApiKeyEvent>();
        pool.Changed += (_, value) => events.Add(value);
        Assert.Equal(1, pool.State().Available);
        Assert.Equal("b", pool.ForThread(Guid.NewGuid()).Current()!.Id);
        pool.Pin(Guid.Empty, "a");
        Assert.Null(pool.ForThread(Guid.Empty, "a").Current());
        Assert.Null(Assert.Single(events, value => value.Kind == ApiKeyEventKind.PinnedKeyOut).ReturnsAt);
    }
}
