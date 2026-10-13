using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-1")]
public sealed class MissionPcIntegrationTests
{
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Every_route_reaches_the_real_Dextra_card_and_denial_never_acts(ProviderKind provider, bool webCall)
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var desktop = typeof(AssistantController).GetProperty("MissionDesktop");
        Assert.NotNull(desktop);
        desktop.SetValue(rig.Controller, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        using var pc = new PcAssemblyRig();
        var assembly = pc.Build();
        using var parts = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var web = new WebAgentRig();
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        using var host = AssistantMcpBridge.Host.Override(rig.Controller);
        var config = BuiltInServerFixtures.Config(provider, record.LeadThread!.Value) with { Sealed = new SealedLaunch(record.Id) };
        var registration = PcControlBridge.Registration(config);
        Assert.NotNull(registration);
        var tool = webCall ? "web_open" : "pc_type";
        var args = webCall ? JSONValue.Parse("""{"url":"https://a.test/invite"}""")! : JSONValue.Parse("""{"window":"w:1a","text":"sentinel-secret-123"}""")!;
        Task call;
        if (provider.IsAPIKeyBased || provider == ProviderKind.Local)
            call = webCall ? WebNativeTools.For(config)!.Run(tool, args, true, default) : PcNativeTools.For(config, true)!.Execute(tool, args, default);
        else
        {
            Assert.Contains(MCPProviderConfig.BuiltInServers(config), s => s.Id == "nth-pc");
            var server = new PcControlMcpServer(registration, PcControlBridge.Engine, () => pc.Settings) { Web = WebMcpTools.Shared };
            call = server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonNode.Parse(args.CompactString) } }.ToJsonString(), default);
        }
        Assert.False(call.IsCompleted);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        var no = card.Weight == DecisionWeight.Irreversible ? DecisionAnswerKind.Dont : DecisionAnswerKind.Deny;
        Assert.Null(rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(no)));
        await call.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        Assert.Contains(rig.Logged(), e => e.Tool == tool && e.Mission == record.Id.ToString("D"));
        Assert.DoesNotContain(rig.Logged(), e => System.Text.Json.JsonSerializer.Serialize(e).Contains("sentinel-secret-123", StringComparison.Ordinal));
        PcControlBridge.Forget(registration.Thread);
    }

    public static IEnumerable<object[]> Routes() => Enum.GetValues<ProviderKind>().SelectMany(provider => new[] { new object[] { provider, false }, new object[] { provider, true } });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_allowed_call_records_intent_and_outcome_with_one_call_id(bool outcomeWriteFails)
    {
        using var rig = new DextraTaintRig();
        var record = rig.Mission();
        var desktop = typeof(AssistantController).GetProperty("MissionDesktop");
        Assert.NotNull(desktop);
        desktop.SetValue(rig.Controller, new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { }));
        using var pc = new PcAssemblyRig();
        var assembly = pc.Build();
        using var parts = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var host = AssistantMcpBridge.Host.Override(rig.Controller);
        var config = BuiltInServerFixtures.Config(ProviderKind.OpenRouter, record.LeadThread!.Value) with { Sealed = new SealedLaunch(record.Id) };
        var tools = PcNativeTools.For(config, true);
        Assert.NotNull(tools);
        rig.AuditFails = e => outcomeWriteFails && e.Tool == "pc_list_apps" && e.Result == "done";
        var result = await tools.Execute("pc_list_apps", JSONValue.FromDictionaryLiteral(), default);
        Assert.Equal(outcomeWriteFails, result.Failed);
        var log = rig.Logged().Where(e => e.Tool == "pc_list_apps").ToArray();
        Assert.Contains(log, e => e.Result == "requested");
        if (outcomeWriteFails)
        {
            Assert.DoesNotContain(log, e => e.Result == "done");
            Assert.Contains("do not repeat", result.Text, StringComparison.OrdinalIgnoreCase);
            var retry = tools.Execute("pc_list_apps", JSONValue.FromDictionaryLiteral(), default);
            Assert.False(retry.IsCompleted);
            var card = Assert.Single(rig.Controller.Queue.Waiting);
            Assert.Null(rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(DecisionAnswerKind.Deny)));
            Assert.True((await retry.WaitAsync(TimeSpan.FromSeconds(10))).Failed);
        }
        else Assert.Contains(log, e => e.Result == "done");
        Assert.Single(log.Select(e => Assert.Single(e.Inputs, i => i.Name == "call").Text).Distinct());
        Assert.True(rig.Controller.Runner.TaintOf(record.Id, record.LeadThread!.Value).IsTainted);
        PcControlBridge.Forget(tools.Registration.Thread);
    }
}
