using Marrow.Core.Models;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Core.Tests.Port.Ignis.Engine;

// Ignis integration (OD-0134, ED-NEW-ignis-integration): whether nth asks pi for PI_CACHE_RETENTION=long. The Claude one-hour gap
// rule over pi's learned gaps for an Anthropic upstream (Opus as the points give it: input 4, cache read 0.2, 5-minute
// write 5, 1-hour write 8); an OpenAI upstream has no write premium so it always pays; anything else is left alone.
public sealed class IgnisPiRetentionTests
{
    private const string Anthropic = "anthropic/claude-opus-5-5";

    private static IgnisPiRetentionInput Input(string? model = Anthropic, IReadOnlyList<IgnisRequestSample>? samples = null, Func<string?, IgnisModelPoints?>? points = null) => new()
    {
        Provider = ProviderKind.Pi,
        Mode = IgnisMode.WarmAndTidy,
        Model = model,
        Samples = samples ?? [],
        PointsFor = points ?? (id => id == "claude-opus-5-5" ? IgnisMoneyTests.Opus() : null),
    };

    private static IgnisRequestSample Sample(double? gap, string? model = Anthropic, long prefix = 100_000, long unknown = 1_000) =>
        new(DateTimeOffset.UnixEpoch, model, gap, prefix, 0, 0, unknown, false);

    /// <summary>One first request and <paramref name="gaps"/> later ones after <paramref name="gapSeconds"/> of idle time.</summary>
    private static List<IgnisRequestSample> Run(int gaps, double gapSeconds, string? model = Anthropic, long prefix = 100_000, long unknown = 1_000)
    {
        var list = new List<IgnisRequestSample> { Sample(null, model, prefix, unknown) };
        for (var i = 0; i < gaps; i++) list.Add(Sample(gapSeconds, model, prefix, unknown));
        return list;
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Only_a_pi_chat_with_ignis_on_and_no_user_setting_is_decided()
    {
        Assert.Equal(IgnisPiRetentionReason.NotPi, IgnisPiRetention.Decide(Input() with { Provider = ProviderKind.Claude }).Reason);
        Assert.Equal(IgnisPiRetentionReason.IgnisOff, IgnisPiRetention.Decide(Input() with { Mode = IgnisMode.Off }).Reason);
        // The user's own PI_CACHE_RETENTION wins in any letter case, whatever the model and the gaps say.
        foreach (var name in new[] { "PI_CACHE_RETENTION", "pi_cache_retention", "Pi_Cache_Retention" })
        {
            var chosen = IgnisPiRetention.Decide(Input("openai/gpt-5.1", Run(20, 600)) with { Environment = new Dictionary<string, string> { [name] = "short" } });
            Assert.Equal(IgnisPiRetentionReason.UserChoice, chosen.Reason);
            Assert.False(chosen.SetsLong);
        }
        // Another variable does not count.
        Assert.True(IgnisPiRetention.Decide(Input("openai/gpt-5.1") with { Environment = new Dictionary<string, string> { ["PI_SKIP_VERSION_CHECK"] = "1" } }).SetsLong);
    }

    [Theory]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    [InlineData("openai/gpt-5.1")]
    [InlineData("openai-codex/gpt-5.1-codex")]
    [InlineData("OpenAI/gpt-5.1")]
    public void An_openai_upstream_always_gets_the_24_hour_retention_with_no_gaps_needed(string model)
    {
        var verdict = IgnisPiRetention.Decide(Input(model));
        Assert.Equal(IgnisPiRetentionReason.LongerFree, verdict.Reason);
        Assert.True(verdict.SetsLong);
        Assert.Equal(0, verdict.Gaps);
        Assert.Equal(0m, verdict.Cost);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Without_a_model_the_newest_request_pi_made_names_the_upstream_and_unknown_upstreams_are_left_alone()
    {
        Assert.Equal(IgnisPiRetentionReason.UnknownUpstream, IgnisPiRetention.Decide(Input(model: null)).Reason);
        Assert.Equal(IgnisPiRetentionReason.UnknownUpstream, IgnisPiRetention.Decide(Input(model: "default")).Reason);
        Assert.Equal(IgnisPiRetentionReason.UnknownUpstream, IgnisPiRetention.Decide(Input("opus-without-prefix")).Reason);
        var google = IgnisPiRetention.Decide(Input("google/gemini-3-pro"));
        Assert.Equal((IgnisPiRetentionReason.UnknownUpstream, "google"), (google.Reason, google.Upstream));
        Assert.False(google.SetsLong);
        Assert.False(IgnisPiRetention.Decide(Input("openrouter/anthropic/claude-opus-5-5", Run(20, 600))).SetsLong);

        var newestIsOpenAi = new List<IgnisRequestSample> { Sample(null, Anthropic), Sample(600, "openai/gpt-5.1") };
        Assert.Equal(IgnisPiRetentionReason.LongerFree, IgnisPiRetention.Decide(Input(model: null, newestIsOpenAi)).Reason);
        // The launch model, when pi names one, beats the samples.
        Assert.Equal(IgnisPiRetentionReason.TooFewGaps, IgnisPiRetention.Decide(Input(Anthropic, newestIsOpenAi)).Reason);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Anthropic_waits_for_twelve_gaps_and_counts_only_anthropic_requests()
    {
        var eleven = IgnisPiRetention.Decide(Input(samples: Run(11, 600)));
        Assert.Equal((IgnisPiRetentionReason.TooFewGaps, 11), (eleven.Reason, eleven.Gaps));
        Assert.False(eleven.SetsLong);
        // Twelve 10-minute gaps, but nine of them were openai requests: three Anthropic gaps are too few.
        var mixed = Run(3, 600).Concat(Run(9, 600, model: "openai/gpt-5.1")).ToList();
        Assert.Equal((IgnisPiRetentionReason.TooFewGaps, 3), (IgnisPiRetention.Decide(Input(samples: mixed)).Reason, IgnisPiRetention.Decide(Input(samples: mixed)).Gaps));
        // A request with no model cannot be placed on an upstream: not counted.
        Assert.Equal(0, IgnisPiRetention.Decide(Input(samples: Run(20, 600, model: null))).Gaps);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Anthropic_gets_one_hour_when_the_measured_saving_beats_the_extra_write_cost()
    {
        // 13 requests, 12 gaps of 10 minutes over a 100000 prefix, 1000 tokens written each (unknown lifetime = 5-minute write).
        // Benefit: 12 · 100000·(5 − 0.2) / 1e6 = 12 · 0.48 = $5.76. Cost: 13 · 1000·(8 − 5) / 1e6 = 13 · 0.003 = $0.039. 5.76 > 0.039·1.1.
        var verdict = IgnisPiRetention.Decide(Input(samples: Run(12, 600)));
        Assert.Equal(IgnisPiRetentionReason.Longer, verdict.Reason);
        Assert.True(verdict.SetsLong);
        Assert.Equal(("anthropic", 12, 5.76m, 0.039m), (verdict.Upstream, verdict.Gaps, verdict.Benefit, verdict.Cost));
        // The same numbers the Claude rule gives over those samples (the same function, the model id without "anthropic/").
        var (benefit, cost) = IgnisCacheLifetime.Worth(Run(12, 600).Select(sample => sample with { Model = "claude-opus-5-5" }).ToList(), _ => IgnisMoneyTests.Opus());
        Assert.Equal((benefit, cost), (verdict.Benefit, verdict.Cost));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Anthropic_stays_short_when_the_gaps_are_too_short_too_long_or_the_writes_too_big()
    {
        // Gaps under 5 minutes are read warm either way: no benefit.
        var quick = IgnisPiRetention.Decide(Input(samples: Run(20, 120)));
        Assert.Equal((IgnisPiRetentionReason.NotWorthIt, 0m, 0.063m), (quick.Reason, quick.Benefit, quick.Cost));
        // Gaps over an hour are cold either way: no benefit.
        var long_ = IgnisPiRetention.Decide(Input(samples: Run(20, 7200)));
        Assert.Equal((IgnisPiRetentionReason.NotWorthIt, 0m), (long_.Reason, long_.Benefit));
        // 12 gaps of 10 minutes over 10000 tokens: benefit 12 · 10000·4.8/1e6 = $0.576; 100000 written per request:
        // cost 13 · 100000·3/1e6 = $3.9. Not worth it.
        var heavy = IgnisPiRetention.Decide(Input(samples: Run(12, 600, prefix: 10_000, unknown: 100_000)));
        Assert.Equal((IgnisPiRetentionReason.NotWorthIt, 0.576m, 3.9m), (heavy.Reason, heavy.Benefit, heavy.Cost));
        Assert.False(heavy.SetsLong);
        // The margin: benefit 5.76 must beat cost x 1.1. 13 requests writing w tokens cost 13 · w · 3 / 1e6:
        // w = 134266 costs 5.236374, x 1.1 = 5.7600114 >= 5.76 (not worth it); w = 134265 costs 5.236335, x 1.1 = 5.7599685 < 5.76.
        var edge = IgnisPiRetention.Decide(Input(samples: Run(12, 600, unknown: 134_266)));
        Assert.Equal(IgnisPiRetentionReason.NotWorthIt, edge.Reason);
        var inside = IgnisPiRetention.Decide(Input(samples: Run(12, 600, unknown: 134_265)));
        Assert.Equal(IgnisPiRetentionReason.Longer, inside.Reason);
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Anthropic_models_without_write_prices_cannot_be_judged_and_the_launch_model_prices_requests_without_one()
    {
        // No points at all, or points without 5-minute and 1-hour writes: no prices.
        Assert.Equal(IgnisPiRetentionReason.NoPrices, IgnisPiRetention.Decide(Input(samples: Run(20, 600), points: _ => null)).Reason);
        Assert.Equal(IgnisPiRetentionReason.NoPrices, IgnisPiRetention.Decide(Input(samples: Run(20, 600), points: _ => IgnisMoneyTests.Codex())).Reason);
        // The points are asked by the bare model id, and by the launch model's id when a sample's own is not priced.
        var asked = new List<string?>();
        IgnisPiRetention.Decide(Input(samples: [.. Run(12, 600), .. Run(1, 600, model: "anthropic/claude-unknown-9")], points: id => { asked.Add(id); return id == "claude-opus-5-5" ? IgnisMoneyTests.Opus() : null; }));
        Assert.Contains("claude-opus-5-5", asked);
        Assert.Contains("claude-unknown-9", asked);
        Assert.DoesNotContain(asked, id => id is not null && id.Contains('/', StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void The_model_id_splits_at_the_first_slash_only()
    {
        Assert.Equal(("anthropic", "claude-opus-5-5"), IgnisPiRetention.Split("anthropic/claude-opus-5-5"));
        Assert.Equal(("openrouter", "anthropic/claude-opus-5-5"), IgnisPiRetention.Split("OpenRouter/anthropic/claude-opus-5-5"));
        Assert.Null(IgnisPiRetention.Split("claude"));
        Assert.Null(IgnisPiRetention.Split("/claude"));
        Assert.Null(IgnisPiRetention.Split("anthropic/"));
        Assert.Null(IgnisPiRetention.Split(null));
        Assert.Null(IgnisPiRetention.Split(""));
    }
}
