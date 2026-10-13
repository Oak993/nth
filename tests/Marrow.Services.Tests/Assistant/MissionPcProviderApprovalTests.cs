using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-4")]
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
    public async Task Prompt_acceptance_does_not_authorize_the_adapter(ProviderKind provider, string tool)
    {
        using var storage = new StorageScratchFolder();
        var config = BuiltInServerFixtures.Config(provider, Guid.NewGuid()) with { Sealed = new SealedLaunch(Guid.NewGuid()), RuntimeMode = RuntimeMode.FullAccess };
        using var prompt = new MissionPcPromptRig(config, storage.Root);
        if (prompt.Session is Marrow.Services.Providers.PiSession or Marrow.Services.Providers.CommandCodeSession)
            PcSessionPokes.Get<HashSet<string>>(prompt.Session, "sessionGrants").Add("tool");
        var input = JSONValue.FromDictionaryLiteral(("window", "w:1a"), ("tab", "t1"));
        prompt.Prompt(tool, input);
        Assert.Empty(prompt.Events.OfType<Marrow.Services.Providers.ProviderEvent.Approval>());
        prompt.AssertAccepted();

        using var pc = new PcAssemblyRig();
        var assembly = pc.Build();
        using var parts = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var web = new WebAgentRig();
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        var host = new MissionPcHost();
        using var dextra = AssistantMcpBridge.Host.Override(host);
        var registration = PcControlBridge.Registration(config)!;
        var server = new PcControlMcpServer(registration, PcControlBridge.Engine, () => pc.Settings) { Web = WebMcpTools.Shared };
        var call = server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonNode.Parse(input.CompactString) } }.ToJsonString(), default);
        Assert.Equal([tool], host.Calls);
        Assert.False(call.IsCompleted);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        host.Answer.SetResult(false);
        Assert.Contains("not allow", (await call.WaitAsync(TimeSpan.FromSeconds(10)))!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        PcControlBridge.Forget(registration.Thread);
    }
}