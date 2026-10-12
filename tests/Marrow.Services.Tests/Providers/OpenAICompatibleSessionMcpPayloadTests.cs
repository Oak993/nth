using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;
using Xunit;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: web surfaces W18 (DESIGN.md 7.6, ED-NEW-web-w18-1). An MCP tool row of the API providers carries the
// structured part of the result (images, links, structuredContent, the UI resource) next to its text: the row starts
// with the UI resource a tool names, and finishes with the payload of the result. The model still receives text.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-w18-1")]
public sealed class OpenAICompatibleSessionMcpPayloadTests
{
    private static OpenAICompatibleSession.Config MakeConfig() => new()
    {
        Kind = ProviderKind.Deepseek,
        Title = "DeepSeek",
        AgentIdentity = model => $"agent ({model})",
        DefaultModel = "deepseek-chat",
        ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
        ContextWindow = 64_000,
        IsVisionModel = _ => false,
    };

    private static SwiftURL WorkingDirectoryURL(string root) => SwiftURL.Create("file:" + root.Replace('\\', '/'))!;

    private static SessionConfiguration MakeSessionConfiguration(string root) => new()
    {
        Provider = ProviderKind.Deepseek,
        WorkingDirectory = WorkingDirectoryURL(root),
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        ApiKey = "sk-test",
    };

    private static string FixtureScript([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-rich-fixture.ps1");

    private static OpenAICompatibleSession.PendingToolCall Call(string name) => new("call-1", name, "{}");

    private static async Task<(OpenAICompatibleSession Session, List<ProviderEvent> Events)> Wired(string root, string serverId)
    {
        var session = new OpenAICompatibleSession(MakeConfig(), MakeSessionConfiguration(root));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var spec = new MCPServerSpec.Stdio("powershell",
            new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", FixtureScript()]),
            ValueMap<string, string>.Empty);
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [serverId] = spec });
        await MCPHub.Shared.Tools(); // the session lists the tools before the model can call one; the hub learns the views then
        session.SeedMcpToolsForTests(
        [
            new MCPHub.Tool(serverId, "board", "", new JSONValue.Null()) { UiResourceUri = "ui://board/view.html" },
            new MCPHub.Tool(serverId, "plain", "", new JSONValue.Null()),
            new MCPHub.Tool(serverId, "picture", "", new JSONValue.Null()),
        ]);
        return (session, events);
    }

    [Fact]
    public async Task A_tool_that_names_a_view_starts_its_row_with_the_ui_resource_and_a_plain_tool_does_not()
    {
        using var folder = new ScratchFolder();
        const string serverId = "srv-w18-a";
        var (session, events) = await Wired(folder.Root, serverId);
        try
        {
            await session.ExecuteToolForTests(Call($"mcp__{serverId}__board"));
            var started = events.OfType<ProviderEvent.ToolStarted>().Single().Call;
            Assert.Equal(ToolCall.Kind.Mcp, started.ToolCallKind);
            Assert.Equal(serverId, started.Payload!.Server);
            Assert.Equal("board", started.Payload.Tool);
            Assert.Equal("ui://board/view.html", started.Payload.UiResourceUri);

            events.Clear();
            await session.ExecuteToolForTests(Call($"mcp__{serverId}__plain"));
            Assert.Null(events.OfType<ProviderEvent.ToolStarted>().Single().Call.Payload);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    public async Task A_finished_row_carries_the_structured_result_and_the_model_still_gets_text()
    {
        using var folder = new ScratchFolder();
        const string serverId = "srv-w18-b";
        var (session, events) = await Wired(folder.Root, serverId);
        try
        {
            var forModel = await session.ExecuteToolForTests(Call($"mcp__{serverId}__board"));
            Assert.Equal("ran:board", forModel);
            var update = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
            Assert.Equal(ToolCall.Status.Completed, update.Status);
            Assert.Equal("ran:board", update.Output);
            var payload = update.Payload!;
            Assert.Equal(("ui://board/view.html", serverId, "board"), (payload.UiResourceUri, payload.Server, payload.Tool));
            Assert.Contains("\"count\":3", payload.StructuredContent, StringComparison.Ordinal);
            Assert.Contains("\"viewState\":\"abc\"", payload.Meta, StringComparison.Ordinal);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    public async Task An_image_result_reaches_the_row_as_an_image_while_the_model_gets_the_placeholder()
    {
        using var folder = new ScratchFolder();
        const string serverId = "srv-w18-c";
        var (session, events) = await Wired(folder.Root, serverId);
        try
        {
            var forModel = await session.ExecuteToolForTests(Call($"mcp__{serverId}__picture"));
            Assert.Equal("here is the chart\n[image]", forModel);
            var image = Assert.Single(events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Payload!.Images);
            Assert.Equal(("image/png", "iVBORw0KGgo=", false), (image.MimeType, image.Data, image.Omitted));
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    public async Task A_plain_text_result_leaves_the_rows_payload_alone()
    {
        using var folder = new ScratchFolder();
        const string serverId = "srv-w18-d";
        var (session, events) = await Wired(folder.Root, serverId);
        try
        {
            await session.ExecuteToolForTests(Call($"mcp__{serverId}__plain"));
            Assert.Null(events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Payload);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }
}
