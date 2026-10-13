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

// PORT-ONLY: security review of the web feature, S2 (ED-NEW-web-sec-rev-2). The API providers are the model's one way to the
// MCP hub. A model's MCP call runs only when the tool is in the model's list as it stands now: the session reads the list first
// when it has not, or when a server said its tools changed since, so an app-only tool (its _meta.ui.visibility leaves out
// "model") and the tool of a server whose listing failed are refused before anything reaches the server.
[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-web-sec-rev-2")]
public sealed class OpenAICompatibleSessionMcpVisibilityTests
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

    private static MCPServerSpec.Stdio Rich() => new("powershell",
        new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", RichFixture()]),
        ValueMap<string, string>.Empty);

    private static OpenAICompatibleSession.PendingToolCall Call(string name) => new("call-1", name, "{}");

    [Fact]
    public async Task A_models_call_to_an_app_only_tool_before_the_session_read_the_tool_list_is_refused()
    {
        using var folder = new ScratchFolder();
        var events = new List<ProviderEvent>();
        var session = Session(folder.Root, events);
        const string server = "srv-sec-rev-a";
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance) { [server] = Rich() });
        try
        {
            // Nothing listed the tools yet: neither the session nor the hub knows that refresh is the app view's alone.
            var refused = await session.ExecuteToolForTests(Call($"mcp__{server}__refresh"));
            Assert.Equal($"Error: Unknown tool: mcp__{server}__refresh.", refused);
            var update = events.OfType<ProviderEvent.ToolUpdated>().Single().Update;
            Assert.Equal(ToolCall.Status.Failed, update.Status);
            Assert.Equal($"Unknown tool: mcp__{server}__refresh.", update.Output);

            // What the model may call still runs.
            Assert.Equal("ran:board", await session.ExecuteToolForTests(Call($"mcp__{server}__board")));
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }

    [Fact]
    public async Task A_models_call_to_a_server_whose_listing_failed_is_refused_without_reaching_the_server()
    {
        using var folder = new ScratchFolder();
        var events = new List<ProviderEvent>();
        var session = Session(folder.Root, events);
        const string server = "srv-sec-rev-b";
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            [server] = new MCPServerSpec.Stdio("marrow-sec-rev-tests-no-such-binary", ValueList<string>.Empty, ValueMap<string, string>.Empty),
        });
        try
        {
            // The listing fails (the server cannot start), so nothing of it is in the model's list: the call is refused as
            // unknown instead of starting the server again for a tool nobody could see.
            var refused = await session.ExecuteToolForTests(Call($"mcp__{server}__anything"));
            Assert.Equal($"Error: Unknown tool: mcp__{server}__anything.", refused);
        }
        finally
        {
            await MCPHub.Shared.Reload();
        }
    }
}
