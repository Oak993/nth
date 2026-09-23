using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B2 1fbf81e), :61-65, :86-97,
// :114-148, :152-227, :506-800.
//
// Part 2 of 5 (PR-PROVAPI-113 to 136): request payload, MCP tool naming, the transport-retry
// ladder, SSE draining, in-stream error and usage parsing, and tool-call accumulation. Drives the
// real, non-seamed StreamOneRound/PerformStream against FakeUpstreamServer (R-TEST-10: a loopback
// server, not a mock). PR-PROVAPI-113..117 (Stop, ResolveApproval, AnswerQuestion, Compact,
// ReportUsage) were already implemented and are not retested here: see
// OpenAICompatibleSessionTests for those and for the seam this part's tests do not use.
public sealed class OpenAICompatibleSessionSecondTests
{
    private static OpenAICompatibleSession.Config MakeConfig(Uri chatUrl) => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = chatUrl,
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

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

    private static TurnInput MakeTurn(string text = "hi") => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
    };

    private static async Task RunSend(TestRuntime runtime, OpenAICompatibleSessionCapturingSeam session, TurnInput input)
    {
        var task = session.Send(input);
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private static string SseBody(params string[] dataLines) =>
        string.Join("", dataLines.Select(line => $"data: {line}\n\n")) + "data: [DONE]\n\n";

    private static FakeResponse SseResponse(params string[] dataLines) =>
        new(200, [("Content-Type", "text/event-stream")], [], [SseBody(dataLines)]);

    // ---- PR-PROVAPI-119, PR-PROVAPI-124 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-124")] // OpenAICompatibleSession.swift:601-607
    public async Task Chat_request_is_a_post_with_the_three_headers_and_the_full_history()
    {
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer
        {
            Handler = r => { seen = r; return SseResponse("""{"choices":[{"delta":{"content":"hi"}}]}"""); },
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn("hello there"));

        Assert.NotNull(seen);
        Assert.Equal("POST", seen!.Method);
        Assert.Equal("application/json", seen.Headers["Content-Type"]);
        Assert.Equal("Bearer sk-test", seen.Headers["Authorization"]);
        Assert.Equal("application/json", seen.Headers["Accept"]);
        var body = JSONValue.Parse(seen.Body)!;
        Assert.Equal("deepseek-chat", body["model"]!.JSONValueString);
        Assert.True(body["stream"]!.JSONValueBool);
        var messages = body["messages"]!.JSONValueArray!;
        Assert.Equal("system", messages[0]["role"]!.JSONValueString);
        Assert.Equal("user", messages[^1]["role"]!.JSONValueString);
        Assert.Equal("hello there", messages[^1]["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-119")] // OpenAICompatibleSession.swift:569-581
    public async Task Tool_round_payload_carries_the_six_built_ins_then_mcp_tools_and_tool_choice_auto()
    {
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer
        {
            Handler = r => { seen = r; return SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""); },
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        session.SeedMcpToolsForTests([new MCPHub.Tool("srv", "tool", "does a thing", JSONValue.FromDictionaryLiteral(("type", "object")))]);
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var body = JSONValue.Parse(seen!.Body)!;
        var tools = body["tools"]!.JSONValueArray!;
        Assert.Equal(7, tools.Count);
        Assert.Equal("read_file", tools[0]["function"]!["name"]!.JSONValueString);
        Assert.Equal("list_files", tools[1]["function"]!["name"]!.JSONValueString);
        Assert.Equal("search_text", tools[2]["function"]!["name"]!.JSONValueString);
        Assert.Equal("write_file", tools[3]["function"]!["name"]!.JSONValueString);
        Assert.Equal("edit_file", tools[4]["function"]!["name"]!.JSONValueString);
        Assert.Equal("run_command", tools[5]["function"]!["name"]!.JSONValueString);
        Assert.Equal("mcp__srv__tool", tools[6]["function"]!["name"]!.JSONValueString); // PR-PROVAPI-120
        Assert.Equal("does a thing", tools[6]["function"]!["description"]!.JSONValueString);
        Assert.Equal("auto", body["tool_choice"]!.JSONValueString);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-120")] // OpenAICompatibleSession.swift:61-65; MCPHub.swift:38
    public async Task Mcp_tool_name_is_mcp_double_underscore_server_double_underscore_tool()
    {
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer
        {
            Handler = r => { seen = r; return SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""); },
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        session.SeedMcpToolsForTests([new MCPHub.Tool("filesystem", "read", "reads", JSONValue.FromDictionaryLiteral(("type", "object")))]);
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var body = JSONValue.Parse(seen!.Body)!;
        var mcpTool = body["tools"]!.JSONValueArray!.Last();
        Assert.Equal("mcp__filesystem__read", mcpTool["function"]!["name"]!.JSONValueString);
    }

    // ---- PR-PROVAPI-125, PR-PROVAPI-126 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-125")] // OpenAICompatibleSession.swift:633-644
    [Trait("Req", "PR-PROVAPI-126")] // OpenAICompatibleSession.swift:792-800
    public async Task Non_200_response_reads_error_message_from_json_error_field()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(503, [("Content-Type", "application/json")], Encoding.UTF8.GetBytes("""{"error":{"message":"rate limited"}}""")),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("rate limited", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-126")] // OpenAICompatibleSession.swift:792-800 - top-level message field
    public async Task Error_message_falls_back_to_top_level_message_field()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(500, [], Encoding.UTF8.GetBytes("""{"message":"boom"}""")),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal("boom", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-126")] // OpenAICompatibleSession.swift:792-800 - unparseable body falls back to the trimmed raw body
    public async Task Error_message_falls_back_to_trimmed_raw_body_when_not_json()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(500, [], Encoding.UTF8.GetBytes("  not json at all  ")),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal("not json at all", completed.Error);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-126")] // OpenAICompatibleSession.swift:799 - empty body falls back to the status sentence
    public async Task Error_message_falls_back_to_status_sentence_when_body_is_empty()
    {
        await using var server = new FakeUpstreamServer { Handler = _ => new FakeResponse(503, [], []) };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal("DeepSeek returned status 503.", completed.Error);
    }

    // ---- PR-PROVAPI-127 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-127")] // OpenAICompatibleSession.swift:742-748
    public async Task Only_data_prefixed_lines_are_parsed_and_done_ends_the_stream()
    {
        var raw = ": a comment\n\nevent: message\n\n" +
            """data: {"choices":[{"delta":{"content":"he"}}]}""" + "\n\n" +
            "not-data-at-all\n\n" +
            """data: {"not valid json""" + "\n\n" + // unparseable JSON is skipped
            """data: {"choices":[{"delta":{"content":"llo"}}]}""" + "\n\n" +
            "data: [DONE]\n\n" +
            """data: {"choices":[{"delta":{"content":"never"}}]}""" + "\n\n"; // after [DONE]: never read
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream")], [], [raw]),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completedText = session.Events.OfType<ProviderEvent.MessageCompleted>().Last();
        Assert.Equal("hello", completedText.Text);
    }

    // ---- PR-PROVAPI-130 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-130")] // OpenAICompatibleSession.swift:753-766
    public async Task Usage_accepts_either_field_spelling_and_the_three_cached_spellings()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse(
                """{"choices":[{"delta":{"content":"hi"}}],"usage":{"input_tokens":10,"output_tokens":5,"input_tokens_details":{"cached_tokens":3}}}"""),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var spend = session.Events.OfType<ProviderEvent.TokenSpend>().Single().Value;
        Assert.Equal(15, spend.TotalTokens); // total_tokens absent -> input + output
        Assert.Equal(10, spend.InputTokens);
        Assert.Equal(5, spend.OutputTokens);
        Assert.Equal(3, spend.CachedInputTokens);
        var usage = session.Events.OfType<ProviderEvent.Usage>().Single().Value;
        Assert.Equal(15, usage.UsedTokens);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-130")] // OpenAICompatibleSession.swift:761-763 - a zero total emits no context usage but still emits token usage
    public async Task Zero_total_usage_emits_no_context_usage_but_still_emits_token_spend()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse("""{"choices":[{"delta":{"content":"hi"}}],"usage":{}}"""),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        Assert.Empty(session.Events.OfType<ProviderEvent.Usage>());
        Assert.Empty(session.Events.OfType<ProviderEvent.TokenSpend>()); // total 0 -> hasSpend false too
    }

    // ---- PR-PROVAPI-131 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-131")] // OpenAICompatibleSession.swift:767-774
    public async Task Only_the_first_choice_is_read_and_reasoning_content_wins_over_reasoning()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse(
                """{"choices":[{"delta":{"content":"a","reasoning_content":"r1","reasoning":"r2"}},{"delta":{"content":"ignored"}}]}"""),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        Assert.Equal("a", session.Events.OfType<ProviderEvent.MessageCompleted>().Single().Text);
        Assert.Equal("r1", session.Events.OfType<ProviderEvent.ReasoningCompleted>().Single().Text);
    }

    // ---- PR-PROVAPI-133, PR-PROVAPI-134, PR-PROVAPI-135 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-133")] // OpenAICompatibleSession.swift:775-781, :683-688
    [Trait("Req", "PR-PROVAPI-134")] // OpenAICompatibleSession.swift:724-733
    public async Task Tool_calls_accumulate_per_index_ordered_and_unnamed_ones_are_dropped()
    {
        var raw =
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"id":"call-1","function":{"name":"read_file","arguments":"{\"pa"}}]}}]}""" + "\n\n" +
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"unnamed"}}]}}]}""" + "\n\n" + // index 0: no name -> dropped
            """data: {"choices":[{"delta":{"tool_calls":[{"index":1,"function":{"arguments":"th\":\"a\"}"}}]}}]}""" + "\n\n" + // index 1: arguments concatenated
            "data: [DONE]\n\n";
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => new FakeResponse(200, [("Content-Type", "text/event-stream")], [], [raw]),
        };
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var round = await session.PerformStreamForTests("deepseek-chat");

        var call = Assert.Single(round.ToolCalls); // index 0 had no name and was dropped
        Assert.Equal("call-1", call.Id);
        Assert.Equal("read_file", call.Name);
        Assert.Equal("""{"path":"a"}""", call.Arguments); // fragments concatenated in arrival order
    }

    // ---- PR-PROVAPI-128, PR-PROVAPI-129 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-128")] // OpenAICompatibleSession.swift:749-752, :693-694
    [Trait("Req", "PR-PROVAPI-129")] // OpenAICompatibleSession.swift:621, :711-717 - status code is always 200 for an in-stream error
    public async Task In_stream_error_becomes_the_rounds_error_mapped_with_status_200()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse("""{"error":{"message":"invalid request"}}"""),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.Equal("invalid request", completed.Error);
        Assert.Equal(200, session.LastFriendlyErrorStatusCode);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-128")] // OpenAICompatibleSession.swift:750 - a message-less error uses the value's display text
    public async Task Error_object_with_no_message_field_uses_the_display_text()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse("""{"error":{"code":"boom"}}"""),
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        await RunSend(runtime, session, MakeTurn());

        var completed = Assert.Single(session.Events.OfType<ProviderEvent.TurnCompleted>());
        Assert.Equal(TurnStatus.Failed, completed.Status);
        Assert.NotNull(completed.Error);
        Assert.NotEqual("", completed.Error);
    }

    // ---- PR-PROVAPI-132, PR-PROVAPI-135 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-132")] // OpenAICompatibleSession.swift:152-227 - text and reasoning delta ordering is preserved
    [Trait("Req", "PR-PROVAPI-135")] // OpenAICompatibleSession.swift:610-611, :718-723, :1332-1335
    public async Task Each_round_mints_its_own_message_and_thought_ids_and_completion_carries_the_whole_text()
    {
        await using var server = new FakeUpstreamServer
        {
            Handler = _ => SseResponse("""{"choices":[{"delta":{"content":"a","reasoning_content":"r"}}]}""", """{"choices":[{"delta":{"content":"b"}}]}"""),
        };
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();

        var first = await session.PerformStreamForTests("deepseek-chat");
        var second = await session.PerformStreamForTests("deepseek-chat");
        Assert.Equal("ab", first.Content);
        Assert.Equal("ab", second.Content);

        var messageIds = session.Events.OfType<ProviderEvent.MessageCompleted>().Select(e => e.Id).ToList();
        Assert.Equal(2, messageIds.Count);
        Assert.NotEqual(messageIds[0], messageIds[1]); // fresh ids each round, :610
        Assert.StartsWith("message-", messageIds[0], StringComparison.Ordinal);
        // The fake server replays the same full SSE body for each request, so both rounds stream
        // the same reasoning text; what matters here is that each round mints its own thought id.
        var thoughtIds = session.Events.OfType<ProviderEvent.ReasoningCompleted>().Select(e => e.Id).ToList();
        Assert.Equal(2, thoughtIds.Count);
        Assert.NotEqual(thoughtIds[0], thoughtIds[1]);
        Assert.StartsWith("thought-", thoughtIds[0], StringComparison.Ordinal);
        Assert.All(session.Events.OfType<ProviderEvent.ReasoningCompleted>(), e => Assert.Equal("r", e.Text));
    }

    // ---- PR-PROVAPI-118 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-118")] // OpenAICompatibleSession.swift:508-510 - history is repaired (sanitized) before every request
    public async Task History_is_sanitized_before_every_request_even_with_an_orphaned_tool_message()
    {
        FakeRequest? seen = null;
        await using var server = new FakeUpstreamServer
        {
            Handler = r => { seen = r; return SseResponse("""{"choices":[{"delta":{"content":"ok"}}]}"""); },
        };
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri($"{server.BaseUrl}/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        // Inject an orphaned tool message directly (no preceding assistant tool_calls entry).
        var messagesField = typeof(OpenAICompatibleSession).GetField("messages", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var messages = (List<JSONValue>)messagesField.GetValue(session)!;
        messages.Add(JSONValue.FromDictionaryLiteral(("role", "tool"), ("tool_call_id", "orphan-1"), ("content", "leftover")));
        await RunSend(runtime, session, MakeTurn());

        var body = JSONValue.Parse(seen!.Body)!;
        var roles = body["messages"]!.JSONValueArray!.Select(m => m["role"]!.JSONValueString).ToList();
        Assert.DoesNotContain("tool", roles); // the orphan was dropped by SanitizeHistory, never sent
    }
}
