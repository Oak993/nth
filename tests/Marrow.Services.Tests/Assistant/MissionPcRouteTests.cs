using System.Text.Json.Nodes;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Core.Web;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-1")]
public sealed class MissionPcRouteTests
{
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Mission_calls_wait_for_Dextra_and_No_runs_nothing(ProviderKind provider, bool webCall)
    {
        using var pc = new PcAssemblyRig();
        var assembly = pc.Build();
        using var parts = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var web = new WebAgentRig();
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        var host = new MissionPcHost();
        using var dextra = AssistantMcpBridge.Host.Override(host);
        var configuration = BuiltInServerFixtures.Config(provider, Guid.NewGuid()) with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        var registration = PcControlBridge.Registration(configuration);
        Assert.NotNull(registration);
        Assert.Equal("a mission chat", registration.Head);
        var tool = webCall ? WebToolSchemas.Open : PcToolSchemas.ListApps;
        var args = webCall ? JSONValue.Parse("""{"url":"https://a.test/invite"}""")! : JSONValue.FromDictionaryLiteral();
        Task call;
        if (provider.IsAPIKeyBased || provider == ProviderKind.Local)
            call = webCall ? WebNativeTools.For(configuration)!.Run(tool, args, true, default)
                : PcNativeTools.For(configuration, true)!.Execute(tool, args, default);
        else
        {
            Assert.Contains(MCPProviderConfig.BuiltInServers(configuration), s => s.Id == "nth-pc");
            var server = new PcControlMcpServer(registration, PcControlBridge.Engine, () => pc.Settings) { Web = WebMcpTools.Shared };
            call = server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
                ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonNode.Parse(args.CompactString) } }.ToJsonString(), default);
        }
        Assert.Equal([tool], host.Calls);
        Assert.False(call.IsCompleted);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        host.Answer.SetResult(false);
        await call;
        var answer = call switch
        {
            Task<PcNativeResult> pcCall => (await pcCall).Text,
            Task<WebToolResult> webTool => (await webTool).Render(),
            Task<string?> mcpCall => (await mcpCall)!,
            _ => throw new InvalidOperationException(),
        };
        Assert.Contains("not allow", answer, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        PcControlBridge.Forget(registration.Thread);
    }

    public static IEnumerable<object[]> Routes() => Enum.GetValues<ProviderKind>().SelectMany(provider => new[] { new object[] { provider, false }, new object[] { provider, true } });

    [Fact]
    public async Task Each_mission_batch_step_is_decided_before_it_runs()
    {
        var pc = new PcEngineHarness();
        var app = pc.Notepad();
        using var parts = PcControlBridge.Parts.Override(() => pc.Parts);
        using var perThread = PcControlBridge.PartsFor.Override(_ => pc.Parts);
        var host = new MissionPcHost();
        host.Answers.Enqueue(new TaskCompletionSource<bool>());
        var step = new TaskCompletionSource<bool>();
        host.Answers.Enqueue(step);
        host.Answers.Peek().SetResult(true); // allow the outer batch only
        using var dextra = AssistantMcpBridge.Host.Override(host);
        var config = BuiltInServerFixtures.Config(ProviderKind.OpenRouter, Guid.NewGuid()) with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        Assert.NotNull(PcControlBridge.Registration(config));
        var tools = PcNativeTools.For(config, true)!;
        var call = tools.Execute("pc_batch", JSONValue.Parse("""{"window":"w:1a","observe":"none","actions":[{"tool":"pc_type","element":2,"text":"sentinel-secret-123"},{"tool":"pc_type","element":2,"text":"later"}]}""")!, default);
        Assert.Equal(["pc_batch", "pc_type"], host.Calls);
        Assert.False(call.IsCompleted);
        Assert.Equal("", app.Root.Children[0].Value);
        step.SetResult(false);
        Assert.True((await call).Failed);
        Assert.Equal("", app.Root.Children[0].Value);
        PcControlBridge.Forget(tools.Registration.Thread);
    }

    [Fact]
    public async Task A_mission_ignores_the_owners_standing_app_grant_and_checks_attendance()
    {
        var pc = new PcEngineHarness();
        pc.Notepad();
        pc.Approver.Standings[("notepad.exe", PcCapability.See)] = PcApproval.Always;
        using var parts = PcControlBridge.Parts.Override(() => pc.Parts);
        var config = BuiltInServerFixtures.Config(ProviderKind.Claude, Guid.NewGuid()) with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        var ordinary = PcControlBridge.Engine(PcControlBridge.Registration(config with { Sealed = null })!)!;
        Assert.True((await PcEngineHarness.Call(ordinary, "pc_observe", """{"window":"w:1a","screenshot":"no"}""")).Ok);
        Assert.Empty(pc.Approver.Asked);
        var registration = PcControlBridge.Registration(config);
        Assert.NotNull(registration);
        Assert.Equal("a mission chat", registration.Head);
        var engine = PcControlBridge.Engine(registration)!;
        Assert.NotSame(ordinary, engine); // an already cached ordinary engine must not lend its standing grant to a seal
        Assert.True((await PcEngineHarness.Call(engine, "pc_observe", """{"window":"w:1a","screenshot":"no"}""")).Ok);
        Assert.Equal([("notepad.exe", PcCapability.See, false)], pc.Approver.Asked);
        using var away = PcControlBridge.Attended.Override(_ => false);
        Assert.Equal(PcErrorCode.Unattended, (await PcEngineHarness.Call(engine, "pc_list_apps", "{}")).Error);
        PcControlBridge.Forget(registration.Thread);
    }
}
