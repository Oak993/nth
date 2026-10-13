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

// PORT-ONLY: security review of the web feature, follow-up (ED-NEW-web-sec-rev-5). The API session checks a model's MCP call against
// the model's list of tools (ED-NEW-web-sec-rev-2) and reads that list again when the hub's tools revision moved. A hub Reload (the
// person changed their MCP servers) moves it too, so a tool a server now keeps for its app view is refused even though the session
// read its list, with that tool open to the model, before the reload. Driven end to end against the scripted stand-in server
// (Fixtures/mcp-stdio-rich-fixture.ps1, a system binary; no real AI CLI, no network, no model provider).
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-sec-rev-5")]
public sealed class OpenAICompatibleSessionMcpReloadTests
{
    private static OpenAICompatibleSession Session(string root, List<ProviderEvent> events)
    {
        var session = new OpenAICompatibleSession(new OpenAICompatibleSession.Config
        {
            Kind = ProviderKind.Deepseek,
            Title = "DeepSeek",
            AgentIdentity = model => $"agent ({model})",
            DefaultModel = "deepseek-chat",
            ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
            ContextWindow = 64_000,
            IsVisionModel = _ => false,
        }, new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek,
            WorkingDirectory = SwiftURL.Create("file:" + root.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess,
            InteractionMode = InteractionMode.Build,
            ApiKey = "sk-test",
        });
        session.OnEvent = events.Add;
        return session;
    }

    private static string RichFixture([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-rich-fixture.ps1");

    private static MCPServerSpec.Stdio Rich(IReadOnlyDictionary<string, string>? env = null) => new("powershell",
        new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", RichFixture()]),
        new ValueMap<string, string>(env ?? new Dictionary<string, string>(), SwiftStringComparer.Instance));

    private static OpenAICompatibleSession.PendingToolCall Call(string name) => new("call-1", name, "{}");

    [Fact]
    public async Task A_tool_a_server_keeps_for_its_app_after_a_reload_is_refused_though_the_session_read_its_list_before()
    {
        using var folder = new ScratchFolder();
        var events = new List<ProviderEvent>();
        var session = Session(folder.Root, events);
        const string server = "srv-sec-rev-c";
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [server] = Rich() });
        try
        {
            // The session reads its list: board is the model's.
            Assert.Equal("ran:board", await session.ExecuteToolForTests(Call($"mcp__{server}__board")));

            // The person's servers change: the same server now keeps board for its app view.
            await MCPHub.Shared.Reload();
            await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
            {
                [server] = Rich(new Dictionary<string, string> { ["MCP_RICH_BOARD_VISIBILITY"] = "app" }),
            });

            events.Clear();
            var refused = await session.ExecuteToolForTests(Call($"mcp__{server}__board"));
            Assert.Equal($"Error: Unknown tool: mcp__{server}__board.", refused);
            Assert.Equal(ToolCall.Status.Failed, events.OfType<ProviderEvent.ToolUpdated>().Single().Update.Status);

            // What the model may still call runs.
            Assert.Equal("ran:modelonly", await session.ExecuteToolForTests(Call($"mcp__{server}__modelonly")));
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }
}
