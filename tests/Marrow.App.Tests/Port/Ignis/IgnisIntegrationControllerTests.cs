using Marrow.App.Port.Ignis.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.Port.Ignis;

// PORT-ONLY: Ignis integration (ED-NEW-ignis-integration). The controller on a manual clock with the real engine (its money maths
// are the point here): a chat's cache lifetime and the write price of that exact lifetime (a 30-minute Codex model, a Claude
// plan chat that fell to 5-minute writes), and pi's retention decision over the gaps learned for provider "pi". Fakes for
// points, ledger and environment; nothing reaches a model provider.
[Trait("Decision", "OD-0134")]
public sealed class IgnisIntegrationControllerTests : IDisposable
{
    private const string Sol = "gpt-6.1-sol";

    private readonly TestRuntime main = new();
    private readonly FakeIgnisPoints points = new();
    private readonly FakeSavingsLedger ledger = new();
    private readonly FakeIgnisEnvironment environment = new();
    private readonly FakeIgnisPort port = new();
    private readonly IgnisSettings settings;
    private readonly IgnisController controller;

    public IgnisIntegrationControllerTests()
    {
        // GPT-6.1 Sol as the bundled points give it: input 2, read 0.1, a 30-minute write at 2.5, ttlMainSeconds 1800, no keep-warm.
        points.Models.Add(new IgnisModelPoints("codex", new ValueList<string>([Sol]), ValueList<string>.Empty, "GPT-6.1 Sol", 1_050_000,
            new IgnisPrices(2m, 10m, 0.1m, null, null, ValueList<IgnisPriceStep>.Empty) { CacheWrites = new([new IgnisCacheWrite(1800, 2.5m)]) },
            new IgnisCachePoints(1800, null, false), null, null, null, "https://example.invalid/pricing", "2026-10-10"));
        settings = new IgnisSettings(main.Defaults);
        controller = main.OnMain(() => new IgnisController(settings, points, ledger, IgnisEngine.Shared, environment));
    }

    public void Dispose() => main.Dispose();

    private Guid Thread => port.ThreadID;

    private IgnisChatStatus Status => main.OnMain(() => controller.Status(Thread));

    private void Advance(TimeSpan by) => main.Advance(by);

    private void Turn(IgnisTurnUsage usage, bool attach = false) => main.OnMain(() =>
    {
        if (attach) controller.Attach(port);
        controller.Observe(Thread, usage);
        controller.TurnFinished(Thread);
    });

    private decimal Lost() => ledger.Entries.Where(entry => entry.Kind == IgnisLedgerKind.Lost).Sum(entry => entry.Amount);

    // ---- the lifetime and its write price ----

    [Fact]
    public void A_30_minute_codex_chat_takes_its_lifetime_from_the_points_and_loses_the_30_minute_write_when_it_goes_cold()
    {
        port.Provider = ProviderKind.Codex;
        port.Model = Sol;
        // Codex reports reads only: no write shows a lifetime, so the points' ttlMainSeconds (1800) decides.
        Turn(new IgnisTurnUsage(ProviderKind.Codex, Sol, null, 1_000, 40_000, 0, 0, 0, 50, 41_000, 1_050_000, true), attach: true);
        Assert.Equal(TimeSpan.FromMinutes(30), Status.Ttl);

        Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(IgnisCacheState.Warm, Status.Cache);
        Assert.Equal(0m, Lost());
        Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
        // 41000 · (2.5 − 0.1) / 1e6 = 98400 / 1e6 = $0.0984 (the plain-input price would have lost $0.0779)
        Assert.Equal(0.0984m, Lost());
        Assert.Equal(0.0984m, Status.LostToday);
        Assert.Equal(0, port.Pings); // Codex has no keep-warm
    }

    [Fact]
    public void A_plan_chat_that_fell_to_5_minute_writes_loses_the_5_minute_write_not_the_1_hour_one()
    {
        port.Model = IgnisSamples.Haiku; // keep-warm off in the fake points, prices 5 / 25 / 0.5, writes 6.25 (5 min) and 10 (1 h), ttlMain 3600
        Turn(IgnisSamples.Usage(read: 40_000, write1h: 2_000, model: IgnisSamples.Haiku), attach: true);
        Assert.Equal(TimeSpan.FromHours(1), Status.Ttl);

        // The CLI wrote 5-minute entries where the points promise an hour: paid credits, a 5-minute cache.
        Turn(IgnisSamples.Usage(read: 40_000, write5m: 2_000, model: IgnisSamples.Haiku));
        Assert.True(Status.PaidCredits);
        Assert.Equal(TimeSpan.FromMinutes(5), Status.Ttl);

        Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(IgnisCacheState.Cold, Status.Cache);
        // The chat held 42100 tokens: 42100 · (6.25 − 0.5) / 1e6 = 242075 / 1e6 = $0.242075 (the 1-hour write would say $0.39995)
        Assert.Equal(0.242075m, Lost());
    }

    // ---- pi's gaps and its retention ----

    private IgnisPiRetentionVerdict ChoosePi(string? model, Dictionary<string, string>? env = null, Guid? thread = null) =>
        main.OnMain(() => controller.ChoosePiRetention(new IgnisPiRetentionQuery(thread ?? Thread, model, env ?? [])));

    private static IgnisRequestSample PiSample(double? gap) => new(DateTimeOffset.UnixEpoch, "anthropic/" + IgnisSamples.Opus, gap, 100_000, 0, 0, 1_000, false);

    /// <summary>Thirteen pi requests with twelve 10-minute breaks over a 100k prefix (6.9 vs 0.04875 USD at the fake points' prices).</summary>
    private void TeachPi()
    {
        controller.Gaps.Add(ProviderKind.Pi, IgnisCacheBucket.Main, PiSample(null));
        for (var i = 0; i < 12; i++) controller.Gaps.Add(ProviderKind.Pi, IgnisCacheBucket.Main, PiSample(600));
    }

    [Fact]
    public void Pi_requests_teach_the_gap_history_for_provider_pi()
    {
        port.Provider = ProviderKind.Pi;
        port.Model = "anthropic/" + IgnisSamples.Opus;
        var first = new IgnisTurnUsage(ProviderKind.Pi, "anthropic/" + IgnisSamples.Opus, null, 1_200, 40_000, 0, 0, 800, 300, null, 200_000, true);
        Turn(first, attach: true);
        Advance(TimeSpan.FromMinutes(10));
        Turn(first);

        var samples = controller.Gaps.Samples(ProviderKind.Pi, IgnisCacheBucket.Main);
        Assert.Equal(2, samples.Count);
        Assert.Equal((null, 600d), (samples[0].GapSeconds, samples[1].GapSeconds));
        Assert.Equal(("anthropic/" + IgnisSamples.Opus, 800L, 42_000L), (samples[1].Model, samples[1].WriteUnknown, samples[1].PrefixTokens));
        Assert.Empty(controller.Gaps.Samples(ProviderKind.Claude, IgnisCacheBucket.Main)); // the Claude history is untouched
        Assert.Empty(ledger.Entries); // pi has no model points: no money is claimed for it
    }

    [Fact]
    public void Pi_asks_for_long_retention_on_anthropic_once_the_learned_gaps_pay_and_on_openai_at_once()
    {
        var model = "anthropic/" + IgnisSamples.Opus;
        var before = ChoosePi(model);
        Assert.Equal((IgnisPiRetentionReason.TooFewGaps, 0), (before.Reason, before.Gaps));
        Assert.False(before.SetsLong);

        TeachPi();
        var after = ChoosePi(model);
        Assert.Equal(IgnisPiRetentionReason.Longer, after.Reason);
        // Benefit: 12 · 100000·(6.25 − 0.5) / 1e6 = 12 · 0.575 = 6.9. Cost: 13 · 1000·(10 − 6.25) / 1e6 = 13 · 0.00375 = 0.04875.
        Assert.Equal((12, 6.9m, 0.04875m), (after.Gaps, after.Benefit, after.Cost));
        Assert.True(after.SetsLong);

        // The Claude lifetimes read only Claude's own history: pi's gaps teach them nothing.
        Assert.Equal(IgnisLifetimeReason.TooFewGaps, main.OnMain(() => controller.ChooseCacheLifetimes(new IgnisLifetimeQuery(ProviderKind.Claude, IgnisSamples.Opus, Thread, new Dictionary<string, string>(), "C:/work")))!.Helpers.Reason);

        Assert.Equal(IgnisPiRetentionReason.LongerFree, ChoosePi("openai/gpt-5.1").Reason);
        Assert.Equal(IgnisPiRetentionReason.UnknownUpstream, ChoosePi("google/gemini-3-pro").Reason);
        // A launch model the points do not know is judged on the priced requests pi already made (their own models' prices).
        Assert.Equal(IgnisPiRetentionReason.Longer, ChoosePi("anthropic/claude-unheard-of").Reason);
    }

    [Fact]
    public void Pi_keeps_the_users_setting_and_ignis_off_and_ignores_the_gaps_then()
    {
        TeachPi();
        var model = "anthropic/" + IgnisSamples.Opus;
        Assert.Equal(IgnisPiRetentionReason.UserChoice, ChoosePi(model, new Dictionary<string, string> { ["pi_cache_retention"] = "short" }).Reason);
        Assert.Equal(IgnisPiRetentionReason.UserChoice, ChoosePi("openai/gpt-5.1", new Dictionary<string, string> { ["PI_CACHE_RETENTION"] = "long" }).Reason);

        settings.SetChat(Thread, new IgnisChatPreference(IgnisMode.Off, true));
        var off = ChoosePi(model);
        Assert.Equal(IgnisPiRetentionReason.IgnisOff, off.Reason);
        Assert.False(off.SetsLong);
        Assert.Equal(IgnisPiRetentionReason.IgnisOff, ChoosePi("openai/gpt-5.1").Reason);

        // A chat that is not known follows the default mode.
        Assert.Equal(IgnisPiRetentionReason.Longer, main.OnMain(() => controller.ChoosePiRetention(new IgnisPiRetentionQuery(null, model, new Dictionary<string, string>()))).Reason);
    }
}
