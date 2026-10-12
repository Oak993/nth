using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W25 (DESIGN.md 7.3 ask_user: "blocks the turn for the answer, then returns it to the model", for every
// provider). CLI providers reach it over the nth-ui server, API providers as a native tool; both are offered only while a host
// for the chat is bound, and a question that waits never holds up the server's other calls.
[Trait("Decision", "ED-NEW-web-w25-1")]
public sealed class AskUserWiringTests
{
    private static readonly Guid Chat = Guid.Parse("5d0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d41");

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///C:/ask-test")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = "m",
        IgnisThreadID = thread,
    };

    private static async Task<JsonNode> Mcp(UiMcpServer server, string method, object? parameters = null, int id = 9)
    {
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        return JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
    }

    private static readonly object Question = new
    {
        questions = new[] { new { question = "Which database?", type = "single", options = new[] { "PostgreSQL", "SQLite" } } },
    };

    [Fact]
    public async Task The_server_lists_ask_user_for_a_chat_once_a_host_is_bound_even_with_artifacts_off()
    {
        var silent = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        Assert.Empty((await Mcp(silent, "tools/list"))["result"]!["tools"]!.AsArray());
        Assert.Null(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));

        using var scope = new AskUserScope();
        var registration = UiMcpBridge.For(Config(ProviderKind.Claude, Chat));
        Assert.NotNull(registration);
        var server = new UiMcpServer(registration);
        var tools = (await Mcp(server, "tools/list"))["result"]!["tools"]!.AsArray();
        var tool = Assert.Single(tools)!;
        Assert.Equal("ask_user", tool["name"]!.GetValue<string>());
        Assert.Equal(AskUserTool.Description, tool["description"]!.GetValue<string>());
        Assert.Equal(AskUserTool.InputSchema().ToJsonString(), tool["inputSchema"]!.ToJsonString());
        Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.Equal(["ask_user"], UiMcpServer.Offered);
        Assert.True(UiMcpServer.IsOwnTool("mcp__nth-ui__ask_user"));
        Assert.Null(UiMcpBridge.For(Config(ProviderKind.Claude, null)));

        var artifact = await Mcp(server, "tools/call", new { name = "artifact_list", arguments = new { } });
        Assert.Equal(-32602, artifact["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task With_artifacts_on_ask_user_comes_last()
    {
        using var artifacts = new ArtifactHostScope();
        using var ask = new AskUserScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Codex, Chat)));
        var names = (await Mcp(server, "tools/list"))["result"]!["tools"]!.AsArray().Select(node => node!["name"]!.GetValue<string>());
        Assert.Equal(["artifact_create", "artifact_update", "artifact_read", "artifact_list", "ui_reference", "ask_user"], names);
        Assert.Equal(UiMcpServer.ToolNames, UiMcpServer.Offered);
        var instructions = (await Mcp(server, "initialize", new { protocolVersion = "2025-06-18" }))["result"]!["instructions"]!.GetValue<string>();
        Assert.Equal(UiMcpServer.Instructions, instructions);
        Assert.True(UiTeaching.Options(UiMcpServer.Offered).AskUser);
        Assert.Contains("ask_user", instructions, StringComparison.Ordinal);
        Assert.InRange(NthUiTeaching.Core(UiTeaching.Options(UiMcpServer.Offered)).Length, 1_200, NthUiTeaching.McpCoreLimit);
    }

    [Fact]
    public async Task A_call_waits_for_the_person_and_a_ping_is_answered_meanwhile()
    {
        using var scope = new AskUserScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        var call = Mcp(server, "tools/call", new { name = "ask_user", arguments = Question }, id: 41);
        await scope.Host.WaitForAsked(1);
        Assert.False(call.IsCompleted);
        Assert.Equal(1, scope.Host.Waiting);
        Assert.Equal(JsonNode.Parse("{}")!.ToJsonString(), (await Mcp(server, "ping", id: 42))["result"]!.ToJsonString());

        scope.Host.Answer(0, ("q1", "PostgreSQL"));
        var reply = await call;
        Assert.Equal(41, reply["id"]!.GetValue<int>());
        var result = reply["result"]!;
        Assert.False(result["isError"]!.GetValue<bool>());
        Assert.Equal("The user answered your question.\n\nQuestion 1: Which database?\nAnswer 1: PostgreSQL", result["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("PostgreSQL", result["structuredContent"]!["answers"]![0]!["answer"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task A_closed_connection_ends_the_wait_without_closing_the_card()
    {
        using var scope = new AskUserScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        using var closed = new CancellationTokenSource();
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 5, method = "tools/call", @params = new { name = "ask_user", arguments = Question } });
        var call = server.Handle(line, closed.Token);
        await scope.Host.WaitForAsked(1);
        await closed.CancelAsync();
        var reply = JsonNode.Parse((await call)!)!;
        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal(AskState.Waiting, scope.Host.Asked[0].State);
    }

    [Fact]
    public async Task A_bad_call_is_an_error_result_and_nothing_is_shown()
    {
        using var scope = new AskUserScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        var reply = await Mcp(server, "tools/call", new { name = "ask_user", arguments = new { questions = Array.Empty<object>() } });
        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("questions is empty", reply["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Empty(scope.Host.Asked);
    }

    [Fact]
    public async Task A_chat_with_no_host_of_its_own_gets_the_error_and_other_tools_are_unaffected()
    {
        using var scope = new AskUserScope(everyChat: false);
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        var reply = await Mcp(server, "tools/call", new { name = "ask_user", arguments = Question });
        Assert.True(reply["result"]!["isError"]!.GetValue<bool>());
        Assert.Contains("cannot show a question", reply["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task API_providers_get_ask_user_as_a_function_tool_and_the_loop_draws_no_row_for_it()
    {
        var off = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        Assert.False(off.Owns("ask_user"));

        using var scope = new AskUserScope();
        var tools = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        Assert.True(tools.Owns("ask_user"));
        var definition = Assert.Single(tools.Definitions, d => d["function"]!["name"]!.JSONValueString == "ask_user");
        Assert.Equal(AskUserTool.Description, definition["function"]!["description"]!.JSONValueString);
        Assert.Equal(AskUserTool.InputSchema().ToJsonString(), JsonNode.Parse(definition["function"]!["parameters"]!.CompactString)!.ToJsonString());
        Assert.True(NativeSkillTools.HidesRow("ask_user"));
        Assert.False(NativeSkillTools.HidesRow("artifact_create"));
        Assert.Equal(AskUserTool.ProviderRowTitle, NativeSkillTools.Title("ask_user", JSONValue.Parse("{}")!));

        var arguments = JSONValue.Parse("""{"questions":[{"question":"Which database?","options":["PostgreSQL","SQLite"]}]}""")!;
        var run = tools.Execute("ask_user", arguments);
        await scope.Host.WaitForAsked(1);
        scope.Host.Answer(0, ("q1", "SQLite"));
        var result = await run;
        Assert.False(result.Failed);
        Assert.Contains("Answer 1: SQLite", result.Output, StringComparison.Ordinal);
        var block = tools.UiTeachingBlock()!;
        Assert.Contains("call ask_user", block, StringComparison.Ordinal);
    }

    [Fact]
    public void Codex_is_told_to_let_an_nth_ui_call_wait_for_a_person_and_only_that_server()
    {
        using var scope = new AskUserScope();
        var server = MCPProviderConfig.UiServer(Config(ProviderKind.Codex, Chat))!;
        var connected = JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));
        var codex = MCPProviderConfig.CodexServers(connected, null, server)!;
        Assert.Equal(AskUserTool.ToolTimeoutSeconds, codex["nth-ui"]!["tool_timeout_sec"]!.JSONValueInt);
        Assert.Equal(86400, AskUserTool.ToolTimeoutSeconds);
        Assert.Equal(server.Args.ToArray(), codex["nth-ui"]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!)); // nothing else of the entry changes
        Assert.Null(codex["github"]!["tool_timeout_sec"]); // the person's own servers keep Codex's limit
        Assert.Same(connected, MCPProviderConfig.CodexServers(connected, null, null));
    }

    [Fact]
    public void Every_CLI_shape_carries_the_nth_ui_relay_entry_when_only_ask_user_is_bound()
    {
        Assert.Null(MCPProviderConfig.UiServer(Config(ProviderKind.Codex, Chat)));
        using var scope = new AskUserScope();
        var entry = MCPProviderConfig.UiServer(Config(ProviderKind.Codex, Chat));
        Assert.NotNull(entry);
        Assert.Equal("nth-ui", entry!.Id);
        Assert.Equal(UiMcpBridge.Command, entry.Args[0]);
    }
}
