using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

// Dextra AS-4 (DESIGN.md rev 5, 0 point 12, 12.3, 15.5; ED-NEW-dextra-as4-7): nth-dextra in every provider's MCP config,
// on the fixtures Tactus's provider tests use. A chat that is not Dextra's launches exactly as before; a Dextra chat
// carries the entry beside the connected servers and the advisor, in each provider's own form, and the provider's own
// prompt for an nth-dextra call is answered at once (the server asks the policy engine itself), only in that session.
public sealed class AssistantProviderInjectionTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-inst-u7-1")]
    public void The_dextra_relay_uses_the_running_executable_and_preserves_a_scoped_override()
    {
        var host = new DextraFakeHost();
        var thread = host.Add();
        using var bound = AssistantMcpBridge.Host.Override(host);
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Codex, thread);
        try
        {
            Assert.Equal(AppExecutable.Path, Assert.IsType<MCPResolvedServer>(AssistantMcpBridge.Server(configuration)).Command);
            using (AssistantMcpBridge.Executable.Override(() => "C:/fixture/nth-relay.exe"))
                Assert.Equal("C:/fixture/nth-relay.exe", Assert.IsType<MCPResolvedServer>(AssistantMcpBridge.Server(configuration)).Command);
            Assert.Equal(AppExecutable.Path, Assert.IsType<MCPResolvedServer>(AssistantMcpBridge.Server(configuration)).Command);
        }
        finally { AssistantMcpBridge.Forget(thread); }
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-7")]
    public void A_chat_that_is_not_dextras_gets_no_entry_and_every_shape_stays_as_it_was()
    {
        using var storage = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var known = host.Add();
        foreach (var provider in BuiltInServerFixtures.Providers)
        {
            Assert.Null(MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(provider, known))); // no host bound
            using var bound = AssistantMcpBridge.Host.Override(host);
            Assert.Null(MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(provider, Guid.NewGuid())));
            Assert.Null(MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(provider, null)));
            Assert.Empty(MCPProviderConfig.BuiltInServers(BuiltInServerFixtures.Config(provider, Guid.NewGuid())));
        }
        host.IdentifyThrows = true;
        using (AssistantMcpBridge.Host.Override(host)) Assert.Null(MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(ProviderKind.Codex, known)));

        var none = MCPProviderConfig.BuiltInServers(null, null);
        var codex = BuiltInServerFixtures.CodexConnected();
        var acp = BuiltInServerFixtures.AcpConnected();
        Assert.Empty(none);
        Assert.Same(codex, MCPProviderConfig.CodexBuiltIns(codex, none));
        Assert.Null(MCPProviderConfig.CodexBuiltIns(null, none));
        Assert.Null(MCPProviderConfig.CopilotBuiltIns(none));
        Assert.Same(acp, MCPProviderConfig.AcpBuiltIns(acp, none));
        Assert.Null(MCPProviderConfig.GeminiSessionSettings(none, Guid.NewGuid()));
        Assert.Null(MCPProviderConfig.ClaudeBuiltInConfig(none, Guid.NewGuid()));
        Assert.Empty(Directory.Exists(MCPPaths.Directory) ? Directory.GetFiles(MCPPaths.Directory) : []);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-7")]
    public void A_dextra_chat_carries_the_entry_in_each_providers_form_beside_the_connected_servers_and_the_advisor()
    {
        var host = new DextraFakeHost();
        var thread = host.Add();
        using var bound = AssistantMcpBridge.Host.Override(host);
        foreach (var provider in BuiltInServerFixtures.Providers)
        {
            var entry = Assert.IsType<MCPResolvedServer>(MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(provider, thread)));
            Assert.Equal(("nth-dextra", AssistantMcpBridge.Executable.Current(), AssistantMcpBridge.Command), (entry.Id, entry.Command, entry.Args[0]));
            var registration = AssistantMcpBridge.Registration(AssistantMcpBridge.Token(entry))!;
            Assert.Equal((thread, "dextra", provider, "fixture-model"), (registration.Chat.Thread, registration.Chat.AssistantId, registration.Chat.Provider, registration.Chat.Model));
            Assert.Same(host, registration.Host);
        }
        var dextra = MCPProviderConfig.DextraServer(BuiltInServerFixtures.Config(ProviderKind.Codex, thread))!;
        var builtIns = MCPProviderConfig.BuiltInServers(BuiltInServerFixtures.Advisor, dextra);
        Assert.Equal(new[] { "nth-advisor", "nth-dextra" }, builtIns.Select(s => s.Id));
        Assert.Equal(new[] { "nth-advisor", "nth-dextra" }, MCPProviderConfig.BuiltInServers(null, BuiltInServerFixtures.Advisor, null, dextra).Select(s => s.Id));

        var codex = MCPProviderConfig.CodexBuiltIns(BuiltInServerFixtures.CodexConnected(), builtIns)!;
        Assert.Equal(new HashSet<string> { "github", "nth-advisor", "nth-dextra" }, codex.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(dextra.Args.ToArray(), codex["nth-dextra"]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!));
        Assert.Equal(MCPProviderConfig.CodexServers(BuiltInServerFixtures.CodexConnected(), BuiltInServerFixtures.Advisor)!.CompactString,
            MCPProviderConfig.CodexBuiltIns(BuiltInServerFixtures.CodexConnected(), [BuiltInServerFixtures.Advisor])!.CompactString);

        var copilot = MCPProviderConfig.CopilotBuiltIns(builtIns)!;
        Assert.Equal(new HashSet<string> { "nth-advisor", "nth-dextra" }, copilot.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(("local", "*"), (copilot["nth-dextra"]!["type"]!.JSONValueString, copilot["nth-dextra"]!["tools"]![0]!.JSONValueString));

        var acp = MCPProviderConfig.AcpBuiltIns(BuiltInServerFixtures.AcpConnected(), builtIns).JSONValueArray!;
        Assert.Equal(new[] { "github", "nth-advisor", "nth-dextra" }, acp.Select(e => e["name"]!.JSONValueString!));
        Assert.Equal(dextra.Command, acp[2]["command"]!.JSONValueString);
        Assert.Empty(acp[2]["env"]!.JSONValueArray!);
        AssistantMcpBridge.Forget(thread);
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-7")]
    public void Claude_the_js_bridge_and_antigravity_get_one_file_per_chat_with_the_entry_and_the_shared_files_untouched()
    {
        using var storage = new StorageScratchFolder();
        var dextra = new MCPResolvedServer("nth-dextra", "nth-dextra") { Command = "C:/nth/nth.exe", Args = new ValueList<string>([AssistantMcpBridge.Command, "pipe", "token"]) };
        var thread = Guid.NewGuid();

        var alone = Assert.IsType<(string Path, bool Strict)>(MCPProviderConfig.ClaudeBuiltInConfig([dextra], thread));
        Assert.False(alone.Strict);
        Assert.Equal(new[] { "nth-dextra" }, JSONValue.Parse(File.ReadAllBytes(alone.Path))!["mcpServers"]!.JSONValueObject!.Keys);
        var gemini = JSONValue.Parse(File.ReadAllBytes(MCPProviderConfig.GeminiSessionSettings([dextra], thread)!))!["mcpServers"]!;
        Assert.True(gemini["nth-dextra"]!["trust"]!.JSONValueBool);

        // A connected server that calls itself nth-dextra is replaced by nth's own entry, never merged with it.
        MCPProviderConfig.WriteAll([BuiltInServerFixtures.Connected, new MCPResolvedServer("nth-dextra", "nth-dextra") { Url = "https://imposter.example/mcp" }]);
        var shared = (Claude: File.ReadAllBytes(MCPPaths.ClaudeConfigURL), Gemini: File.ReadAllBytes(MCPProviderConfig.GeminiSettingsPath()!));
        var merged = Assert.IsType<(string Path, bool Strict)>(MCPProviderConfig.ClaudeBuiltInConfig([BuiltInServerFixtures.Advisor, dextra], thread));
        Assert.True(merged.Strict);
        Assert.NotEqual(MCPPaths.ClaudeConfigURL, merged.Path);
        var servers = JSONValue.Parse(File.ReadAllBytes(merged.Path))!["mcpServers"]!;
        Assert.Equal(new HashSet<string> { "github", "nth-advisor", "nth-dextra" }, servers.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(("C:/nth/nth.exe", (string?)null), (servers["nth-dextra"]!["command"]!.JSONValueString, servers["nth-dextra"]!["url"]?.JSONValueString));
        var settings = JSONValue.Parse(File.ReadAllBytes(MCPProviderConfig.GeminiSessionSettings([dextra], thread)!))!["mcpServers"]!;
        Assert.Equal(new HashSet<string> { "github", "nth-dextra" }, settings.JSONValueObject!.Keys.ToHashSet());
        Assert.Null(settings["github"]!["trust"]);
        Assert.Equal(("C:/nth/nth.exe", true), (settings["nth-dextra"]!["command"]!.JSONValueString, settings["nth-dextra"]!["trust"]!.JSONValueBool));
        Assert.Equal(shared.Claude, File.ReadAllBytes(MCPPaths.ClaudeConfigURL));
        Assert.Equal(shared.Gemini, File.ReadAllBytes(MCPProviderConfig.GeminiSettingsPath()!));
        Assert.Equal(MCPProviderConfig.ClaudeSessionConfig(null, thread), MCPProviderConfig.ClaudeBuiltInConfig([], thread));
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-7")]
    public void Each_providers_prompt_is_answered_only_for_a_known_nth_dextra_tool()
    {
        Assert.True(AssistantProviderWiring.IsClaudeTool("mcp__nth-dextra__recall"));
        Assert.True(AssistantProviderWiring.IsClaudeTool("mcp__nth-dextra__list_sessions"));
        foreach (var name in new[] { "mcp__nth-dextra__grant_everything", "mcp__nth-dextrax__recall", "mcp__nth-advisor__ask_advisor", "Bash", "recall", null })
            Assert.False(AssistantProviderWiring.IsClaudeTool(name), name);

        var options = new[] { JSONValue.FromDictionaryLiteral(("kind", "reject_once"), ("optionId", "no")), JSONValue.FromDictionaryLiteral(("kind", "allow_once"), ("optionId", "yes")) };
        JSONValue Call(string kind, string title) => JSONValue.FromDictionaryLiteral(("kind", kind), ("title", title));
        foreach (var title in new[] { "nth-dextra-recall: recall", "nth-dextra_recall", "mcp__nth-dextra__recall", "nth-dextra/recall", "nth-dextra: recall", "nth-dextra.recall (query)" })
            Assert.Equal("yes", AssistantProviderWiring.AcpAutomaticOption(Call("other", title), options));
        foreach (var (kind, title) in new[] { ("execute", "nth-dextra: recall"), ("edit", "mcp__nth-dextra__recall"), ("other", "nth-dextra: grant_everything"),
                     ("other", "rm -rf nth-dextra: recall"), ("other", "nth-pc: pc_observe"), ("other", "nth-dextrarecall") })
            Assert.Null(AssistantProviderWiring.AcpAutomaticOption(Call(kind, title), options));
        Assert.Null(AssistantProviderWiring.AcpAutomaticOption(Call("other", "nth-dextra: recall"), [options[0]]));

        Assert.True(AssistantProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "MCP"), ("serverName", "nth-dextra"))));
        Assert.False(AssistantProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "shell"), ("serverName", "nth-dextra"))));
        Assert.False(AssistantProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", "github"))));
        Assert.Equal("approve-once", AssistantProviderWiring.CopilotApproval()["kind"]!.JSONValueString);
        Assert.True(AssistantProviderWiring.IsCodexElicitation(JSONValue.FromDictionaryLiteral(("serverName", "nth-dextra"))));
        Assert.False(AssistantProviderWiring.IsCodexElicitation(JSONValue.FromDictionaryLiteral(("serverName", "nth-advisor"))));
        Assert.Equal("accept", AssistantProviderWiring.CodexElicitationAccept()["action"]!.JSONValueString);
    }

    private static (string[] Arguments, RecordingClaudeProcess Process, ClaudeSession? Session) LaunchClaude(TestRuntime runtime, Guid? thread, bool legacy)
    {
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Claude, thread) with
        {
            Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!, WorkingDirectory = SwiftURL.Create("file:///C:/dextra-test")!, ResumeID = "fixture-chat", Model = "sonnet",
        };
        if (legacy)
        {
            var old = new LegacyClaudeSession(configuration);
            runtime.Loop.Run(() => old.Start(), TimeSpan.FromSeconds(5));
            old.Stop();
            return (call!.Arguments, call, null);
        }
        var session = new ClaudeSession(configuration, () => null);
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        return (call!.Arguments, call, session);
    }

    private static string[] WithoutMcpConfig(string[] arguments)
    {
        var list = arguments.ToList();
        var at = list.IndexOf("--mcp-config");
        if (at < 0) return [.. list];
        list.RemoveRange(at, at + 2 < list.Count && list[at + 2] == "--strict-mcp-config" ? 3 : 2);
        return [.. list];
    }

    [Fact]
    [Trait("Feature", "AS-4")]
    [Trait("Decision", "ED-NEW-dextra-as4-7")]
    public void ClaudeSession_launches_byte_identical_for_other_chats_and_a_dextra_chat_runs_its_own_tools_without_a_card()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var thread = host.Add();
        using var bound = AssistantMcpBridge.Host.Override(host);

        var today = LaunchClaude(runtime, Guid.NewGuid(), legacy: true).Arguments;
        var (plainArguments, _, plain) = LaunchClaude(runtime, Guid.NewGuid(), legacy: false);
        Assert.Equal(today, plainArguments);

        var (arguments, process, session) = LaunchClaude(runtime, thread, legacy: false);
        Assert.Single(arguments, a => a == "--mcp-config");
        Assert.DoesNotContain("--allowedTools", arguments);
        Assert.Equal(WithoutMcpConfig(today), WithoutMcpConfig(arguments));
        var file = JSONValue.Parse(File.ReadAllBytes(arguments[Array.IndexOf(arguments, "--mcp-config") + 1]))!["mcpServers"]!.JSONValueObject!;
        Assert.Equal(new[] { "nth-dextra" }, file.Keys);

        JSONValue Ask(string id, string tool) => JSONValue.FromDictionaryLiteral(("request_id", id), ("request", JSONValue.FromDictionaryLiteral(
            ("subtype", "can_use_tool"), ("tool_name", tool), ("input", JSONValue.FromDictionaryLiteral(("query", "q"))))));
        var events = new List<ProviderEvent>();
        session!.OnEvent = events.Add;
        var sentBefore = process.Sent.Count;
        session.HandleControlRequest(Ask("r1", "mcp__nth-dextra__recall"));
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        var reply = JSONValue.Parse(process.Sent[sentBefore])!;
        Assert.Equal(("r1", "allow"), (reply["response"]!["request_id"]!.JSONValueString, reply["response"]!["response"]!["behavior"]!.JSONValueString));
        session.HandleControlRequest(Ask("r2", "mcp__nth-dextra__grant_everything"));
        Assert.Single(events, e => e is ProviderEvent.Approval);

        var plainEvents = new List<ProviderEvent>();
        plain!.OnEvent = plainEvents.Add;
        plain.HandleControlRequest(Ask("r3", "mcp__nth-dextra__recall"));
        Assert.Single(plainEvents, e => e is ProviderEvent.Approval);
        session.Stop();
        plain.Stop();
        AssistantMcpBridge.Forget(thread);
    }
}
