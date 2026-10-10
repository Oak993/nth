using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the money maths. Prices are USD per million tokens (Opus 5.5 as the bundled
// points give it: input 4, output 20, cache read 0.20, 5-minute write 5, 1-hour write 8); every expected value is worked
// out by hand in the comment beside it.
public sealed class IgnisMoneyTests
{
    internal static IgnisModelPoints Opus() => Model("claude", 1_000_000, new IgnisPrices(4m, 20m, 0.2m, 5m, 8m, new([])), new IgnisCachePoints(3600, 300, true));

    internal static IgnisModelPoints HaikuWithStep() => Model("claude", 1_000_000,
        new IgnisPrices(0.1m, 0.5m, 0.01m, 0.125m, 0.2m, new([new IgnisPriceStep(100_000, 0.5m, 2.5m, 0.05m, 0.625m, 1m)])), new IgnisCachePoints(3600, 300, true));

    internal static IgnisModelPoints Codex() => Model("codex", 400_000, new IgnisPrices(1.75m, 14m, 0.175m, null, null, new([])), new IgnisCachePoints(null, null, false));

    internal static IgnisModelPoints Model(string provider, long window, IgnisPrices prices, IgnisCachePoints cache, long? tidyAt = null, string? tidyReason = null) =>
        new(provider, new(["m"]), new([]), "M", window, prices, cache, tidyAt, tidyReason, null, "https://example.com/pricing", "2026-10-10");

    internal static IgnisTurnUsage Usage(long uncached, long read, long w5 = 0, long w1h = 0, long unknown = 0, long output = 0, ProviderKind provider = ProviderKind.Claude) =>
        new(provider, "m", null, uncached, read, w5, w1h, unknown, output, null, null, true);

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_Claude_turn_prices_each_bucket_and_summed_iterations_cost_what_their_parts_cost()
    {
        var opus = Opus();
        // Two iterations of one result (ClaudeSession sums iterations[] into one usage, PLAN U2).
        var first = Usage(uncached: 600, read: 60_000, w5: 2_000, w1h: 4_000, output: 200);
        var second = Usage(uncached: 400, read: 40_000, w1h: 6_000, output: 300);
        var turn = Usage(uncached: 1_000, read: 100_000, w5: 2_000, w1h: 10_000, output: 500);
        Assert.Equal(turn.TotalInput, first.TotalInput + second.TotalInput);
        // 1000·4 + 100000·0.2 + 2000·5 + 10000·8 + 500·20 = 4000 + 20000 + 10000 + 80000 + 10000 = 124000 → $0.124
        Assert.Equal(0.124m, IgnisMoney.Cost(opus, turn));
        Assert.Equal(IgnisMoney.Cost(opus, first) + IgnisMoney.Cost(opus, second), IgnisMoney.Cost(opus, turn));
        // Without a cache: 113000·4 + 500·20 = 462000 → $0.462; the cache saved 0.462 − 0.124 = $0.338.
        Assert.Equal(0.462m, IgnisMoney.CostWithoutCache(opus, turn));
        Assert.Equal(0.338m, IgnisMoney.ReuseSaving(opus, turn));
        Assert.Equal(100_000d / 113_000d, IgnisMoney.ReuseRate(turn));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_write_whose_ttl_the_stream_did_not_say_pays_the_5_minute_premium()
    {
        // 10000 unknown-TTL writes at 5 → $0.05.
        Assert.Equal(0.05m, IgnisMoney.Cost(Opus(), Usage(uncached: 0, read: 0, unknown: 10_000)));
        var tier = IgnisMoney.Tier(Opus().Prices, 0);
        Assert.Equal(5m, tier.CacheWriteUnknown);
        Assert.Equal(8m, tier.CacheWriteFor(TimeSpan.FromHours(1)));
        Assert.Equal(5m, tier.CacheWriteFor(TimeSpan.FromMinutes(5)));
        Assert.Equal(5m, tier.CacheWriteFor(null));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_price_step_prices_the_whole_request_once_the_prompt_is_above_it()
    {
        var haiku = HaikuWithStep();
        Assert.Equal(new IgnisPriceTier(0.1m, 0.5m, 0.01m, 0.125m, 0.2m), IgnisMoney.Tier(haiku.Prices, 100_000));
        Assert.Equal(new IgnisPriceTier(0.5m, 2.5m, 0.05m, 0.625m, 1m), IgnisMoney.Tier(haiku.Prices, 100_001));
        // 99000 read + 1000 uncached = 100000 (not above): 99000·0.01 + 1000·0.1 = 990 + 100 = 1090 → $0.00109
        Assert.Equal(0.00109m, IgnisMoney.Cost(haiku, Usage(uncached: 1_000, read: 99_000)));
        // 149000 read + 1000 uncached = 150000 (above): 149000·0.05 + 1000·0.5 = 7450 + 500 = 7950 → $0.00795
        Assert.Equal(0.00795m, IgnisMoney.Cost(haiku, Usage(uncached: 1_000, read: 149_000)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Codex_pays_no_write_premium()
    {
        var codex = Codex();
        // Codex reports reads only: 20000 uncached·1.75 + 80000 read·0.175 + 1000 out·14 = 35000 + 14000 + 14000 = 63000 → $0.063
        var turn = Usage(uncached: 20_000, read: 80_000, output: 1_000, provider: ProviderKind.Codex);
        Assert.Equal(0.063m, IgnisMoney.Cost(codex, turn));
        // A write, should one be reported, costs plain input: 10000·1.75 = $0.0175 either TTL.
        Assert.Equal(0.0175m, IgnisMoney.Cost(codex, Usage(uncached: 0, read: 0, w5: 10_000, provider: ProviderKind.Codex)));
        Assert.Equal(0.0175m, IgnisMoney.Cost(codex, Usage(uncached: 0, read: 0, w1h: 10_000, provider: ProviderKind.Codex)));
        // A rebuild premium is then only the read discount lost: 100000·(1.75 − 0.175) = $0.1575.
        Assert.Equal(0.1575m, IgnisMoney.RebuildPremium(codex, 100_000, TimeSpan.FromHours(1)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Unreported_cache_numbers_record_no_saving_and_no_loss()
    {
        var unreported = IgnisTurnUsage.NotReported(ProviderKind.Codex, "m", null, contextUsed: 50_000, contextWindow: 400_000);
        Assert.Equal(0m, IgnisMoney.ReuseSaving(Codex(), unreported));
        Assert.Equal(0m, IgnisMoney.ColdLoss(Codex(), unreported, TimeSpan.FromHours(2), TimeSpan.FromMinutes(5), 50_000));
        Assert.Null(IgnisMoney.ReuseRate(unreported));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_gap_longer_than_the_ttl_is_a_loss_and_a_shorter_one_is_not()
    {
        var opus = Opus();
        // The 1-hour cache held 100000 tokens; after a 61-minute break the turn wrote 102000 again (1h) and read 0.
        var rebuilt = Usage(uncached: 500, read: 0, w1h: 102_000, output: 300);
        // Loss = the 100000 held tokens at write − read: 100000·(8 − 0.2) = 780000 → $0.78
        Assert.Equal(0.78m, IgnisMoney.ColdLoss(opus, rebuilt, TimeSpan.FromMinutes(61), TimeSpan.FromHours(1), 100_000));
        Assert.Equal(0m, IgnisMoney.ColdLoss(opus, rebuilt, TimeSpan.FromMinutes(59), TimeSpan.FromHours(1), 100_000));
        Assert.Equal(0m, IgnisMoney.ColdLoss(opus, rebuilt, TimeSpan.FromHours(1), TimeSpan.FromHours(1), 100_000));
        // A provider without writes pays the held prefix at input: Codex 50000·(1.75 − 0.175) = $0.07875
        var codexTurn = Usage(uncached: 52_000, read: 0, provider: ProviderKind.Codex);
        Assert.Equal(0.07875m, IgnisMoney.ColdLoss(Codex(), codexTurn, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), 50_000));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_ping_costs_the_prefix_read_once_plus_its_own_small_message()
    {
        // 100000·0.2 + 60·4 + 5·20 = 20000 + 240 + 100 = 20340 → $0.02034
        Assert.Equal(0.02034m, IgnisMoney.PingCost(Opus(), 100_000));
        // Letting a 1-hour cache go cold: 100000·(8 − 0.2) = $0.78; a 5-minute one: 100000·(5 − 0.2) = $0.48
        Assert.Equal(0.78m, IgnisMoney.RebuildPremium(Opus(), 100_000, TimeSpan.FromHours(1)));
        Assert.Equal(0.48m, IgnisMoney.RebuildPremium(Opus(), 100_000, TimeSpan.FromMinutes(5)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_helper_on_the_cheaper_model_saves_the_price_difference()
    {
        var helper = Model("claude", 1_000_000, new IgnisPrices(0.1m, 0.5m, 0.01m, 0.125m, 0.2m, new([])), new IgnisCachePoints(3600, 300, true));
        var job = Usage(uncached: 10_000, read: 0, output: 1_000);
        // Opus: 10000·4 + 1000·20 = 60000 → $0.06; Haiku: 10000·0.1 + 1000·0.5 = 1500 → $0.0015; saved $0.0585
        Assert.Equal(0.0585m, IgnisMoney.HelperSaving(Opus(), helper, job));
    }
}
