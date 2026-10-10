using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Ignis;
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

    [Fact]
    [Trait("Feature", "IG-2")]
    [Trait("Decision", "OD-0134")]
    public void Pi_asks_for_long_cache_retention_only_while_ignis_is_on_and_keeps_the_users_value()
    {
        var chat = Guid.NewGuid();
        var unbound = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        PiSession.WithIgnisCacheRetention(unbound, chat);
        Assert.Empty(unbound);

        Guid? asked = null;
        using (IgnisSeams.IsOn.Override((provider, thread) => { asked = thread; return provider == ProviderKind.Pi; }))
        {
            var on = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PiSession.WithIgnisCacheRetention(on, chat);
            Assert.Equal("long", on["PI_CACHE_RETENTION"]);
            Assert.Single(on);

            var own = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["pi_cache_retention"] = "short" };
            PiSession.WithIgnisCacheRetention(own, chat);
            Assert.Equal("short", own["PI_CACHE_RETENTION"]);
            Assert.Single(own);
        }
        Assert.Equal(chat, asked);

        using (IgnisSeams.IsOn.Override((_, _) => false))
        {
            var off = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            PiSession.WithIgnisCacheRetention(off, chat);
            Assert.Empty(off);
        }
    }
}
