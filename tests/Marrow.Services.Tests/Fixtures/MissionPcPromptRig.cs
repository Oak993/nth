using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Port.Skills.Providers;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// ED-NEW-mission-pc-4: real handlers and wire replies, with no process ever started.
public sealed class MissionPcPromptRig : IDisposable
{
    private readonly TestRuntime runtime = new();
    private readonly RecordingClaudeProcess? claude;
    private readonly StdioProcess? transport;
    private readonly JSONRPCConnection? connection;
    private readonly string replyFile;
    public ProviderSession Session { get; }
    public List<ProviderEvent> Events { get; } = [];
    public Action<ProviderEvent>? OnEvent { get; set; }

    public MissionPcPromptRig(SessionConfiguration config, string folder)
    {
        replyFile = Path.Combine(folder, "r1.reply.json");
        Session = config.Provider switch
        {
            ProviderKind.Claude => new ClaudeSession(config),
            ProviderKind.Antigravity => new AntigravitySession(config),
            ProviderKind.Codex => new CodexSession(config),
            ProviderKind.Pi => new PiSession(config),
            ProviderKind.Commandcode => new CommandCodeSession(config),
            ProviderKind.Copilot => new CopilotSession(config),
            _ => new ACPSession(config),
        };
        Session.OnEvent = e => { Events.Add(e); OnEvent?.Invoke(e); };
        // Antigravity is headless: it reports a tool step and has no permission request/reply.
        if (Session is AntigravitySession) return;
        if (Session is PiSession or CommandCodeSession) PcSessionPokes.Set(Session, "pcTools", true);
        else PcSessionPokes.Set(Session, "pcServer", new MCPResolvedServer("nth-pc", "nth-pc") { Command = "offline" });
        if (Session is ClaudeSession)
        {
            claude = new RecordingClaudeProcess([], new Dictionary<string, string>());
            PcSessionPokes.Set(Session, "process", claude);
        }
        else if (Session is CommandCodeSession)
        {
            Directory.CreateDirectory(folder);
            PcSessionPokes.Set(Session, "approvalDirectory", CommandCodeSessionRig.NativeUrl(folder));
        }
        else
        {
            transport = new StdioProcess("never-started", [], folder, new Dictionary<string, string>());
            PcSessionPokes.Set(transport, "writeQueue", new SerialQueue("mission-pc-test", runtime.Loop));
            if (Session is PiSession) PcSessionPokes.Set(Session, "process", transport);
            else
            {
                connection = new JSONRPCConnection(transport, true);
                PcSessionPokes.Set(Session, "connection", connection);
                if (Session is CopilotSession) PcSessionPokes.Set(Session, "sessionID", "fake-session");
            }
        }
    }

    public void Prompt(string tool, JSONValue input)
    {
        var payload = JSONValue.FromDictionaryLiteral(("kind", "tool"), ("toolName", "mcp__nth-pc__" + tool), ("input", input));
        runtime.OnMain(() =>
        {
            switch (Session)
            {
                case ClaudeSession c:
                    c.HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "r1"),
                        ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "mcp__nth-pc__" + tool), ("input", input)))));
                    break;
                case AntigravitySession:
                    PcSessionPokes.Call(Session, "Handle", JSONValue.FromDictionaryLiteral(("event", "step_update"),
                        ("step_update", JSONValue.FromDictionaryLiteral(("step_type", "tool"), ("step_index", 1), ("state", "ACTIVE"),
                            ("tool_name", "mcp__nth-pc__" + tool), ("tool_info", JSONValue.FromDictionaryLiteral(("parameters", input)))))));
                    break;
                case CodexSession:
                    PcSessionPokes.Call(Session, "HandleRequest", new RPCID.Int(1), "mcpServer/elicitation/request",
                        JSONValue.FromDictionaryLiteral(("serverName", "nth-pc"), ("toolName", tool), ("message", "Allow this call?")));
                    break;
                case ACPSession:
                    PcSessionPokes.Call(Session, "HandleRequest", new RPCID.Int(1), "session/request_permission", JSONValue.FromDictionaryLiteral(
                        ("options", JSONValue.FromArrayLiteral(
                            JSONValue.FromDictionaryLiteral(("optionId", "yes"), ("kind", "allow_once"), ("name", "Allow")),
                            JSONValue.FromDictionaryLiteral(("optionId", "no"), ("kind", "reject_once"), ("name", "No")))),
                        ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "c1"), ("kind", "other"), ("title", "nth-pc-" + tool + ": " + tool)))));
                    break;
                case CommandCodeSession: PcSessionPokes.Call(Session, "Decide", "r1", payload); break;
                case PiSession:
                    PcSessionPokes.Call(Session, "HandleSelect", "r1", JSONValue.FromDictionaryLiteral(
                        ("title", "droppy-gate " + Encoding.UTF8.GetString(payload.Data())), ("options", JSONValue.FromArrayLiteral("Allow", "Deny"))));
                    break;
                case CopilotSession:
                    PcSessionPokes.Call(Session, "HandlePermission", JSONValue.FromDictionaryLiteral(("requestId", "r1"), ("resolvedByHook", false),
                        ("permissionRequest", JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", "nth-pc"), ("toolName", tool), ("toolCallId", "c1")))));
                    break;
            }
        });
    }

    public void AssertAccepted()
    {
        runtime.RunUntilIdle();
        if (Session is AntigravitySession)
        {
            Assert.Single(Events.OfType<ProviderEvent.ToolStarted>());
            Assert.Empty(Events.OfType<ProviderEvent.Approval>());
            return;
        }
        JSONValue? reply;
        if (claude is not null) reply = claude.Sent.Select(b => JSONValue.Parse(b)).SingleOrDefault();
        else if (Session is CommandCodeSession) reply = File.Exists(replyFile) ? JSONValue.Parse(File.ReadAllBytes(replyFile)) : null;
        else
        {
            var pipe = PcSessionPokes.Get<ProcessPipe>(transport!, "stdin");
            pipe.CloseWriting();
            reply = Encoding.UTF8.GetString(pipe.ReadDataToEndOfFile()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => JSONValue.Parse(line)).SingleOrDefault();
        }
        Assert.NotNull(reply);
        switch (Session)
        {
            case ClaudeSession: Assert.Equal("allow", reply["response"]?["response"]?["behavior"]?.JSONValueString); break;
            case CodexSession: Assert.Equal("accept", reply["result"]?["action"]?.JSONValueString); break;
            case ACPSession: Assert.Equal("yes", reply["result"]?["outcome"]?["optionId"]?.JSONValueString); break;
            case CommandCodeSession: Assert.Equal("allow", reply["decision"]?.JSONValueString); break;
            case PiSession: Assert.Equal("Allow", reply["value"]?.JSONValueString); break;
            case CopilotSession:
                Assert.Equal("approve-once", reply["params"]?["result"]?["kind"]?.JSONValueString);
                runtime.OnMain(() => PcSessionPokes.Call(connection!, "Handle",
                    JSONValue.FromDictionaryLiteral(("id", reply["id"]!), ("result", JSONValue.FromDictionaryLiteral()))));
                break;
        }
    }

    public void Dispose()
    {
        if (connection is not null) runtime.OnMain(() => PcSessionPokes.Call(connection, "DidClose"));
        claude?.Terminate();
        if (transport is not null)
            foreach (var name in new[] { "stdin", "stdout", "stderr" })
            {
                var pipe = PcSessionPokes.Get<ProcessPipe>(transport, name);
                pipe.CloseWriting();
                PcSessionPokes.Get<IDisposable>(pipe, "ReadEnd").Dispose();
            }
        runtime.Dispose();
    }
}