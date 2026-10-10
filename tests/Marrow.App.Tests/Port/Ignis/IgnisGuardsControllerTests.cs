using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// ED-NEW-ignis-guards: the keep-warm guards in the controller on a manual clock. Owner 2026-10-10: "Not every chat stays
// active; I have lots of old chats. If Ignis was on, will it keep sending messages in old chats to keep the cache warm?"
// The idle limit (learned and/or "Cool idle chats after"), three chats at most, the daily allowance, the sidebar flame
// (lit, cooled, "Turn off"), the switch-cost notice and the never-reused check. Fakes only; nothing reaches a provider.
[Trait("Decision", "OD-0134")]
public sealed class IgnisGuardsControllerTests : IDisposable
{
    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEngine engine = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisSettings settings;
    private readonly IgnisController controller;

    public IgnisGuardsControllerTests()
    {
        settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, engine, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private DateTimeOffset Now => main.OnMain(() => SwiftDate.Now);

    // A small 1-hour cache: pings cost a tenth of a cent each (the fake's 0.001 per 1,000), far under the daily allowance.
    private void Start(FakeIgnisPort? chat = null, IgnisTurnUsage? usage = null) => main.OnMain(() =>
    {
        var target = chat ?? port;
        controller.Attach(target);
        controller.Observe(target.ThreadID, usage ?? IgnisSamples.Usage(read: 1_000, write1h: 200));
        controller.TurnFinished(target.ThreadID);
    });

    // A real user turn after a break (its gap is learned), ending now.
    private void UserTurn(FakeIgnisPort? chat = null, IgnisTurnUsage? usage = null) => main.OnMain(() =>
    {
        var target = chat ?? port;
        controller.Observe(target.ThreadID, usage ?? IgnisSamples.Usage(read: 1_000, write1h: 50));
        controller.TurnFinished(target.ThreadID);
    });

    // Runs the clock in steps, answering every ping like a CLI's ping turn would; returns when each ping was sent.
    private List<DateTimeOffset> Run(TimeSpan total, TimeSpan step, FakeIgnisPort? chat = null)
    {
        var target = chat ?? port;
        var sent = new List<DateTimeOffset>();
        for (var at = TimeSpan.Zero; at < total; at += step)
        {
            var before = target.Pings;
            main.Advance(step);
            if (target.Pings == before) continue;
            sent.Add(Now);
            main.OnMain(() =>
            {
                controller.Observe(target.ThreadID, IgnisSamples.Usage(read: 1_200, uncached: 1));
                controller.TurnFinished(target.ThreadID);
            });
        }
        return sent;
    }

    private IgnisChatStatus Status => main.OnMain(() => controller.Status(Thread));

    private IgnisEmber Ember(FakeIgnisPort? chat = null) => main.OnMain(() => controller.Ember((chat ?? port).ThreadID).Ember);

    private IEnumerable<IgnisEvent> Held(IgnisKeepWarmHold hold) =>
        ledger.Recorded.Where(e => e.Kind == IgnisEventKind.KeepWarmHeld && e.Reason == IgnisWords.RawName(hold));

    // ---- the idle limit and its two settings

    [Fact]
    public void By_default_a_chat_with_no_history_is_kept_warm_for_three_hours_then_cools_once_with_one_history_row()
    {
        Start();
        var finished = Now;
        Assert.Equal(IgnisEmber.Lit, Ember());
        Assert.Equal(finished + TimeSpan.FromHours(3), Status.IdleLimitAt);
        var pings = Run(TimeSpan.FromHours(5), TimeSpan.FromMinutes(5));
        Assert.Equal(3, pings.Count); // at 55 min, 1 h 50 and 2 h 45 of idle
        Assert.All(pings, at => Assert.True(at - finished < TimeSpan.FromHours(3)));
        Assert.Equal(IgnisEmber.Cooled, Ember());
        Assert.Single(Held(IgnisKeepWarmHold.IdleLimit));
        Assert.DoesNotContain(port.Lines, line => line.Kind == IgnisEventKind.KeepWarmHeld); // History only, never a timeline line
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
    }

    [Fact]
    public void The_flame_dims_at_the_idle_limit_itself_not_later()
    {
        settings.Update(p => p with { CoolAfterHours = 1, LearnCooling = false });
        Start();
        main.Advance(TimeSpan.FromMinutes(55)); // the ping
        main.OnMain(() => { controller.Observe(Thread, IgnisSamples.Usage(read: 1_200, uncached: 1)); controller.TurnFinished(Thread); });
        main.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(IgnisEmber.Lit, Ember());
        main.Advance(TimeSpan.FromMinutes(1)); // one hour idle: the limit
        Assert.Equal(IgnisEmber.Cooled, Ember());
        Assert.Equal(IgnisKeepWarmHold.IdleLimit, Status.Hold);
        Assert.Equal(IgnisCacheState.Warm, Status.Cache); // the ping's cache is still warm; it just is not kept warm any longer
    }

    [Fact]
    public void Learning_off_keeps_warm_until_the_fixed_limit()
    {
        settings.Update(p => p with { LearnCooling = false }); // 12 hours
        Start();
        var finished = Now;
        var pings = Run(TimeSpan.FromHours(14), TimeSpan.FromMinutes(5));
        Assert.True(pings.Count >= 11, pings.Count + " pings");
        Assert.All(pings, at => Assert.True(at - finished < TimeSpan.FromHours(12)));
        Assert.True(pings[^1] - finished > TimeSpan.FromHours(11));
    }

    [Fact]
    public void Both_off_keeps_warm_while_it_pays_with_no_idle_limit()
    {
        settings.Update(p => p with { CoolAfterHours = null, LearnCooling = false });
        Start();
        var finished = Now;
        Assert.Null(Status.IdleLimitAt);
        var pings = Run(TimeSpan.FromHours(30), TimeSpan.FromMinutes(5));
        Assert.True(pings[^1] - finished > TimeSpan.FromHours(29));
        Assert.Equal(IgnisEmber.Lit, Ember());
    }

    [Fact]
    public void Never_with_learning_caps_the_learned_time_at_a_day()
    {
        settings.Update(p => p with { CoolAfterHours = null });
        Start();
        Assert.Equal(Now + TimeSpan.FromHours(3), Status.IdleLimitAt); // no history: 3 hours
    }

    [Fact]
    public void The_learned_time_comes_from_the_chats_own_breaks_and_never_under_one_lifetime()
    {
        Start();
        for (var i = 0; i < 5; i++)
        {
            main.Advance(TimeSpan.FromMinutes(10));
            UserTurn();
        }
        // five 10-minute breaks: the 90th percentile is 10 minutes, raised to the 1-hour lifetime
        var finished = Now;
        Assert.Equal(finished + TimeSpan.FromHours(1), Status.IdleLimitAt);
        var pings = Run(TimeSpan.FromHours(3), TimeSpan.FromMinutes(5));
        Assert.Single(pings); // the first keep-warm of the break (at 55 min), then the limit
        Assert.Single(Held(IgnisKeepWarmHold.IdleLimit));
    }

    [Fact]
    public void A_draft_sets_the_learned_time_aside()
    {
        Start();
        for (var i = 0; i < 5; i++) { main.Advance(TimeSpan.FromMinutes(10)); UserTurn(); }
        environment.Draft = true;
        Assert.Equal(Now + TimeSpan.FromHours(12), Status.IdleLimitAt);
    }

    [Fact]
    public void Settings_changes_reach_a_running_chat_at_once()
    {
        Start();
        var finished = Now;
        settings.Update(p => p with { CoolAfterHours = 2 });
        Assert.Equal(finished + TimeSpan.FromHours(2), Status.IdleLimitAt);
        settings.Update(p => p with { CoolAfterHours = 24 });
        Assert.Equal(finished + TimeSpan.FromHours(3), Status.IdleLimitAt);
    }

    // ---- three chats at most

    [Fact]
    public void Only_the_three_most_recently_used_chats_are_kept_warm()
    {
        var chats = new[] { port, new FakeIgnisPort(), new FakeIgnisPort(), new FakeIgnisPort() };
        foreach (var chat in chats)
        {
            Start(chat);
            main.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.Equal(IgnisEmber.Cooled, Ember(chats[0])); // lit while it was among the three, then a fourth came
        Assert.All(chats.Skip(1), chat => Assert.Equal(IgnisEmber.Lit, Ember(chat)));
        Assert.Equal(IgnisKeepWarmHold.OtherChats, Status.Hold);
        main.Advance(TimeSpan.FromMinutes(52)); // the first chat's keep-warm is due (55 min after its turn)
        Assert.Equal(0, chats[0].Pings);
        Assert.Single(Held(IgnisKeepWarmHold.OtherChats));
        main.Advance(TimeSpan.FromMinutes(3));
        Assert.All(chats.Skip(1), chat => Assert.Equal(1, chat.Pings));
    }

    [Fact]
    public void Turning_off_a_kept_chat_frees_its_place_for_the_next()
    {
        var chats = new[] { port, new FakeIgnisPort(), new FakeIgnisPort(), new FakeIgnisPort() };
        foreach (var chat in chats) { Start(chat); main.Advance(TimeSpan.FromMinutes(1)); }
        main.OnMain(() => controller.LetItCool(chats[3].ThreadID));
        Assert.Equal(IgnisEmber.Lit, Ember(chats[0]));
        Assert.Equal(IgnisEmber.Cooled, Ember(chats[3]));
    }

    // ---- the daily allowance

    [Fact]
    public void Pings_rest_for_the_day_once_they_cost_the_days_allowance()
    {
        Start();
        main.OnMain(() => ledger.Record(new IgnisLedgerEntry(SwiftDate.Now, Guid.NewGuid(), IgnisLedgerKind.Spent, IgnisSource.KeptWarm, 0.50m)));
        Assert.Equal(IgnisKeepWarmHold.DailyLimit, Status.Hold);
        Assert.Equal(IgnisEmber.Cooled, Ember());
        main.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(0, port.Pings);
        Assert.Single(Held(IgnisKeepWarmHold.DailyLimit));
        Assert.Contains(IgnisWords.HoldReportLine(IgnisKeepWarmHold.DailyLimit), main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);
    }

    [Fact]
    public void What_keep_warm_saved_this_week_raises_the_allowance()
    {
        Start();
        main.OnMain(() =>
        {
            ledger.Record(new IgnisLedgerEntry(SwiftDate.Now.AddDays(-2), Guid.NewGuid(), IgnisLedgerKind.Saved, IgnisSource.KeptWarm, 2m));
            ledger.Record(new IgnisLedgerEntry(SwiftDate.Now, Guid.NewGuid(), IgnisLedgerKind.Spent, IgnisSource.KeptWarm, 0.60m));
        });
        Assert.Equal(IgnisKeepWarmHold.None, Status.Hold);
        main.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
    }

    // ---- the flame: lit, Turn off, relit by the next message

    [Fact]
    public void Turn_off_cools_a_lit_chat_until_the_next_message_with_no_loss_and_one_history_row()
    {
        Assert.Equal(IgnisEmber.None, Ember()); // never kept warm: nothing
        Start();
        Assert.Equal(IgnisEmber.Lit, Ember());
        main.OnMain(() => controller.LetItCool(Thread));
        Assert.Equal(IgnisEmber.Cooled, Ember());
        Assert.Equal(IgnisKeepWarmHold.LetCool, Status.Hold);
        Assert.Single(ledger.Recorded, e => e.Kind == IgnisEventKind.LetCool);
        main.Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(0, port.Pings);
        Assert.Empty(Held(IgnisKeepWarmHold.LetCool)); // the cool itself is the row
        var cold = Assert.Single(port.Lines, line => line.Kind == IgnisEventKind.WentCold);
        Assert.Null(cold.Amount); // the user was done with it: no loss
        Assert.Equal(0m, Status.LostToday);

        UserTurn(); // the next message relights it
        Assert.Equal(IgnisEmber.Lit, Ember());
        main.Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
    }

    [Fact]
    public void Turn_off_is_ignored_unless_lit()
    {
        main.OnMain(() => controller.LetItCool(Thread));
        Start();
        main.Advance(TimeSpan.FromMinutes(61)); // went cold
        Assert.Equal(IgnisEmber.Cooled, Ember());
        main.OnMain(() => controller.LetItCool(Thread));
        Assert.DoesNotContain(ledger.Recorded, e => e.Kind == IgnisEventKind.LetCool);
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        Assert.Equal(IgnisEmber.None, Ember());
    }

    // ---- the switch-cost notice

    [Fact]
    public void Switching_the_model_of_a_big_warm_chat_shows_the_rebuild_cost_once_and_never_blocks()
    {
        Start(usage: IgnisSamples.Usage(read: 290_000, write1h: 10_000, context: 300_000));
        main.OnMain(() => controller.ModelChanged(Thread, ProviderKind.Claude, IgnisSamples.Sonnet));
        var line = Assert.Single(port.Lines, l => l.Kind == IgnisEventKind.SwitchCost);
        Assert.Equal(3.00m, line.Amount); // 300K at the sample's $10 per million 1-hour write
        Assert.Equal(300_000, line.AtTokens);
        Assert.Equal("Sonnet Test", line.Model);
        Assert.Equal("Switching to Sonnet Test rebuilds 300K tokens: about $3.00.", IgnisWords.EventLine(line));
        Assert.Contains(line, ledger.Recorded); // History has it too
        Assert.DoesNotContain(ledger.Entries, e => e.Kind == IgnisLedgerKind.Spent); // a notice, not money spent

        main.OnMain(() => controller.ModelChanged(Thread, ProviderKind.Claude, IgnisSamples.Sonnet));
        main.OnMain(() => controller.ModelChanged(Thread, ProviderKind.Claude, IgnisSamples.Opus)); // back: nothing to rebuild
        Assert.Single(port.Lines, l => l.Kind == IgnisEventKind.SwitchCost);
        Assert.Equal(0, port.Pings); // nothing was sent or changed in the chat
    }

    [Theory]
    [InlineData("small")]
    [InlineData("cold")]
    [InlineData("off")]
    [InlineData("unknown")]
    public void No_switch_notice_for_a_small_or_cold_cache_with_Ignis_off_or_an_unknown_model(string why)
    {
        Start(usage: IgnisSamples.Usage(read: why == "small" ? 40_000 : 290_000, write1h: 9_000, context: why == "small" ? 49_000 : 300_000));
        if (why == "cold") main.Advance(TimeSpan.FromMinutes(61));
        if (why == "off") settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        main.OnMain(() => controller.ModelChanged(Thread, ProviderKind.Claude, why == "unknown" ? "mystery-model" : IgnisSamples.Sonnet));
        Assert.DoesNotContain(port.Lines, l => l.Kind == IgnisEventKind.SwitchCost);
    }

    // ---- the cache never reused

    [Fact]
    public void Four_turns_that_read_nothing_flag_the_chat_once_with_the_likely_cause_and_a_real_read_clears_it()
    {
        Start(usage: IgnisSamples.Usage(read: 0, write1h: 20_000));
        for (var i = 0; i < 2; i++) UserTurn(usage: IgnisSamples.Usage(read: 0, write1h: 20_000));
        Assert.Null(Status.CacheNotReused);
        UserTurn(usage: IgnisSamples.Usage(read: 0, write1h: 20_000));
        Assert.Equal(IgnisCacheMissCause.PrefixChanges, Status.CacheNotReused);
        UserTurn(usage: IgnisSamples.Usage(read: 0, write1h: 20_000));
        var flagged = Assert.Single(ledger.Recorded, e => e.Kind == IgnisEventKind.CacheNotReused);
        Assert.Equal("PrefixChanges", flagged.Reason);
        Assert.Contains(IgnisWords.NotReusedLine(IgnisCacheMissCause.PrefixChanges), main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);
        UserTurn(usage: IgnisSamples.Usage(read: 20_000));
        Assert.Null(Status.CacheNotReused);
    }

    [Fact]
    public void Alternating_models_are_named_as_the_cause()
    {
        Start(usage: IgnisSamples.Usage(read: 0, write1h: 20_000));
        foreach (var model in new[] { IgnisSamples.Sonnet, IgnisSamples.Opus, IgnisSamples.Sonnet })
            UserTurn(usage: IgnisSamples.Usage(read: 0, write1h: 20_000, model: model));
        Assert.Equal(IgnisCacheMissCause.ModelSwitches, Status.CacheNotReused);
    }

    // ---- the report

    [Fact]
    public void The_report_says_what_pings_cost_today()
    {
        Start();
        Run(TimeSpan.FromMinutes(60), TimeSpan.FromMinutes(5));
        Assert.Equal(1, Status.PingsToday);
        Assert.Equal(1_201 / 1000m * 0.001m, Status.PingSpentToday);
        Assert.Contains(IgnisWords.PingsReportLine(1, 1_201 / 1000m * 0.001m), main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);
    }
}
