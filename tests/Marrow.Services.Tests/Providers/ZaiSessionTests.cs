using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/ZaiSession.swift (B2 1fbf81e), :1-106.
public sealed class ZaiSessionTests
{
    [Theory]
    [InlineData("\u0130 does not yet include access to glm-air.", "glm-air")]
    [InlineData("does not yet include access to x . ", "x")]
    [InlineData("does not yet include ACCESS TO ' . \n glm-air . ' \n", "glm-air")]
    [Trait("Req", "PR-PROVAPI-078")] // Services/Providers/ZaiSession.swift:101-105 search original indices and trim the character-set union
    public void Review_refused_model_keeps_original_positions_and_trims_interleaved_boundaries(string message, string model)
    {
        Assert.Equal($"Your Z.ai Coding Plan does not include {model}. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier.",
            Friendly(message, statusCode: 429));
    }
    private static SessionConfiguration MakeSessionConfiguration(string? apiKey = "sk-test") => new()
    {
        Provider = ProviderKind.Zai,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = apiKey,
        Transcript = ValueList<TranscriptMessage>.Empty,
    };

    private static ZaiSession MakeSession() => new(MakeSessionConfiguration());

    private static Dictionary<string, JSONValue> ApplyPayload(string? effort, bool withTools)
    {
        var session = MakeSession();
        var payload = new Dictionary<string, JSONValue>();
        session.ApplyProviderPayload(payload, "glm-5.3", effort, withTools);
        return payload;
    }

    private static string Friendly(string message, int statusCode) => MakeSession().FriendlyError(message, statusCode);

    // ---- PR-PROVAPI-073 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-073")] // ZaiSession.swift:8-18
    public void Config_titles_errors_and_notices_as_Zai_with_the_fixed_identity_and_default_model()
    {
        var config = MakeSession().OpenAICompatibleSessionConfig;
        Assert.Equal(ProviderKind.Zai, config.Kind);
        Assert.Equal("Z.ai", config.Title);
        Assert.Equal("glm-5.3", config.DefaultModel);
        // :11 the identity closure ignores the model and always returns the same phrase.
        Assert.Equal("Z.ai GLM coding agent", config.AgentIdentity("glm-5.3"));
        Assert.Equal("Z.ai GLM coding agent", config.AgentIdentity("glm-5.3-flash"));
        Assert.True(config.ReplaysReasoning); // :16 assistant history messages carry reasoning_content
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-073")] // ZaiSession.swift:13-17
    public void Config_takes_chatURL_contextWindow_maxOutputTokens_and_isVisionModel_from_ZaiAPI()
    {
        var config = MakeSession().OpenAICompatibleSessionConfig;
        Assert.Equal(ZaiAPI.ChatURL, config.ChatURL);
        Assert.Equal(ZaiAPI.ContextWindow, config.ContextWindow);
        Assert.Equal(ZaiAPI.MaxOutputTokens, config.MaxOutputTokens);
        Assert.Equal(ZaiAPI.IsVisionModel("glm-5.3"), config.IsVisionModel("glm-5.3"));
        Assert.Equal(ZaiAPI.IsVisionModel("glm-5.3-flash"), config.IsVisionModel("glm-5.3-flash"));
    }

    // ---- PR-PROVAPI-074 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-074")] // ZaiSession.swift:26-31
    public void Payload_with_tools_carries_tool_stream_true_and_without_tools_omits_it()
    {
        var withTools = ApplyPayload(effort: null, withTools: true);
        Assert.Equal(new JSONValue.Bool(true), withTools["tool_stream"]);
        var withoutTools = ApplyPayload(effort: null, withTools: false);
        Assert.False(withoutTools.ContainsKey("tool_stream"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-074")] // ZaiSession.swift:28-29
    public void Payload_always_carries_enabled_thinking_that_keeps_prior_thinking_and_max_tokens()
    {
        var payload = ApplyPayload(effort: null, withTools: true);
        Assert.Equal(
            JSONValue.FromDictionaryLiteral(("type", "enabled"), ("clear_thinking", false)),
            payload["thinking"]);
        Assert.Equal(new JSONValue.Int(128000), payload["max_tokens"]);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-074")] // ZaiSession.swift:30
    public void Payload_carries_reasoning_effort_only_when_the_mapping_returns_a_value()
    {
        var mapped = ApplyPayload(effort: "high", withTools: false);
        Assert.Equal(new JSONValue.String("high"), mapped["reasoning_effort"]);
        var unmapped = ApplyPayload(effort: "unknown-effort", withTools: false);
        Assert.False(unmapped.ContainsKey("reasoning_effort"));
    }

    // ---- PR-PROVAPI-075 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-075")] // ZaiSession.swift:33-41
    public void Reasoning_effort_maps_low_to_low_medium_and_high_to_high()
    {
        Assert.Equal("low", ZaiSession.ReasoningEffort("low"));
        Assert.Equal("high", ZaiSession.ReasoningEffort("medium"));
        Assert.Equal("high", ZaiSession.ReasoningEffort("high"));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-075")] // ZaiSession.swift:38-39
    public void Reasoning_effort_maps_xhigh_extra_high_and_max_to_max_and_anything_else_to_null()
    {
        Assert.Equal("max", ZaiSession.ReasoningEffort("xhigh"));
        Assert.Equal("max", ZaiSession.ReasoningEffort("extra-high"));
        Assert.Equal("max", ZaiSession.ReasoningEffort("max"));
        Assert.Null(ZaiSession.ReasoningEffort("extreme"));
        // :35 the switch is exact: no case folding, so uppercase spellings fall to default.
        Assert.Null(ZaiSession.ReasoningEffort("LOW"));
        Assert.Null(ZaiSession.ReasoningEffort(null));
        Assert.Null(ZaiSession.ReasoningEffort(""));
    }

    // ---- PR-PROVAPI-076 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-076")] // ZaiSession.swift:50-54
    public void Context_overflow_messages_report_the_conversation_no_longer_fitting()
    {
        const string expected = "The conversation no longer fits GLM-5.3's context window. Start a new thread or compact first.";
        Assert.Equal(expected, Friendly("Request failed: context length exceeded", statusCode: 429));
        Assert.Equal(expected, Friendly("The conversation is too large for the context window", statusCode: 429));
        Assert.Equal(expected, Friendly("context token limit hit", statusCode: 429));
        Assert.Equal(expected, Friendly("your prompt too long to process", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-076")] // ZaiSession.swift:51-52, 88-89
    public void Context_overflow_is_tested_before_the_plan_window_so_it_is_never_reported_as_a_spent_window()
    {
        // :51-52 a context-length message also says "limit", so the context branch runs first.
        Assert.Equal(
            "The conversation no longer fits GLM-5.3's context window. Start a new thread or compact first.",
            Friendly("usage limit reached: the context length of this conversation is too large", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-076")] // ZaiSession.swift:52
    public void Context_word_without_length_window_or_token_naming_takes_no_context_branch()
    {
        // "context" alone, without "length"/"window"/"token" and without "prompt too long", falls through.
        Assert.Equal(
            "Z.ai is overloaded right now, which says nothing about your plan. Send the message again in a moment.",
            Friendly("the service is temporarily overloaded under heavy context demand", statusCode: 429));
    }

    // ---- PR-PROVAPI-077 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-077")] // ZaiSession.swift:55-57
    public void Rejected_credentials_report_the_key_as_rejected()
    {
        const string expected = "Z.ai rejected the API key. Check it in Settings \U00002192 Providers \U00002192 Z.ai."; // U+2192 RIGHTWARDS ARROW
        Assert.Equal(expected, Friendly("nope", statusCode: 401));
        Assert.Equal(expected, Friendly("your token expired, sign in again", statusCode: 429));
        Assert.Equal(expected, Friendly("the key is incorrect", statusCode: 429));
        Assert.Equal(expected, Friendly("authentication failed", statusCode: 200));
        Assert.Equal(expected, Friendly("AUTHENTICATION FAILED", statusCode: 200));
    }

    // ---- PR-PROVAPI-078 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-078")] // ZaiSession.swift:58-62
    public void Plan_does_not_include_access_names_the_refused_model_back()
    {
        Assert.Equal(
            "Your Z.ai Coding Plan does not include glm-4.7. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier.",
            Friendly("Error 1311: your plan does not yet include access to glm-4.7.", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-078")] // ZaiSession.swift:100-105
    public void Plan_refused_model_is_case_insensitive_and_trims_whitespace_newlines_and_punctuation()
    {
        Assert.Equal(
            "Your Z.ai Coding Plan does not include glm-air. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier.",
            Friendly("Your plan DOES NOT YET INCLUDE ACCESS TO `glm-air`.", statusCode: 429));
        // Trim set is exactly whitespace, newlines, full stop, backtick, double quote, single quote.
        Assert.Equal(
            "Your Z.ai Coding Plan does not include glm-air\"x. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier.",
            Friendly("does not yet include access to  \n .'\"glm-air\"x'\". ", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-078")] // ZaiSession.swift:102-104
    public void Plan_refused_model_falls_back_to_that_model_when_nothing_is_left()
    {
        const string expected = "Your Z.ai Coding Plan does not include that model. Pick GLM-5.3 or GLM-5.3 Flash for the model, or move up a plan tier.";
        // The outer branch's own trigger phrase is present, but 'access to ' is not, so the inner
        // search finds nothing at all.
        Assert.Equal(expected, Friendly("Error 1311: your plan does not yet include access, sorry.", statusCode: 429));
        // 'access to ' present, but every character left after it is in the trim set, so trimming
        // both ends of the remainder empties it out.
        Assert.Equal(expected, Friendly("does not yet include access to .`\"'", statusCode: 429));
    }

    // ---- PR-PROVAPI-079 ----

    [Fact]
    [Trait("Decision", "ED-030")]
    [Trait("Req", "PR-PROVAPI-079")] // ZaiSession.swift:63-66
    public void Throttling_messages_report_that_Zai_is_throttling_requests_verbatim()
    {
        // ED-030: the em dash (U+2014) is a reproduced upstream user-facing string, kept byte-identical.
        const string expected = "Z.ai is throttling requests right now \U00002014 its plans cap how many run at once. Wait a moment and send again; fewer at a time get through."; // U+2014 EM DASH
        Assert.Equal(expected, Friendly("Error 1302: rate limit reached for this account", statusCode: 429));
        Assert.Equal(expected, Friendly("Too Many Requests", statusCode: 429));
    }

    // ---- PR-PROVAPI-080 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-080")] // ZaiSession.swift:67-69
    public void Overloaded_messages_report_that_Zai_is_overloaded_right_now()
    {
        Assert.Equal(
            "Z.ai is overloaded right now, which says nothing about your plan. Send the message again in a moment.",
            Friendly("the model is temporarily overloaded, please retry", statusCode: 429));
    }

    // ---- PR-PROVAPI-081 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-081")] // ZaiSession.swift:70-73
    public void Fair_usage_messages_report_the_account_pause_under_the_policy()
    {
        Assert.Equal(
            "Z.ai has paused this account's requests under its Fair Usage Policy. It lifts on its own; details at z.ai/devpack/usage-policy.",
            Friendly("Error 1313: you have reached the fair usage threshold", statusCode: 429));
    }

    // ---- PR-PROVAPI-082 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-082")] // ZaiSession.swift:74-77
    public void No_active_plan_messages_report_a_key_without_a_coding_plan()
    {
        const string expected = "This Z.ai key has no active Coding Plan. Subscribe at z.ai/subscribe, then try again.";
        Assert.Equal(expected, Friendly("Error 1113: insufficient balance for this request", statusCode: 429));
        Assert.Equal(expected, Friendly("no resource package on this key", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-082")] // ZaiSession.swift:75
    public void No_active_plan_matches_the_bare_digits_1113_anywhere_in_the_message()
    {
        const string expected = "This Z.ai key has no active Coding Plan. Subscribe at z.ai/subscribe, then try again.";
        Assert.Equal(expected, Friendly("business code 1113 returned", statusCode: 429));
        // The literal digits match anywhere, even inside unrelated wording.
        Assert.Equal(expected, Friendly("request id 1113009 failed", statusCode: 429));
    }

    // ---- PR-PROVAPI-083 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-083")] // ZaiSession.swift:78-81
    public void Expired_plan_messages_report_the_lapsed_subscription()
    {
        Assert.Equal(
            "Your GLM Coding Plan has expired. Renew it at z.ai/subscribe, then try again.",
            Friendly("Error 1309: your package has expired", statusCode: 429));
    }

    // ---- PR-PROVAPI-084 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-084")] // ZaiSession.swift:82-85
    public void Enterprise_messages_report_a_key_belonging_to_an_enterprise_package()
    {
        const string expected = "This Z.ai key belongs to an enterprise coding package. Use the personal Coding Plan key from z.ai/manage-apikey/subscription.";
        Assert.Equal(expected, Friendly("Error 1314: this is an enterprise package key", statusCode: 429));
        Assert.Equal(expected, Friendly("ENTERPRISE keys are managed centrally", statusCode: 200));
    }

    // ---- PR-PROVAPI-085 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-085")] // ZaiSession.swift:86-90
    public void Spent_window_messages_report_the_plan_usage_limit_for_this_window()
    {
        const string expected = "Your GLM Coding Plan usage limit is reached for this window. It refreshes on its own; the usage ring shows when. Details at z.ai/manage-apikey/subscription.";
        Assert.Equal(expected, Friendly("Error 1308: usage limit of the current window reached", statusCode: 429));
        Assert.Equal(expected, Friendly("limit exhausted for this window", statusCode: 429));
        Assert.Equal(expected, Friendly("you need extra usage, upgrade your plan", statusCode: 429));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-085")] // ZaiSession.swift:86-89
    public void Spent_window_message_is_the_only_one_containing_the_usage_limit_phrase()
    {
        // The exact phrase "usage limit" is the auto-continue signal; every other refusal must avoid it.
        string[] otherMessages =
        {
            "Request failed: context length exceeded",
            "the key is incorrect",
            "your plan does not yet include access to glm-4.7.",
            "rate limit reached for this account",
            "the model is temporarily overloaded",
            "you have reached the fair usage threshold",
            "insufficient balance for this request",
            "your package has expired",
            "this is an enterprise package key",
            "unknown model glm-99",
        };
        foreach (var message in otherMessages)
        {
            var friendly = Friendly(message, statusCode: 429);
            Assert.DoesNotContain("usage limit", friendly, StringComparison.Ordinal);
        }
    }

    // ---- PR-PROVAPI-086 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-086")] // ZaiSession.swift:91-94
    public void Unknown_model_messages_report_that_Zai_does_not_offer_the_model()
    {
        const string expected = "Z.ai does not offer that model. Pick GLM-5.3 or GLM-5.3 Flash in the model picker.";
        Assert.Equal(expected, Friendly("Error 1211: unknown model glm-99", statusCode: 429));
        Assert.Equal(expected, Friendly("the requested model does not exist", statusCode: 429));
        Assert.Equal(expected, Friendly("this MODEL is NOT EXIST here", statusCode: 200));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-086")] // ZaiSession.swift:92
    public void Model_word_alone_without_not_exist_takes_no_unknown_model_branch()
    {
        // "model" alone, without "not exist" and without "unknown model", falls through to the passthrough.
        Assert.Equal(
            "the model needs a different setting entirely",
            Friendly("the model needs a different setting entirely", statusCode: 429));
    }

    // ---- PR-PROVAPI-087 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-087")] // ZaiSession.swift:95-97
    public void Unmapped_refusals_pass_Zai_wording_through_unchanged()
    {
        Assert.Equal(
            "Something else went wrong entirely.",
            Friendly("Something else went wrong entirely.", statusCode: 429));
        Assert.Equal(
            "Gateway hiccup upstream",
            Friendly("Gateway hiccup upstream", statusCode: 502));
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-087")] // ZaiSession.swift:97
    public void Empty_refusal_becomes_the_stopped_before_finishing_message()
    {
        Assert.Equal("Z.ai stopped before finishing.", Friendly("", statusCode: 429));
    }
}
