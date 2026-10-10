using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
using Marrow.Core.Port.Ignis.Engine;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: Ignis runtime (OD-0134, ED-NEW-ignis-runtime). The request-level cache levers apply only while Ignis is on
// for the chat, and only where the provider documents them: OpenRouter's cache_control marker for Anthropic upstreams,
// pi's PI_CACHE_RETENTION=long. Nothing the model reads changes. The OpenRouter rig answers in-process (no socket).
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class IgnisRequestLeverTests
{
    private static Dictionary<string, JSONValue> Payload(OpenRouterSession session, string model)
    {
        var payload = new Dictionary<string, JSONValue>(StringComparer.Ordinal);
        session.ApplyProviderPayload(payload, model, null, withTools: true);
        return payload;
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void OpenRouter_marks_anthropic_requests_for_caching_only_while_ignis_is_on()
    {
        using var rig = new OpenRouterSessionRig();
        Assert.False(Payload(rig.Session, "anthropic/claude-sonnet-4.5").ContainsKey("cache_control")); // unbound: off

        (ProviderKind, Guid?)? asked = null;
        using (IgnisSeams.IsOn.Override((provider, thread) => { asked = (provider, thread); return true; }))
        {
            var anthropic = Payload(rig.Session, "anthropic/claude-sonnet-4.5");
            Assert.Equal("{\"type\":\"ephemeral\"}", anthropic["cache_control"].CompactString);
            Assert.True(anthropic.ContainsKey("stream_options")); // the rest of the payload is unchanged
            Assert.False(Payload(rig.Session, "openai/gpt-5.1").ContainsKey("cache_control"));
            Assert.False(Payload(rig.Session, "google/gemini-3-pro").ContainsKey("cache_control"));
            Assert.False(Payload(rig.Session, "openrouter/auto").ContainsKey("cache_control"));
        }
        Assert.Equal(ProviderKind.OpenRouter, asked!.Value.Item1);

        using (IgnisSeams.IsOn.Override((_, _) => false))
            Assert.False(Payload(rig.Session, "anthropic/claude-sonnet-4.5").ContainsKey("cache_control"));

        Assert.True(OpenRouterSession.IgnisCacheBreakpoint("Anthropic/claude-opus-4.1", true));
        Assert.False(OpenRouterSession.IgnisCacheBreakpoint("anthropic/claude-opus-4.1", false));
    }

    private static Dictionary<string, string> PiEnvironment(params (string Name, string Value)[] values)
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values) environment[name] = value;
        return environment;
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Pi_asks_for_long_cache_retention_only_while_ignis_is_on_and_its_gap_rule_says_it_pays()
    {
        var chat = Guid.NewGuid();
        var unbound = PiEnvironment();
        PiSession.WithIgnisCacheRetention(unbound, chat, "anthropic/claude-sonnet-4-5");
        Assert.Empty(unbound);

        Guid? onAsked = null;
        IgnisPiRetentionQuery? retentionAsked = null;
        using (IgnisSeams.IsOn.Override((provider, thread) => { onAsked = thread; return provider == ProviderKind.Pi; }))
        {
            // Ignis on but no gap rule bound (or no choice): nothing is set.
            var noRule = PiEnvironment();
            PiSession.WithIgnisCacheRetention(noRule, chat, "anthropic/claude-sonnet-4-5");
            Assert.Empty(noRule);

            // The rule says the longer lifetime pays: long, and the rule was asked about this chat, this model and this environment.
            using (IgnisSeams.PiRetention.Override(query => { retentionAsked = query; return new IgnisPiRetentionVerdict(IgnisPiRetentionReason.Longer, "anthropic", 14, 5.76m, 0.039m); }))
            {
                var on = PiEnvironment(("PI_SKIP_VERSION_CHECK", "1"));
                PiSession.WithIgnisCacheRetention(on, chat, "anthropic/claude-sonnet-4-5");
                Assert.Equal("long", on["PI_CACHE_RETENTION"]);
                Assert.Equal(2, on.Count);
                Assert.Equal((chat, "anthropic/claude-sonnet-4-5"), (retentionAsked!.ThreadID, retentionAsked.Model));
                Assert.Equal("1", retentionAsked.Environment["PI_SKIP_VERSION_CHECK"]);

                // A value the user set is never replaced, in any letter case, and the rule is not even asked.
                retentionAsked = null;
                var own = PiEnvironment(("pi_cache_retention", "short"));
                PiSession.WithIgnisCacheRetention(own, chat, "anthropic/claude-sonnet-4-5");
                Assert.Equal("short", own["PI_CACHE_RETENTION"]);
                Assert.Single(own);
                Assert.Null(retentionAsked);
            }

            // The free OpenAI case is a verdict too.
            using (IgnisSeams.PiRetention.Override(_ => new IgnisPiRetentionVerdict(IgnisPiRetentionReason.LongerFree, "openai")))
            {
                var openai = PiEnvironment();
                PiSession.WithIgnisCacheRetention(openai, chat, "openai/gpt-5.1");
                Assert.Equal("long", openai["PI_CACHE_RETENTION"]);
                Assert.Single(openai);
            }

            // The rule says it does not pay (too few gaps, not worth it, unknown upstream, no prices...): nothing is set.
            foreach (var reason in new[] { IgnisPiRetentionReason.TooFewGaps, IgnisPiRetentionReason.NotWorthIt, IgnisPiRetentionReason.UnknownUpstream, IgnisPiRetentionReason.NoPrices, IgnisPiRetentionReason.UserChoice })
                using (IgnisSeams.PiRetention.Override(_ => new IgnisPiRetentionVerdict(reason)))
                {
                    var stay = PiEnvironment();
                    PiSession.WithIgnisCacheRetention(stay, chat, "anthropic/claude-sonnet-4-5");
                    Assert.Empty(stay);
                }
        }
        Assert.Equal(chat, onAsked);

        // Ignis off for the chat: nothing is set and the rule is not asked.
        retentionAsked = null;
        using (IgnisSeams.IsOn.Override((_, _) => false))
        using (IgnisSeams.PiRetention.Override(query => { retentionAsked = query; return new IgnisPiRetentionVerdict(IgnisPiRetentionReason.LongerFree, "openai"); }))
        {
            var off = PiEnvironment();
            PiSession.WithIgnisCacheRetention(off, chat, "openai/gpt-5.1");
            Assert.Empty(off);
            Assert.Null(retentionAsked);
        }
    }

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Pi_with_the_real_gap_rule_asks_for_long_on_openai_and_waits_for_gaps_on_anthropic()
    {
        var chat = Guid.NewGuid();
        IgnisPiRetentionVerdict Rule(IgnisPiRetentionQuery query) => IgnisPiRetention.Decide(new IgnisPiRetentionInput
        {
            Provider = ProviderKind.Pi,
            Mode = IgnisMode.WarmAndTidy,
            Environment = query.Environment,
            Model = query.Model,
            PointsFor = _ => null,
        });
        using var on = IgnisSeams.IsOn.Override((_, _) => true);
        using var rule = IgnisSeams.PiRetention.Override(Rule);

        var openai = PiEnvironment();
        PiSession.WithIgnisCacheRetention(openai, chat, "openai/gpt-5.1");
        Assert.Equal("long", openai["PI_CACHE_RETENTION"]);

        var codex = PiEnvironment();
        PiSession.WithIgnisCacheRetention(codex, chat, "openai-codex/gpt-5.1-codex");
        Assert.Equal("long", codex["PI_CACHE_RETENTION"]);

        var anthropic = PiEnvironment(); // no gaps learned yet
        PiSession.WithIgnisCacheRetention(anthropic, chat, "anthropic/claude-sonnet-4-5");
        Assert.Empty(anthropic);

        var google = PiEnvironment();
        PiSession.WithIgnisCacheRetention(google, chat, "google/gemini-3-pro");
        Assert.Empty(google);

        var unnamed = PiEnvironment();
        PiSession.WithIgnisCacheRetention(unnamed, chat);
        Assert.Empty(unnamed);

        var own = PiEnvironment(("PI_CACHE_RETENTION", "short"));
        PiSession.WithIgnisCacheRetention(own, chat, "openai/gpt-5.1");
        Assert.Equal("short", own["PI_CACHE_RETENTION"]);
    }
}
