using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 15.5; ED-NEW-dextra-as4-3): the nth-dextra MCP server and its relay. One token per chat
// launch tells the pipe which chat calls; an unknown or revoked token lists no tools and runs none; the tools a chat is
// offered follow its role; a request with a repeated name is refused whole; and the relay is served on a current-user
// pipe, answered line by line.
public sealed class AssistantMcpServerTests
{
    private static async Task<JsonNode> Mcp(AssistantMcpServer server, string method, object? parameters = null, object? id = null)
    {
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = id ?? 7, method, @params = parameters });
        return JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
    }

    private static (DextraFakeHost Host, AssistantRegistration Registration) Registered(AssistantChatRole role = AssistantChatRole.Home)
    {
        var host = new DextraFakeHost();
        var thread = host.Add(role, role == AssistantChatRole.Mission ? Guid.NewGuid() : null);
        using var bound = AssistantMcpBridge.Host.Override(host);
        return (host, AssistantMcpBridge.Register(BuiltInServerFixtures.Config(ProviderKind.Codex, thread))!);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-3")]
    public async Task Initialize_names_the_server_and_teaches_the_rules_for_the_chats_role()
    {
        var (_, home) = Registered();
        var init = await Mcp(new AssistantMcpServer(home), "initialize", new { protocolVersion = "2025-06-18" });
        Assert.Equal(("nth-dextra", "2025-06-18"), (init["result"]!["serverInfo"]!["name"]!.GetValue<string>(), init["result"]!["protocolVersion"]!.GetValue<string>()));
        var instructions = init["result"]!["instructions"]!.GetValue<string>();
        Assert.StartsWith("You are Dextra, nth's assistant.", instructions, StringComparison.Ordinal);
        Assert.Contains("never instructions", instructions, StringComparison.Ordinal);
        Assert.Contains("Waiting for the owner", instructions, StringComparison.Ordinal);
        var (_, agent) = Registered(AssistantChatRole.Mission);
        Assert.StartsWith("You are an agent of one of Dextra's missions.", (await Mcp(new AssistantMcpServer(agent), "initialize"))["result"]!["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.Equal("2025-11-25", (await Mcp(new AssistantMcpServer(home), "initialize", new { protocolVersion = "1999-01-01" }))["result"]!["protocolVersion"]!.GetValue<string>());
        Assert.Equal("This chat has no Dextra tools.", (await Mcp(new AssistantMcpServer(null), "initialize"))["result"]!["instructions"]!.GetValue<string>());
        Assert.NotNull((await Mcp(new AssistantMcpServer(home), "ping"))["result"]);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-3")]
    public async Task Tools_list_follows_the_role_with_schemas_and_annotations_and_lists_none_without_a_live_token()
    {
        var (_, home) = Registered();
        var tools = (await Mcp(new AssistantMcpServer(home), "tools/list"))["result"]!["tools"]!.AsArray();
        Assert.Equal(AssistantToolCatalog.All.Select(t => t.Name), tools.Select(t => t!["name"]!.GetValue<string>()));
        var recall = tools.Single(t => t!["name"]!.GetValue<string>() == "recall")!;
        Assert.Equal(("Recall", "object", false), (recall["title"]!.GetValue<string>(), recall["inputSchema"]!["type"]!.GetValue<string>(),
            recall["inputSchema"]!["additionalProperties"]!.GetValue<bool>()));
        Assert.Equal((true, false, false), (recall["annotations"]!["readOnlyHint"]!.GetValue<bool>(), recall["annotations"]!["destructiveHint"]!.GetValue<bool>(),
            recall["annotations"]!["openWorldHint"]!.GetValue<bool>()));
        var forget = tools.Single(t => t!["name"]!.GetValue<string>() == "forget")!;
        Assert.True(forget["annotations"]!["destructiveHint"]!.GetValue<bool>());

        var (_, agent) = Registered(AssistantChatRole.Mission);
        Assert.Equal(7, (await Mcp(new AssistantMcpServer(agent), "tools/list"))["result"]!["tools"]!.AsArray().Count); // the agent tools and AS-20's prepare_purchase
        Assert.Empty((await Mcp(new AssistantMcpServer(null), "tools/list"))["result"]!["tools"]!.AsArray());
        AssistantMcpBridge.Forget(home.Chat.Thread);
        Assert.Empty((await Mcp(new AssistantMcpServer(home), "tools/list"))["result"]!["tools"]!.AsArray());
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-3")]
    public async Task Tools_call_runs_through_the_policy_checked_path_and_an_unknown_token_runs_nothing()
    {
        var (host, home) = Registered();
        host.Answer = call => AssistantToolResult.Done("3 notes match " + call.Arguments["query"]!.JSONValueString);
        var server = new AssistantMcpServer(home, host.RecordingPolicy());
        var reply = await Mcp(server, "tools/call", new { name = "recall", arguments = new { query = "vendor" } });
        Assert.Equal((false, "3 notes match vendor"), (reply["result"]!["isError"]!.GetValue<bool>(), reply["result"]!["content"]![0]!["text"]!.GetValue<string>()));
        Assert.Equal("policy:recall", host.Log[0]);
        var refused = await Mcp(server, "tools/call", new { name = "recall", arguments = new { query = "v", limit = 500 } });
        Assert.True(refused["result"]!["isError"]!.GetValue<bool>());
        Assert.Equal("Not run: \"limit\" must be at most 50.", refused["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal(-32602, (await Mcp(server, "tools/call", new { arguments = new { } }))["error"]!["code"]!.GetValue<int>());

        var nobody = new AssistantMcpServer(AssistantMcpBridge.Registration(Guid.NewGuid().ToString("N")));
        Assert.Equal(-32602, (await Mcp(nobody, "tools/call", new { name = "recall", arguments = new { query = "q" } }))["error"]!["code"]!.GetValue<int>());
        Assert.Single(host.Runs);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-3")]
    public async Task A_line_with_a_repeated_name_bad_json_or_an_unknown_method_is_refused_and_a_notification_gets_no_reply()
    {
        var (host, home) = Registered();
        var server = new AssistantMcpServer(home);
        var twice = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"recall\",\"name\":\"forget\",\"arguments\":{\"notes\":[\"n-1\"]}}}";
        var nested = "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"recall\",\"arguments\":{\"query\":\"a\",\"query\":\"b\"}}}";
        foreach (var line in new[] { twice, nested, "{not json", "[1,2]" })
        {
            var answer = JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
            Assert.Contains(answer["error"]!["code"]!.GetValue<int>(), new[] { -32700, -32600 });
        }
        Assert.Empty(host.Runs);
        Assert.Equal(-32601, (await Mcp(server, "resources/list"))["error"]!["code"]!.GetValue<int>());
        Assert.Null(await server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None));
        Assert.Equal("abc", (await Mcp(server, "ping", id: "abc"))["id"]!.GetValue<string>());
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-3")]
    public async Task The_relay_pipe_answers_a_registered_token_line_by_line_and_lists_nothing_for_a_stranger()
    {
        var (host, home) = Registered();
        var pipe = AssistantMcpBridge.PipeName;
        Assert.StartsWith("MarrowDextra-", pipe, StringComparison.Ordinal);

        async Task<List<JsonNode>> Talk(string token, params string[] lines)
        {
            using var client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await client.ConnectAsync(timeout.Token);
            var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
            await writer.WriteLineAsync(token);
            foreach (var line in lines) await writer.WriteLineAsync(line);
            var replies = new List<JsonNode>();
            while (replies.Count < lines.Length && await reader.ReadLineAsync(timeout.Token) is { } reply) replies.Add(JsonNode.Parse(reply)!);
            return replies;
        }

        var replies = await Talk(home.Token,
            "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{}}",
            "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}",
            "{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"usage_status\",\"arguments\":{}}}");
        var byId = replies.ToDictionary(r => r["id"]!.GetValue<int>());
        Assert.Equal("nth-dextra", byId[1]["result"]!["serverInfo"]!["name"]!.GetValue<string>());
        Assert.Equal(25, byId[2]["result"]!["tools"]!.AsArray().Count); // the 24 of 15.5 and AS-20's prepare_purchase
        Assert.Equal("ran usage_status", byId[3]["result"]!["content"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("usage_status", Assert.Single(host.Runs).Tool.Name);

        var stranger = await Talk(Guid.NewGuid().ToString("N"), "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}");
        Assert.Empty(Assert.Single(stranger)["result"]!["tools"]!.AsArray());

        Assert.Equal(1, await AssistantMcpBridge.RunClient([AssistantMcpBridge.Command, "not-a-pipe", home.Token], Stream.Null, Stream.Null));
        Assert.Equal(1, await AssistantMcpBridge.RunClient([AssistantMcpBridge.Command, pipe, "not-a-token"], Stream.Null, Stream.Null));
        Assert.Equal(1, await AssistantMcpBridge.RunClient(["--marrow-advisor-mcp", pipe, home.Token], Stream.Null, Stream.Null));
    }
}
