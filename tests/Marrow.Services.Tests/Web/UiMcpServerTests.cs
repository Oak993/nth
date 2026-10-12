using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2: artifact_create, artifact_update, artifact_read and artifact_list
// on the built-in nth-ui MCP server for CLI providers). The server is driven line by line as the relay would; versions
// land in a throwaway threads folder. No model provider is ever called.
[Trait("Decision", "ED-NEW-web-w12-1")]
public sealed class UiMcpServerTests
{
    private static readonly Guid Chat = Guid.Parse("5d0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d01");
    private static readonly Guid TurnId = Guid.Parse("5d0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d02");

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread, string? model = "sonnet-5") => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///C:/artifact-test")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = model,
        IgnisThreadID = thread,
    };

    private static async Task<JsonNode> Mcp(UiMcpServer server, string method, object? parameters = null, int id = 7)
    {
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });
        return JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
    }

    private static async Task<JsonNode> Call(UiMcpServer server, string tool, object arguments) =>
        (await Mcp(server, "tools/call", new { name = tool, arguments }))["result"]!;

    private static string Text(JsonNode result) => result["content"]![0]!["text"]!.GetValue<string>();

    [Fact]
    public async Task Initialize_and_tools_list_offer_the_four_artifact_tools_only_to_a_registered_chat()
    {
        using var scope = new ArtifactHostScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Codex, Chat)));
        var init = (await Mcp(server, "initialize", new { protocolVersion = "2025-06-18" }))["result"]!;
        Assert.Equal(("2025-06-18", "nth-ui"), (init["protocolVersion"]!.GetValue<string>(), init["serverInfo"]!["name"]!.GetValue<string>()));
        Assert.Equal(UiMcpServer.Instructions, init["instructions"]!.GetValue<string>());
        Assert.Equal("2025-11-25", (await Mcp(server, "initialize", new { protocolVersion = "1999-01-01" }))["result"]!["protocolVersion"]!.GetValue<string>());

        var tools = (await Mcp(server, "tools/list"))["result"]!["tools"]!.AsArray();
        // The four artifact tools, then W17's read-only live-view reference.
        Assert.Equal(new[] { "artifact_create", "artifact_update", "artifact_read", "artifact_list", "ui_reference" }, tools.Select(t => t!["name"]!.GetValue<string>()));
        // ask_user (W25) is served only while a host for it is bound (here none is): the list is exactly what the process offers, and nothing else is held back.
        Assert.Equal(UiMcpServer.Offered, tools.Select(t => t!["name"]!.GetValue<string>()));
        Assert.Equal(UiMcpServer.ToolNames.Where(name => name != "ask_user"), UiMcpServer.Offered);
        foreach (var tool in tools.Where(t => ArtifactTools.IsName(t!["name"]!.GetValue<string>())))
        {
            var name = tool!["name"]!.GetValue<string>();
            Assert.Equal(ArtifactTools.Description(name), tool["description"]!.GetValue<string>());
            Assert.Equal(ArtifactTools.InputSchema(name).ToJsonString(), tool["inputSchema"]!.ToJsonString());
            Assert.Equal(name is "artifact_read" or "artifact_list", tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        }
        var create = ArtifactTools.InputSchema("artifact_create");
        Assert.Equal(ArtifactKinds.WireNames, create["properties"]!["kind"]!["enum"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal(new[] { "kind", "title" }, create["required"]!.AsArray().Select(k => k!.GetValue<string>()));
        Assert.Equal(new[] { "id" }, ArtifactTools.InputSchema("artifact_update")["required"]!.AsArray().Select(k => k!.GetValue<string>()));

        foreach (var silent in new[] { new UiMcpServer(null), new UiMcpServer(new UiRegistration(Chat, ProviderKind.Codex, null, new ArtifactHost(null, null))) })
        {
            Assert.Empty((await Mcp(silent, "tools/list"))["result"]!["tools"]!.AsArray());
            Assert.Equal(-32602, (await Mcp(silent, "tools/call", new { name = "artifact_list", arguments = new { } }))["error"]!["code"]!.GetValue<int>());
        }
        Assert.Null(UiMcpBridge.For(Config(ProviderKind.Codex, null)));
    }

    [Fact]
    public async Task Create_update_read_and_list_work_end_to_end_and_record_the_author_and_prompt()
    {
        using var scope = new ArtifactHostScope(new ArtifactTurn(TurnId, "Make a pricing calculator for Orbit.", "opus-5"));
        var changes = new List<ArtifactChange>();
        scope.Store.Changed += changes.Add;
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));

        var created = await Call(server, "artifact_create", new { kind = "html", title = "Pricing calculator", content = "<h3>Orbit</h3>\n<p>Seats</p>\n" });
        Assert.False(created["isError"]!.GetValue<bool>());
        Assert.Equal("Created \"pricing-calculator\" (HTML app), v1. It is open in Tabula. To change it, call artifact_update with id \"pricing-calculator\".", Text(created));
        Assert.Equal(("pricing-calculator", 1, "html"), (created["structuredContent"]!["id"]!.GetValue<string>(), created["structuredContent"]!["version"]!.GetValue<int>(), created["structuredContent"]!["kind"]!.GetValue<string>()));
        var v1 = scope.Store.Read(Chat, "pricing-calculator").Version;
        Assert.Equal((new ArtifactAuthor("claude", "opus-5", TurnId), "Make a pricing calculator for Orbit."), (v1.Author, v1.Prompt));
        Assert.True(Assert.Single(changes).Created);

        var edited = await Call(server, "artifact_update", new { id = "pricing-calculator", edits = new[] { new { find = "<p>Seats</p>", replace = "<p>Seats: 5</p>" } }, title = "Orbit pricing" });
        Assert.Equal("Updated \"pricing-calculator\" to v2. Its title is now \"Orbit pricing\".", Text(edited));
        var whole = await Call(server, "artifact_update", new { id = "pricing-calculator", content = "<h3>Orbit 3</h3>\n" });
        Assert.Equal("Updated \"pricing-calculator\" to v3.", Text(whole));

        var read = Text(await Call(server, "artifact_read", new { id = "pricing-calculator", version = "v2" }));
        Assert.Equal("id: pricing-calculator\ntitle: Orbit pricing\nkind: html\nversion: v2 (the latest is v3)\nby: claude \u00B7 opus-5\nprompt: Make a pricing calculator for Orbit.\n\n<h3>Orbit</h3>\n<p>Seats: 5</p>\n", read);

        await Call(server, "artifact_create", new { kind = "project", title = "Orbit site", files = new Dictionary<string, string> { ["index.html"] = "<p>Hi</p>\n", ["style.css"] = "p{}" } });
        var site = Text(await Call(server, "artifact_read", new { id = "orbit-site" }));
        Assert.EndsWith("entry: index.html\nfiles: index.html, style.css\n\n--- index.html ---\n<p>Hi</p>\n\n--- style.css ---\np{}\n", site, StringComparison.Ordinal);
        Assert.EndsWith("\n--- style.css ---\np{}", Text(await Call(server, "artifact_read", new { id = "orbit-site", path = "style.css" })), StringComparison.Ordinal);
        var deleted = await Call(server, "artifact_update", new { id = "orbit-site", files = new Dictionary<string, string?> { ["style.css"] = null } });
        Assert.Equal("Updated \"orbit-site\" to v2.", Text(deleted));

        var list = await Call(server, "artifact_list", new { });
        Assert.Equal("2 artifacts in this chat, newest change first:\n- orbit-site \u00B7 project \u00B7 \"Orbit site\" \u00B7 v2\n- pricing-calculator \u00B7 html \u00B7 \"Orbit pricing\" \u00B7 v3", Text(list));
        Assert.Equal(2, list["structuredContent"]!["artifacts"]!.AsArray().Count);
    }

    [Fact]
    public async Task A_refusal_is_an_error_result_the_model_can_act_on_and_protocol_errors_keep_their_codes()
    {
        using var scope = new ArtifactHostScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Codex, Chat)));
        Assert.Equal("This chat has no artifacts yet.", Text(await Call(server, "artifact_list", new { })));

        async Task<string> Refused(string tool, object arguments)
        {
            var result = await Call(server, tool, arguments);
            Assert.True(result["isError"]!.GetValue<bool>());
            Assert.Null(result["structuredContent"]);
            return Text(result);
        }
        Assert.Equal("Error: kind must be one of: html, react, svg, mermaid, markdown, code, nth-ui, project.", await Refused("artifact_create", new { kind = "pdf", title = "x", content = "y" }));
        Assert.Equal("Error: A title is required.", await Refused("artifact_create", new { kind = "html", content = "y" }));
        Assert.StartsWith("Error: This chat has no artifact \"nope\"", await Refused("artifact_update", new { id = "nope", content = "x" }), StringComparison.Ordinal);
        Assert.Equal("Error: The artifact's id is required.", await Refused("artifact_read", new { }));
        await Call(server, "artifact_create", new { kind = "markdown", title = "Notes", content = "# a\n# a\n" });
        Assert.Contains("matches 2 places in v1", await Refused("artifact_update", new { id = "notes", edits = new[] { new { find = "# a", replace = "# b" } } }), StringComparison.Ordinal);
        Assert.Equal("Error: \"seven\" is not a version number.", await Refused("artifact_read", new { id = "notes", version = "seven" }));
        Assert.Equal("Error: Each edit needs find and replace.", await Refused("artifact_update", new { id = "notes", edits = new[] { new { find = "x" } } }));
        Assert.Equal("Error: edits is not valid JSON.", await Refused("artifact_update", new { id = "notes", edits = "[{" }));

        Assert.Equal(-32602, (await Mcp(server, "tools/call", new { name = "rm_rf", arguments = new { } }))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32601, (await Mcp(server, "resources/list"))["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32700, JsonNode.Parse((await server.Handle("{not json", CancellationToken.None))!)!["error"]!["code"]!.GetValue<int>());
        Assert.Equal(-32600, JsonNode.Parse((await server.Handle("[1]", CancellationToken.None))!)!["error"]!["code"]!.GetValue<int>());
        Assert.Null(await server.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", CancellationToken.None));
        Assert.Equal(7, (await Mcp(server, "ping"))["id"]!.GetValue<int>());
    }

    [Fact]
    public async Task Lenient_arguments_from_small_models_still_work()
    {
        using var scope = new ArtifactHostScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Opencode, Chat)));
        // files as a JSON string and no kind: a project; edits as a JSON string holding one edit; a version as a number.
        var made = await Call(server, "artifact_create", new { title = "Site", files = "{\"index.html\":\"<p>1</p>\"}" });
        Assert.Equal("Created \"site\" (Project), v1. It is open in Tabula. To change it, call artifact_update with id \"site\".", Text(made));
        Assert.Equal("Updated \"site\" to v2.", Text(await Call(server, "artifact_update", new { id = "Site", edits = "{\"find\":\"1\",\"replace\":\"2\"}" })));
        Assert.EndsWith("<p>1</p>\n", Text(await Call(server, "artifact_read", new { id = "site", version = 1 })), StringComparison.Ordinal);
        Assert.Equal(("Tabula \u00B7 Site", "Tabula \u00B7 site", "Tabula"),
            (ArtifactTools.RowTitle("artifact_create", JsonNode.Parse("{\"title\":\"Site\"}")), ArtifactTools.RowTitle("artifact_read", JsonNode.Parse("{\"id\":\"site\"}")), ArtifactTools.RowTitle("artifact_list", null)));
        Assert.True(UiMcpServer.IsOwnTool("mcp__nth-ui__artifact_create"));
        Assert.False(UiMcpServer.IsOwnTool("mcp__nth-ui__rm"));
        Assert.False(UiMcpServer.IsOwnTool("mcp__other__artifact_create"));
    }
}
