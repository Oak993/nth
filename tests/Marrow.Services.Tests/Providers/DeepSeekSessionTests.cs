using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Expectations come from DroppyCode/Services/Providers/DeepSeekSession.swift (B2 1fbf81e), :1-58: the configuration
// record, the provider payload, the effort mapping and the friendly error table (PR-PROVAPI-026 to 033).
public sealed class DeepSeekSessionTests
{
    private static SessionConfiguration MakeSessionConfiguration(string? apiKey = "sk-test") => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = apiKey,
        Transcript = ValueList<TranscriptMessage>.Empty,
    };

    private static DeepSeekSession Session() => new(MakeSessionConfiguration());

    private static Dictionary<string, JSONValue> Payload(string? effort, bool withTools, string model = "deepseek-v4-pro")
    {
        var payload = new Dictionary<string, JSONValue>();
        Session().ApplyProviderPayload(payload, model, effort, withTools);
        return payload;
    }

    // ---- PR-PROVAPI-026 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-026")] // Services/Providers/DeepSeekSession.swift:8-18
    public void The_configuration_names_DeepSeek_and_defaults_to_deepseek_v4_pro()
    {
        var config = Session().OpenAICompatibleSessionConfig;
        Assert.Equal(ProviderKind.Deepseek, config.Kind);
        Assert.Equal("DeepSeek", config.Title);
        Assert.Equal("deepseek-v4-pro", config.DefaultModel);
        Assert.Equal(DeepSeekAPI.ChatURL, config.ChatURL);
        Assert.Equal(new Uri("https://api.deepseek.com/chat/completions"), config.ChatURL);
        Assert.Equal(DeepSeekAPI.ContextWindow, config.ContextWindow);
        Assert.Equal(1_048_576, config.ContextWindow);
        Assert.Null(config.MaxOutputTokens);
        Assert.True(config.ReplaysReasoning);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-026")] // Services/Providers/DeepSeekSession.swift:11
    public void The_agent_identity_is_DeepSeek_coding_agent_whatever_the_model()
    {
        var config = Session().OpenAICompatibleSessionConfig;
        Assert.Equal("DeepSeek coding agent", config.AgentIdentity("deepseek-v4-pro"));
        Assert.Equal("DeepSeek coding agent", config.AgentIdentity("deepseek-v4.1-flash"));
        Assert.Equal("DeepSeek coding agent", config.AgentIdentity(""));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-026")] // Services/Providers/DeepSeekSession.swift:17
    public void The_vision_test_is_DeepSeekAPI_isVisionModel()
    {
        var config = Session().OpenAICompatibleSessionConfig;
        Assert.Equal(typeof(DeepSeekAPI).GetMethod(nameof(DeepSeekAPI.IsVisionModel)), config.IsVisionModel.Method);
        Assert.Null(config.IsVisionModel.Target);
    }

    // ---- PR-PROVAPI-027 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-027")] // Services/Providers/DeepSeekSession.swift:27-31
    [InlineData(true)]
    [InlineData(false)]
    public void Every_request_streams_usage_enables_thinking_and_carries_the_mapped_effort(bool withTools)
    {
        var payload = Payload("medium", withTools);
        Assert.Equal(["reasoning_effort", "stream_options", "thinking"], payload.Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Equal(JSONValue.FromDictionaryLiteral(("include_usage", true)), payload["stream_options"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "enabled")), payload["thinking"]);
        Assert.Equal(new JSONValue.String("high"), payload["reasoning_effort"]);
    }

    [Theory]
    [Trait("Req", "PR-PROVAPI-027")] // Services/Providers/DeepSeekSession.swift:30
    [InlineData(null)]
    [InlineData("")]
    [InlineData("minimal")]
    public void A_request_without_a_mapped_effort_carries_no_reasoning_effort(string? effort)
    {
        var payload = Payload(effort, withTools: true);
        Assert.Equal(["stream_options", "thinking"], payload.Keys.OrderBy(key => key, StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-027")] // Services/Providers/DeepSeekSession.swift:28-30
    public void The_payload_keys_are_set_over_whatever_the_payload_already_held()
    {
        var payload = new Dictionary<string, JSONValue>
        {
            ["model"] = "deepseek-v4-pro",
            ["stream_options"] = JSONValue.FromDictionaryLiteral(("include_usage", false)),
            ["reasoning_effort"] = "kept",
        };
        Session().ApplyProviderPayload(payload, "deepseek-v4-pro", "nonsense", withTools: false);
        Assert.Equal(new JSONValue.String("deepseek-v4-pro"), payload["model"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("include_usage", true)), payload["stream_options"]);
        Assert.Equal(JSONValue.FromDictionaryLiteral(("type", "enabled")), payload["thinking"]);
        // An unmapped effort leaves the key alone rather than removing it.
        Assert.Equal(new JSONValue.String("kept"), payload["reasoning_effort"]);
    }

    // ---- PR-PROVAPI-028 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-028")] // Services/Providers/DeepSeekSession.swift:33-40
    [InlineData("low", "low")]
    [InlineData("high", "high")]
    [InlineData("max", "max")]
    [InlineData("medium", "high")]
    [InlineData("xhigh", "high")]
    [InlineData("extra-high", "high")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("minimal", null)]
    [InlineData("none", null)]
    [InlineData("HIGH", null)]
    [InlineData(" low", null)]
    public void The_effort_maps_low_high_max_through_and_medium_xhigh_extra_high_to_high(string? effort, string? expected)
    {
        Assert.Equal(expected, DeepSeekSession.ReasoningEffort(effort));
    }

    // ---- PR-PROVAPI-029 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-029")] // Services/Providers/DeepSeekSession.swift:43-46
    [InlineData("Insufficient Balance", 402)]
    [InlineData("your BALANCE is empty", 400)]
    [InlineData("insufficient funds; rate limit", 429)]
    [InlineData("insufficient context window tokens for model that does not exist", 400)]
    public void A_balance_or_insufficient_message_says_DeepSeek_has_no_balance_left(string message, int statusCode)
    {
        Assert.Equal("DeepSeek has no balance left. Top up at platform.deepseek.com, then try again.", Session().FriendlyError(message, statusCode));
    }

    // ---- PR-PROVAPI-030 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-030")] // Services/Providers/DeepSeekSession.swift:47-49
    [InlineData("Too many requests", 429)]
    [InlineData("", 429)]
    [InlineData("Rate Limit reached for context length", 400)]
    public void A_429_or_a_rate_limit_message_says_DeepSeek_is_rate_limited(string message, int statusCode)
    {
        Assert.Equal("DeepSeek is rate-limited right now. Wait a moment and try again.", Session().FriendlyError(message, statusCode));
    }

    // ---- PR-PROVAPI-031 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-031")] // Services/Providers/DeepSeekSession.swift:50-52
    [InlineData("This model's maximum context length is 1048576 tokens")]
    [InlineData("CONTEXT WINDOW exceeded")]
    [InlineData("context: too many token(s); model does not exist")]
    public void A_context_message_about_length_window_or_tokens_says_the_conversation_no_longer_fits(string message)
    {
        Assert.Equal("The conversation no longer fits DeepSeek's context window. Start a new thread or compact first.", Session().FriendlyError(message, 400));
    }

    // ---- PR-PROVAPI-032 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-032")] // Services/Providers/DeepSeekSession.swift:53-55
    [InlineData("Model Not Exist")]
    [InlineData("The model `deepseek-chat` does not exist")]
    public void A_model_that_does_not_exist_says_DeepSeek_no_longer_offers_it(string message)
    {
        Assert.Equal("DeepSeek no longer offers that model. Pick V4 Pro or V4.1 Flash in the model picker.", Session().FriendlyError(message, 400));
    }

    // ---- PR-PROVAPI-033 ----

    [Theory]
    [Trait("Req", "PR-PROVAPI-033")] // Services/Providers/DeepSeekSession.swift:56
    [InlineData("Invalid API key", 401)]
    [InlineData("context only", 400)]
    [InlineData("model is busy", 503)]
    [InlineData("does not exist", 404)]
    [InlineData("  Server Error  ", 500)]
    public void Any_other_message_passes_through_unchanged(string message, int statusCode)
    {
        Assert.Equal(message, Session().FriendlyError(message, statusCode));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-033")] // Services/Providers/DeepSeekSession.swift:56
    public void An_empty_message_says_DeepSeek_stopped_before_finishing()
    {
        Assert.Equal("DeepSeek stopped before finishing.", Session().FriendlyError("", 500));
    }
}
