// PORT-ONLY: Tactus (ED-NEW-pcctl-41; DESIGN.md 3.4, 4.1). nth's own per-app approval governs the nth-pc tools, so each
// CLI and ACP provider's own permission prompt for an nth-pc call is answered at once, and only in a session that was
// given the nth-pc server: any other tool, any other server, a look-alike shell command, and the same call in a session
// launched while Tactus was off all still reach the user. The sessions are built from recorded request shapes and never
// started against a provider (Claude's runs on the offline recording transport).
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Models;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-41")]
public sealed class PcProviderPromptsTests
{
    private static readonly MCPResolvedServer Pc = new(PcToolSchemas.ServerName, PcToolSchemas.ServerName) { Command = "C:/nth/nth.exe", Args = new ValueList<string>(["--marrow-pc-mcp", "pipe", "token"]) };

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread = null) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/pc-wire-work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = "fixture-model",
        IgnisThreadID = thread ?? Guid.NewGuid(),
    };

    private static JSONValue Options() => JSONValue.FromArrayLiteral(
        JSONValue.FromDictionaryLiteral(("optionId", "once"), ("kind", "allow_once"), ("name", "Allow")),
        JSONValue.FromDictionaryLiteral(("optionId", "always"), ("kind", "allow_always"), ("name", "Always")),
        JSONValue.FromDictionaryLiteral(("optionId", "no"), ("kind", "reject_once"), ("name", "Reject")));

    [Fact]
    public void The_wiring_names_only_the_servers_own_tools()
    {
        Assert.True(PcProviderWiring.IsClaudeTool("mcp__nth-pc__pc_observe"));
        Assert.True(PcProviderWiring.IsClaudeTool("mcp__nth-pc__pc_delegate"));
        Assert.False(PcProviderWiring.IsClaudeTool("mcp__nth-pc__rm_rf"));
        Assert.False(PcProviderWiring.IsClaudeTool("mcp__github__pc_observe"));
        Assert.False(PcProviderWiring.IsClaudeTool("Bash"));
        Assert.False(PcProviderWiring.IsClaudeTool(null));

        foreach (var title in new[] { "nth-pc-pc_observe: pc_observe", "nth-pc_pc_click", "mcp__nth-pc__pc_type", "nth-pc/pc_key", "nth-pc: pc_scroll", "nth-pc.pc_batch (3 steps)" })
            Assert.NotNull(PcProviderWiring.AcpTool(title));
        foreach (var title in new[] { "nth-pc-rm_rf: rm_rf", "github-pc_observe: pc_observe", "echo nth-pc-pc_observe", "nth-pcpc_observe", "nth-pc-pc_observex", "", null })
            Assert.Null(PcProviderWiring.AcpTool(title));

        var options = Options().JSONValueArray!;
        JSONValue Call(string kind, string title) => JSONValue.FromDictionaryLiteral(("toolCallId", "c1"), ("kind", kind), ("title", title));
        Assert.Equal("once", PcProviderWiring.AcpAutomaticOption(Call("other", "nth-pc-pc_observe: pc_observe"), options));
        Assert.Null(PcProviderWiring.AcpAutomaticOption(Call("execute", "nth-pc-pc_observe: pc_observe"), options));
        Assert.Null(PcProviderWiring.AcpAutomaticOption(Call("edit", "nth-pc-pc_type: pc_type"), options));
        Assert.Null(PcProviderWiring.AcpAutomaticOption(Call("other", "github-search: search"), options));

        Assert.True(PcProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", "nth-pc"), ("toolName", "pc_observe"))));
        Assert.False(PcProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", "github"), ("toolName", "pc_observe"))));
        Assert.False(PcProviderWiring.IsCopilotRequest(JSONValue.FromDictionaryLiteral(("kind", "shell"), ("serverName", "nth-pc"))));
        Assert.True(PcProviderWiring.IsCodexElicitation(JSONValue.FromDictionaryLiteral(("serverName", "nth-pc"))));
        Assert.False(PcProviderWiring.IsCodexElicitation(JSONValue.FromDictionaryLiteral(("serverName", "files-mcp"))));
        Assert.Equal("accept", PcProviderWiring.CodexElicitationAccept()["action"]!.JSONValueString);
        Assert.Equal("approve-once", PcProviderWiring.CopilotApproval()["kind"]!.JSONValueString);
    }

    [Fact]
    public void Codex_carries_nth_pc_with_its_timeout_and_turns_its_own_computer_use_off_only_while_tactus_is_on()
    {
        using var storage = new StorageScratchFolder();
        var off = new CodexSession(Config(ProviderKind.Codex));
        var plain = PcSessionPokes.Get<Dictionary<string, JSONValue>>(off, "ThreadParameters");
        Assert.False(plain.ContainsKey("config") && plain["config"].JSONValueObject!.Keys.Any(k => k.StartsWith("plugins.", StringComparison.Ordinal) || k == "mcp_servers"));
        Assert.Null(PcSessionPokes.Get<MCPResolvedServer?>(off, "pcServer"));

        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        var thread = Guid.NewGuid();
        var on = new CodexSession(Config(ProviderKind.Codex, thread));
        var config = PcSessionPokes.Get<Dictionary<string, JSONValue>>(on, "ThreadParameters")["config"];

        Assert.Equal(120L, config["mcp_servers"]!["nth-pc"]!["tool_timeout_sec"]!.JSONValueInt);
        Assert.Equal(PcControlBridge.Command, config["mcp_servers"]!["nth-pc"]!["args"]![0]!.JSONValueString);
        Assert.False(config["plugins.computer-use@openai-bundled.enabled"]!.JSONValueBool);
        Assert.False(config["plugins.unified-computer-use@openai-bundled.enabled"]!.JSONValueBool);
        var server = PcSessionPokes.Get<MCPResolvedServer>(on, "pcServer");
        PcSessionPokes.Get<Dictionary<string, JSONValue>>(on, "ThreadParameters");
        Assert.Same(server, PcSessionPokes.Get<MCPResolvedServer>(on, "pcServer"));
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public void Codex_accepts_only_the_elicitation_of_nth_pc_and_only_in_a_session_that_has_it()
    {
        using var storage = new StorageScratchFolder();
        var withPc = new CodexSession(Config(ProviderKind.Codex));
        PcSessionPokes.Set(withPc, "pcServer", Pc);
        var without = new CodexSession(Config(ProviderKind.Codex));
        var events = new List<ProviderEvent>();
        var plainEvents = new List<ProviderEvent>();
        withPc.OnEvent = events.Add;
        without.OnEvent = plainEvents.Add;
        var nthPc = JSONValue.FromDictionaryLiteral(("serverName", "nth-pc"), ("message", "Allow pc_observe?"));

        PcSessionPokes.Call(withPc, "HandleRequest", new RPCID.Int(4), "mcpServer/elicitation/request", nthPc);
        Assert.Empty(events);
        PcSessionPokes.Call(withPc, "HandleRequest", new RPCID.Int(5), "mcpServer/elicitation/request", JSONValue.FromDictionaryLiteral(("serverName", "files-mcp")));
        Assert.Equal("files-mcp asked for input, which nth cannot show yet.", Assert.Single(events.OfType<ProviderEvent.Notice>()).Value.Message);
        PcSessionPokes.Call(without, "HandleRequest", new RPCID.Int(6), "mcpServer/elicitation/request", nthPc);
        Assert.Equal("nth-pc asked for input, which nth cannot show yet.", Assert.Single(plainEvents.OfType<ProviderEvent.Notice>()).Value.Message);
    }

    [Fact]
    public async Task Acp_offers_nth_pc_in_session_new_and_answers_its_own_prompt_for_an_nth_pc_call()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        var thread = Guid.NewGuid();
        var session = new ACPSession(Config(ProviderKind.Cursor, thread));
        JSONValue? sent = null;
        using (PcControlBridge.Parts.Override(() => assembly.Shared))
        {
            await (Task<JSONValue>)PcSessionPokes.Call(session, "OpenSessionCore", new Func<string, JSONValue, Task<JSONValue>>((method, parameters) =>
            {
                if (method == "session/new") sent = parameters;
                return Task.FromResult(JSONValue.FromDictionaryLiteral(("sessionId", "s-1")));
            }))!;
        }
        var entry = Assert.Single(sent!["mcpServers"]!.JSONValueArray!, e => e["name"]!.JSONValueString == "nth-pc");
        Assert.Equal(PcControlBridge.Command, entry["args"]![0]!.JSONValueString);

        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        JSONValue Ask(string kind, string title) => JSONValue.FromDictionaryLiteral(("sessionId", "s-1"), ("options", Options()),
            ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "c-" + title.Length), ("kind", kind), ("title", title), ("status", "pending"))));
        PcSessionPokes.Call(session, "HandleRequest", new RPCID.Int(1), "session/request_permission", Ask("other", "nth-pc-pc_observe: pc_observe"));
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        PcSessionPokes.Call(session, "HandleRequest", new RPCID.Int(2), "session/request_permission", Ask("execute", "nth-pc-pc_observe: pc_observe"));
        PcSessionPokes.Call(session, "HandleRequest", new RPCID.Int(3), "session/request_permission", Ask("other", "github-search: search"));
        Assert.Equal(2, events.Count(e => e is ProviderEvent.Approval));

        var off = new ACPSession(Config(ProviderKind.Cursor));
        var offEvents = new List<ProviderEvent>();
        off.OnEvent = offEvents.Add;
        PcSessionPokes.Call(off, "HandleRequest", new RPCID.Int(1), "session/request_permission", Ask("other", "nth-pc-pc_observe: pc_observe"));
        Assert.Single(offEvents, e => e is ProviderEvent.Approval);
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public void Copilot_merges_nth_pc_into_its_session_servers_and_approves_only_nth_pc_calls_itself()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var plain = (Dictionary<string, JSONValue>)CopilotSessionHarness.Invoke(new CopilotSession(CopilotSessionHarness.Config()), "SessionParameters")!;
        Assert.False(plain.ContainsKey("mcpServers"));

        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        var thread = Guid.NewGuid();
        var configuration = CopilotSessionHarness.Config() with { IgnisThreadID = thread };
        Dictionary<string, JSONValue> parameters;
        CopilotSession session;
        using (PcControlBridge.Parts.Override(() => assembly.Shared))
        {
            session = new CopilotSession(configuration);
            parameters = (Dictionary<string, JSONValue>)CopilotSessionHarness.Invoke(session, "SessionParameters")!;
        }
        Assert.Equal("local", parameters["mcpServers"]["nth-pc"]!["type"]!.JSONValueString);

        CopilotSessionHarness.SetField(session, "sessionID", "s-1");
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        JSONValue Ask(string id, string kind, string server) => JSONValue.FromDictionaryLiteral(("requestId", id),
            ("permissionRequest", JSONValue.FromDictionaryLiteral(("kind", kind), ("serverName", server), ("toolName", "pc_observe"), ("toolCallId", "t-" + id))));
        CopilotSessionHarness.Invoke(session, "HandlePermission", Ask("p1", "mcp", "nth-pc"));
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        CopilotSessionHarness.Invoke(session, "HandlePermission", Ask("p2", "mcp", "github"));
        Assert.Single(events, e => e is ProviderEvent.Approval);

        var (off, offEvents) = CopilotSessionHarness.Offline();
        CopilotSessionHarness.Invoke(off, "HandlePermission", Ask("p3", "mcp", "nth-pc"));
        Assert.Single(offEvents, e => e is ProviderEvent.Approval);
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public void Command_Code_and_Pi_let_an_nth_pc_call_through_their_gates_only_with_the_server()
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var commandCode = new CommandCodeSession(Config(ProviderKind.Commandcode));
        var commandCodeEvents = new List<ProviderEvent>();
        commandCode.OnEvent = commandCodeEvents.Add;
        var observe = JSONValue.FromDictionaryLiteral(("toolCallId", "c1"), ("toolName", "mcp__nth-pc__pc_observe"), ("input", JSONValue.FromDictionaryLiteral(("window", "w:1a"))));

        PcSessionPokes.Call(commandCode, "Decide", "c1", observe);
        Assert.Single(commandCodeEvents, e => e is ProviderEvent.Approval);
        PcSessionPokes.Set(commandCode, "pcTools", true);
        PcSessionPokes.Call(commandCode, "Decide", "c2", observe);
        PcSessionPokes.Call(commandCode, "Decide", "c3", JSONValue.FromDictionaryLiteral(("toolCallId", "c3"), ("toolName", "mcp__github__search"), ("input", JSONValue.FromDictionaryLiteral())));
        Assert.Equal(2, commandCodeEvents.Count(e => e is ProviderEvent.Approval));

        var pi = new PiSession(Config(ProviderKind.Pi));
        var piEvents = new List<ProviderEvent>();
        pi.OnEvent = piEvents.Add;
        JSONValue Gate(string tool) => JSONValue.FromDictionaryLiteral(("title", "droppy-gate " + Encoding.UTF8.GetString(JSONValue.FromDictionaryLiteral(
            ("kind", "tool"), ("toolName", tool), ("input", JSONValue.FromDictionaryLiteral())).Data())), ("options", JSONValue.FromArrayLiteral("Allow", "Deny")));
        PcSessionPokes.Call(pi, "HandleSelect", "g1", Gate("mcp__nth-pc__pc_observe"));
        Assert.Single(piEvents, e => e is ProviderEvent.Approval);
        PcSessionPokes.Set(pi, "pcTools", true);
        PcSessionPokes.Call(pi, "HandleSelect", "g2", Gate("mcp__nth-pc__pc_observe"));
        PcSessionPokes.Call(pi, "HandleSelect", "g3", Gate("mcp__github__search"));
        Assert.Equal(2, piEvents.Count(e => e is ProviderEvent.Approval));
    }

    [Fact]
    public void Claude_merges_nth_pc_into_its_one_config_and_allows_only_nth_pc_tools_without_a_card()
    {
        using var runtime = new TestRuntime();
        using var help = ClaudeProviderRuntime.Help.Override((_, _) => Task.FromResult(new ShellResult(0, Encoding.UTF8.GetBytes("--system-prompt-snapshot"), [])));
        using var storage = new StorageScratchFolder();
        var (offArgs, _, off) = LaunchClaude(runtime, Guid.NewGuid());
        Assert.DoesNotContain("--mcp-config", offArgs);

        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        var thread = Guid.NewGuid();
        string[] args;
        RecordingClaudeProcess process;
        ClaudeSession session;
        using (PcControlBridge.Parts.Override(() => assembly.Shared)) (args, process, session) = LaunchClaude(runtime, thread);
        var file = JSONValue.Parse(File.ReadAllBytes(args[Array.IndexOf(args, "--mcp-config") + 1]))!["mcpServers"]!.JSONValueObject!;
        Assert.Equal(new[] { "nth-pc" }, file.Keys);
        Assert.DoesNotContain("--strict-mcp-config", args);

        JSONValue Ask(string id, string tool) => JSONValue.FromDictionaryLiteral(("request_id", id), ("request", JSONValue.FromDictionaryLiteral(
            ("subtype", "can_use_tool"), ("tool_name", tool), ("input", JSONValue.FromDictionaryLiteral(("window", "w:1a"))))));
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        var before = process.Sent.Count;
        session.HandleControlRequest(Ask("r1", "mcp__nth-pc__pc_observe"));
        Assert.DoesNotContain(events, e => e is ProviderEvent.Approval);
        var reply = JSONValue.Parse(process.Sent[before])!;
        Assert.Equal(("r1", "allow"), (reply["response"]!["request_id"]!.JSONValueString, reply["response"]!["response"]!["behavior"]!.JSONValueString));
        session.HandleControlRequest(Ask("r2", "mcp__nth-pc__not_a_tool"));
        session.HandleControlRequest(Ask("r3", "mcp__github__search"));
        Assert.Equal(2, events.Count(e => e is ProviderEvent.Approval));

        var offEvents = new List<ProviderEvent>();
        off.OnEvent = offEvents.Add;
        off.HandleControlRequest(Ask("r4", "mcp__nth-pc__pc_observe"));
        Assert.Single(offEvents, e => e is ProviderEvent.Approval);
        session.Stop();
        off.Stop();
        PcControlBridge.Forget(thread.ToString("D"));
    }

    private static (string[] Arguments, RecordingClaudeProcess Process, ClaudeSession Session) LaunchClaude(TestRuntime runtime, Guid thread)
    {
        RecordingClaudeProcess? call = null;
        using var transport = ClaudeProviderRuntime.Process.Override((_, args, _, env) => call = new(args, env));
        var configuration = Config(ProviderKind.Claude, thread) with { Executable = SwiftURL.Create("/offline/claude-" + Guid.NewGuid().ToString("N"))!, ResumeID = "fixture-chat" };
        var session = new ClaudeSession(configuration, () => null);
        runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(5));
        return (call!.Arguments, call, session);
    }
}
