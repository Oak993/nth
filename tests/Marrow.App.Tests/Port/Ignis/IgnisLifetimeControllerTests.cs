using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// PORT-ONLY: Ignis runtime (OD-0134, ED-NEW-ignis-runtime). The controller's cache-lifetime work on a manual clock
// (TestRuntime): the plan-to-credits switch and its keep-warm timing, gap learning for the chat and for helpers at every
// depth, helper rebuilds ("missed"), the longer-memory saving and its extra writes (never counted twice with keep-warm),
// and the launch decision kept for the status. Fakes only; nothing reaches a model provider.
[Trait("Decision", "OD-0134")]
public sealed class IgnisLifetimeControllerTests : IDisposable
{
    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEngine engine = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisSettings settings;
    private readonly IgnisController controller;

    public IgnisLifetimeControllerTests()
    {
        settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, engine, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private IgnisChatStatus Status => main.OnMain(() => controller.Status(Thread));

    private DateTimeOffset Now => main.OnMain(() => SwiftDate.Now);

    private void Advance(TimeSpan by) => main.Advance(by);

    private void Start(IgnisTurnUsage? usage = null) => Turn(usage ?? IgnisSamples.Usage(read: 40_000, write1h: 2_000), attach: true);

    private void Turn(IgnisTurnUsage usage, bool attach = false) => main.OnMain(() =>
    {
        if (attach) controller.Attach(port);
        controller.Observe(Thread, usage);
        controller.TurnFinished(Thread);
    });

    private void Head(IgnisTurnUsage usage) => main.OnMain(() => controller.Observe(Thread, usage));

    private static IgnisRequestSample Learned(double? gap, long w5 = 1_000) => new(DateTimeOffset.UnixEpoch, null, gap, 100_000, w5, 0, 0, false);

    /// <summary>Thirteen requests with twelve 10-minute breaks over a 100k prefix: enough for one hour (6.9 vs 0.04875 USD).</summary>
    private void Teach(IgnisCacheBucket bucket)
    {
        controller.Gaps.Add(ProviderKind.Claude, bucket, Learned(null));
        for (var i = 0; i < 12; i++) controller.Gaps.Add(ProviderKind.Claude, bucket, Learned(600));
    }

    private IgnisLifetimeChoice? Choose(Dictionary<string, string>? env = null, Guid? thread = null, ProviderKind provider = ProviderKind.Claude) =>
        main.OnMain(() => controller.ChooseCacheLifetimes(new IgnisLifetimeQuery(provider, IgnisSamples.Opus, thread ?? Thread, env ?? [], "C:/work")));

    private decimal Spent(IgnisSource source) => ledger.Entries.Where(e => e.Kind == IgnisLedgerKind.Spent && e.Source == source).Sum(e => e.Amount);

    // ---- plan vs paid credits ----

    [Fact]
    public void Five_minute_writes_on_a_plan_switch_the_chat_to_paid_credits_and_one_hour_writes_switch_it_back()
    {
        Start();
        Assert.Equal(TimeSpan.FromHours(1), Status.Ttl);
        Assert.False(Status.PaidCredits);

        // The plan's points say 1 h, but the CLI wrote 5-minute entries: paid usage credits.
        Turn(IgnisSamples.Usage(read: 40_000, write5m: 2_000));
        Assert.True(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromMinutes(5), Status.Ttl);
        Assert.Contains("Paid credits: 5-minute cache", main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);

        // Keep-warm follows the 5 minutes: 30 s before it goes cold, not 5 minutes before the hour.
        Advance(TimeSpan.FromMinutes(4));
        Assert.Equal(0, port.Pings);
        Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, port.Pings);
        Turn(IgnisSamples.Usage(read: 42_000, uncached: 10)); // the ping reads only
        Assert.True(Status.PaidCredits);

        // A read-only user turn proves nothing either way: still 5 minutes.
        Turn(IgnisSamples.Usage(read: 42_000));
        Assert.True(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromMinutes(5), Status.Ttl);

        // One-hour writes are back (the plan's usage reset): the hour rules again.
        Turn(IgnisSamples.Usage(read: 40_000, write1h: 500));
        Assert.False(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromHours(1), Status.Ttl);
        Advance(TimeSpan.FromMinutes(54));
        Assert.Equal(1, port.Pings);
        Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(2, port.Pings);
        Assert.DoesNotContain("Paid credits", main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_clis_overage_hint_switches_at_once_until_one_hour_writes_return()
    {
        Start();
        main.OnMain(() => controller.CreditsHint(Thread, true));
        Assert.True(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromMinutes(5), Status.Ttl);
        Advance(TimeSpan.FromSeconds(270));
        Assert.Equal(1, port.Pings); // replanned on the 5 minutes at once

        Turn(IgnisSamples.Usage(read: 42_000, uncached: 10));
        Assert.True(Status.PaidCredits);
        Turn(IgnisSamples.Usage(read: 40_000, write1h: 300)); // the observed writes win over the hint
        Assert.False(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromHours(1), Status.Ttl);

        main.OnMain(() => controller.CreditsHint(Thread, false));
        Assert.False(Status.PaidCredits);
    }

    [Fact]
    public void An_api_key_chat_is_five_minutes_by_default_not_paid_credits()
    {
        Choose(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-test" });
        Start(IgnisSamples.Usage(read: 40_000, write5m: 2_000));
        Assert.Equal(TimeSpan.FromMinutes(5), Status.Ttl);
        Assert.False(Status.PaidCredits);
    }

    // ---- gap learning ----

    [Fact]
    public void The_chat_learns_each_break_from_the_end_of_one_turn_to_the_start_of_the_next()
    {
        Start();
        Advance(TimeSpan.FromMinutes(10));
        port.TurnStartedAt = Now;
        Advance(TimeSpan.FromMinutes(1)); // the turn runs a minute before its usage arrives
        Turn(IgnisSamples.Usage(read: 42_000, write1h: 300));
        Advance(TimeSpan.FromMinutes(7));
        port.TurnStartedAt = null; // unknown: the usage's own time
        Turn(IgnisSamples.Usage(read: 42_000));

        var samples = controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Main);
        Assert.Equal([null, 600d, 420d], samples.Select(s => s.GapSeconds));
        Assert.Equal([0L, 42_100L, 42_400L], samples.Select(s => s.PrefixTokens)); // the chat's size before each break
        Assert.All(samples, s => Assert.Equal(0L, s.Write5m));
        Assert.Equal([2_000L, 300L, 0L], samples.Select(s => s.Write1h));
        Assert.All(samples, s => Assert.Equal(IgnisSamples.Opus, s.Model));
        Assert.All(samples, s => Assert.False(s.LongerByNth));

        // Pings are not requests of the user's rhythm, and an Off chat learns nothing.
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
        Turn(IgnisSamples.Usage(read: 42_000, uncached: 10));
        Assert.Equal(3, controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Main).Count);
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        Turn(IgnisSamples.Usage(read: 42_000));
        Assert.Equal(3, controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Main).Count);
    }

    [Fact]
    public void Helpers_at_every_depth_learn_their_own_breaks_and_a_rebuild_after_their_lifetime_is_missed_money()
    {
        Start();
        // Level 1 (toolu_A, spawned by the lead) and level 2 (toolu_B, spawned by toolu_A): each its own head.
        Head(IgnisSamples.Usage(read: 0, write5m: 10_000, agent: "toolu_A", context: 10_000));
        Head(IgnisSamples.Usage(read: 0, write5m: 4_000, agent: "toolu_B", context: 4_000));
        Advance(TimeSpan.FromMinutes(7));
        // A's 5-minute cache ran out and was written again: 10000 rewritten at the fake's 2¢ per 1000 = $0.20.
        Head(IgnisSamples.Usage(read: 0, write5m: 10_000, agent: "toolu_A", context: 10_500));
        // B still read its whole prefix: its cache was alive (a longer lifetime than assumed), nothing missed.
        Head(IgnisSamples.Usage(read: 4_000, write5m: 100, agent: "toolu_B", context: 4_100));

        Assert.Equal(["Head 1", "Head 2"], Status.Heads.Select(h => h.Label));
        var missed = Assert.Single(ledger.Entries, e => e.Kind == IgnisLedgerKind.Missed);
        Assert.Equal(0.20m, missed.Amount);
        Assert.Null(missed.Source);
        Assert.Equal(0.20m, Status.MissedToday);
        Assert.Equal(0m, Status.LostToday); // kept apart from the chat's own loss
        Assert.Contains("Helpers rebuilt their quick memory: $0.20", main.OnMain(() => controller.Report(Thread)), StringComparison.Ordinal);

        var helpers = controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers);
        Assert.Equal([null, null, 420d, 420d], helpers.Select(s => s.GapSeconds));
        Assert.Equal([0L, 0L, 10_000L, 4_000L], helpers.Select(s => s.PrefixTokens));
    }

    [Fact]
    public void Helpers_are_learned_but_not_shown_or_counted_when_heads_are_left_out()
    {
        settings.Update(value => value with { IncludeHeads = false });
        Start();
        var before = ledger.Entries.Count;
        Head(IgnisSamples.Usage(read: 0, write5m: 10_000, agent: "toolu_A", context: 10_000));
        Advance(TimeSpan.FromMinutes(7));
        Head(IgnisSamples.Usage(read: 0, write5m: 10_000, agent: "toolu_A", context: 10_000));

        Assert.Empty(Status.Heads);
        Assert.Equal(before, ledger.Entries.Count);
        Assert.Equal(2, controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers).Count);
    }

    // ---- the launch decision and its accounting ----

    [Fact]
    public void A_launch_decision_is_kept_for_the_status_and_the_report()
    {
        Teach(IgnisCacheBucket.Helpers);
        var choice = Choose();
        Assert.NotNull(choice);
        Assert.Equal(IgnisLifetimeReason.Longer, choice.Helpers.Reason);
        Assert.Equal(IgnisLifetimeReason.AlreadyLong, choice.Main.Reason); // a plan's chat is already an hour
        Assert.Equal([new KeyValuePair<string, string>("CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL", "1h")], choice.Environment);
        Start();
        Assert.Same(choice, Status.CacheLifetimes);
        var report = main.OnMain(() => controller.Report(Thread));
        Assert.Contains("Helpers’ quick memory: 1 hour, chosen by Ignis", report, StringComparison.Ordinal);
        Assert.Contains("Chat’s quick memory: already 1 hour", report, StringComparison.Ordinal);
        Assert.Equal(1, environment.SettingsReads);
    }

    [Fact]
    public void The_users_own_choice_an_old_cli_or_ignis_off_leave_the_defaults()
    {
        Teach(IgnisCacheBucket.Helpers);
        environment.ClaudeSettings.Add("subagentPromptCacheTtl");
        Assert.Equal(IgnisLifetimeVerdict.Because(IgnisCacheBucket.Helpers, IgnisLifetimeReason.UserChoice, "subagentPromptCacheTtl"), Choose()!.Helpers);
        environment.ClaudeSettings.Clear();

        environment.Version = "2.1.241";
        Assert.Equal(IgnisLifetimeReason.CliTooOld, Choose()!.Helpers.Reason);
        environment.Version = null;
        Assert.Equal(IgnisLifetimeReason.CliVersionUnknown, Choose()!.Helpers.Reason);
        environment.Version = "2.1.300";

        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        var reads = environment.SettingsReads;
        var off = Choose()!;
        Assert.Equal((IgnisLifetimeReason.IgnisOff, IgnisLifetimeReason.IgnisOff), (off.Main.Reason, off.Helpers.Reason));
        Assert.Empty(off.Environment);
        Assert.Equal(reads, environment.SettingsReads); // the user's files are not even read

        Assert.Null(Choose(provider: ProviderKind.Codex));
    }

    [Fact]
    public void A_helper_on_ignis_longer_memory_is_credited_with_the_rebuild_it_avoided_and_charged_its_extra_writes()
    {
        Teach(IgnisCacheBucket.Helpers);
        Choose();
        Start();
        Head(IgnisSamples.Usage(read: 0, write1h: 10_000, agent: "toolu_A", context: 10_000));
        Advance(TimeSpan.FromMinutes(20)); // a 5-minute cache would be cold by now
        Head(IgnisSamples.Usage(read: 10_000, write1h: 200, agent: "toolu_A", context: 10_300));

        // Avoided: 10000 read at the fake's 2¢ per 1000 = $0.20. Extra writes: 10200 at 0.3¢ per 1000 = $0.0306.
        Assert.Equal(0.20m, Status.SavedBySource[IgnisSource.LongerMemory]);
        Assert.Equal(0.0306m, Spent(IgnisSource.LongerMemory));
        Assert.DoesNotContain(ledger.Entries, e => e.Kind == IgnisLedgerKind.Missed);
        Assert.All(controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Helpers).Skip(13), s => Assert.True(s.LongerByNth));
    }

    [Fact]
    public void The_chats_longer_memory_and_keep_warm_never_count_the_same_break_twice()
    {
        Teach(IgnisCacheBucket.Main);
        var choice = Choose(new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "sk-test" })!;
        Assert.Equal(IgnisLifetimeReason.Longer, choice.Main.Reason);
        Start(); // 40000 read, 2000 written at 1 h: 2000 × 0.3¢ / 1000 = $0.006 extra
        Assert.Equal(0.006m, Spent(IgnisSource.LongerMemory));

        // A 20-minute break with no ping: the 1-hour cache was read where 5 minutes would have rebuilt 40000 → $0.80.
        Advance(TimeSpan.FromMinutes(20));
        Turn(IgnisSamples.Usage(read: 40_000, write1h: 100));
        Assert.Equal(0.80m, Status.SavedBySource[IgnisSource.LongerMemory]);
        Assert.Equal(0m, Status.SavedBySource[IgnisSource.KeptWarm]);
        Assert.False(Status.PaidCredits); // nth asked for the hour: not a credits signal

        // A break kept warm by a ping: the saving is the ping's, never also the longer memory's.
        Advance(TimeSpan.FromMinutes(55));
        Assert.Equal(1, port.Pings);
        Turn(IgnisSamples.Usage(read: 40_100, uncached: 10));
        Advance(TimeSpan.FromMinutes(10));
        Turn(IgnisSamples.Usage(read: 40_100));
        Assert.Equal(0.80m, Status.SavedBySource[IgnisSource.LongerMemory]);
        Assert.Equal(40_100 / 1000m * 0.02m, Status.SavedBySource[IgnisSource.KeptWarm]);
        Assert.True(controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Main)[^1].LongerByNth);
    }

    // ---- where keep-warm may run ----

    [Fact]
    public void Keep_warm_runs_only_where_a_ping_can_reach_the_same_cache()
    {
        Assert.Equal(
            [ProviderKind.Claude, ProviderKind.Opencode, ProviderKind.OpenRouter],
            Enum.GetValues<ProviderKind>().Where(IgnisController.KeepWarmAllowed).ToArray());

        port.Provider = ProviderKind.Codex;
        Start(IgnisSamples.Usage(read: 40_000, write1h: 2_000) with { Provider = ProviderKind.Codex });
        Advance(TimeSpan.FromMinutes(61));
        Assert.Equal(0, port.Pings);
        Assert.Equal(IgnisPause.NotSupported, Status.Pause);
        Assert.False(Status.CanKeepWarm);
        Assert.Equal(0.40m, Status.SavedToday); // the savings report still runs
    }

    [Fact]
    public void Ignis_is_on_for_a_chat_in_any_mode_but_off()
    {
        Assert.False(IgnisRuntimeStartup.IsOn(null, Thread));
        Assert.True(IgnisRuntimeStartup.IsOn(controller, Thread));
        Assert.True(IgnisRuntimeStartup.IsOn(controller, null));
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.KeepWarm, false));
        Assert.True(IgnisRuntimeStartup.IsOn(controller, Thread));
        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        Assert.False(IgnisRuntimeStartup.IsOn(controller, Thread));
    }
}
