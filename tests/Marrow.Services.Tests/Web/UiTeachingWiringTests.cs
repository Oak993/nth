using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.NthUi;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// DESIGN.md 14 row W17 and 7.3 (web surfaces W17; ED-NEW-web-w17-1): how the teaching reaches the models. CLI providers get it as
// the nth-ui server's initialize.instructions and read the rest through the read-only ui_reference tool; API providers get the
// block in their system prompt and the same tool as a native function. What the text says follows the tools the session is
// offered, and a chat with no artifact tools gets none of it. No model provider is ever called.
[Trait("Decision", "ED-NEW-web-w17-1")]
public sealed class UiTeachingWiringTests
{
    private static readonly Guid Chat = Guid.Parse("7a0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d11");

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///C:/teaching-test")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        Model = "deepseek-chat",
        IgnisThreadID = thread,
    };

    private static async Task<JsonNode> Mcp(UiMcpServer server, string method, object? parameters = null)
    {
        var line = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 3, method, @params = parameters });
        return JsonNode.Parse((await server.Handle(line, CancellationToken.None))!)!;
    }

    private static async Task<JsonNode> Call(UiMcpServer server, object arguments) =>
        (await Mcp(server, "tools/call", new { name = "ui_reference", arguments }))["result"]!;

    private static int Fences(string text) => text.Split('\n').Count(line => line.StartsWith("```nth-ui", StringComparison.Ordinal));

    [Fact]
    public async Task Initialize_carries_the_teaching_for_the_tools_the_server_serves_and_nothing_to_a_silent_one()
    {
        using var scope = new ArtifactHostScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
        var instructions = (await Mcp(server, "initialize", new { protocolVersion = "2025-06-18" }))["result"]!["instructions"]!.GetValue<string>();
        Assert.Equal(UiMcpServer.Instructions, instructions);
        var options = UiTeaching.Options(UiMcpServer.Offered);
        Assert.Equal(NthUiTeaching.McpInstructions(options), instructions);
        Assert.StartsWith(NthUiTeaching.Core(options) + "\n\n", instructions, StringComparison.Ordinal);
        Assert.InRange(NthUiTeaching.Core(options).Length, 1_200, NthUiTeaching.McpCoreLimit);

        // The tools this server serves today: artifacts and the reference. The live-answer tools of W24 are not among them, so a view goes in a fence.
        Assert.True(options.ArtifactTools && options.Reference);
        Assert.False(options.LiveTools || options.HtmlWidget || options.AskUser);
        Assert.Contains("artifact_create", instructions, StringComparison.Ordinal);
        Assert.Contains("ui_reference", instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("ui_show", instructions, StringComparison.Ordinal);
        Assert.Equal(NthUiExamples.Six.Count, Fences(instructions));

        var silent = new UiMcpServer(null);
        Assert.Equal("Artifacts are not available in this session.", (await Mcp(silent, "initialize", new { protocolVersion = "2025-06-18" }))["result"]!["instructions"]!.GetValue<string>());
    }

    [Fact]
    public async Task The_reference_tool_reads_the_page_by_topic_over_MCP()
    {
        using var scope = new ArtifactHostScope();
        var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Codex, Chat)));
        var tool = (await Mcp(server, "tools/list"))["result"]!["tools"]!.AsArray().Single(t => t!["name"]!.GetValue<string>() == "ui_reference")!;
        Assert.Equal(UiReferenceTool.Description, tool["description"]!.GetValue<string>());
        Assert.Equal(UiReferenceTool.InputSchema().ToJsonString(), tool["inputSchema"]!.ToJsonString());
        Assert.Equal(NthUiTeaching.Topics, tool["inputSchema"]!["properties"]!["topic"]!["enum"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.Equal(["topic"], tool["inputSchema"]!["required"]!.AsArray().Select(t => t!.GetValue<string>()));
        Assert.True(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        foreach (var topic in NthUiTeaching.Topics) Assert.Contains(topic, UiReferenceTool.Description, StringComparison.Ordinal);

        var language = await Call(server, new { topic = "language" });
        Assert.False(language["isError"]!.GetValue<bool>());
        Assert.Equal(NthUiTeaching.Reference("language"), language["content"]![0]!["text"]!.GetValue<string>());
        Assert.StartsWith("## 1. Lines", language["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        var examples = (await Call(server, new { topic = "Examples" }))["content"]![0]!["text"]!.GetValue<string>();
        Assert.Equal(NthUiExamples.Six.Count + NthUiExamples.More.Count, Fences(examples));

        foreach (var arguments in new object[] { new { topic = "colours" }, new { topic = "" }, new { } })
        {
            var refused = await Call(server, arguments);
            Assert.True(refused["isError"]!.GetValue<bool>());
            Assert.StartsWith("Error: ", refused["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Contains("Topics: language, components, actions, examples, lite, errors.", refused["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        }
        Assert.True(UiMcpServer.IsOwnTool("mcp__nth-ui__ui_reference"));
        Assert.False(UiMcpServer.IsOwnTool("mcp__nth-ui__ui_show")); // W24 adds its tools to ToolNames when it adds them

        var silent = new UiMcpServer(null);
        Assert.Equal(-32602, (await Mcp(silent, "tools/call", new { name = "ui_reference", arguments = new { topic = "language" } }))["error"]!["code"]!.GetValue<int>());
    }

    [Fact]
    public async Task API_providers_get_the_reference_as_a_function_tool_and_the_block_for_their_system_prompt()
    {
        var off = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        Assert.Null(off.UiTeachingBlock());
        Assert.False(off.TeachesLiveViews);
        Assert.False(off.Owns("ui_reference"));

        using var scope = new ArtifactHostScope();
        Assert.Null(SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, null)).UiTeachingBlock()); // a session of no chat
        var tools = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        Assert.True(tools.TeachesLiveViews);
        Assert.True(tools.Owns("ui_reference"));
        var definition = Assert.Single(tools.Definitions, d => d["function"]!["name"]!.JSONValueString == "ui_reference");
        Assert.Equal(UiReferenceTool.Description, definition["function"]!["description"]!.JSONValueString);
        Assert.Equal(UiReferenceTool.InputSchema().ToJsonString(), JsonNode.Parse(definition["function"]!["parameters"]!.CompactString)!.ToJsonString());

        var arguments = JSONValue.Parse("{\"topic\":\"components\"}")!;
        Assert.Equal("Live answers \u00B7 components", NativeSkillTools.Title("ui_reference", arguments));
        var result = await tools.Execute("ui_reference", arguments);
        Assert.False(result.Failed);
        Assert.Equal("Live answers \u00B7 components", result.Title);
        Assert.Equal(NthUiTeaching.Reference("components"), result.Output);
        var refused = await tools.Execute("ui_reference", JSONValue.Parse("{\"topic\":\"nope\"}")!);
        Assert.True(refused.Failed);
        Assert.Equal("Live answers", NativeSkillTools.Title("ui_reference", JSONValue.Parse("{\"topic\":\"nope\"}")!));

        var block = tools.UiTeachingBlock()!;
        Assert.Equal(NthUiTeaching.SystemBlock(UiTeaching.Options(tools.OfferedUiTools)), block);
        Assert.Contains("artifact_create", block, StringComparison.Ordinal);
        Assert.Contains("call ui_reference with a topic", block, StringComparison.Ordinal);
        Assert.Equal(NthUiExamples.Six.Count, Fences(block));

        // A small local model: the lite subset, and fenced blocks in place of tools it cannot call.
        var lite = tools.UiTeachingBlock(NthUiProfile.Lite, toolsUsable: false)!;
        Assert.Equal(NthUiExamples.Lite.Count, Fences(lite));
        Assert.Contains("THE VIEW LANGUAGE (SMALL SET)", lite, StringComparison.Ordinal);
        Assert.Contains("nth-artifact", lite, StringComparison.Ordinal);
        Assert.DoesNotContain("artifact_create", lite, StringComparison.Ordinal);
        Assert.DoesNotContain("ui_reference", lite, StringComparison.Ordinal);
        var liteWithTools = tools.UiTeachingBlock(NthUiProfile.Lite)!;
        Assert.Contains("artifact_create", liteWithTools, StringComparison.Ordinal);
        Assert.Equal(NthUiExamples.Lite.Count, Fences(liteWithTools));
    }

    [Fact]
    public void The_options_follow_the_names_of_the_tools_offered()
    {
        string[] everything = ["artifact_create", "artifact_update", "artifact_read", "artifact_list", "ui_reference", "ui_show", "ui_update", "html_widget", "ask_user"];
        var all = UiTeaching.Options(everything);
        Assert.True(all.LiveTools && all.ArtifactTools && all.HtmlWidget && all.AskUser && all.Reference);
        Assert.Equal(VisualAnswerLevel.Balanced, all.Visual);
        var block = NthUiTeaching.SystemBlock(all);
        foreach (var phrase in new[] { "Call ui_show", "html_widget", "ask_user", "artifact_create", "ui_reference" }) Assert.Contains(phrase, block, StringComparison.Ordinal);

        var none = UiTeaching.Options([]);
        Assert.False(none.LiveTools || none.ArtifactTools || none.HtmlWidget || none.AskUser || none.Reference);
        var cannotCall = UiTeaching.Options(everything, NthUiProfile.Full, toolsUsable: false);
        Assert.False(cannotCall.LiveTools || cannotCall.ArtifactTools || cannotCall.AskUser || cannotCall.Reference);
        Assert.True(cannotCall.HtmlWidget); // a full model that cannot call tools still writes an nth-html block
        var lite = UiTeaching.Options(everything, NthUiProfile.Lite);
        Assert.False(lite.HtmlWidget);
        Assert.True(lite.IsLite);
    }

    [Fact]
    public async Task The_visual_answers_setting_is_a_seam_the_settings_page_binds()
    {
        using var scope = new ArtifactHostScope();
        var tools = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        Assert.DoesNotContain("fewer visual answers", tools.UiTeachingBlock()!, StringComparison.Ordinal);
        Func<VisualAnswerLevel> fewer = () => VisualAnswerLevel.Fewer;
        Func<VisualAnswerLevel> more = () => VisualAnswerLevel.More;
        using (UiTeaching.Visual.Override(fewer))
        {
            Assert.Contains("asked for fewer visual answers", tools.UiTeachingBlock()!, StringComparison.Ordinal);
            var server = new UiMcpServer(UiMcpBridge.For(Config(ProviderKind.Claude, Chat)));
            var instructions = (await Mcp(server, "initialize", new { protocolVersion = "2025-06-18" }))["result"]!["instructions"]!.GetValue<string>();
            Assert.Contains("The reader prefers fewer", instructions, StringComparison.Ordinal);
            Assert.InRange(NthUiTeaching.Core(UiTeaching.Options(UiMcpServer.ToolNames)).Length, 1, NthUiTeaching.McpCoreLimit);
        }
        using (UiTeaching.Visual.Override(more))
            Assert.Contains("likes visual answers", tools.UiTeachingBlock()!, StringComparison.Ordinal);
        Assert.DoesNotContain("visual answers", tools.UiTeachingBlock()!, StringComparison.Ordinal);
    }
}
