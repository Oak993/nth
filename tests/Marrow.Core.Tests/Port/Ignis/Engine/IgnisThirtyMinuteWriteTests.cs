using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis integration (OD-0134, ED-NEW-ignis-integration): the money maths use the write price of the exact cache lifetime the points
// carry. The model is OpenAI GPT-6.1 Sol as the bundled points give it (USD per million tokens): input 2, output 10, cache read
// 0.1, a 30-minute write at 1.25x input = 2.5 (no 5-minute or 1-hour field); above 272000 prompt tokens input 4, output 15,
// read 0.2, 30-minute write 5. Every expected value is worked out by hand in the comment beside it.
public sealed class IgnisThirtyMinuteWriteTests
{
    private static readonly TimeSpan Thirty = TimeSpan.FromMinutes(30);

    private static IgnisPrices SolPrices(bool stepNamesWrite = true) => new(2m, 10m, 0.1m, null, null,
        new([new IgnisPriceStep(272_000, 4m, 15m, 0.2m, null, null) { CacheWrites = stepNamesWrite ? new([new IgnisCacheWrite(1800, 5m)]) : new([]) }]))
    {
        CacheWrites = new([new IgnisCacheWrite(1800, 2.5m)]),
    };

    internal static IgnisModelPoints Sol(long window = 1_050_000, bool keepWarm = false, bool stepNamesWrite = true) =>
        IgnisMoneyTests.Model("codex", window, SolPrices(stepNamesWrite), new IgnisCachePoints(1800, null, keepWarm));

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_tier_carries_the_30_minute_write_and_a_step_replaces_or_keeps_it()
    {
        var baseTier = IgnisMoney.Tier(SolPrices(), 100_000);
        Assert.Equal(2.5m, baseTier.CacheWriteFor(Thirty));
        Assert.Equal(new[] { new IgnisCacheWrite(1800, 2.5m) }, baseTier.CacheWrites);
        // Above the step the step's 30-minute write replaces the base's.
        Assert.Equal(5m, IgnisMoney.Tier(SolPrices(), 300_000).CacheWriteFor(Thirty));
        // A step that names no write keeps the base's 30-minute price, as it keeps the 5-minute and 1-hour ones.
        var kept = IgnisMoney.Tier(SolPrices(stepNamesWrite: false), 300_000);
        Assert.Equal(4m, kept.Input);
        Assert.Equal(2.5m, kept.CacheWriteFor(Thirty));
        // The exact-lifetime price agrees with the points' own lookup.
        Assert.Equal(SolPrices().WriteFor(Thirty, 300_000), IgnisMoney.Tier(SolPrices(), 300_000).CacheWriteFor(Thirty));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Other_lifetimes_and_models_without_a_write_list_price_exactly_as_before()
    {
        // The 30-minute model has no 5-minute or 1-hour field, so any other (or unknown) lifetime pays plain input.
        var tier = IgnisMoney.Tier(SolPrices(), 100_000);
        Assert.Equal(2m, tier.CacheWriteFor(TimeSpan.FromMinutes(5)));
        Assert.Equal(2m, tier.CacheWriteFor(TimeSpan.FromHours(1)));
        Assert.Equal(2m, tier.CacheWriteFor(null));
        Assert.Equal(2m, tier.CacheWriteUnknown);
        // Opus (5-minute 5, 1-hour 8, no list): 5 minutes, 1 hour, shorter, longer and unknown give what they always gave.
        var opus = IgnisMoney.Tier(IgnisMoneyTests.Opus().Prices, 0);
        Assert.Empty(opus.CacheWrites);
        Assert.Equal(5m, opus.CacheWriteFor(TimeSpan.FromMinutes(5)));
        Assert.Equal(8m, opus.CacheWriteFor(TimeSpan.FromHours(1)));
        Assert.Equal(8m, opus.CacheWriteFor(TimeSpan.FromHours(2)));
        Assert.Equal(5m, opus.CacheWriteFor(Thirty)); // Opus lists no 30-minute write: the 5-minute premium, as before
        Assert.Equal(5m, opus.CacheWriteFor(null));
        // Codex without writes (no premium at all): plain input at every lifetime.
        var codex = IgnisMoney.Tier(IgnisMoneyTests.Codex().Prices, 0);
        Assert.Equal(1.75m, codex.CacheWriteFor(Thirty));
        Assert.Equal(1.75m, codex.CacheWriteFor(TimeSpan.FromHours(1)));
        // The Tier record still compares by value with the write list empty.
        Assert.Equal(new IgnisPriceTier(4m, 20m, 0.2m, 5m, 8m), opus);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_30_minute_rebuild_premium_is_the_prefix_at_the_30_minute_write_minus_the_read()
    {
        var sol = Sol();
        // 100000 · (2.5 − 0.1) = 240000 → $0.24
        Assert.Equal(0.24m, IgnisMoney.RebuildPremium(sol, 100_000, Thirty));
        // Above the step: 300000 · (5 − 0.2) = 1440000 → $1.44
        Assert.Equal(1.44m, IgnisMoney.RebuildPremium(sol, 300_000, Thirty));
        // Without the lifetime (or with another) the premium is the plain-input one it was before: 100000 · (2 − 0.1) = $0.19
        Assert.Equal(0.19m, IgnisMoney.RebuildPremium(sol, 100_000, null));
        Assert.Equal(0.19m, IgnisMoney.RebuildPremium(sol, 100_000, TimeSpan.FromMinutes(5)));
        // The engine takes the lifetime from the points' ttlMainSeconds (1800 s) when none is passed.
        Assert.Equal(0.24m, IgnisEngine.Shared.RebuildPremium(sol, 100_000));
        Assert.Equal(0.24m, IgnisEngine.Shared.RebuildPremium(sol, 100_000, null));
        Assert.Equal(0.19m, IgnisEngine.Shared.RebuildPremium(sol, 100_000, TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromSeconds(1800), IgnisKeepWarm.TtlFromPoints(sol));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_ping_is_worth_the_30_minute_rebuild_it_saves()
    {
        var sol = Sol(keepWarm: true);
        // Ping cost at 100000 prefix: 100000·0.1 + 60·2 + 5·10 = 10000 + 120 + 50 = 10170 → $0.01017
        // Benefit at odds 0.5: 0.5 · $0.24 = $0.12 (the plain-input price would have given 0.5 · 0.19 = $0.095)
        var (benefit, cost) = IgnisKeepWarm.Worth(sol, 100_000, Thirty, 0.5);
        Assert.Equal(0.12m, benefit);
        Assert.Equal(0.01017m, cost);
        Assert.Equal(0.095m, IgnisKeepWarm.Worth(sol, 100_000, TimeSpan.FromMinutes(5), 0.5).Benefit);
        // Prefix not reported: per token of prefix, 0.5 · (2.5 − 0.1) = 1.2 against the read price 0.1.
        Assert.Equal((1.2m, 0.1m), IgnisKeepWarm.Worth(sol, null, Thirty, 0.5));

        // The decision, 28 minutes after the last request on a 30-minute cache (lead = a tenth, at most 2 minutes: due at 28): it pings.
        var start = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var decision = IgnisKeepWarm.Decide(new IgnisKeepWarmInput
        {
            Now = start + TimeSpan.FromMinutes(28),
            Mode = IgnisMode.WarmAndTidy,
            Auto = true,
            BetweenTasks = true,
            Points = sol,
            LastRequestAt = start,
            PrefixTokens = 100_000,
        });
        Assert.Equal(IgnisKeepWarmAction.Ping, decision.Action);
        Assert.Equal(0.01017m, decision.Cost);
        Assert.Equal(IgnisKeepWarm.Worth(sol, 100_000, Thirty, decision.Odds).Benefit, decision.Benefit);
        // Three minutes earlier it waits until the ping is due at 28 minutes.
        var early = IgnisKeepWarm.Decide(new IgnisKeepWarmInput
        {
            Now = start + TimeSpan.FromMinutes(25),
            Mode = IgnisMode.WarmAndTidy,
            Auto = true,
            BetweenTasks = true,
            Points = sol,
            LastRequestAt = start,
            PrefixTokens = 100_000,
        });
        Assert.Equal(IgnisKeepWarmAction.Wait, early.Action);
        Assert.Equal(start + TimeSpan.FromMinutes(28), early.At);
        // A 25-minute observed lifetime from the usage would not change the price (no 25-minute write listed): plain input.
        Assert.Equal(0.095m, IgnisKeepWarm.Worth(sol, 100_000, TimeSpan.FromMinutes(25), 0.5).Benefit);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_tidy_point_break_even_uses_the_30_minute_write()
    {
        // Read 0.1, output 10, write 2.5: C = (4000·10 + 20000·(2.5 + 10·0.1)) / (0.1·9) = (40000 + 70000) / 0.9 = 122222.2… → 122223
        var sol = Sol(window: 150_000);
        Assert.Equal(122_223, IgnisTidyPoints.BreakEven(sol, Thirty));
        // With the lifetime unknown it is the plain-input one: (40000 + 20000·(2 + 1)) / 0.9 = 111111.1… → 111112
        Assert.Equal(111_112, IgnisTidyPoints.BreakEven(sol, null));
        // The window is 150000, so the last-quarter point is 112500: the 30-minute break-even is above it and wins,
        // the plain-input one is below it and does not.
        var point = IgnisTidyPoints.For(sol, Thirty);
        Assert.Equal(122_223, point.Tokens);
        Assert.Equal(IgnisTidyReason.BreakEven, point.Reason);
        var plain = IgnisTidyPoints.For(sol, null);
        Assert.Equal(112_500, plain.Tokens);
        Assert.Equal(IgnisTidyReason.LastQuarter, plain.Reason);
        // The engine asks with the points' own lifetime (1800 s).
        Assert.Equal(122_223, IgnisEngine.Shared.TidyPoint(sol));
    }
}
