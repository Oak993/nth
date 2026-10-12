using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Services.Web;
using Marrow.Testing;

namespace Marrow.Services.Tests.Web;

// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.2 "every model": nth-ui for CLI providers, native tools for API
// providers). How the artifact tools reach each provider: the relay entry in every CLI shape, the Claude launch and its
// card-free approval, the API providers' tool loop, and the stdio relay itself. With artifacts off, every launch is
// exactly what it was. No model provider is ever called.
[Trait("Decision", "ED-NEW-web-w12-1")]
public sealed class ArtifactWiringTests
{
    private static readonly Guid Chat = Guid.Parse("6e0c2d1e-7a3b-4c55-9e21-0a6d1b2c3d01");

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread, string? model = "deepseek-chat") => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("file:///C:/artifact-test")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.FullAccess,
        InteractionMode = InteractionMode.Build,
        Model = model,
        IgnisThreadID = thread,
    };

    private static IEnumerable<string> FunctionNames(NativeSkillTools tools) => tools.Definitions.Select(d => d["function"]!["name"]!.JSONValueString!);

    [Fact]
    public async Task API_providers_get_the_four_tools_as_native_tools_only_in_a_chat_with_artifacts_on()
    {
        var before = FunctionNames(SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat))).ToList(); // artifacts off: what it always was
        Assert.DoesNotContain(before, ArtifactTools.IsName);
        using var scope = new ArtifactHostScope(new ArtifactTurn(null, "Draw the release flow.", null));
        Assert.DoesNotContain(FunctionNames(SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, null))), ArtifactTools.IsName);
        var tools = SkillSessionFactory.NativeTools(Config(ProviderKind.Deepseek, Chat));
        // Added after the others, nothing else changes: the four artifact tools, then W17's read-only live-view reference.
        Assert.Equal(before.Concat(["artifact_create", "artifact_update", "artifact_read", "artifact_list", "ui_reference"]), FunctionNames(tools));
        // ask_user (W25) is the last of the tools there are, and is offered only while a host for it is bound (here none is).
        Assert.Equal(ArtifactTools.Names.Append("ui_reference").Append("ask_user"), ArtifactNativeTools.Names);
        Assert.Equal(ArtifactTools.Names.Append("ui_reference"), tools.OfferedUiTools);
        Assert.All(tools.OfferedUiTools, name => Assert.True(tools.Owns(name)));
        Assert.False(tools.Owns("ask_user"));
        Assert.False(tools.Owns("artifact_delete"));
        foreach (var definition in tools.Definitions.Where(d => ArtifactTools.IsName(d["function"]!["name"]!.JSONValueString)))
        {
            var name = definition["function"]!["name"]!.JSONValueString!;
            Assert.Equal(JsonNode.Parse(ArtifactTools.InputSchema(name).ToJsonString())!.ToJsonString(), JsonNode.Parse(definition["function"]!["parameters"]!.CompactString)!.ToJsonString());
            Assert.Equal(ArtifactTools.Description(name), definition["function"]!["description"]!.JSONValueString);
        }

        var arguments = JSONValue.Parse("{\"kind\":\"mermaid\",\"title\":\"Release flow\",\"content\":\"graph TD; A-->B\"}")!;
        Assert.Equal("Tabula \u00B7 Release flow", NativeSkillTools.Title("artifact_create", arguments));
        var result = await tools.Execute("artifact_create", arguments);
        Assert.Equal(("Tabula \u00B7 Release flow", false), (result.Title, result.Failed));
        Assert.StartsWith("Created \"release-flow\" (Diagram), v1.", result.Output, StringComparison.Ordinal);
        var version = scope.Store.Read(Chat, "release-flow").Version;
        Assert.Equal((new ArtifactAuthor("deepseek", "deepseek-chat", null), "Draw the release flow."), (version.Author, version.Prompt));
        var refused = await tools.Execute("artifact_update", JSONValue.Parse("{\"id\":\"release-flow\"}")!);
        Assert.True(refused.Failed);
        Assert.StartsWith("Error: Pass the new content, edits", refused.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_CLI_shape_carries_the_nth_ui_relay_entry_beside_the_others_and_nothing_when_off()
    {
        Assert.Null(MCPProviderConfig.UiServer(Config(ProviderKind.Codex, Chat)));
        using var scope = new ArtifactHostScope();
        Assert.Null(MCPProviderConfig.UiServer(Config(ProviderKind.Codex, null)));
        var server = Assert.IsType<MCPResolvedServer>(MCPProviderConfig.UiServer(Config(ProviderKind.Codex, Chat, "astra-6")));
        Assert.Equal((UiMcpServer.ServerName, UiMcpBridge.Executable.Current(), UiMcpBridge.Command, 3), (server.Id, server.Command, server.Args[0], server.Args.Count));
        Assert.StartsWith("NthUi-", server.Args[1], StringComparison.Ordinal);
        var registration = UiMcpBridge.Registration(server.Args[2])!;
        Assert.Equal((Chat, ProviderKind.Codex, "astra-6", scope.Host), (registration.Thread, registration.Provider, registration.Model, registration.Host));
        Assert.Null(UiMcpBridge.Registration(Guid.NewGuid().ToString("N")));
        Assert.Null(UiMcpBridge.Registration(null));

        var connected = JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));
        Assert.Same(connected, MCPProviderConfig.CodexServers(connected, null, null));
        var codex = MCPProviderConfig.CodexServers(connected, null, server)!;
        Assert.Equal(new HashSet<string> { "github", UiMcpServer.ServerName }, codex.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(server.Args.ToArray(), codex[UiMcpServer.ServerName]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!));
        Assert.Equal(new HashSet<string> { UiMcpServer.ServerName }, MCPProviderConfig.CopilotSessionServers(null, server)!.JSONValueObject!.Keys.ToHashSet());
        var acp = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "github")));
        Assert.Equal(new[] { "github", UiMcpServer.ServerName }, MCPProviderConfig.AcpServers(acp, null, server).JSONValueArray!.Select(e => e["name"]!.JSONValueString!));

        using var storage = new StorageScratchFolder();
        var alone = MCPProviderConfig.ClaudeSessionConfig(null, Chat, server)!.Value;
        Assert.False(alone.Strict);
        Assert.StartsWith("nth-", Path.GetFileName(alone.Path), StringComparison.Ordinal);
        Assert.Equal(new[] { UiMcpServer.ServerName }, JSONValue.Parse(File.ReadAllBytes(alone.Path))!["mcpServers"]!.JSONValueObject!.Keys);
    }

    [Fact]
    public void A_Claude_chat_launches_with_nth_ui_in_its_one_config_and_runs_its_tools_without_a_card()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        (string[] Arguments, RecordingClaudeProcess Process, ClaudeSession Session) Launch(Guid? thread)
        {
            RecordingClaudeProcess? call = null;
            using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
            var configuration = Config(ProviderKind.Claude, thread, "sonnet") with { Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!, RuntimeMode = RuntimeMode.Supervised, ResumeID = "fixture-chat" };
            var session = new ClaudeSession(configuration, () => null);
            runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
            return (call!.Arguments, call, session);
        }
        var (off, _, offSession) = Launch(Chat);
        Assert.DoesNotContain("--mcp-config", off); // artifacts off: the launch is what it always was
        offSession.Stop();

        using var scope = new ArtifactHostScope();
        var (plainArgs, plainProcess, plain) = Launch(null);
        Assert.Equal(off, plainArgs); // a session of no chat: byte for byte as before
        var (args, process, session) = Launch(Chat);
        var file = JSONValue.Parse(File.ReadAllBytes(args[Array.IndexOf(args, "--mcp-config") + 1]))!["mcpServers"]!.JSONValueObject!;
        Assert.Equal(new[] { UiMcpServer.ServerName }, file.Keys);
        Assert.DoesNotContain("--strict-mcp-config", args);
        var rest = args.ToList();
        rest.RemoveRange(rest.IndexOf("--mcp-config"), 2);
        Assert.Equal(off, rest); // the one config file is the only change

        JSONValue Ask(string id, string tool) => JSONValue.FromDictionaryLiteral(("request_id", id), ("request", JSONValue.FromDictionaryLiteral(
            ("subtype", "can_use_tool"), ("tool_name", tool), ("input", JSONValue.FromDictionaryLiteral(("kind", "html"))))));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var sentBefore = process.Sent.Count;
        session.HandleControlRequest(Ask("r1", "mcp__nth-ui__artifact_create"));
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        var reply = JSONValue.Parse(process.Sent[sentBefore])!;
        Assert.Equal(("r1", "allow"), (reply["response"]!["request_id"]!.JSONValueString, reply["response"]!["response"]!["behavior"]!.JSONValueString));
        session.HandleControlRequest(Ask("r2", "mcp__nth-ui__artifact_destroy"));
        Assert.Single(events, e => e is ProviderEvent.Approval); // only nth-ui's own artifact tools skip the card
        var plainEvents = new List<ProviderEvent>();
        plain.OnEvent = plainEvents.Add;
        plain.HandleControlRequest(Ask("r3", "mcp__nth-ui__artifact_create"));
        Assert.Single(plainEvents, e => e is ProviderEvent.Approval); // a session with no nth-ui server of its own still asks
        _ = plainProcess;
        session.Stop();
        plain.Stop();
    }

    [Fact]
    public async Task The_stdio_relay_carries_the_token_and_the_JSON_RPC_lines_to_the_chat_s_server()
    {
        using var scope = new ArtifactHostScope();
        var server = MCPProviderConfig.UiServer(Config(ProviderKind.Copilot, Chat))!;
        Assert.Equal(1, await UiMcpBridge.RunClient(["--other", server.Args[1], server.Args[2]], Stream.Null, Stream.Null));
        Assert.Equal(1, await UiMcpBridge.RunClient([UiMcpBridge.Command, "NthUi-not-a-guid", server.Args[2]], Stream.Null, Stream.Null));
        Assert.Equal(1, await UiMcpBridge.RunClient([UiMcpBridge.Command, server.Args[1]], Stream.Null, Stream.Null));

        var relayName = "NthUiTestRelay-" + Guid.NewGuid().ToString("N");
        using var peer = new NamedPipeServerStream(relayName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var streams = new NamedPipeClientStream(".", relayName, PipeDirection.InOut, PipeOptions.Asynchronous);
        var connected = peer.WaitForConnectionAsync();
        await streams.ConnectAsync(5000);
        await connected;
        var run = UiMcpBridge.RunClient([.. server.Args], streams, streams);
        using (var writer = new StreamWriter(peer, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true })
        using (var reader = new StreamReader(peer, Encoding.UTF8, leaveOpen: true))
        {
            await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"artifact_create\",\"arguments\":{\"kind\":\"svg\",\"title\":\"Logo\",\"content\":\"<svg/>\"}}}");
            var created = JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10)))!)!;
            Assert.Equal(1, created["id"]!.GetValue<int>());
            Assert.StartsWith("Created \"logo\" (SVG image), v1.", created["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal("copilot", scope.Store.Read(Chat, "logo").Version.Author.Provider);
        } // a StreamWriter that leaves its stream open flushes on every Dispose, so it goes before the pipe does
        peer.Disconnect(); // the provider goes away: the relay ends cleanly
        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
