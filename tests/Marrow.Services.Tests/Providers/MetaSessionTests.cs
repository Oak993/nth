using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/MetaSession.swift (B2 1fbf81e), :1-77.
public sealed class MetaSessionTests
{
    private static SessionConfiguration MakeSessionConfiguration(string? apiKey = "sk-test") => new()
    {
        Provider = ProviderKind.Meta,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = apiKey,
        Transcript = ValueList<TranscriptMessage>.Empty,
    };

    // ---- PR-PROVAPI-045 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-045")] // MetaSession.swift:15-25
    public void Config_matches_the_pinned_Meta_identity_and_defaults()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        var config = session.OpenAICompatibleSessionConfig;
        Assert.Equal(ProviderKind.Meta, config.Kind);
        Assert.Equal("Meta", config.Title);
        Assert.Equal("Meta coding agent (Muse Spark muse-spark-1.3)", config.AgentIdentity("muse-spark-1.3"));
        // ED-NEW-muse-wonder-7: owner flagship order replaces the old default assertion.
        Assert.Equal("muse-wonder-1.0", config.DefaultModel);
        Assert.Equal(MetaAPI.ChatURL, config.ChatURL);
        Assert.Equal(MetaAPI.ContextWindow, config.ContextWindow);
        Assert.Null(config.MaxOutputTokens);
        Assert.False(config.ReplaysReasoning);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-045")] // MetaSession.swift:20 (isVisionModel: MetaAPI.isVisionModel)
    public void Config_isVisionModel_delegates_to_MetaAPI()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        var config = session.OpenAICompatibleSessionConfig;
        Assert.Equal(MetaAPI.IsVisionModel("muse-spark-1.3"), config.IsVisionModel("muse-spark-1.3"));
    }

    // ---- PR-PROVAPI-046 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-046")] // MetaSession.swift:33-36
    public void ApplyProviderPayload_always_sets_stream_options_include_usage()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        var payload = new Dictionary<string, JSONValue>();
        session.ApplyProviderPayload(payload, "muse-spark-1.3", null, withTools: false);
        Assert.Equal(
            JSONValue.FromDictionaryLiteral(("include_usage", true)),
            payload["stream_options"]);
        Assert.False(payload.ContainsKey("reasoning_effort"));
        Assert.False(payload.ContainsKey("thinking"));
        Assert.False(payload.ContainsKey("max_tokens"));
        Assert.False(payload.ContainsKey("tool_stream"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-046")] // MetaSession.swift:33-36
    public void ApplyProviderPayload_sets_reasoning_effort_when_the_mapping_returns_a_value()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        var payload = new Dictionary<string, JSONValue>();
        session.ApplyProviderPayload(payload, "muse-spark-1.3", "high", withTools: false);
        Assert.Equal(new JSONValue.String("high"), payload["reasoning_effort"]);
    }

    // ---- PR-PROVAPI-047 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:40-41
    [InlineData("minimal")]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    public void ReasoningEffort_passes_through_the_standard_efforts(string effort)
    {
        Assert.Equal(effort, MetaSession.ReasoningEffort(effort, "muse-spark-1.3"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:42-45
    public void ReasoningEffort_max_stays_max_on_muse_spark_1_3()
    {
        Assert.Equal("max", MetaSession.ReasoningEffort("max", "muse-spark-1.3"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:42-45
    public void ReasoningEffort_max_downgrades_to_xhigh_on_any_other_model()
    {
        Assert.Equal("xhigh", MetaSession.ReasoningEffort("max", "muse-spark-1.2"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:45 (model ?? "muse-spark-1.3")
    public void ReasoningEffort_max_with_no_model_assumes_muse_spark_1_3_and_stays_max()
    {
        Assert.Equal("max", MetaSession.ReasoningEffort("max"));
        Assert.Equal("max", MetaSession.ReasoningEffort("max", null));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:46
    public void ReasoningEffort_extra_high_always_becomes_xhigh()
    {
        Assert.Equal("xhigh", MetaSession.ReasoningEffort("extra-high", "muse-spark-1.3"));
        Assert.Equal("xhigh", MetaSession.ReasoningEffort("extra-high", "muse-spark-1.2"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-047")] // MetaSession.swift:39, :47
    public void ReasoningEffort_returns_null_for_nil_empty_or_unrecognized()
    {
        Assert.Null(MetaSession.ReasoningEffort(null, "muse-spark-1.3"));
        Assert.Null(MetaSession.ReasoningEffort("", "muse-spark-1.3"));
        Assert.Null(MetaSession.ReasoningEffort("bogus", "muse-spark-1.3"));
    }

    // ---- PR-PROVAPI-048 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-048")] // MetaSession.swift:53-55
    public void FriendlyError_401_status_returns_the_rejected_key_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta rejected the API key. Check it in Settings \u2192 Providers \u2192 Meta (MODEL_API_KEY from dev.meta.ai).",
            session.FriendlyError("nope", 401));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-048")] // MetaSession.swift:53-55
    public void FriendlyError_message_with_invalid_and_key_returns_the_rejected_key_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta rejected the API key. Check it in Settings \u2192 Providers \u2192 Meta (MODEL_API_KEY from dev.meta.ai).",
            session.FriendlyError("Invalid API Key supplied", 0));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-048")] // MetaSession.swift:53-55
    [InlineData("invalid_api_key")]
    [InlineData("authentication_error")]
    public void FriendlyError_message_containing_invalid_api_key_or_authentication_error_returns_the_rejected_key_message(string needle)
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta rejected the API key. Check it in Settings \u2192 Providers \u2192 Meta (MODEL_API_KEY from dev.meta.ai).",
            session.FriendlyError($"error: {needle}", 0));
    }

    // ---- PR-PROVAPI-049 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-049")] // MetaSession.swift:56-58
    public void FriendlyError_404_status_returns_the_unknown_model_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker.",
            session.FriendlyError("nope", 404));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-049")] // MetaSession.swift:56-58
    public void FriendlyError_message_containing_model_not_found_returns_the_unknown_model_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker.",
            session.FriendlyError("error: model_not_found", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-049")] // MetaSession.swift:56-58
    public void FriendlyError_message_containing_model_and_not_exist_returns_the_unknown_model_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker.",
            session.FriendlyError("that model does not exist", 0));
    }

    // ---- PR-PROVAPI-050 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-050")] // MetaSession.swift:59-61
    public void FriendlyError_429_status_returns_the_rate_limit_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta is rate-limited right now. Wait a moment and try again.",
            session.FriendlyError("nope", 429));
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-050")] // MetaSession.swift:59-61
    [InlineData("rate limit")]
    [InlineData("rate_limit_exceeded")]
    public void FriendlyError_message_containing_rate_limit_text_returns_the_rate_limit_message(string needle)
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta is rate-limited right now. Wait a moment and try again.",
            session.FriendlyError($"error: {needle}", 0));
    }

    // ---- PR-PROVAPI-051 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-051")] // MetaSession.swift:62-64
    public void FriendlyError_504_status_returns_the_gateway_timeout_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta timed out before finishing. Try again; long generations already stream.",
            session.FriendlyError("nope", 504));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-051")] // MetaSession.swift:62-64
    public void FriendlyError_message_containing_gateway_timeout_returns_the_gateway_timeout_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta timed out before finishing. Try again; long generations already stream.",
            session.FriendlyError("error: gateway_timeout", 0));
    }

    // ---- PR-PROVAPI-052 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-052")] // MetaSession.swift:65-67
    public void FriendlyError_message_containing_reasoning_effort_and_none_returns_the_none_rejected_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Muse Wonder and Muse Spark always reason; \"none\" is rejected. Pick Minimal or higher in the model picker.",
            session.FriendlyError("reasoning_effort \"none\" is not supported", 0));
    }

    // ---- PR-PROVAPI-053 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-053")] // MetaSession.swift:68-70
    [InlineData("balance")]
    [InlineData("insufficient")]
    [InlineData("quota")]
    [InlineData("billing")]
    public void FriendlyError_message_containing_billing_words_returns_the_billing_message(string needle)
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta reported a billing/quota problem. Check your Model API balance at dev.meta.ai, then try again.",
            session.FriendlyError($"error: {needle}", 0));
    }

    // ---- PR-PROVAPI-054 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-054")] // MetaSession.swift:71-73
    [InlineData("context length exceeded")]
    [InlineData("context window too small")]
    [InlineData("context token limit")]
    public void FriendlyError_message_containing_context_and_a_size_word_returns_the_context_overflow_message(string message)
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "The conversation no longer fits Meta's 1M-token context window. Start a new thread or compact first.",
            session.FriendlyError(message, 0));
    }

    // ---- PR-PROVAPI-055 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:74
    public void FriendlyError_returns_an_unmatched_nonempty_message_unchanged()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal("something else entirely", session.FriendlyError("something else entirely", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:74
    public void FriendlyError_returns_the_stopped_before_finishing_message_for_an_empty_message()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal("Meta stopped before finishing.", session.FriendlyError("", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: key beats model
    public void FriendlyError_branch_order_key_wins_over_model()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta rejected the API key. Check it in Settings \u2192 Providers \u2192 Meta (MODEL_API_KEY from dev.meta.ai).",
            session.FriendlyError("invalid key, model_not_found", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: model beats rate limit
    public void FriendlyError_branch_order_model_wins_over_rate_limit()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta no longer offers that model. Pick Muse Wonder 1.0 in the model picker.",
            session.FriendlyError("model_not_found, rate limit hit", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: rate limit beats timeout
    public void FriendlyError_branch_order_rate_limit_wins_over_timeout()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta is rate-limited right now. Wait a moment and try again.",
            session.FriendlyError("rate limit hit, gateway_timeout", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: timeout beats none-effort
    public void FriendlyError_branch_order_timeout_wins_over_none_effort()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta timed out before finishing. Try again; long generations already stream.",
            session.FriendlyError("gateway_timeout, reasoning_effort \"none\"", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: none-effort beats billing
    public void FriendlyError_branch_order_none_effort_wins_over_billing()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Muse Wonder and Muse Spark always reason; \"none\" is rejected. Pick Minimal or higher in the model picker.",
            session.FriendlyError("reasoning_effort \"none\", insufficient balance", 0));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-055")] // MetaSession.swift:51-75 branch order: billing beats context
    public void FriendlyError_branch_order_billing_wins_over_context()
    {
        var session = new MetaSession(MakeSessionConfiguration());
        Assert.Equal(
            "Meta reported a billing/quota problem. Check your Model API balance at dev.meta.ai, then try again.",
            session.FriendlyError("insufficient balance, context length exceeded", 0));
    }
}
