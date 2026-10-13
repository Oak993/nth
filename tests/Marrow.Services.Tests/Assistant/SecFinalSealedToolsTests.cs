using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Assistant;

// PORT-ONLY: ED-NEW-mission-pc-1 supersedes the S1 stopgap: entries are present and every call is Dextra-gated.
[Trait("Decision", "ED-NEW-mission-pc-1")]
public sealed class SecFinalSealedToolsTests
{
    [Fact]
    public async Task A_sealed_chat_gets_mission_gated_nth_pc_and_API_pc_tools_while_Tactus_is_on()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        var host = new MissionPcHost();
        using var dextra = AssistantMcpBridge.Host.Override(host);
        Assert.True(PcControlBridge.TactusOn);
        var open = BuiltInServerFixtures.Config(ProviderKind.Claude, Guid.NewGuid());
        Assert.NotNull(PcControlBridge.Registration(open)); // the ordinary chat keeps Tactus
        var configuration = open with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        Assert.Equal("a mission chat", PcControlBridge.Registration(configuration)?.Head);
        Assert.NotNull(PcControlBridge.Server(configuration));
        Assert.Equal("a mission chat", PcNativeTools.For(configuration, vision: true)?.Registration.Head);
        Assert.Contains(MCPProviderConfig.BuiltInServers(configuration), server => server.Id == PcControlMcpServer.ServerName);
        var call = PcNativeTools.For(configuration, true)!.Execute("pc_list_apps", JSONValue.FromDictionaryLiteral(), default);
        Assert.Equal(["pc_list_apps"], host.Calls);
        Assert.False(call.IsCompleted);
        host.Answer.SetResult(false);
        Assert.True((await call).Failed);
        Assert.All(rig.Audits.Values, a => Assert.Empty(a.Entries));
        PcControlBridge.Forget(configuration.IgnisThreadID!.Value.ToString("D"));
    }

    [Fact]
    public async Task A_sealed_chat_gets_mission_gated_Visus_web_tools_while_Visus_model_access_is_on()
    {
        using var web = new WebAgentRig();
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        var host = new MissionPcHost();
        using var dextra = AssistantMcpBridge.Host.Override(host);
        Assert.True(WebAgentHub.Shared.Current.Offered);
        var open = BuiltInServerFixtures.Config(ProviderKind.OpenRouter, Guid.NewGuid());
        Assert.NotNull(WebNativeTools.For(open)); // the ordinary chat keeps Visus
        Assert.NotNull(PcControlBridge.Registration(open));
        var configuration = open with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        Assert.NotNull(WebNativeTools.For(configuration));
        Assert.Equal("a mission chat", PcControlBridge.Registration(configuration)?.Head); // nth-pc carries Visus for CLI providers
        var call = WebNativeTools.For(configuration)!.Run("web_open", JSONValue.Parse("""{"url":"https://a.test/"}""")!, true, default);
        Assert.Equal(["web_open"], host.Calls);
        Assert.False(call.IsCompleted);
        host.Answer.SetResult(false);
        Assert.True((await call).Failed);
        Assert.Empty(web.Host.Opened);
        PcControlBridge.Forget(configuration.IgnisThreadID!.Value.ToString("D"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_sealed_codex_launch_has_a_mission_gated_nth_pc_entry_and_its_own_computer_use_plugins_stay_off(bool tactusOn)
    {
        using var storage = new StorageScratchFolder();
        using var scratch = new ScratchFolder();
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => tactusOn ? assembly.Shared : null);
        using var perThread = PcControlBridge.PartsFor.Override(assembly.PartsFor);
        var host = new MissionPcHost();
        using var dextra = AssistantMcpBridge.Host.Override(host);
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Codex, Guid.NewGuid()) with
        {
            Sealed = new SealedLaunch(Guid.NewGuid()),
            Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["CODEX_HOME"] = scratch.Child("empty-codex-home") }),
        };
        var session = new CodexSession(configuration);
        var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var config = parameters["config"];
        if (tactusOn) Assert.NotNull(config["mcp_servers"]?[PcControlMcpServer.ServerName]);
        else Assert.Null(config["mcp_servers"]?[PcControlMcpServer.ServerName]);
        foreach (var plugin in PcProviderWiring.CodexComputerUsePlugins)
            Assert.False(config["plugins." + plugin + ".enabled"]?.JSONValueBool ?? true);
        if (tactusOn)
        {
            var token = config["mcp_servers"]![PcControlMcpServer.ServerName]!["args"]!.JSONValueArray![^1].JSONValueString;
            var registration = PcControlBridge.Registration(token);
            Assert.Equal("a mission chat", registration?.Head);
            var server = new PcControlMcpServer(registration, PcControlBridge.Engine, () => rig.Settings);
            var call = server.Handle("""{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"pc_list_apps","arguments":{}}}""", default);
            Assert.Equal(["pc_list_apps"], host.Calls);
            Assert.False(call.IsCompleted);
            host.Answer.SetResult(false);
            Assert.Contains("\"isError\":true", (await call)!, StringComparison.Ordinal);
            Assert.All(rig.Audits.Values, a => Assert.Empty(a.Entries));
            PcControlBridge.Forget(registration!.Thread);
        }
    }
}
