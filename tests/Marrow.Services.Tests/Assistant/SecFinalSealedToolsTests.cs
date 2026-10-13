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

// PORT-ONLY: security gate S1 (ED-NEW-secfinal-1). A mission (sealed) chat gets no nth-pc server, no Tactus native tools and
// no Visus web tools, so no desktop or browser step of a mission can run on the owner's standing "Always" grants outside
// Dextra's approval, classifier, taint and audit. Codex's own computer-use plugins stay off in every sealed launch.
[Trait("Decision", "ED-NEW-secfinal-1")]
public sealed class SecFinalSealedToolsTests
{
    [Fact]
    public void A_sealed_chat_gets_no_nth_pc_server_or_native_pc_tools_while_Tactus_is_on()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        Assert.True(PcControlBridge.TactusOn);
        var open = BuiltInServerFixtures.Config(ProviderKind.Claude, Guid.NewGuid());
        Assert.NotNull(PcControlBridge.Registration(open)); // the ordinary chat keeps Tactus
        var configuration = open with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        Assert.Null(PcControlBridge.Registration(configuration));
        Assert.Null(PcControlBridge.Server(configuration));
        Assert.Null(PcNativeTools.For(configuration, vision: true));
        Assert.DoesNotContain(MCPProviderConfig.BuiltInServers(configuration), server => server.Id == PcControlMcpServer.ServerName);
    }

    [Fact]
    public void A_sealed_chat_gets_no_Visus_web_tools_while_Visus_model_access_is_on()
    {
        using var web = new WebAgentRig();
        using var hub = WebAgentHub.Shared.Override(web.Hub);
        Assert.True(WebAgentHub.Shared.Current.Offered);
        var open = BuiltInServerFixtures.Config(ProviderKind.OpenRouter, Guid.NewGuid());
        Assert.NotNull(WebNativeTools.For(open)); // the ordinary chat keeps Visus
        Assert.NotNull(PcControlBridge.Registration(open));
        var configuration = open with { Sealed = new SealedLaunch(Guid.NewGuid()) };
        Assert.Null(WebNativeTools.For(configuration));
        Assert.Null(PcControlBridge.Registration(configuration)); // nth-pc carries the Visus tools for CLI providers
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_sealed_codex_launch_has_no_nth_pc_entry_and_its_own_computer_use_plugins_stay_off(bool tactusOn)
    {
        using var storage = new StorageScratchFolder();
        using var scratch = new ScratchFolder();
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => tactusOn ? assembly.Shared : null);
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Codex, Guid.NewGuid()) with
        {
            Sealed = new SealedLaunch(Guid.NewGuid()),
            Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["CODEX_HOME"] = scratch.Child("empty-codex-home") }),
        };
        var session = new CodexSession(configuration);
        var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var config = parameters["config"];
        Assert.Null(config["mcp_servers"]?[PcControlMcpServer.ServerName]);
        foreach (var plugin in PcProviderWiring.CodexComputerUsePlugins)
            Assert.False(config["plugins." + plugin + ".enabled"]?.JSONValueBool ?? true);
    }
}
