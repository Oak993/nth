using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis U1 (OD-0134, ED-NEW-ignis-engine): the keep-warm decision table on a manual clock (every time is passed in).
// Ping when P(return within the TTL) × (rebuild − read) > ping cost, only between tasks, never when off, paused,
// unsupported or already cold.
public sealed class IgnisKeepWarmTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static IgnisKeepWarmInput Input(TimeSpan sinceLast) => new()
    {
        Now = Start + sinceLast,
        Mode = IgnisMode.WarmAndTidy,
        Auto = true,
        BetweenTasks = true,
        Points = IgnisMoneyTests.Opus(),
        LastRequestAt = Start,
        PrefixTokens = 100_000,
    };

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Before_the_lead_time_it_waits_until_the_ping_is_due()
    {
        // 1 h cache: lead = min(6 min, 2 min) = 2 min, so the ping is due at 58 min.
        var decision = IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(10)));
        Assert.Equal(IgnisKeepWarmAction.Wait, decision.Action);
        Assert.Equal(Start + TimeSpan.FromMinutes(58), decision.At);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void When_due_and_worth_it_between_tasks_it_pings()
    {
        // Idle 58 min, no history: prior 0.8 / (1 + 58/30) = 0.2727…; benefit 0.2727 · $0.78 = $0.2127 > ping $0.02034.
        var decision = IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(58)));
        Assert.Equal(IgnisKeepWarmAction.Ping, decision.Action);
        Assert.Equal(Start + TimeSpan.FromMinutes(58), decision.At);
        Assert.Equal(0.8 / (1 + 58.0 / 30), decision.Odds, 10);
        Assert.Equal(0.02034m, decision.Cost);
        Assert.True(decision.Benefit > decision.Cost);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Not_between_tasks_it_waits_and_asks_again_while_the_cache_is_warm()
    {
        var busy = IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(58)) with { BetweenTasks = false });
        Assert.Equal(IgnisKeepWarmAction.Wait, busy.Action);
        Assert.Equal(Start + TimeSpan.FromMinutes(58) + TimeSpan.FromSeconds(10), busy.At);
        // With no time left before the cache goes cold, it gives up.
        var late = IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(55)) with { BetweenTasks = false });
        Assert.Equal(IgnisKeepWarmAction.Skip, late.Action);
        Assert.Equal(IgnisKeepWarmSkip.AlreadyCold, late.Skip);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("off", IgnisKeepWarmSkip.Off)]
    [InlineData("autoOff", IgnisKeepWarmSkip.AutoOff)]
    [InlineData("unknownModel", IgnisKeepWarmSkip.NotSupported)]
    [InlineData("keepWarmOff", IgnisKeepWarmSkip.NotSupported)]
    [InlineData("locked", IgnisKeepWarmSkip.Paused)]
    [InlineData("archived", IgnisKeepWarmSkip.Paused)]
    [InlineData("planNearlyFull", IgnisKeepWarmSkip.Paused)]
    [InlineData("usageLimit", IgnisKeepWarmSkip.Paused)]
    [InlineData("neverSent", IgnisKeepWarmSkip.NoCache)]
    [InlineData("noTtl", IgnisKeepWarmSkip.NoCache)]
    [InlineData("cold", IgnisKeepWarmSkip.AlreadyCold)]
    [InlineData("longBreak", IgnisKeepWarmSkip.NotWorthIt)]
    public void The_decision_table_skips(string situation, IgnisKeepWarmSkip expected)
    {
        var due = Input(TimeSpan.FromMinutes(58));
        var input = situation switch
        {
            "off" => due with { Mode = IgnisMode.Off },
            "autoOff" => due with { Auto = false },
            "unknownModel" => due with { Points = null },
            "keepWarmOff" => due with { Points = IgnisMoneyTests.Codex() },
            "locked" => due with { Pause = IgnisPause.Locked },
            "archived" => due with { Pause = IgnisPause.Archived },
            "planNearlyFull" => due with { Pause = IgnisPause.PlanNearlyFull },
            "usageLimit" => due with { Pause = IgnisPause.UsageLimit },
            "neverSent" => due with { LastRequestAt = null },
            "noTtl" => due with { Points = IgnisMoneyTests.Opus() with { Cache = new IgnisCachePoints(null, null, true) } },
            "cold" => Input(TimeSpan.FromMinutes(61)),
            // Idle for 20 h (pings kept the cache warm meanwhile): prior 0.8 / 41 = 0.0195; 0.0195 · $0.78 = $0.0152 < $0.0203.
            "longBreak" => due with { IdleSince = due.Now - TimeSpan.FromHours(20) },
            _ => throw new ArgumentException(situation),
        };
        var decision = IgnisKeepWarm.Decide(input);
        Assert.Equal(IgnisKeepWarmAction.Skip, decision.Action);
        Assert.Equal(expected, decision.Skip);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Draft_text_and_the_chat_on_screen_raise_the_odds()
    {
        var longBreak = Input(TimeSpan.FromMinutes(58)) with { IdleSince = Start + TimeSpan.FromMinutes(58) - TimeSpan.FromHours(20) };
        Assert.Equal(IgnisKeepWarmAction.Ping, IgnisKeepWarm.Decide(longBreak with { HasDraft = true }).Action);
        Assert.Equal(0.9, IgnisKeepWarm.Decide(longBreak with { HasDraft = true }).Odds, 10);
        // On screen: 1 − (1 − 0.0195…)·0.6 = 0.4117…
        var onScreen = IgnisKeepWarm.Decide(longBreak with { OnScreen = true });
        Assert.Equal(IgnisKeepWarmAction.Ping, onScreen.Action);
        Assert.Equal(1 - (1 - 0.8 / 41) * 0.6, onScreen.Odds, 10);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_chats_own_gaps_outweigh_the_prior()
    {
        var idle = TimeSpan.FromMinutes(5);
        var horizon = TimeSpan.FromMinutes(5);
        // Six past gaps longer than 5 min, all ending within 10 min: (6 + 3·prior(5 min)) / (6 + 3).
        var quick = Enumerable.Repeat(TimeSpan.FromMinutes(8), 6).ToList();
        var prior = 0.8 / (1 + 5.0 / 30);
        Assert.Equal((6 + 3 * prior) / 9, IgnisKeepWarm.ReturnOdds(quick, [], idle, horizon), 10);
        // Six gaps of 3 hours: (0 + 3·prior) / 9.
        var slow = Enumerable.Repeat(TimeSpan.FromHours(3), 6).ToList();
        Assert.Equal(3 * prior / 9, IgnisKeepWarm.ReturnOdds(slow, [], idle, horizon), 10);
        // A global history of five or more longer gaps replaces the built-in prior: 4 of 5 return within the horizon.
        var global = new List<TimeSpan> { TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(7), TimeSpan.FromMinutes(8), TimeSpan.FromMinutes(9), TimeSpan.FromHours(2) };
        Assert.Equal(0.8, IgnisKeepWarm.ReturnOdds([], global, idle, horizon), 10);
        Assert.Equal(0.8, IgnisKeepWarm.Prior(TimeSpan.Zero), 10);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Lead_times_and_ttls_come_from_the_stream_before_the_points()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IgnisKeepWarm.Lead(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(2), IgnisKeepWarm.Lead(TimeSpan.FromHours(1)));
        Assert.Equal(TimeSpan.FromSeconds(15), IgnisKeepWarm.Lead(TimeSpan.FromMinutes(1)));
        Assert.Equal(TimeSpan.FromHours(1), IgnisKeepWarm.TtlFromPoints(IgnisMoneyTests.Opus()));
        Assert.Equal(TimeSpan.FromMinutes(5), IgnisKeepWarm.TtlFromPoints(IgnisMoneyTests.Opus(), subagent: true));
        Assert.Null(IgnisKeepWarm.TtlFromPoints(IgnisMoneyTests.Codex()));
        Assert.Equal(TimeSpan.FromHours(1), IgnisKeepWarm.TtlFromUsage(IgnisMoneyTests.Usage(0, 0, w5: 10, w1h: 10)));
        Assert.Equal(TimeSpan.FromMinutes(5), IgnisKeepWarm.TtlFromUsage(IgnisMoneyTests.Usage(0, 0, w5: 10)));
        Assert.Null(IgnisKeepWarm.TtlFromUsage(IgnisMoneyTests.Usage(0, 10, unknown: 10)));
        // A 5-minute stream TTL overrides the points' hour: due at 4.5 min.
        var decision = IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(1)) with { Ttl = TimeSpan.FromMinutes(5) });
        Assert.Equal(Start + TimeSpan.FromSeconds(270), decision.At);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Without_a_reported_prefix_the_price_ratio_decides()
    {
        // Per token: benefit p·(8 − 0.2), cost 0.2 → worth it while p > 0.0256.
        var (benefit, cost) = IgnisKeepWarm.Worth(IgnisMoneyTests.Opus(), null, TimeSpan.FromHours(1), 0.5);
        Assert.Equal(3.9m, benefit);
        Assert.Equal(0.2m, cost);
        Assert.Equal(IgnisKeepWarmAction.Ping, IgnisKeepWarm.Decide(Input(TimeSpan.FromMinutes(58)) with { PrefixTokens = null }).Action);
        // Kept warm saving: the $0.78 rebuild avoided minus $0.06 of pings.
        Assert.Equal(0.72m, IgnisKeepWarm.KeptWarmSaving(IgnisMoneyTests.Opus(), 100_000, TimeSpan.FromHours(1), 0.06m));
        Assert.Equal(ProviderKind.Claude, IgnisMoneyTests.Usage(0, 0).Provider);
    }
}
