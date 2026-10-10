using Marrow.Testing;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// ED-NEW-ignis-guards: the keep-warm guards' pure maths. Owner 2026-10-10: "Not every chat stays active; I have lots of old
// chats. If Ignis was on, will it keep sending messages in old chats to keep the cache warm?"
public sealed class IgnisKeepWarmGuardsTests
{
    private static TimeSpan M(double minutes) => TimeSpan.FromMinutes(minutes);
    private static TimeSpan H(double hours) => TimeSpan.FromHours(hours);
    private static TimeSpan[] Gaps(params double[] minutes) => [.. minutes.Select(M)];

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_constants_are_the_briefs()
    {
        Assert.Equal(0.9, IgnisKeepWarmGuards.ReturnPercentile);
        Assert.Equal(H(3), IgnisKeepWarmGuards.FallbackReturn);
        Assert.Equal(H(24), IgnisKeepWarmGuards.LearnedCeiling);
        Assert.Equal(3, IgnisKeepWarmGuards.WarmChats);
        Assert.Equal(0.50m, IgnisKeepWarmGuards.StartAllowance);
        Assert.Equal(50_000, IgnisKeepWarmGuards.SwitchNoticeTokens);
        Assert.Equal(4, IgnisKeepWarmGuards.MissTurns);
        Assert.Equal(12, IgnisSettings.DefaultCoolAfterHours);
        Assert.Equal([1, 2, 4, 6, 8, 12, 24], IgnisSettings.CoolAfterChoices);
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_learned_return_is_the_90th_percentile_of_own_then_global_gaps_else_three_hours_never_under_one_lifetime()
    {
        var own = Gaps(1, 2, 3, 4, 5, 6, 7, 8, 9, 100);
        Assert.Equal(M(9), IgnisKeepWarmGuards.Percentile(own, .9));
        Assert.Equal(M(9), IgnisKeepWarmGuards.LearnedReturn(own, Gaps(500, 500, 500, 500, 500), M(5)));
        // fewer than five own gaps: the global history
        Assert.Equal(M(400), IgnisKeepWarmGuards.LearnedReturn(Gaps(1, 2), Gaps(100, 200, 300, 400, 400, 50), M(5)));
        // neither: three hours
        Assert.Equal(H(3), IgnisKeepWarmGuards.LearnedReturn(Gaps(1), Gaps(2, 3), M(5)));
        // never under the cache lifetime: the first keep-warm of a break stays possible
        Assert.Equal(H(1), IgnisKeepWarmGuards.LearnedReturn(own, [], H(1)));
        Assert.Null(IgnisKeepWarmGuards.Percentile(Gaps(1, 2, 3, 4), .9));
    }

    [Theory]
    [Trait("Decision", "OD-0134")]
    // coolAfterHours (null: Never), learn, learned minutes, draft, expected limit minutes (-1: none)
    [InlineData(12, true, 180, false, 180)]      // both on: the sooner (learned)
    [InlineData(2, true, 180, false, 120)]       // both on: the sooner (fixed)
    [InlineData(12, false, 180, false, 720)]     // learning off: the fixed limit
    [InlineData(null, true, 180, false, 180)]    // Never + learning: learned
    [InlineData(null, true, 3000, false, 1440)]  // Never + learning: learned capped at 24 h
    [InlineData(12, true, 3000, false, 720)]     // a long learned time is still capped by the fixed limit
    [InlineData(null, false, 180, false, -1)]    // both off: no idle limit
    [InlineData(12, true, 60, true, 720)]        // a draft sets the learned part aside: the fixed limit
    [InlineData(null, true, 60, true, 1440)]     // a draft with Never: the 24-hour ceiling
    [InlineData(null, false, 60, true, -1)]      // a draft with both off: still no limit
    public void The_idle_limit_follows_the_two_settings(int? hours, bool learn, double learnedMinutes, bool draft, double expected)
    {
        var limit = IgnisKeepWarmGuards.IdleLimit(hours, learn, M(learnedMinutes), draft);
        if (expected < 0) Assert.Null(limit);
        else Assert.Equal(M(expected), limit);
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_three_most_recently_used_chats_are_kept_warm_a_chat_in_front_first()
    {
        var at = new DateTimeOffset(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var slots = IgnisKeepWarmGuards.WarmSlots([(ids[0], at, false), (ids[1], at + M(1), false), (ids[2], at + M(2), false), (ids[3], at + M(3), false), (ids[4], at - H(5), true)]);
        Assert.Equal(3, slots.Count);
        Assert.Contains(ids[4], slots); // on screen or with a draft
        Assert.Contains(ids[3], slots);
        Assert.Contains(ids[2], slots);
        Assert.DoesNotContain(ids[0], slots);
    }

    private static IgnisDayTotals Day(int offset, decimal keptWarm = 0m, decimal spent = 0m, decimal longerSpent = 0m) =>
        IgnisDayTotals.Empty(new DateOnly(2026, 10, 4).AddDays(offset)) with { KeptWarm = keptWarm, Spent = spent + longerSpent, LongerMemorySpent = longerSpent };

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_daily_allowance_is_the_start_plus_what_keep_warm_saved_this_week_less_earlier_pings()
    {
        var empty = Enumerable.Range(0, 7).Select(i => Day(i)).ToArray();
        Assert.Equal(0.50m, IgnisKeepWarmGuards.DailyAllowance(empty));
        Assert.Equal(0.50m, IgnisKeepWarmGuards.DailyAllowance([]));
        // saved $3 over the week, pings cost $1 on the earlier days: $0.50 + $2
        var week = new[] { Day(0, keptWarm: 1m, spent: 0.4m), Day(1, keptWarm: 1m, spent: 0.6m), Day(2), Day(3), Day(4), Day(5), Day(6, keptWarm: 1m, spent: 0.3m, longerSpent: 5m) };
        Assert.Equal(2.50m, IgnisKeepWarmGuards.DailyAllowance(week));
        Assert.Equal(0.3m, IgnisKeepWarmGuards.PingSpent(week[6])); // the longer-lifetime writes are not pings
        Assert.False(IgnisKeepWarmGuards.OverDailyLimit(week));
        // never below the start, however much was spent
        var spentMore = new[] { Day(0, spent: 9m), Day(1), Day(2), Day(3), Day(4), Day(5), Day(6, spent: 0.49m) };
        Assert.Equal(0.50m, IgnisKeepWarmGuards.DailyAllowance(spentMore));
        Assert.False(IgnisKeepWarmGuards.OverDailyLimit(spentMore));
        Assert.True(IgnisKeepWarmGuards.OverDailyLimit(spentMore[..6].Append(Day(6, spent: 0.50m)).ToArray()));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void A_switch_costs_the_prefix_at_the_new_models_write_price()
    {
        var opus = IgnisMoneyTests.Opus(); // writes $5 (5 min) and $8 (1 h) per million
        Assert.Equal(2.40m, IgnisKeepWarmGuards.SwitchCost(opus, 300_000, H(1)));
        Assert.Equal(1.50m, IgnisKeepWarmGuards.SwitchCost(opus, 300_000, M(5)));
        Assert.Equal(2.40m, IgnisKeepWarmGuards.SwitchCost(opus, 300_000, null)); // the points' main lifetime (1 h)
        // a price step above the prefix size applies
        var haiku = IgnisMoneyTests.HaikuWithStep();
        Assert.Equal(200_000 * 1m / 1_000_000m, IgnisKeepWarmGuards.SwitchCost(haiku, 200_000, H(1)));
        Assert.Equal(0m, IgnisKeepWarmGuards.SwitchCost(opus, 0, H(1)));
        Assert.True(IgnisKeepWarmGuards.ShowsSwitchNotice(true, 50_000));
        Assert.False(IgnisKeepWarmGuards.ShowsSwitchNotice(true, 49_999));
        Assert.False(IgnisKeepWarmGuards.ShowsSwitchNotice(false, 300_000));
        Assert.False(IgnisKeepWarmGuards.ShowsSwitchNotice(true, null));
        Assert.Equal("Switching to Opus 5.5 rebuilds 300K tokens: about $1.50.", IgnisWords.SwitchCostLine("Opus 5.5", 300_000, 1.50m));
    }

    private static IgnisCacheTurn Turn(long prompt = 20_000, long read = 0, long written = 20_000, string model = "m", string? effort = "high") => new(prompt, read, written, model, effort);

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void Four_turns_that_read_nothing_flag_the_cache_with_its_likely_cause()
    {
        Assert.Null(IgnisKeepWarmGuards.NotReused([Turn(), Turn(), Turn()]));
        Assert.Equal(IgnisCacheMissCause.PrefixChanges, IgnisKeepWarmGuards.NotReused([Turn(), Turn(), Turn(), Turn()]));
        Assert.Equal(IgnisCacheMissCause.ModelSwitches, IgnisKeepWarmGuards.NotReused([Turn(model: "a"), Turn(model: "b"), Turn(model: "a"), Turn(model: "b")]));
        Assert.Equal(IgnisCacheMissCause.EffortSwitches, IgnisKeepWarmGuards.NotReused([Turn(effort: "low"), Turn(effort: "high"), Turn(), Turn()]));
        Assert.Equal(IgnisCacheMissCause.NoCaching, IgnisKeepWarmGuards.NotReused([Turn(written: 0), Turn(written: 0), Turn(written: 0), Turn(written: 0)]));
        Assert.Equal(IgnisCacheMissCause.Unknown, IgnisKeepWarmGuards.NotReused([Turn(written: 0), Turn(), Turn(), Turn()]));
        // near zero counts; a real read does not
        Assert.NotNull(IgnisKeepWarmGuards.NotReused([Turn(read: 400), Turn(), Turn(), Turn()]));
        Assert.Null(IgnisKeepWarmGuards.NotReused([Turn(read: 401), Turn(), Turn(), Turn()]));
        // a prompt too short to cache is never flagged
        Assert.Null(IgnisKeepWarmGuards.NotReused([Turn(prompt: 4_095, written: 0), Turn(), Turn(), Turn()]));
        // only the last four count
        Assert.Null(IgnisKeepWarmGuards.NotReused([Turn(), Turn(), Turn(), Turn(read: 15_000)]));
        Assert.NotNull(IgnisKeepWarmGuards.NotReused([Turn(read: 15_000), Turn(), Turn(), Turn(), Turn()]));
        Assert.Equal("This chat’s cache isn’t being reused: the model keeps changing", IgnisWords.NotReusedLine(IgnisCacheMissCause.ModelSwitches));
        Assert.Equal("This chat’s cache isn’t being reused", IgnisWords.NotReusedLine(IgnisCacheMissCause.Unknown));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_guard_words_and_their_stored_names()
    {
        Assert.Equal("Turn off", IgnisWords.TurnOff);
        Assert.Equal("Cooled", IgnisWords.EmberCooled);
        Assert.Equal("Kept warm · 42 min left", IgnisWords.EmberLitTip(M(41.5)));
        Assert.Equal("Kept warm · under a minute left", IgnisWords.EmberLitTip(TimeSpan.FromSeconds(20)));
        Assert.Equal("Kept warm", IgnisWords.EmberLitTip(null));
        Assert.Equal("12 hours", IgnisSettingsWords.CoolAfter(12));
        Assert.Equal("1 hour", IgnisSettingsWords.CoolAfter(1));
        Assert.Equal("Never", IgnisSettingsWords.CoolAfter(null));
        foreach (var hold in Enum.GetValues<IgnisKeepWarmHold>()) Assert.Equal(hold, IgnisWords.HoldFrom(IgnisWords.RawName(hold)));
        foreach (var cause in Enum.GetValues<IgnisCacheMissCause>()) Assert.Equal(cause, IgnisWords.CauseFrom(IgnisWords.RawName(cause)));
        Assert.Equal(IgnisKeepWarmHold.None, IgnisWords.HoldFrom("nonsense"));
        var held = new IgnisEvent("e", Guid.Empty, IgnisEventKind.KeepWarmHeld, DateTimeOffset.UnixEpoch, Reason: "IdleLimit");
        Assert.Equal("Let it cool: idle past its limit", IgnisWords.HistoryRow(held));
        var sw = new IgnisEvent("s", Guid.Empty, IgnisEventKind.SwitchCost, DateTimeOffset.UnixEpoch, 1.5m, Model: "Opus 5.5", AtTokens: 300_000);
        Assert.Equal("Switch to Opus 5.5 rebuilds 300K", IgnisWords.HistoryRow(sw));
        Assert.Equal("$1.50", IgnisWords.EventAmount(sw));
        Assert.Equal("Switching to Opus 5.5 rebuilds 300K tokens: about $1.50.", IgnisWords.EventLine(sw));
    }

    [Fact]
    [Trait("Decision", "OD-0134")]
    public void The_idle_limit_settings_default_on_and_round_trip_while_older_values_read_as_the_defaults()
    {
        var store = new InMemorySettingsStore();
        var settings = new IgnisSettings(store);
        Assert.Equal(12, settings.Value.CoolAfterHours);
        Assert.True(settings.Value.LearnCooling);
        // an older stored value (before these settings) reads with the defaults, and the defaults write no new keys
        store.Set("ignis", "{\"defaultMode\":\"keepWarm\",\"auto\":true}");
        Assert.Equal(12, settings.Value.CoolAfterHours);
        Assert.True(settings.Value.LearnCooling);
        Assert.DoesNotContain("cool", IgnisSettings.ToJson(settings.Value), StringComparison.OrdinalIgnoreCase);
        foreach (var (hours, learn) in new (int?, bool)[] { (null, false), (null, true), (1, false), (24, true), (12, false) })
        {
            settings.Update(p => p with { CoolAfterHours = hours, LearnCooling = learn });
            var reread = new IgnisSettings(store).Value;
            Assert.Equal(hours, reread.CoolAfterHours);
            Assert.Equal(learn, reread.LearnCooling);
        }
        Assert.Contains("\"coolAfterHours\":null", IgnisSettings.ToJson(IgnisPreferences.Default with { CoolAfterHours = null }), StringComparison.Ordinal);
        Assert.Equal(12, IgnisSettings.FromJson("{\"coolAfterHours\":0}").CoolAfterHours); // out of range keeps the default
        Assert.Equal(12, IgnisSettings.FromJson("{\"coolAfterHours\":\"x\"}").CoolAfterHours);
    }
}
