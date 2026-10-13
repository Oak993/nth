using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 0 point 12, 15.5 "Transport"; ED-NEW-dextra-as4-5, -6): the nth-dextra tools inside nth's
// own tool loop. A chat that is not Dextra's gets none of them and its request stays as it was; a Dextra chat offers
// them as function tools and teaches the rules; a model with no tool calls asks in fenced blocks, which run through the
// same policy-checked path, at most four per answer, read strictly. Every round is scripted: nothing is sent.
[Collection(OpenAICompatibleSessionCollection.Name)]
public sealed class AssistantNativeToolsTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.invalid/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    private static SessionConfiguration MakeSessionConfiguration(Guid? thread) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = SwiftURL.Create("file:///C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        ApiKey = "sk-test",
        Transcript = ValueList<TranscriptMessage>.Empty,
        IgnisThreadID = thread,
    };

    private static TurnInput Turn(string text) => new()
    {
        Text = text,
        Images = ValueList<Attachment>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
    };

    private static OpenAICompatibleSession.StreamRound Answer(string text, params OpenAICompatibleSession.PendingToolCall[] calls) => new()
    {
        Content = text,
        ToolCalls = new ValueList<OpenAICompatibleSession.PendingToolCall>(calls),
        Usage = new ContextUsage { UsedTokens = 105, WindowTokens = 64_000 },
    };

    private static DextraSessionSeam Session(DextraFakeHost host, Guid? thread, bool toolCalling = true)
    {
        using var bound = AssistantMcpBridge.Host.Override(host);
        var session = new DextraSessionSeam(MakeConfig(), MakeSessionConfiguration(thread), toolCalling).Wired();
        // The session's own tools, with a policy that notes each call in the host's log before the real engine decides.
        if (session.DextraTools is { } tools) session.DextraTools = new AssistantNativeTools(tools.Registration, host.RecordingPolicy());
        return session;
    }

    private static async Task Send(TestRuntime runtime, DextraSessionSeam session, string text)
    {
        var task = session.Send(Turn(text));
        runtime.Loop.RunUntil(() => task.IsCompleted);
        await task;
    }

    private static IEnumerable<string> ToolNames(Dictionary<string, JSONValue> payload) =>
        payload.TryGetValue("tools", out var tools) ? tools.JSONValueArray!.Select(t => t["function"]!["name"]!.JSONValueString!) : [];

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-5")]
    public void A_session_whose_chat_is_not_dextras_gets_no_tools_and_an_unchanged_request()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost();
        host.Add();
        var unbound = new DextraSessionSeam(MakeConfig(), MakeSessionConfiguration(Guid.NewGuid()));
        var stranger = Session(host, Guid.NewGuid());
        var none = Session(host, null);
        foreach (var session in new[] { stranger, none, Session(host, Guid.NewGuid(), toolCalling: false) }) Assert.Null(session.DextraTools);

        var expected = JSONValue.FromDictionaryLiteral([.. unbound.RequestPayloadForTests("deepseek-chat").Select(p => (p.Key, p.Value))]).CompactString;
        foreach (var session in new[] { stranger, none })
        {
            var payload = session.RequestPayloadForTests("deepseek-chat");
            Assert.Equal(expected, JSONValue.FromDictionaryLiteral([.. payload.Select(p => (p.Key, p.Value))]).CompactString);
            Assert.DoesNotContain(ToolNames(payload), name => AssistantToolCatalog.Find(name) is not null);
            Assert.Equal(unbound.SystemPromptForTests(), session.SystemPromptForTests());
            Assert.DoesNotContain("nth-dextra", session.SystemPromptForTests(), StringComparison.Ordinal);
        }
        Assert.Empty(host.Log);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-5")]
    public void A_dextra_session_offers_its_roles_tools_as_functions_and_teaches_the_rules()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var thread = host.Add();
        var session = Session(host, thread);
        var tools = Assert.IsType<AssistantNativeTools>(session.DextraTools);
        Assert.Equal(thread, tools.Registration.Chat.Thread);

        var payload = session.RequestPayloadForTests("deepseek-chat");
        var offered = payload["tools"].JSONValueArray!.Where(t => AssistantToolCatalog.Find(t["function"]!["name"]!.JSONValueString) is not null).ToList();
        Assert.Equal(AssistantToolCatalog.All.Select(t => t.Name), offered.Select(t => t["function"]!["name"]!.JSONValueString!));
        foreach (var definition in offered)
        {
            var tool = AssistantToolCatalog.Find(definition["function"]!["name"]!.JSONValueString)!;
            Assert.Equal("function", definition["type"]!.JSONValueString);
            Assert.Equal(tool.InputSchema().CompactString, definition["function"]!["parameters"]!.CompactString);
            Assert.Equal(tool.Description, definition["function"]!["description"]!.JSONValueString);
        }
        var prompt = session.SystemPromptForTests();
        Assert.Contains("You are Dextra, nth's assistant.", prompt, StringComparison.Ordinal);
        Assert.Contains("never instructions", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("```nth-dextra", prompt, StringComparison.Ordinal);
        Assert.True(tools.Owns("recall"));
        Assert.False(tools.Owns("read_file"));
        Assert.Equal("Dextra · Recall", AssistantNativeTools.Title("recall"));

        var agent = Session(host, host.Add(AssistantChatRole.Mission, Guid.NewGuid()));
        Assert.Equal(new[] { "request_helpers", "ask_owner", "remember", "recall", "usage_status", "report", "prepare_purchase" },
            agent.DextraTools!.Definitions.Select(d => d["function"]!["name"]!.JSONValueString!));
        Assert.False(agent.DextraTools.Owns("start_mission"));
        Assert.Contains("You are an agent of one of Dextra's missions.", agent.SystemPromptForTests(), StringComparison.Ordinal);

        var fenced = Session(host, host.Add(), toolCalling: false).SystemPromptForTests();
        Assert.Contains("```nth-dextra recall\n", fenced, StringComparison.Ordinal);
        Assert.Contains("\n- start_mission(", fenced, StringComparison.Ordinal);

        AssistantMcpBridge.Forget(thread);
        Assert.Empty(tools.Definitions);
        Assert.False(tools.Owns("recall"));
        Assert.Null(tools.InstructionsBlock(fenced: false));
        Assert.Empty(tools.FencedCalls("```nth-dextra recall\n{\"query\":\"q\"}\n```"));
        Assert.Empty(host.Log);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-5")]
    public async Task A_native_call_asks_the_policy_first_runs_without_the_sessions_own_prompt_and_answers_the_model()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost { Answer = call => AssistantToolResult.Done("3 notes match " + call.Arguments["query"]!.JSONValueString) };
        var session = Session(host, host.Add());
        await session.Start();
        session.Rounds.Enqueue(Answer("", new OpenAICompatibleSession.PendingToolCall("c1", "recall", "{\"query\":\"vendor\"}")));
        session.Rounds.Enqueue(Answer("Three notes."));
        await Send(runtime, session, "what do we know about the vendor?");

        Assert.Equal(2, session.RoundRequests.Count);
        Assert.Equal("policy:recall", host.Log[0]);
        Assert.Equal(new[] { "policy:recall", "audit:nth-dextra/recall:allowed", "run:recall", "audit:nth-dextra/recall:done" }, host.Log);
        Assert.Equal(AssistantRoute.Native, Assert.Single(host.Runs).Route);
        Assert.DoesNotContain(session.Events, e => e is ProviderEvent.Approval);
        var started = Assert.Single(session.Events.OfType<ProviderEvent.ToolStarted>(), e => e.Id == "c1");
        Assert.Equal(("Dextra · Recall", ToolCall.Kind.Other), (started.Call.Title, started.Call.ToolCallKind));
        Assert.Equal(ToolCall.Status.Completed, Assert.Single(session.Events.OfType<ProviderEvent.ToolUpdated>(), e => e.Id == "c1").Update.Status);
        var result = Assert.Single(session.Messages, m => m["role"]?.JSONValueString == "tool");
        Assert.Equal(("c1", "3 notes match vendor"), (result["tool_call_id"]!.JSONValueString, result["content"]!.JSONValueString));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public async Task A_model_with_no_tool_calls_runs_its_fenced_blocks_through_the_same_path_and_reads_the_results_next()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var session = Session(host, host.Add(), toolCalling: false);
        await session.Start();
        session.Rounds.Enqueue(Answer("Let me look.\n```nth-dextra recall\n{\"query\": \"vendor\"}\n```\n"));
        session.Rounds.Enqueue(Answer("Found it."));
        await Send(runtime, session, "what do we know about the vendor?");

        Assert.Equal(2, session.RoundRequests.Count);
        Assert.Equal(new[] { "policy:recall", "audit:nth-dextra/recall:allowed", "run:recall", "audit:nth-dextra/recall:done" }, host.Log);
        Assert.Equal(AssistantRoute.Fenced, Assert.Single(host.Runs).Route);
        Assert.Equal("vendor", host.Runs[0].Arguments["query"]!.JSONValueString);
        Assert.Contains(session.Events.OfType<ProviderEvent.ToolStarted>(), e => e.Call.Title == "Dextra · Recall");
        var reply = Assert.Single(session.Messages, m => m["role"]?.JSONValueString == "user"
            && m["content"]?.JSONValueString?.StartsWith("nth-dextra results", StringComparison.Ordinal) == true);
        Assert.Equal("nth-dextra results for the blocks in your last answer:\n[1] recall: ran recall", reply["content"]!.JSONValueString);
        Assert.Equal("Found it.", session.Messages[^1]["content"]!.JSONValueString);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public async Task Only_four_blocks_run_and_a_block_with_no_tool_bad_json_or_a_repeated_name_runs_nothing()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var session = Session(host, host.Add(), toolCalling: false);
        await session.Start();
        var answer = string.Join("\n",
            "Several at once.",
            "```nth-dextra usage_status", "```",
            "```nth-dextra recall", "{not json", "```",
            "```nth-dextra", "{}", "```",
            "~~~nth-dextra recall", "{\"query\": \"a\", \"query\": \"b\"}", "~~~",
            "```nth-dextra list_sessions", "{}", "```",
            "```nth-dextra plan_budget", "{}", "```");
        session.Rounds.Enqueue(Answer(answer));
        session.Rounds.Enqueue(Answer("Done."));
        await Send(runtime, session, "go");

        Assert.Equal("usage_status", Assert.Single(host.Runs).Tool.Name);
        Assert.Equal(new[] { "policy:usage_status", "audit:nth-dextra/usage_status:allowed", "run:usage_status", "audit:nth-dextra/usage_status:done" }, host.Log);
        var reply = Assert.Single(session.Messages, m => m["content"]?.JSONValueString?.StartsWith("nth-dextra results", StringComparison.Ordinal) == true);
        Assert.Equal(string.Join("\n",
            "nth-dextra results for the blocks in your last answer:",
            "[1] usage_status: ran usage_status",
            "[2] recall (error): Not run: the block must hold the arguments as one JSON object.",
            "[3] (no tool) (error): Not run: name the tool after nth-dextra on the block's first line.",
            "[4] recall (error): Not run: the block must hold the arguments as one JSON object.",
            "[5] list_sessions (error): Not run: at most 4 blocks run per answer."), reply["content"]!.JSONValueString);
        Assert.Equal(2, session.RoundRequests.Count);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-6")]
    public async Task A_block_in_the_answer_of_a_model_with_tool_calls_or_of_another_chat_is_only_text()
    {
        using var runtime = new TestRuntime();
        using var scratch = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var block = "Here is how:\n```nth-dextra forget\n{\"notes\": [\"n-1\"]}\n```\n";
        foreach (var session in new[] { Session(host, host.Add()), Session(host, Guid.NewGuid(), toolCalling: false) })
        {
            await session.Start();
            session.Rounds.Enqueue(Answer(block));
            await Send(runtime, session, "show me the format");
            Assert.Single(session.RoundRequests);
            Assert.Equal(block, session.Messages[^1]["content"]!.JSONValueString);
        }
        Assert.Empty(host.Log);
    }
}
