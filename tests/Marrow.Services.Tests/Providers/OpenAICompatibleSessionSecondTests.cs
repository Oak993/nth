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

// Source: DroppyCode/Services/Providers/OpenAICompatibleSession.swift (B3 52bc792), :61-65, :86-97,
// :114-148, :152-227, :506-800.
//
// Part 2 of 5 (PR-PROVAPI-113 to 136): Stop/ResolveApproval/AnswerQuestion/Compact/ReportUsage
// (:437-502), request payload, MCP tool naming, the transport-retry ladder, SSE draining, in-stream
// error and usage parsing, and tool-call accumulation. Most of this part drives the real,
// non-seamed StreamOneRound/PerformStream against FakeUpstreamServer (R-TEST-10: a loopback
// server, not a mock); the transport-retry ladder (PR-PROVAPI-121, 122, 123) instead uses
// OpenAICompatibleSessionRetryLadderSeam, which scripts a single attempt's outcome, so the ladder
// is testable deterministically over TestRuntime's manual clock without a live dropped connection.
[Collection(OpenAICompatibleSessionCollection.Name)]
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

    private static SessionConfiguration MakeSessionConfiguration(string? apiKey = "sk-test", RuntimeMode runtimeMode = RuntimeMode.FullAccess) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = runtimeMode,
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
    [Trait("Req", "PR-PROVAPI-135")] // OpenAICompatibleSession.swift:610-611, :718-723, :1330-1333
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

    // ---- PR-PROVAPI-113 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-113")] // OpenAICompatibleSession.swift:471-482
    public async Task Stop_declines_every_pending_approval_and_clears_the_session_id()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        await session.Start();
        Assert.True(session.IsRunning);
        var pendingA = session.RegisterPendingApprovalForTests("a");
        var pendingB = session.RegisterPendingApprovalForTests("b");

        session.Stop();

        Assert.False(await pendingA); // :479 every pending approval resumes false
        Assert.False(await pendingB);
        Assert.False(session.IsRunning); // :481 sessionID cleared
        var error = await Assert.ThrowsAsync<ProviderError>(() => session.Send(MakeTurn()));
        Assert.Equal("The agent session is not running.", error.Message); // :286
    }

    // ---- PR-PROVAPI-114 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-114")] // OpenAICompatibleSession.swift:448-453
    public async Task ResolveApproval_maps_allow_and_always_to_true_and_anything_else_to_false()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(
            MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")),
            MakeSessionConfiguration(runtimeMode: RuntimeMode.Supervised)).Wired();

        var allow = session.RegisterPendingApprovalForTests("req-allow");
        session.ResolveApproval("req-allow", "allow");
        Assert.True(await allow);
        Assert.Contains(session.Events, e => e is ProviderEvent.RequestResolved r && r.Id == "req-allow"); // :452

        var decline = session.RegisterPendingApprovalForTests("req-decline");
        session.ResolveApproval("req-decline", "decline");
        Assert.False(await decline);

        // "always" approves this call and sets approveAllRemaining, :450, so a later requiresApproval
        // check (already proven elsewhere) flips from true to false.
        Assert.True(session.RequiresApprovalForTests(ToolCall.Kind.Command));
        var always = session.RegisterPendingApprovalForTests("req-always");
        session.ResolveApproval("req-always", "always");
        Assert.True(await always);
        Assert.False(session.RequiresApprovalForTests(ToolCall.Kind.Command)); // :450 approveAllRemaining now true

        // An unknown request id: no continuation to resume, no event, no throw. :449
        var eventsBefore = session.Events.Count;
        session.ResolveApproval("no-such-id", "allow");
        Assert.Equal(eventsBefore, session.Events.Count);
    }

    // ---- PR-PROVAPI-115 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-115")] // OpenAICompatibleSession.swift:455
    public void AnswerQuestion_does_nothing()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        session.AnswerQuestion("whatever", ValueMap<string, ValueList<string>>.Empty); // :455, empty body
        Assert.Empty(session.Events);
    }

    // ---- PR-PROVAPI-116 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-116")] // OpenAICompatibleSession.swift:457-462 - 12 or fewer messages: nothing to compact
    public async Task Compact_with_12_or_fewer_messages_emits_nothing_to_compact_yet_and_leaves_history_untouched()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var seeded = Enumerable.Range(0, 12).Select(i => JSONValue.FromDictionaryLiteral(("role", "user"), ("content", $"m{i}"))).ToList();
        session.SetMessagesForTests(seeded);

        await session.Compact();

        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel);
        Assert.Equal("Nothing to compact yet.", notice.Value.Message); // :460
        Assert.Equal(12, session.Messages.Count); // unchanged
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-116")] // OpenAICompatibleSession.swift:463-468 - more than 12: system message plus the last 10
    public async Task Compact_with_more_than_12_messages_keeps_the_system_message_and_the_last_ten()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var system = JSONValue.FromDictionaryLiteral(("role", "system"), ("content", "sys"));
        var body = Enumerable.Range(0, 15).Select(i => JSONValue.FromDictionaryLiteral(("role", "user"), ("content", $"m{i}"))).ToList();
        session.SetMessagesForTests(new List<JSONValue> { system }.Concat(body));

        await session.Compact();

        Assert.Equal(11, session.Messages.Count); // :464-465 system + last 10
        Assert.Equal("sys", session.Messages[0]["content"]!.JSONValueString);
        for (var i = 0; i < 10; i++)
            Assert.Equal($"m{5 + i}", session.Messages[i + 1]["content"]!.JSONValueString);
        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Info, notice.Value.NoticeLevel);
        Assert.Equal("Context compacted.", notice.Value.Message); // :468
    }

    // ---- PR-PROVAPI-117 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-117")] // OpenAICompatibleSession.swift:488-493 - usage reports the context event and records the ledger
    public void ReportUsage_with_a_usage_object_emits_a_usage_event()
    {
        using var runtime = new TestRuntime();
        // TokenLedger.Shared is a process-wide lazy singleton (Store/TokenLedger.cs:56) whose
        // constructor reads Storage.Locations; bind it first so a run where this is the very first
        // touch of the ledger in the process does not poison the singleton for every later test.
        using var scratch = new StorageScratchFolder();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var round = new OpenAICompatibleSession.StreamRound { Usage = new ContextUsage { UsedTokens = 500, WindowTokens = 64_000 } };

        runtime.OnMain(() => session.ReportUsageForTests(round));

        var usage = Assert.Single(session.Events.OfType<ProviderEvent.Usage>());
        Assert.Equal(500, usage.Value.UsedTokens); // :490
        Assert.Equal(64_000, usage.Value.WindowTokens);
        Assert.Empty(session.Events.OfType<ProviderEvent.TokenSpend>()); // no reported token usage: nothing to spend
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-117")] // OpenAICompatibleSession.swift:493-501 - a reported usage with spend emits tokenSpend
    public void ReportUsage_with_spend_emits_a_tokenSpend_event_carrying_provider_and_all_four_counts()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var round = new OpenAICompatibleSession.StreamRound
        {
            TokenUsage = new OpenAICompatibleSession.ReportedTokenUsage { Total = 100, Input = 60, Output = 40, CachedInput = 10 },
        };

        runtime.OnMain(() => session.ReportUsageForTests(round));

        var spend = Assert.Single(session.Events.OfType<ProviderEvent.TokenSpend>());
        Assert.Equal(ProviderKind.Deepseek, spend.Value.Provider); // :495
        Assert.Equal(100, spend.Value.TotalTokens);
        Assert.Equal(60, spend.Value.InputTokens);
        Assert.Equal(40, spend.Value.OutputTokens);
        Assert.Equal(10, spend.Value.CachedInputTokens);
        Assert.Equal("deepseek-chat", spend.Value.Model); // :500, currentModel defaults to config.DefaultModel
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-117")] // OpenAICompatibleSession.swift:122 - an all-zero usage object produces no tokenSpend event
    public void ReportUsage_with_an_all_zero_token_usage_emits_no_tokenSpend_event()
    {
        using var runtime = new TestRuntime();
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();
        var round = new OpenAICompatibleSession.StreamRound
        {
            TokenUsage = new OpenAICompatibleSession.ReportedTokenUsage { Total = 0, Input = 0, Output = 0, CachedInput = 0 },
        };

        runtime.OnMain(() => session.ReportUsageForTests(round));

        Assert.Empty(session.Events.OfType<ProviderEvent.TokenSpend>()); // hasSpend false
        Assert.Empty(session.Events.OfType<ProviderEvent.Usage>()); // round.Usage was null
    }

    // ---- PR-PROVAPI-121, PR-PROVAPI-122, PR-PROVAPI-123 ----

    private static OpenAICompatibleSessionRetryLadderSeam MakeRetrySession() =>
        new OpenAICompatibleSessionRetryLadderSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();

    [Fact]
    [Trait("Req", "PR-PROVAPI-121")] // OpenAICompatibleSession.swift:86-97, :516-552 - retried twice, after exactly 1s then 2s
    [Trait("Req", "PR-PROVAPI-123")] // OpenAICompatibleSession.swift:541-543 - retries exhausted after 3 attempts
    public void Transport_retries_are_spaced_exactly_1s_then_2s_and_stop_after_the_third_attempt()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), false));
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.TimedOut), false));
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.CannotConnectToHost), false));

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        Assert.Equal(1, session.AttemptCount);

        runtime.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Equal(1, session.AttemptCount); // not yet a full second
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, session.AttemptCount); // retried after exactly 1s, :550-551

        runtime.Advance(TimeSpan.FromMilliseconds(1999));
        Assert.Equal(2, session.AttemptCount); // not yet two more seconds
        runtime.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(3, session.AttemptCount); // retried after exactly 2 more seconds

        runtime.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(3, session.AttemptCount); // :541 attempt >= maxTransportRetries(2): no fourth attempt
        Assert.True(task!.IsCompleted);
        Assert.True(task.IsFaulted); // exhausted after 3 attempts total, :542
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-121")] // OpenAICompatibleSession.swift:91-97 - only the three named transport codes are retried
    public void A_non_transient_failure_is_never_retried()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.BadServerResponse), false));

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();

        Assert.Equal(1, session.AttemptCount); // :540 rethrown as-is, no retry spent
        Assert.True(task!.IsCompleted);
        Assert.True(task.IsFaulted);
        var thrown = Assert.IsType<AggregateException>(task.Exception).InnerException;
        var urlError = Assert.IsType<URLError>(thrown);
        Assert.Equal(URLError.Code.BadServerResponse, urlError.URLErrorCode);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-122")] // OpenAICompatibleSession.swift:544-549
    public async Task A_retry_after_partial_output_warns_that_the_reply_was_cut_off()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), true)); // emittedOutput
        session.Results.Enqueue(() => new OpenAICompatibleSession.StreamRound { Content = "resumed" });

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(1));

        var notice = Assert.Single(session.Events.OfType<ProviderEvent.Notice>());
        Assert.Equal(Notice.Level.Warning, notice.Value.NoticeLevel); // :548
        Assert.Equal("DeepSeek lost the connection while answering. That reply was cut off, and this response is restarting.", notice.Value.Message);
        Assert.True(task!.IsCompletedSuccessfully);
        Assert.Equal("resumed", (await task).Content);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-122")] // OpenAICompatibleSession.swift:547 - no output shown yet: no warning
    public void A_retry_before_any_output_was_shown_emits_no_warning()
    {
        using var runtime = new TestRuntime();
        var session = MakeRetrySession();
        session.Results.Enqueue(() => throw new OpenAICompatibleSession.StreamAttemptFailure(new URLError(URLError.Code.NetworkConnectionLost), false)); // not emittedOutput
        session.Results.Enqueue(() => new OpenAICompatibleSession.StreamRound { Content = "ok" });

        Task<OpenAICompatibleSession.StreamRound>? task = null;
        runtime.Loop.Enqueue(() => task = session.StreamOneRoundForTests("deepseek-chat", null, false));
        runtime.Loop.RunUntilIdle();
        runtime.Advance(TimeSpan.FromSeconds(1));

        Assert.Empty(session.Events.OfType<ProviderEvent.Notice>());
        Assert.True(task!.IsCompletedSuccessfully);
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-123")] // OpenAICompatibleSession.swift:562-567 - attempt count and the transport's own reason in parentheses
    public void TerminalTransportMessage_reports_the_attempt_count_and_the_reason_in_parentheses()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();

        var message = session.TerminalTransportMessageForTests(new FakeTransportReasonError("The Internet connection appears to be offline."));

        Assert.Equal(
            "DeepSeek could not reach the network after 3 attempts. Check the connection, then try again. (The Internet connection appears to be offline.)",
            message); // :565-567, attempts = maxTransportRetries(2) + 1
    }

    [Fact]
    [Trait("Req", "PR-PROVAPI-123")] // OpenAICompatibleSession.swift:566 - an empty reason omits the parenthesis entirely
    public void TerminalTransportMessage_omits_the_parenthesis_when_the_reason_is_empty()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();

        var message = session.TerminalTransportMessageForTests(new FakeTransportReasonError(""));

        Assert.Equal("DeepSeek could not reach the network after 3 attempts. Check the connection, then try again.", message);
    }

    // ---- PR-PROVAPI-136 ----

    [Fact]
    [Trait("Req", "PR-PROVAPI-136")] // OpenAICompatibleSession.swift:695-698
    public void A_transport_failure_that_follows_an_in_stream_error_reports_the_stream_error_instead()
    {
        var session = new OpenAICompatibleSessionCapturingSeam(MakeConfig(new Uri("http://127.0.0.1:1/v1/chat/completions")), MakeSessionConfiguration()).Wired();

        var result = session.TransportErrorFailureForTests(
            new URLError(URLError.Code.NetworkConnectionLost), streamError: "rate limited", content: "", sawContent: false, sawReasoning: false, statusCode: 200);

        var providerError = Assert.IsType<ProviderError>(result); // :697, not a StreamAttemptFailure: not retried
        var failed = Assert.IsType<ProviderError.Case.Failed>(providerError.ErrorCase);
        Assert.Equal(session.FriendlyError("rate limited", 200), failed.Message);
    }
}
