using System.Reflection;
using System.Runtime.CompilerServices;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

[Collection(OpenAICompatibleSessionCollection.Name)]
[Trait("Decision", "ED-NEW-finalint-14")]
public sealed class FinalIntSealedApiTests
{
    private static string RichFixture([CallerFilePath] string path = "") =>
        Path.Join(Path.GetDirectoryName(path)!, "..", "Fixtures", "mcp-stdio-rich-fixture.ps1");

    private static MCPServerSpec.Stdio Rich() => new("powershell",
        new ValueList<string>(["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", RichFixture()]),
        ValueMap<string, string>.Empty);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_allowed_call_that_refreshes_the_models_list_keeps_unlisted_servers_out_of_the_next_request(bool previouslyLoaded)
    {
        using var runtime = new TestRuntime();
        using var folder = new ScratchFolder();
        const string allowed = "finalint-listed";
        const string unlisted = "finalint-unlisted";
        var session = new OpenAICompatibleSession(new OpenAICompatibleSession.Config
        {
            Kind = ProviderKind.Deepseek, Title = "DeepSeek", AgentIdentity = model => $"agent ({model})",
            DefaultModel = "deepseek-chat", ChatURL = new Uri("https://api.deepseek.com/chat/completions"),
            ContextWindow = 64_000, IsVisionModel = _ => false,
        }, new SessionConfiguration
        {
            Provider = ProviderKind.Deepseek,
            WorkingDirectory = SwiftURL.Create("file:" + folder.Root.Replace('\\', '/'))!,
            Environment = ValueMap<string, string>.Empty,
            RuntimeMode = RuntimeMode.FullAccess, InteractionMode = InteractionMode.Build, ApiKey = "sk-test",
            Sealed = new SealedLaunch(Guid.NewGuid()) { Connections = new([allowed]) },
        });
        var approvals = new List<ProviderEvent.Approval>();
        session.OnEvent = value =>
        {
            if (value is not ProviderEvent.Approval approval) return;
            approvals.Add(approval);
            runtime.Loop.Enqueue(() => session.ResolveApproval(approval.Value.Id, "allow"));
        };
        await MCPHub.Shared.Seed(new Dictionary<string, MCPServerSpec>(SwiftStringComparer.Instance)
        {
            [allowed] = Rich(), [unlisted] = Rich(),
        });
        var changed = new TaskCompletionSource<MCPListChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(MCPListChange value) => changed.TrySetResult(value);
        MCPHub.Shared.ListChanged += OnChanged;
        try
        {
            if (previouslyLoaded)
            {
                session.SeedMcpToolsForTests(session.SealedTools(await MCPHub.Shared.Tools()).ToArray());
                var revision = MCPHub.Shared.ToolsRevision;
                await MCPHub.Shared.Call($"mcp__{allowed}__change-tools", new JSONValue.Null());
                await changed.Task.WaitAsync(TimeSpan.FromSeconds(60));
                Assert.NotEqual(revision, MCPHub.Shared.ToolsRevision);
            }
            var call = session.ExecuteToolForTests(new("call-1", $"mcp__{allowed}__board", "{}"));
            runtime.Loop.RunUntil(() => call.IsCompleted);
            Assert.Equal("ran:board", await call);
            Assert.Single(approvals);
            var cached = (List<MCPHub.Tool>)typeof(OpenAICompatibleSession)
                .GetField("mcpTools", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.NotEmpty(cached);
            Assert.All(cached, tool => Assert.Equal(allowed, tool.Server));
            Assert.DoesNotContain(cached, tool => tool.Name == "refresh"); // app-only stays excluded as well
            var payload = session.RequestPayloadForTests("deepseek-chat");
            var offered = payload["tools"].JSONValueArray!.Select(tool => tool["function"]!["name"]!.JSONValueString).ToArray();
            Assert.Contains($"mcp__{allowed}__board", offered);
            Assert.DoesNotContain(offered, name => name!.StartsWith($"mcp__{unlisted}__", StringComparison.Ordinal));
            Assert.Equal($"Error: {unlisted} is not available in this mission chat.",
                await session.ExecuteToolForTests(new("call-2", $"mcp__{unlisted}__board", "{}")));
        }
        finally
        {
            MCPHub.Shared.ListChanged -= OnChanged;
            await MCPHub.Shared.Reload();
        }
    }
}
