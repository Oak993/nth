using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// PORT-ONLY: Ignis (ED-NEW-ignis-runtime; PLAN unit U3). The per-chat controller on a manual clock (TestRuntime): when it
// keeps warm, when it tidies up, when it pauses, what it records. Engine, points, ledger and runtime are fakes; nothing
// reaches a model provider.
[Trait("Decision", "OD-0134")]
public sealed class IgnisControllerTests : IDisposable
{
    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEngine engine = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisSettings settings;
    private readonly IgnisController controller;

    public IgnisControllerTests()
    {
        settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, engine, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private void Start(IgnisTurnUsage? usage = null) => main.OnMain(() =>
    {
        controller.Attach(port);
        controller.Observe(Thread, usage ?? IgnisSamples.Usage(read: 40_000, write1h: 2_000));
        controller.TurnFinished(Thread);
    });

    private void Advance(TimeSpan by) => main.Advance(by);

    private IgnisChatStatus Status => main.OnMain(() => controller.Status(Thread));

    [Fact]
    public void Off_does_nothing_no_ping_no_tidy_no_line_no_money()
    {
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        Start(IgnisSamples.Usage(read: 40_000, write1h: 2_000, context: 180_000));
        Advance(TimeSpan.FromHours(3));

        Assert.Equal(0, port.Pings);
        Assert.Empty(port.Tidies);
        Assert.Empty(port.Lines);
        Assert.Empty(ledger.Entries);
        Assert.Empty(engine.Questions);
        Assert.Equal(IgnisCacheState.Off, Status.Cache);
        Assert.Equal(IgnisNext.Off, Status.Next);
    }

    [Fact]
    public void A_one_hour_cache_is_kept_warm_five_minutes_before_it_goes_cold_and_the_ping_is_costed()
    {
        Start();
        Assert.Equal(IgnisCacheState.Warm, Status.Cache);
        Assert.Equal(TimeSpan.FromHours(1), Status.Ttl);
        Assert.Equal(0.40m, Status.SavedToday); // 40,000 reads at the fake's 1¢ per 1,000

        Advance(TimeSpan.FromMinutes(54));
        Assert.Equal(0, port.Pings);
        Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, port.Pings);
        Assert.True(Status.KeepingWarm);
        var question = Assert.Single(engine.Questions);
        Assert.Equal(TimeSpan.FromMinutes(5), question.TimeLeft);
        Assert.Equal(TimeSpan.FromMinutes(55), question.IdleFor);

        // the ping turn reads the cache and ends: one Spent entry, one quiet "kept warm" line, the clock restarts
        main.OnMain(() =>
        {
            controller.Observe(Thread, IgnisSamples.Usage(read: 42_000, uncached: 10));
            controller.TurnFinished(Thread);
        });
        var spent = Assert.Single(ledger.Entries, entry => entry.Kind == IgnisLedgerKind.Spent);
        Assert.Equal(IgnisSource.KeptWarm, spent.Source);
        Assert.Equal(42_010 / 1000m * 0.001m, spent.Amount);
        var line = Assert.Single(port.Lines);
        Assert.Equal(IgnisEventKind.KeptWarm, line.Kind);
        Assert.Equal(IgnisAmountKind.Cost, line.AmountKind);
        Assert.False(Status.KeepingWarm);
        Assert.Equal(1, Status.PingsToday);
        Assert.Equal(0.40m, Status.SavedToday); // a ping's own cache read is not a saving

        // the user comes back and reads the warm cache: the ping's saving is the rebuild it avoided
        Advance(TimeSpan.FromMinutes(10));
        main.OnMain(() => controller.Observe(Thread, IgnisSamples.Usage(read: 42_000)));
        Assert.Equal(42_000 / 1000m * 0.02m, Status.SavedBySource[IgnisSource.KeptWarm]);
    }

    [Fact]
    public void An_api_session_keeps_warm_with_its_silent_request_instead_of_a_ping()
    {
        port.CanKeepWarmSilently = true;
        Start();
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(0, port.Pings);
        Assert.Equal(1, port.SilentKeepWarms);
    }

    [Fact]
    public void A_chat_that_is_busy_is_not_pinged_until_it_is_between_tasks_again()
    {
        Start();
        port.IsBetweenTasks = false;
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(0, port.Pings);
        Advance(IgnisController.BusyRetry);
        Assert.Equal(0, port.Pings);
        port.IsBetweenTasks = true;
        Advance(IgnisController.BusyRetry);
        Assert.Equal(1, port.Pings);
    }

    [Fact]
    public void When_the_engine_says_it_is_not_worth_it_the_cache_goes_cold_with_one_line_and_the_loss()
    {
        engine.KeepWarm = false;
        Start(IgnisSamples.Usage(read: 40_000, write1h: 2_000, context: 50_000));
        Advance(TimeSpan.FromMinutes(59));
        Assert.Empty(port.Lines);
        Assert.Equal(IgnisCacheState.Warm, Status.Cache);
        Advance(TimeSpan.FromMinutes(1));

        Assert.Equal(0, port.Pings);
        var line = Assert.Single(port.Lines);
        Assert.Equal(IgnisEventKind.WentCold, line.Kind);
        Assert.Equal(IgnisAmountKind.Lost, line.AmountKind);
        Assert.Equal(1.00m, line.Amount); // 50,000 tokens at the fake's 2¢ per 1,000
        Assert.Equal(1.00m, Status.LostToday);
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
        Assert.Equal(IgnisNext.Cold, Status.Next);
        Assert.Same(line, Assert.Single(ledger.Recorded));

        Advance(TimeSpan.FromHours(2));
        Assert.Single(port.Lines); // once per warm spell
    }

    [Fact]
    public void Tidy_up_waits_for_between_tasks_then_keeps_the_settings_turns_and_says_so_quietly()
    {
        port.IsBetweenTasks = false;
        Start(IgnisSamples.Usage(read: 150_000, write1h: 10_000, context: 160_000)); // over the fake's 150K point
        Assert.Empty(port.Tidies);
        Advance(IgnisController.BusyRetry);
        Assert.Empty(port.Tidies);

        port.IsBetweenTasks = true;
        Advance(IgnisController.BusyRetry);
        Assert.Equal([12], port.Tidies);
        Assert.True(Status.Tidying);
        Assert.Equal(IgnisNext.Tidying, Status.Next);
        Assert.Equal(0, port.Pings);

        port.IsTidying = false;
        main.OnMain(() => controller.TurnFinished(Thread));
        Assert.False(Status.Tidying);
        Assert.True(Status.JustTidied);
        var line = Assert.Single(port.Lines);
        Assert.Equal(IgnisEventKind.TidiedUp, line.Kind);
        Assert.Equal(160_000, line.AtTokens);
        Advance(IgnisController.JustTidiedFor);
        Assert.False(Status.JustTidied);

        // the next request shows the shorter chat: the tidy-up's saving is recorded then
        main.OnMain(() => controller.Observe(Thread, IgnisSamples.Usage(read: 20_000, context: 30_000)));
        Assert.Equal((160_000 - 30_000) / 1000m * 0.005m, Status.SavedBySource[IgnisSource.TidyUp]);
    }

    [Fact]
    public void Keep_warm_mode_never_tidies_up()
    {
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.KeepWarm, true));
        Start(IgnisSamples.Usage(read: 150_000, write1h: 10_000, context: 160_000));
        Advance(TimeSpan.FromMinutes(55));
        Assert.Empty(port.Tidies);
        Assert.Equal(1, port.Pings);
        Assert.False(Status.CanTidy);
    }

    [Fact]
    public void With_auto_off_nothing_runs_by_itself_but_the_buttons_still_work()
    {
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.WarmAndTidy, false));
        Start(IgnisSamples.Usage(read: 150_000, write1h: 10_000, context: 160_000));
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(0, port.Pings);
        Assert.Empty(port.Tidies);
        Assert.Equal(IgnisNext.AutoOffWarmAndTidy, Status.Next);

        Assert.True(Status.CanKeepWarm);
        main.OnMain(() => controller.KeepWarmNow(Thread));
        Assert.Equal(1, port.Pings);
        main.OnMain(() => controller.TurnFinished(Thread));
        main.OnMain(() => controller.TidyUpNow(Thread));
        Assert.Equal([12], port.Tidies);
    }

    [Theory]
    [InlineData("locked", IgnisPause.Locked)]
    [InlineData("archived", IgnisPause.Archived)]
    [InlineData("plan", IgnisPause.PlanNearlyFull)]
    public void A_paused_chat_is_not_kept_warm_and_goes_cold(string reason, IgnisPause pause)
    {
        Start();
        switch (reason)
        {
            case "locked": environment.IsLocked = true; break;
            case "archived": environment.Archived.Add(Thread); break;
            case "plan": environment.PlanPercent = 95; break;
        }
        Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(0, port.Pings);
        Assert.Equal(pause, Status.Pause);
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
    }

    [Fact]
    public void A_plan_window_just_under_95_percent_does_not_pause()
    {
        environment.PlanPercent = 94.9;
        Start();
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
    }

    [Fact]
    public void After_a_usage_limit_error_it_pauses_until_the_limit_resets()
    {
        Start();
        main.OnMain(() => controller.UsageLimitHit(Thread, SwiftDate.Now + TimeSpan.FromHours(2)));
        Assert.Equal(IgnisPause.UsageLimit, Status.Pause);
        Assert.False(Status.CanKeepWarm);
        Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(0, port.Pings);

        Advance(TimeSpan.FromMinutes(60));
        main.OnMain(() =>
        {
            controller.Observe(Thread, IgnisSamples.Usage(read: 0, write1h: 40_000));
            controller.TurnFinished(Thread);
        });
        Assert.Equal(IgnisPause.None, Status.Pause);
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
    }

    [Fact]
    public void A_model_without_keep_warm_in_the_points_is_never_pinged()
    {
        port.Model = IgnisSamples.Haiku;
        Start(IgnisSamples.Usage(read: 40_000, write1h: 2_000, model: IgnisSamples.Haiku));
        Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(0, port.Pings);
        Assert.Equal(IgnisPause.NotSupported, Status.Pause);
        Assert.False(Status.CanKeepWarm);
    }

    [Fact]
    public void Switching_the_model_moves_the_tidy_point()
    {
        Start(IgnisSamples.Usage(read: 100_000, write1h: 10_000, context: 120_000));
        Assert.Equal(150_000, Status.TidyAt);
        Assert.Equal("Opus Test", Status.ModelDisplayName);

        main.OnMain(() =>
        {
            controller.Observe(Thread, IgnisSamples.Usage(read: 0, write1h: 160_000, model: IgnisSamples.Sonnet, context: 160_000));
            controller.TurnFinished(Thread);
        });
        Assert.Equal(190_000, Status.TidyAt);
        Assert.Empty(port.Tidies); // 160K is under Sonnet's point

        main.OnMain(() =>
        {
            controller.Observe(Thread, IgnisSamples.Usage(read: 150_000, write1h: 10_000, context: 160_000));
            controller.TurnFinished(Thread);
        });
        Assert.Equal([12], port.Tidies); // back on Opus, 160K is over its point
    }

    [Fact]
    public void Heads_are_tracked_with_their_own_cache_and_point_when_included()
    {
        Start();
        main.OnMain(() => controller.Observe(Thread, IgnisSamples.Usage(read: 10_000, write5m: 500, agent: "agent-1", context: 20_000)));
        var head = Assert.Single(Status.Heads);
        Assert.Equal("Head 1", head.Label);
        Assert.Equal(IgnisCacheState.Warm, head.Cache);
        Assert.Equal(150_000, head.TidyAt);
        Assert.Equal(SwiftDate.Now + TimeSpan.FromMinutes(5), head.ExpiresAt);

        main.OnMain(() => controller.Observe(Thread, IgnisSamples.Usage(read: 10_000, agent: "agent-1", model: IgnisSamples.Sonnet, context: 20_000)));
        head = Assert.Single(Status.Heads);
        Assert.Equal(190_000, head.TidyAt);
        var line = Assert.Single(port.Lines);
        Assert.Equal(IgnisEventKind.HeadTidiedAfterSwitch, line.Kind);
        Assert.Equal("Head 1", line.HeadLabel);
        Assert.Equal("Sonnet Test", line.Model);
        Assert.Equal(190_000, line.AtTokens);
    }

    [Fact]
    public void Heads_are_left_out_when_the_setting_is_off()
    {
        settings.Update(value => value with { IncludeHeads = false });
        Start();
        var before = ledger.Entries.Count;
        main.OnMain(() => controller.Observe(Thread, IgnisSamples.Usage(read: 10_000, agent: "agent-1")));
        Assert.Empty(Status.Heads);
        Assert.Equal(before, ledger.Entries.Count);
    }

    [Fact]
    public void Missing_cache_numbers_record_no_savings_and_fall_back_to_the_points_lifetime()
    {
        Start(IgnisTurnUsage.NotReported(ProviderKind.Claude, IgnisSamples.Opus, null, 30_000, 200_000));
        Assert.False(Status.CacheReported);
        Assert.Null(Status.ReuseRate);
        Assert.Equal(TimeSpan.FromSeconds(3600), Status.Ttl);
        Assert.Empty(ledger.Entries);
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings); // the time-based timer still runs
    }

    [Fact]
    public void Pings_stop_after_the_idle_stretch_limit()
    {
        Start(IgnisSamples.Usage(read: 1_000, write5m: 500)); // a 5-minute cache: pinged 30 s before it goes cold
        for (var round = 0; round < IgnisController.MaxPingsPerIdle + 2; round++)
        {
            var before = port.Pings;
            Advance(TimeSpan.FromMinutes(5));
            if (port.Pings == before) continue;
            main.OnMain(() =>
            {
                controller.Observe(Thread, IgnisSamples.Usage(read: 1_500, write5m: 10, uncached: 1));
                controller.TurnFinished(Thread);
            });
        }
        Assert.Equal(IgnisController.MaxPingsPerIdle, port.Pings);
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
    }

    [Fact]
    public void Lead_is_a_tenth_of_the_lifetime_between_15_seconds_and_5_minutes()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), IgnisController.Lead(TimeSpan.FromMinutes(5)));
        Assert.Equal(TimeSpan.FromMinutes(5), IgnisController.Lead(TimeSpan.FromHours(1)));
        Assert.Equal(TimeSpan.FromSeconds(15), IgnisController.Lead(TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void The_lifetime_comes_from_the_stream_writes_before_the_points()
    {
        var model = points.Models[0];
        Assert.Equal(TimeSpan.FromHours(1), IgnisController.Ttl(IgnisSamples.Usage(read: 0, write1h: 1, write5m: 1), model, subagent: true));
        Assert.Equal(TimeSpan.FromMinutes(5), IgnisController.Ttl(IgnisSamples.Usage(read: 0, write5m: 1), model, subagent: false));
        Assert.Equal(TimeSpan.FromHours(1), IgnisController.Ttl(IgnisSamples.Usage(read: 10), model, subagent: false));
        Assert.Equal(TimeSpan.FromMinutes(5), IgnisController.Ttl(IgnisSamples.Usage(read: 10), model, subagent: true));
        Assert.Null(IgnisController.Ttl(IgnisSamples.Usage(read: 10), null, subagent: false));
    }

    [Fact]
    public void The_report_names_the_model_mode_and_todays_savings()
    {
        Start();
        var report = main.OnMain(() => controller.Report(Thread));
        Assert.Contains("Model: Opus Test · Mode: Warm + tidy up · Auto on", report, StringComparison.Ordinal);
        Assert.Contains("Saved today: $0.40", report, StringComparison.Ordinal);
    }
}
