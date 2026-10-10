using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY, no Swift twin (OD-0049, OD-0060, ED-234). The native OpenRouter agent, in the manner of MetaSession.swift and
// ZaiSession.swift over OpenAICompatibleSession.swift: OpenAI-compatible Chat Completions with tools, the key from the
// session configuration, the model and effort from the turn. Every request goes to https://openrouter.ai through the HTTP
// client's test seam (OpenRouterSessionRig, URLSession.Transport, OD-0057) and is answered in this process: no socket, no
// real key, no real credential store (R-TEST-9, R-TEST-10). One collection with the other session classes because the
// session reaches the process-wide MCP hub, and with the API tests because the model facts are process-wide.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class OpenRouterSessionTests
{
    private const string Sonnet = "anthropic/claude-sonnet-4.5";

    private static string Text(JSONValue? value) => value?.JSONValueString ?? "";

    // ---- the configuration ---------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public void The_session_is_titled_OpenRouter_and_starts_on_the_auto_router()
    {
        using var rig = new OpenRouterSessionRig();
        var config = rig.Session.OpenAICompatibleSessionConfig;

        Assert.Equal(ProviderKind.OpenRouter, config.Kind);
        Assert.Equal("OpenRouter", config.Title);
        Assert.Equal("openrouter/auto", config.DefaultModel);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), config.ChatURL);
        Assert.Equal(OpenRouterAPI.DefaultContextWindow, config.ContextWindow);
        Assert.Null(config.MaxOutputTokens);
        Assert.False(config.ReplaysReasoning);
        Assert.Equal($"OpenRouter coding agent ({Sonnet})", config.AgentIdentity(Sonnet));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_session_asks_the_list_whether_a_model_reads_images()
    {
        using var rig = new OpenRouterSessionRig();
        var config = rig.Session.OpenAICompatibleSessionConfig;
        Assert.False(config.IsVisionModel(Sonnet)); // the list has not been fetched

        await rig.LoadCatalogue();

        Assert.True(config.IsVisionModel(Sonnet));
        Assert.False(config.IsVisionModel("meta-llama/llama-3.3-70b-instruct:free"));
    }

    // ---- not signed in -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \n ")]
    [Trait("Decision", "ED-234")]
    public async Task A_session_with_no_key_does_not_start_and_says_a_key_is_needed(string? key)
    {
        using var rig = new OpenRouterSessionRig(apiKey: key);

        var error = await Assert.ThrowsAsync<ProviderError>(() => rig.Session.Start());

        var notInstalled = Assert.IsType<ProviderError.Case.NotInstalled>(error.ErrorCase);
        Assert.Equal(ProviderKind.OpenRouter, notInstalled.Provider);
        Assert.Equal("OpenRouter needs an API key. Add one in Settings → Providers → OpenRouter.", error.ErrorDescription);
        Assert.False(rig.Session.IsRunning);
        Assert.Empty(rig.Handler.Received); // nothing was asked of the network
    }

    // ---- the chat request ----------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_turn_is_one_POST_to_chat_completions_with_the_bearer_key_and_the_chosen_model()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"ok"}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hello there", model: Sonnet));

        var request = Assert.Single(rig.ChatRequests);
        Assert.Equal("POST", request.Method);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/chat/completions"), request.Url);
        Assert.Equal(OpenRouterBodies.Bearer, request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Content-Type"));
        Assert.Equal("application/json", request.Header("Accept"));
        var body = rig.ChatBody(0);
        Assert.Equal(Sonnet, Text(body["model"]));
        Assert.True(body["stream"]?.JSONValueBool);
        Assert.Equal(true, body["stream_options"]?["include_usage"]?.JSONValueBool); // the usage block only streams when asked for
        var messages = body["messages"]!.JSONValueArray!;
        Assert.Equal("system", Text(messages[0]["role"]));
        Assert.Contains("OpenRouter coding agent (anthropic/claude-sonnet-4.5)", Text(messages[0]["content"]), StringComparison.Ordinal);
        Assert.Equal("user", Text(messages[^1]["role"]));
        Assert.Equal("hello there", Text(messages[^1]["content"]));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_turn_with_no_model_goes_to_the_auto_router()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"ok"}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi"));

        Assert.Equal("openrouter/auto", Text(rig.ChatBody(0)["model"]));
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_request_offers_the_six_built_in_tools_and_lets_the_model_choose()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"ok"}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet));

        var body = rig.ChatBody(0);
        Assert.Equal("auto", Text(body["tool_choice"]));
        var names = body["tools"]!.JSONValueArray!.Select(tool => Text(tool["function"]?["name"])).ToList();
        Assert.Equal(["read_file", "list_files", "search_text", "write_file", "edit_file", "run_command"], names);
        Assert.All(body["tools"]!.JSONValueArray!, tool => Assert.Equal("function", Text(tool["type"])));
    }

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "high")]
    [InlineData("extra-high", "high")]
    [InlineData("max", "high")]
    [Trait("Decision", "ED-234")]
    public async Task The_effort_goes_in_the_reasoning_object_on_the_scale_OpenRouter_takes(string effort, string sent)
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"ok"}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet, effort: effort));

        var body = rig.ChatBody(0);
        Assert.Equal(sent, Text(body["reasoning"]?["effort"]));
        Assert.Null(body["reasoning_effort"]); // not the top-level field Meta takes
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("minimal")]
    [InlineData("turbo")]
    [Trait("Decision", "ED-234")]
    public async Task No_effort_or_an_unknown_one_sends_no_reasoning_field(string? effort)
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"ok"}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet, effort: effort));

        Assert.Null(rig.ChatBody(0)["reasoning"]);
    }

    // ---- the stream ----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_stream_becomes_the_message_the_reasoning_and_a_completed_turn()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream(
            """{"choices":[{"delta":{"reasoning":"Let me ","content":"Hel"}}]}""",
            """{"choices":[{"delta":{"reasoning":"think.","content":"lo"}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"stop"}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet));

        Assert.Equal(["Hel", "lo"], rig.Events.OfType<ProviderEvent.MessageDelta>().Select(e => e.Text));
        Assert.Equal("Hello", Assert.Single(rig.Events.OfType<ProviderEvent.MessageCompleted>()).Text);
        Assert.Equal("Let me think.", Assert.Single(rig.Events.OfType<ProviderEvent.ReasoningCompleted>()).Text); // delta.reasoning is how OpenRouter names it
        var completed = Assert.Single(rig.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Completed, completed.Status);
        Assert.Null(completed.Error);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task The_usage_block_is_measured_against_the_window_the_list_gave_that_model()
    {
        using var rig = new OpenRouterSessionRig();
        await rig.LoadCatalogue();
        rig.ReplyWithStream(
            """{"choices":[{"delta":{"content":"ok"}}]}""",
            """{"choices":[],"usage":{"prompt_tokens":1000,"completion_tokens":234,"total_tokens":1234}}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet));

        var usage = Assert.Single(rig.Events.OfType<ProviderEvent.Usage>()).Value;
        Assert.Equal(1234, usage.UsedTokens);
        Assert.Equal(1_000_000, usage.WindowTokens); // Claude Sonnet 4.5's context_length, not one window for every model
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_model_the_list_does_not_know_is_measured_against_the_default_window()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStream(
            """{"choices":[{"delta":{"content":"ok"}}]}""",
            """{"choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15}}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: "unlisted/model"));

        Assert.Equal(128_000, Assert.Single(rig.Events.OfType<ProviderEvent.Usage>()).Value.WindowTokens);
    }

    // ---- tool calls ----------------------------------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_tool_call_is_run_and_its_result_goes_back_in_the_next_request_with_the_call_id()
    {
        using var rig = new OpenRouterSessionRig();
        await File.WriteAllTextAsync(Path.Combine(rig.Root, "notes.txt"), "the notes say hello");
        // The call arrives in pieces, as OpenAI-compatible streams send it: id and name first, the arguments after.
        rig.ReplyWithStream(
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_abc","type":"function","function":{"name":"read_file","arguments":""}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"{\"path\":"}}]}}]}""",
            """{"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"\"notes.txt\"}"}}]}}]}""",
            """{"choices":[{"delta":{},"finish_reason":"tool_calls"}]}""");
        rig.ReplyWithStream("""{"choices":[{"delta":{"content":"Done."}}]}""");

        await rig.SendAsync(OpenRouterBodies.Turn("read the notes", model: Sonnet));

        Assert.Equal(2, rig.ChatRequests.Count);
        var messages = rig.ChatBody(1)["messages"]!.JSONValueArray!;
        var assistant = messages.Single(message => Text(message["role"]) == "assistant");
        var call = Assert.Single(assistant["tool_calls"]!.JSONValueArray!);
        Assert.Equal("call_abc", Text(call["id"]));
        Assert.Equal("function", Text(call["type"]));
        Assert.Equal("read_file", Text(call["function"]!["name"]));
        Assert.Equal("""{"path":"notes.txt"}""", Text(call["function"]!["arguments"]));
        var tool = messages.Single(message => Text(message["role"]) == "tool");
        Assert.Equal("call_abc", Text(tool["tool_call_id"]));
        Assert.Contains("the notes say hello", Text(tool["content"]), StringComparison.Ordinal);
        Assert.Equal("Done.", rig.Events.OfType<ProviderEvent.MessageCompleted>().Last().Text);
        Assert.Equal(TurnStatus.Completed, Assert.Single(rig.Events.OfType<ProviderEvent.TurnCompleted>()).Status);
        Assert.Contains(rig.Events, e => e is ProviderEvent.ToolStarted);
    }

    // ---- the answers that are not a stream -----------------------------------------------------------------------

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_401_says_the_key_was_rejected_and_where_to_fix_it()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStatus(401, """{"error":{"code":401,"message":"No auth credentials found"}}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet));

        var completed = Assert.Single(rig.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("OpenRouter rejected the API key. Check it in Settings → Providers → OpenRouter.", completed.Error);
    }

    [Fact]
    [Trait("Decision", "ED-234")]
    public async Task A_402_says_there_are_no_credits_left()
    {
        using var rig = new OpenRouterSessionRig();
        rig.ReplyWithStatus(402, """{"error":{"code":402,"message":"Insufficient credits. Add more using https://openrouter.ai/credits"}}""");

        await rig.SendAsync(OpenRouterBodies.Turn("hi", model: Sonnet));

        var completed = Assert.Single(rig.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("OpenRouter has no credits left for this request. Add credits at openrouter.ai/credits, or pick a free model, then try again.", completed.Error);
    }

    [Theory]
    [InlineData("No auth credentials found", 401, "OpenRouter rejected the API key. Check it in Settings → Providers → OpenRouter (keys are made at openrouter.ai/keys).")]
    [InlineData("User not found.", 200, "OpenRouter rejected the API key. Check it in Settings → Providers → OpenRouter (keys are made at openrouter.ai/keys).")]
    [InlineData("Insufficient credits", 402, "OpenRouter has no credits left for this request. Add credits at openrouter.ai/credits, or pick a free model, then try again.")]
    [InlineData("This request requires more credits, or fewer max_tokens.", 200, "OpenRouter has no credits left for this request. Add credits at openrouter.ai/credits, or pick a free model, then try again.")]
    [InlineData("No endpoints found that support tool use. To learn more about provider routing, visit: https://openrouter.ai/docs", 404, "That OpenRouter model cannot use tools, which Nth needs to work on files. Pick another model in the model picker.")]
    [InlineData("No endpoints found for acme/gone-1.", 404, "OpenRouter has no provider serving that model right now. Pick another model in the model picker.")]
    [InlineData("acme/x is not a valid model ID", 400, "OpenRouter has no provider serving that model right now. Pick another model in the model picker.")]
    [InlineData("Rate limit exceeded: free-models-per-day", 429, "OpenRouter is rate-limited right now. Wait a moment and try again.")]
    [InlineData("anything", 429, "OpenRouter is rate-limited right now. Wait a moment and try again.")]
    [InlineData("This model's maximum context length is 8192 tokens. However, you requested 9000 tokens.", 400, "The conversation no longer fits this model's context window. Start a new thread or compact first.")]
    [InlineData("Provider returned error", 504, "OpenRouter timed out before finishing. Try again.")]
    [InlineData("the request timed out", 200, "OpenRouter timed out before finishing. Try again.")]
    [InlineData("Rate limit exceeded: free-models-per-day", 200, "OpenRouter is rate-limited right now. Wait a moment and try again.")]
    [InlineData("The upstream provider is rate-limited", 200, "OpenRouter is rate-limited right now. Wait a moment and try again.")]
    [InlineData("anything", 401, "OpenRouter rejected the API key. Check it in Settings \u2192 Providers \u2192 OpenRouter (keys are made at openrouter.ai/keys).")]
    [InlineData("anything", 402, "OpenRouter has no credits left for this request. Add credits at openrouter.ai/credits, or pick a free model, then try again.")]
    [InlineData("anything", 404, "OpenRouter has no provider serving that model right now. Pick another model in the model picker.")]
    [InlineData("anything", 408, "OpenRouter timed out before finishing. Try again.")]
    [InlineData("", 200, "OpenRouter stopped before finishing.")]
    [InlineData("A reason this build does not know", 400, "A reason this build does not know")]
    [Trait("Decision", "ED-234")]
    public void A_refusal_is_put_in_words_by_its_status_and_its_message(string message, int status, string expected)
    {
        using var rig = new OpenRouterSessionRig();

        Assert.Equal(expected, rig.Session.FriendlyError(message, status));
    }

    // ---- the effort mapping on its own ---------------------------------------------------------------------------

    [Theory]
    [InlineData("low", "low")]
    [InlineData("medium", "medium")]
    [InlineData("high", "high")]
    [InlineData("xhigh", "high")]
    [InlineData("extra-high", "high")]
    [InlineData("max", "high")]
    [InlineData("minimal", null)]
    [InlineData("none", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [Trait("Decision", "ED-234")]
    public void The_effort_words_other_sliders_use_map_onto_the_three_OpenRouter_takes(string? effort, string? expected) =>
        Assert.Equal(expected, OpenRouterSession.ReasoningEffort(effort));
}
