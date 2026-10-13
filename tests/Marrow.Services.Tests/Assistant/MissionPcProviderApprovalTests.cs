using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Models;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-1")]
public sealed class MissionPcProviderApprovalTests
{
    [Theory]
    [InlineData(ProviderKind.Claude, "pc_observe")]
    [InlineData(ProviderKind.Claude, "web_read")]
    [InlineData(ProviderKind.Codex, "pc_observe")]
    [InlineData(ProviderKind.Codex, "web_read")]
    [InlineData(ProviderKind.Cursor, "pc_observe")]
    [InlineData(ProviderKind.Cursor, "web_read")]
    [InlineData(ProviderKind.Pi, "pc_observe")]
    [InlineData(ProviderKind.Pi, "web_read")]
    [InlineData(ProviderKind.Commandcode, "pc_observe")]
    [InlineData(ProviderKind.Commandcode, "web_read")]
    [InlineData(ProviderKind.Copilot, "pc_observe")]
    [InlineData(ProviderKind.Copilot, "web_read")]
    public void A_sealed_provider_never_auto_approves_nth_pc_or_Visus(ProviderKind provider, string tool)
    {
        using var storage = new StorageScratchFolder();
        using var runtime = new TestRuntime();
        var config = BuiltInServerFixtures.Config(provider, Guid.NewGuid()) with
            { Sealed = new SealedLaunch(Guid.NewGuid()), RuntimeMode = RuntimeMode.FullAccess };
        ProviderSession session = provider switch
        {
            ProviderKind.Claude => new ClaudeSession(config),
            ProviderKind.Codex => new CodexSession(config),
            ProviderKind.Cursor => new ACPSession(config),
            ProviderKind.Pi => new PiSession(config),
            ProviderKind.Commandcode => new CommandCodeSession(config),
            ProviderKind.Copilot => new CopilotSession(config),
            _ => throw new InvalidOperationException(),
        };
        var events = new List<ProviderEvent>();
        session.OnEvent = events.Add;
        if (provider is ProviderKind.Pi or ProviderKind.Commandcode) PcSessionPokes.Set(session, "pcTools", true);
        else PcSessionPokes.Set(session, "pcServer", new MCPResolvedServer("nth-pc", "nth-pc") { Command = "offline" });
        if (provider is ProviderKind.Pi or ProviderKind.Commandcode) PcSessionPokes.Get<HashSet<string>>(session, "sessionGrants").Add("tool");
        var input = JSONValue.FromDictionaryLiteral(("window", "w:1a"));
        var payload = JSONValue.FromDictionaryLiteral(("kind", "tool"), ("toolName", "mcp__nth-pc__" + tool), ("input", input));
        switch (provider)
        {
            case ProviderKind.Claude:
                ((ClaudeSession)session).HandleControlRequest(JSONValue.FromDictionaryLiteral(("request_id", "r1"),
                    ("request", JSONValue.FromDictionaryLiteral(("subtype", "can_use_tool"), ("tool_name", "mcp__nth-pc__" + tool), ("input", input)))));
                break;
            case ProviderKind.Codex:
                PcSessionPokes.Call(session, "HandleRequest", new RPCID.Int(1), "mcpServer/elicitation/request",
                    JSONValue.FromDictionaryLiteral(("serverName", "nth-pc"), ("toolName", tool), ("message", "Allow this call?")));
                break;
            case ProviderKind.Cursor:
                PcSessionPokes.Call(session, "HandleRequest", new RPCID.Int(1), "session/request_permission", JSONValue.FromDictionaryLiteral(
                    ("options", JSONValue.FromArrayLiteral(
                        JSONValue.FromDictionaryLiteral(("optionId", "yes"), ("kind", "allow_once"), ("name", "Allow")),
                        JSONValue.FromDictionaryLiteral(("optionId", "no"), ("kind", "reject_once"), ("name", "No")))),
                    ("toolCall", JSONValue.FromDictionaryLiteral(("toolCallId", "c1"), ("kind", "other"), ("title", "nth-pc-" + tool + ": " + tool)))));
                break;
            case ProviderKind.Commandcode:
                PcSessionPokes.Call(session, "Decide", "r1", payload);
                break;
            case ProviderKind.Pi:
                PcSessionPokes.Call(session, "HandleSelect", "r1", JSONValue.FromDictionaryLiteral(
                    ("title", "droppy-gate " + Encoding.UTF8.GetString(payload.Data())), ("options", JSONValue.FromArrayLiteral("Allow", "Deny"))));
                break;
            case ProviderKind.Copilot:
                PcSessionPokes.Call(session, "HandlePermission", JSONValue.FromDictionaryLiteral(("requestId", "r1"), ("resolvedByHook", true),
                    ("permissionRequest", JSONValue.FromDictionaryLiteral(("kind", "mcp"), ("serverName", "nth-pc"), ("toolName", tool), ("toolCallId", "c1")))));
                break;
        }
        Assert.Single(events.OfType<ProviderEvent.Approval>());
    }
}
