using System.Text.Json.Nodes;
using Marrow.App.Assistant;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Web;
using Marrow.Services.Web;

namespace Marrow.App.Tests.Assistant;

[Trait("Decision", "ED-NEW-mission-pc-4")]
public sealed class MissionPcSingleCardTests
{
    [Theory]
    [MemberData(nameof(Routes))]
    public async Task Provider_prompt_then_execution_has_one_card_and_one_trail(ProviderKind provider, bool webCall, bool allow)
    {
        using var storage = new StorageScratchFolder();
        using var rig = new DextraTaintRig(autoAnswer: false);
        var record = rig.Mission();
        rig.Controller.MissionDesktop = new DesktopGate(new FakeDesktop(), TimeProvider.System, (_, _, _) => Task.FromResult(true), (_, _) => { });
        using var pc = new PcAssemblyRig();
        var assembly = pc.Build();
        using var parts = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        using var web = new WebAgentRig();
        web.Host.Conversation.Add("https://a.test/invite");
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        using var host = AssistantMcpBridge.Host.Override(rig.Controller);
        var config = BuiltInServerFixtures.Config(provider, record.LeadThread!.Value) with { Sealed = new SealedLaunch(record.Id) };
        var registration = PcControlBridge.Registration(config)!;
        using var prompt = new MissionPcPromptRig(config, storage.Root);
        prompt.OnEvent = e => { if (e is ProviderEvent.Approval a) rig.Controller.OnApproval(record.LeadThread.Value, a.Value); };
        var tool = webCall ? "web_open" : "pc_list_apps";
        var args = webCall ? JSONValue.Parse("""{"url":"https://a.test/invite"}""")! : JSONValue.FromDictionaryLiteral();
        var before = rig.Logged().Count;
        prompt.Prompt(tool, args);
        Assert.Empty(rig.Controller.Queue.Waiting);
        Assert.Equal(before, rig.Logged().Count);
        prompt.AssertAccepted();

        var server = new PcControlMcpServer(registration, PcControlBridge.Engine, () => pc.Settings) { Web = WebMcpTools.Shared };
        var call = server.Handle(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 1, ["method"] = "tools/call",
            ["params"] = new JsonObject { ["name"] = tool, ["arguments"] = JsonNode.Parse(args.CompactString) } }.ToJsonString(), default);
        Assert.False(call.IsCompleted);
        var card = Assert.Single(rig.Controller.Queue.Waiting);
        Assert.Equal(record.Id, card.MissionId);
        Assert.Empty(web.Host.Opened);
        Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        var answer = card.Weight == DecisionWeight.Irreversible
            ? allow ? DecisionAnswerKind.DoIt : DecisionAnswerKind.Dont
            : allow ? DecisionAnswerKind.AllowOnce : DecisionAnswerKind.Deny;
        Assert.Null(rig.Controller.Queue.Answer(card.Id, new DecisionAnswer(answer)));
        var result = await call.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Empty(rig.Controller.Queue.Waiting);
        var trail = rig.Logged().Where(e => e.Tool == tool).ToArray();
        Assert.Single(trail, e => e.Result == "requested");
        Assert.Single(trail, e => e.Result == (allow ? "approved" : "refused"));
        Assert.Equal(allow ? 3 : 2, trail.Length);
        if (allow)
        {
            Assert.False((bool?)JsonNode.Parse(result!)?["result"]?["isError"] ?? true, result);
            Assert.Single(trail, e => e.Result == "done");
            if (webCall) Assert.Single(web.Host.Opened);
        }
        else
        {
            Assert.Contains("not allow", result!, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(web.Host.Opened);
            Assert.All(pc.Audits.Values, a => Assert.Empty(a.Entries));
        }
        Assert.Single(trail.Select(e => Assert.Single(e.Inputs, i => i.Name == "call").Text).Distinct());
        Assert.Empty(prompt.Events.OfType<ProviderEvent.Approval>());
        PcControlBridge.Forget(registration.Thread);
    }

    public static IEnumerable<object[]> Routes() => Enum.GetValues<ProviderKind>()
        .Where(p => !p.IsAPIKeyBased && p != ProviderKind.Local)
        .SelectMany(p => new[] { new object[] { p, false, false }, new object[] { p, false, true },
            new object[] { p, true, false }, new object[] { p, true, true } });
}