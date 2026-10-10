using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis runtime (OD-0134, ED-NEW-ignis-runtime): the cache-lifetime choice at a Claude launch. Prices are Opus 5.5's
// (read 0.20, 5-minute write 5, 1-hour write 8, USD per million), so a 5-to-60-minute gap saves prefix × 4.8 / 1e6 and
// every write that 1 h would raise costs 3 / 1e6 more. Every expected value is worked out in the comment beside it.
public sealed class IgnisCacheLifetimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    private static IgnisRequestSample Sample(double? gap, long prefix = 100_000, long w5 = 0, long w1h = 0, long unknown = 0, bool byNth = false, string? model = null) =>
        new(T0, model, gap, prefix, w5, w1h, unknown, byNth);

    private static IReadOnlyList<IgnisRequestSample> Run(int gaps, double gap, long prefix = 100_000, long w5 = 0, long w1h = 0, bool byNth = false) =>
        [Sample(null, prefix, w5, w1h, byNth: byNth), .. Enumerable.Range(0, gaps).Select(_ => Sample(gap, prefix, w5, w1h, byNth: byNth))];

    private static readonly Dictionary<string, string> ApiKey = new() { ["ANTHROPIC_API_KEY"] = "sk-test" };

    private static IgnisLifetimeInput Input(
        IReadOnlyList<IgnisRequestSample>? main = null,
        IReadOnlyList<IgnisRequestSample>? helpers = null,
        IReadOnlyDictionary<string, string>? environment = null,
        IReadOnlySet<string>? settings = null,
        string? version = "2.1.300",
        IgnisMode mode = IgnisMode.KeepWarm,
        bool heads = true,
        ProviderKind provider = ProviderKind.Claude,
        Func<string?, IgnisModelPoints?>? points = null) => new()
    {
        Provider = provider,
        Mode = mode,
        IncludeHeads = heads,
        CliVersion = version,
        Environment = environment ?? ApiKey,
        UserSettings = settings ?? new HashSet<string>(),
        Main = main ?? [],
        Helpers = helpers ?? [],
        MainModel = "m",
        PointsFor = points ?? (_ => IgnisMoneyTests.Opus()),
    };

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void With_no_data_nothing_is_set()
    {
        var choice = IgnisCacheLifetime.Decide(Input());
        Assert.Equal(IgnisLifetimeReason.TooFewGaps, choice.Main.Reason);
        Assert.Equal(IgnisLifetimeReason.TooFewGaps, choice.Helpers.Reason);
        Assert.Equal(0, choice.Helpers.Gaps);
        Assert.Empty(choice.Environment);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Eleven_gaps_are_too_few_and_twelve_are_enough()
    {
        var eleven = IgnisCacheLifetime.Bucket(Input(helpers: Run(11, 600, w5: 1_000)), IgnisCacheBucket.Helpers);
        Assert.Equal(IgnisLifetimeReason.TooFewGaps, eleven.Reason);
        Assert.Equal(11, eleven.Gaps);
        Assert.Equal(IgnisLifetimeReason.Longer, IgnisCacheLifetime.Bucket(Input(helpers: Run(12, 600, w5: 1_000)), IgnisCacheBucket.Helpers).Reason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Short_gaps_only_never_pay_for_an_hour()
    {
        // 20 gaps of 4 minutes: a 5-minute cache already reads them all; benefit 0, cost 21 × 1000 × 3 / 1e6 = $0.063.
        var choice = IgnisCacheLifetime.Decide(Input(main: Run(20, 240, w5: 1_000), helpers: Run(20, 240, w5: 1_000)));
        Assert.Equal(IgnisLifetimeReason.NotWorthIt, choice.Main.Reason);
        Assert.Equal(IgnisLifetimeReason.NotWorthIt, choice.Helpers.Reason);
        Assert.Equal(0m, choice.Helpers.Benefit);
        Assert.Equal(0.063m, choice.Helpers.Cost);
        Assert.Empty(choice.Environment);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Gaps_over_an_hour_only_never_pay_for_an_hour()
    {
        // Both lifetimes go cold after 61 minutes: benefit 0.
        var verdict = IgnisCacheLifetime.Bucket(Input(helpers: Run(30, 3_660, w5: 1_000)), IgnisCacheBucket.Helpers);
        Assert.Equal(IgnisLifetimeReason.NotWorthIt, verdict.Reason);
        Assert.Equal(0m, verdict.Benefit);
        Assert.Equal(30, verdict.Gaps);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Mixed_gaps_ask_for_an_hour_when_the_breaks_pay_for_it()
    {
        // Helpers: 12 gaps of 10 minutes over a 100k prefix → 12 × 100000 × 4.8 / 1e6 = $5.76;
        // 13 requests writing 2000 at 5 min → 13 × 2000 × 3 / 1e6 = $0.078. 5.76 > 0.078 × 1.1.
        IReadOnlyList<IgnisRequestSample> helpers = [.. Run(12, 600, w5: 2_000)];
        // Main: the same breaks, plus short and over-an-hour ones that add no benefit.
        IReadOnlyList<IgnisRequestSample> main = [.. Run(12, 600, w5: 2_000), Sample(60, w5: 2_000), Sample(7_200, w5: 2_000)];
        var choice = IgnisCacheLifetime.Decide(Input(main: main, helpers: helpers));
        Assert.Equal(new IgnisLifetimeVerdict(IgnisCacheBucket.Helpers, IgnisLifetimeReason.Longer, 12, 5.76m, 0.078m), choice.Helpers);
        // Main: 14 gaps, benefit 5.76, cost 15 × 2000 × 3 / 1e6 = $0.09.
        Assert.Equal(new IgnisLifetimeVerdict(IgnisCacheBucket.Main, IgnisLifetimeReason.Longer, 14, 5.76m, 0.09m), choice.Main);
        Assert.Equal(
            [new("CLAUDE_CODE_PROMPT_CACHE_TTL", "1h"), new("CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL", "1h")],
            choice.Environment);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_saving_must_beat_the_cost_by_ten_percent()
    {
        // One 10-minute break over an 11000-token prefix: 11000 × 4.8 / 1e6 = $0.0528. Eleven more 1-minute breaks.
        // Writes of 16000 tokens in all: 16000 × 3 / 1e6 = $0.048; × 1.1 = $0.0528 = the benefit → not enough.
        IReadOnlyList<IgnisRequestSample> Helpers(long prefix) =>
            [Sample(null, 0, w5: 4_000), Sample(600, prefix, w5: 1_000), .. Enumerable.Range(0, 11).Select(i => Sample(60, 0, w5: i == 0 ? 11_000 : 0))];
        var even = IgnisCacheLifetime.Bucket(Input(helpers: Helpers(11_000)), IgnisCacheBucket.Helpers);
        Assert.Equal((0.0528m, 0.048m), (even.Benefit, even.Cost));
        Assert.Equal(IgnisLifetimeReason.NotWorthIt, even.Reason);
        // 11250 tokens: 11250 × 4.8 / 1e6 = $0.054 > $0.0528 → one hour.
        Assert.Equal(IgnisLifetimeReason.Longer, IgnisCacheLifetime.Bucket(Input(helpers: Helpers(11_250)), IgnisCacheBucket.Helpers).Reason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_gap_counts_above_five_minutes_and_up_to_the_hour()
    {
        Assert.Equal(0m, IgnisCacheLifetime.Worth([Sample(300)], _ => IgnisMoneyTests.Opus()).Benefit);
        Assert.Equal(0.48m, IgnisCacheLifetime.Worth([Sample(301)], _ => IgnisMoneyTests.Opus()).Benefit);
        Assert.Equal(0.48m, IgnisCacheLifetime.Worth([Sample(3_600)], _ => IgnisMoneyTests.Opus()).Benefit);
        Assert.Equal(0m, IgnisCacheLifetime.Worth([Sample(3_601)], _ => IgnisMoneyTests.Opus()).Benefit);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Only_writes_an_hour_would_raise_count_as_cost()
    {
        // 1000 at 5 min + 1000 unlabelled + 1000 at 1 h nth asked for = 3000 × 3 / 1e6 = $0.009; the provider's own
        // 1-hour writes (server-controlled helpers) cost the same either way.
        var opus = (Func<string?, IgnisModelPoints?>)(_ => IgnisMoneyTests.Opus());
        Assert.Equal(0.009m, IgnisCacheLifetime.Worth([Sample(null, w5: 1_000, unknown: 1_000), Sample(null, w1h: 1_000, byNth: true)], opus).Cost);
        Assert.Equal(0m, IgnisCacheLifetime.Worth([Sample(null, w1h: 1_000)], opus).Cost);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Each_request_is_priced_on_its_own_model()
    {
        // A Haiku helper (read 0.01, w5 0.125) over a 50k prefix after 10 minutes: 50000 × 0.115 / 1e6 = $0.00575.
        Func<string?, IgnisModelPoints?> points = model => model == "haiku" ? IgnisMoneyTests.HaikuWithStep() : IgnisMoneyTests.Opus();
        Assert.Equal(0.00575m, IgnisCacheLifetime.Worth([Sample(600, 50_000, model: "haiku")], points).Benefit);
        // Above its 100k step (read 0.05, w5 0.625): 150000 × 0.575 / 1e6 = $0.08625.
        Assert.Equal(0.08625m, IgnisCacheLifetime.Worth([Sample(600, 150_000, model: "haiku")], points).Benefit);
        // An unknown model falls back to the main model's points.
        Assert.Equal(0.48m, IgnisCacheLifetime.Worth([Sample(600, model: "other")], model => model is null ? IgnisMoneyTests.Opus() : null).Benefit);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_user_choice_in_the_environment_or_settings_wins()
    {
        var gaps = Run(12, 600, w5: 1_000);
        Assert.All(
            [IgnisCacheBucket.Main, IgnisCacheBucket.Helpers],
            bucket => Assert.Equal(
                IgnisLifetimeVerdict.Because(bucket, IgnisLifetimeReason.UserChoice, "FORCE_PROMPT_CACHING_5M"),
                IgnisCacheLifetime.Bucket(Input(gaps, gaps, new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "k", ["FORCE_PROMPT_CACHING_5M"] = "1" }), bucket)));
        // Windows variable names ignore case; the helpers' own variable leaves the main bucket to Ignis.
        var helpersOnly = IgnisCacheLifetime.Decide(Input(gaps, gaps, new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "k", ["claude_code_subagent_prompt_cache_ttl"] = "5m" }));
        Assert.Equal(IgnisLifetimeReason.UserChoice, helpersOnly.Helpers.Reason);
        Assert.Equal("CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL", helpersOnly.Helpers.UserSetting);
        Assert.Equal(IgnisLifetimeReason.Longer, helpersOnly.Main.Reason);
        Assert.Equal([new("CLAUDE_CODE_PROMPT_CACHE_TTL", "1h")], helpersOnly.Environment);
        // ENABLE_PROMPT_CACHING_1H decides both.
        var both = IgnisCacheLifetime.Decide(Input(gaps, gaps, new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "k", ["ENABLE_PROMPT_CACHING_1H"] = "1" }));
        Assert.Equal((IgnisLifetimeReason.UserChoice, IgnisLifetimeReason.UserChoice), (both.Main.Reason, both.Helpers.Reason));
        // A setting in a Claude settings file.
        var setting = IgnisCacheLifetime.Decide(Input(gaps, gaps, settings: new HashSet<string> { "promptCacheTtl" }));
        Assert.Equal(IgnisLifetimeVerdict.Because(IgnisCacheBucket.Main, IgnisLifetimeReason.UserChoice, "promptCacheTtl"), setting.Main);
        Assert.Equal(IgnisLifetimeReason.Longer, setting.Helpers.Reason);
        // An empty variable is not a choice.
        Assert.Equal(IgnisLifetimeReason.Longer, IgnisCacheLifetime.Bucket(Input(gaps, gaps, new Dictionary<string, string> { ["ANTHROPIC_API_KEY"] = "k", ["FORCE_PROMPT_CACHING_5M"] = "" }), IgnisCacheBucket.Helpers).Reason);
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData(null, IgnisLifetimeReason.CliVersionUnknown)]
    [InlineData("unknown", IgnisLifetimeReason.CliVersionUnknown)]
    [InlineData("2.1", IgnisLifetimeReason.CliVersionUnknown)]
    [InlineData("2.1.241", IgnisLifetimeReason.CliTooOld)]
    [InlineData("1.9.999", IgnisLifetimeReason.CliTooOld)]
    [InlineData("2.1.242", IgnisLifetimeReason.Longer)]
    [InlineData("2.1.296 (Claude Code)", IgnisLifetimeReason.Longer)]
    [InlineData("3.0.0", IgnisLifetimeReason.Longer)]
    public void The_cli_must_be_2_1_242_or_later(string? version, IgnisLifetimeReason expected)
    {
        var gaps = Run(12, 600, w5: 1_000);
        var choice = IgnisCacheLifetime.Decide(Input(gaps, gaps, version: version));
        Assert.Equal((expected, expected), (choice.Main.Reason, choice.Helpers.Reason));
        if (expected != IgnisLifetimeReason.Longer) Assert.Empty(choice.Environment);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Nothing_is_set_when_ignis_is_off_or_the_chat_is_not_claude()
    {
        var gaps = Run(40, 600, w5: 1_000);
        var off = IgnisCacheLifetime.Decide(Input(gaps, gaps, mode: IgnisMode.Off));
        Assert.Equal((IgnisLifetimeReason.IgnisOff, IgnisLifetimeReason.IgnisOff), (off.Main.Reason, off.Helpers.Reason));
        Assert.Empty(off.Environment);
        var codex = IgnisCacheLifetime.Decide(Input(gaps, gaps, provider: ProviderKind.Codex));
        Assert.Equal((IgnisLifetimeReason.NotClaude, IgnisLifetimeReason.NotClaude), (codex.Main.Reason, codex.Helpers.Reason));
        Assert.Equal(IgnisLifetimeReason.Longer, IgnisCacheLifetime.Decide(Input(gaps, gaps, mode: IgnisMode.WarmAndTidy)).Helpers.Reason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Helpers_are_left_alone_when_heads_are_not_included()
    {
        var gaps = Run(40, 600, w5: 1_000);
        var choice = IgnisCacheLifetime.Decide(Input(gaps, gaps, heads: false));
        Assert.Equal(IgnisLifetimeReason.HeadsOff, choice.Helpers.Reason);
        Assert.Equal(IgnisLifetimeReason.Longer, choice.Main.Reason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void The_main_bucket_is_only_lengthened_where_it_defaults_to_five_minutes()
    {
        var empty = new Dictionary<string, string>();
        // A subscription within plan: the newest main writes are 1 h → already long.
        Assert.Equal(IgnisLifetimeReason.AlreadyLong, IgnisCacheLifetime.Bucket(Input(main: Run(20, 600, w1h: 1_000), environment: empty), IgnisCacheBucket.Main).Reason);
        // Nothing seen and no API key: assume the plan default.
        Assert.Equal(IgnisLifetimeReason.AlreadyLong, IgnisCacheLifetime.Bucket(Input(environment: empty), IgnisCacheBucket.Main).Reason);
        // On paid credits the main writes are 5 min → Ignis may lengthen.
        Assert.Equal(IgnisLifetimeReason.Longer, IgnisCacheLifetime.Bucket(Input(main: Run(20, 600, w5: 1_000), environment: empty), IgnisCacheBucket.Main).Reason);
        // Writes nth lengthened do not tell the default; the newest of the CLI's own decides.
        IReadOnlyList<IgnisRequestSample> mixed = [.. Run(20, 600, w5: 1_000), Sample(600, w1h: 1_000, byNth: true), Sample(600)];
        Assert.True(IgnisCacheLifetime.MainDefaultIsShort(Input(main: mixed, environment: empty)));
        // An API key or a cloud provider says 5 minutes; "0" and "false" do not.
        Assert.True(IgnisCacheLifetime.EnvironmentSaysShortMain(new Dictionary<string, string> { ["claude_code_use_bedrock"] = "1" }));
        Assert.False(IgnisCacheLifetime.EnvironmentSaysShortMain(new Dictionary<string, string> { ["CLAUDE_CODE_USE_VERTEX"] = "0", ["CLAUDE_CODE_USE_FOUNDRY"] = "false", ["ANTHROPIC_API_KEY"] = "" }));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Helpers_already_writing_an_hour_are_left_alone()
    {
        // Issue anthropics/claude-code#84289: some helper requests already write 1 h; the observed bucket wins.
        Assert.Equal(IgnisLifetimeReason.AlreadyLong, IgnisCacheLifetime.Bucket(Input(helpers: Run(20, 600, w1h: 2_000)), IgnisCacheBucket.Helpers).Reason);
        // 1-hour writes nth itself caused do not count.
        Assert.False(IgnisCacheLifetime.HelpersAlreadyLong(Run(20, 600, w1h: 2_000, byNth: true)));
        Assert.False(IgnisCacheLifetime.HelpersAlreadyLong([Sample(600, w5: 2_000), Sample(600, w1h: 1_000)]));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_model_without_write_prices_is_left_alone()
    {
        var verdict = IgnisCacheLifetime.Bucket(Input(helpers: Run(20, 600, w5: 1_000), points: _ => IgnisMoneyTests.Codex()), IgnisCacheBucket.Helpers);
        Assert.Equal(IgnisLifetimeReason.NoPrices, verdict.Reason);
        Assert.Equal(20, verdict.Gaps);
        Assert.Equal(IgnisLifetimeReason.NoPrices, IgnisCacheLifetime.Bucket(Input(helpers: Run(20, 600, w5: 1_000), points: _ => null), IgnisCacheBucket.Helpers).Reason);
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Settings_files_are_read_for_ttl_choices_only()
    {
        var keys = IgnisCacheLifetime.SettingsKeys([
            "{ // user\n \"promptCacheTtl\": \"5m\", \"model\": \"opus\", }",
            "{ \"env\": { \"claude_code_subagent_prompt_cache_ttl\": \"1h\", \"OTHER\": \"x\" } }",
            "{ \"subagentPromptCacheTtl\": null }",
            "not json",
            "[1, 2]",
        ]);
        Assert.Equal(new HashSet<string> { "promptCacheTtl", "CLAUDE_CODE_SUBAGENT_PROMPT_CACHE_TTL" }, keys);
        Assert.Empty(IgnisCacheLifetime.SettingsKeys([]));
    }

    [Theory]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    [InlineData("2.1.242", "2.1.242")]
    [InlineData("2.1.296 (Claude Code)", "2.1.296")]
    [InlineData("v10.20.30-beta", "10.20.30")]
    [InlineData("2.1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("99999999999.1.1", null)]
    public void A_version_needs_three_parts(string? text, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), IgnisCacheLifetime.ParseVersion(text));

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void A_lengthened_bucket_is_credited_with_the_rebuild_it_avoided_minus_its_extra_writes()
    {
        var opus = IgnisMoneyTests.Opus();
        // 10 minutes after the last request, 80000 of a 100000 prefix read: 80000 × (5 − 0.2) / 1e6 = $0.384.
        Assert.Equal(0.384m, IgnisCacheLifetime.AvoidedRebuild(opus, 100_000, 80_000, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)));
        // A 5-minute cache would still have been warm; or the hour was over; or nothing was read.
        Assert.Equal(0m, IgnisCacheLifetime.AvoidedRebuild(opus, 100_000, 80_000, TimeSpan.FromMinutes(5), TimeSpan.FromHours(1)));
        Assert.Equal(0m, IgnisCacheLifetime.AvoidedRebuild(opus, 100_000, 80_000, TimeSpan.FromMinutes(61), TimeSpan.FromHours(1)));
        Assert.Equal(0m, IgnisCacheLifetime.AvoidedRebuild(opus, 100_000, 0, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)));
        // 4000 one-hour writes cost 4000 × (8 − 5) / 1e6 = $0.012 more; none without write prices.
        Assert.Equal(0.012m, IgnisCacheLifetime.ExtraWriteCost(opus, 4_000, 50_000));
        Assert.Equal(0m, IgnisCacheLifetime.ExtraWriteCost(IgnisMoneyTests.Codex(), 4_000, 50_000));
        // The engine reports the same numbers.
        var engine = new IgnisEngine();
        Assert.Equal(0.384m, engine.LongerMemorySaved(opus, 100_000, 80_000, TimeSpan.FromMinutes(10), TimeSpan.FromHours(1)));
        Assert.Equal(0.012m, engine.LongerMemoryCost(opus, 4_000, 50_000));
        // A helper's 5-minute rebuild: 10000 × (5 − 0.2) / 1e6 = $0.048 (the chat's 1-hour price would be $0.078).
        Assert.Equal(0.048m, engine.RebuildPremium(opus, 10_000, TimeSpan.FromMinutes(5)));
        Assert.Equal(0.078m, engine.RebuildPremium(opus, 10_000, TimeSpan.FromHours(1)));
    }

    [Fact]
    [Trait("Feature", "IG-1")]
    [Trait("Decision", "OD-0134")]
    public void Every_verdict_has_a_plain_line()
    {
        Assert.Equal("Helpers’ quick memory: 1 hour, chosen by Ignis (saves about $5.68 over 12 breaks)",
            IgnisWords.LifetimeLine(new(IgnisCacheBucket.Helpers, IgnisLifetimeReason.Longer, 12, 5.76m, 0.078m)));
        Assert.Equal("Chat’s quick memory: your own setting (promptCacheTtl)",
            IgnisWords.LifetimeLine(IgnisLifetimeVerdict.Because(IgnisCacheBucket.Main, IgnisLifetimeReason.UserChoice, "promptCacheTtl")));
        Assert.Equal("Chat’s quick memory: Claude’s default (still learning: 3 of 12 breaks seen)",
            IgnisWords.LifetimeLine(new(IgnisCacheBucket.Main, IgnisLifetimeReason.TooFewGaps, 3)));
        foreach (var reason in Enum.GetValues<IgnisLifetimeReason>())
            Assert.False(string.IsNullOrWhiteSpace(IgnisWords.LifetimeLine(IgnisLifetimeVerdict.Because(IgnisCacheBucket.Main, reason))));
        Assert.Equal("Paid credits: 5-minute cache", IgnisWords.PaidCreditsCache);
        Assert.Equal("Helpers rebuilt their quick memory: $0.31", IgnisWords.HelpersRebuilt(0.31m));
    }
}
