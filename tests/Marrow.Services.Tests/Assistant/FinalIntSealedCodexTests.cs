using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Assistant;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Assistant;

[Trait("Decision", "ED-NEW-finalint-4")]
public sealed class FinalIntSealedCodexTests
{
    [Fact]
    public void A_NoTools_codex_launch_never_registers_or_offers_dextra_tools()
    {
        using var storage = new StorageScratchFolder();
        var host = new DextraFakeHost();
        var thread = host.Add();
        using var bound = AssistantMcpBridge.Host.Override(host);
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Codex, thread) with { NoTools = true };
        try
        {
            var session = new CodexSession(configuration);
            var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
            Assert.Null(parameters.GetValueOrDefault("config")?["mcp_servers"]?["nth-dextra"]);
            Assert.Null(AssistantMcpBridge.Register(configuration));
            Assert.Null(AssistantNativeTools.For(configuration));
            Assert.Empty(MCPProviderConfig.BuiltInServers(configuration));
        }
        finally { AssistantMcpBridge.Forget(thread); }
    }

    [Fact]
    public void A_sealed_server_map_keeps_built_in_timeouts_and_disables_unlisted_servers()
    {
        var pc = new MCPResolvedServer("nth-pc", "nth-pc") { Command = "offline-pc" };
        var ui = new MCPResolvedServer("nth-ui", "nth-ui") { Command = "offline-ui" };
        var connected = JSONValue.Parse("""{"github":{"url":"https://example.test/gh"},"personal":{"command":"offline-personal"}}""");
        var seal = new SealedLaunch(Guid.NewGuid()) { Connections = new(["github"]) };
        var servers = SealedProviderConfig.CodexServers(connected, ["other"], seal, [pc, ui]);
        Assert.Equal(120L, servers["nth-pc"]!["tool_timeout_sec"]?.JSONValueInt);
        Assert.Equal(86400L, servers["nth-ui"]!["tool_timeout_sec"]?.JSONValueInt);
        Assert.Equal("https://example.test/gh", servers["github"]!["url"]!.JSONValueString);
        Assert.False(servers["personal"]!["enabled"]!.JSONValueBool);
        Assert.False(servers["other"]!["enabled"]!.JSONValueBool);
        Assert.Equal(new HashSet<string> { "github", "personal", "other", "nth-pc", "nth-ui" }, servers.JSONValueObject!.Keys.ToHashSet());
    }

    [Fact]
    public void A_sealed_launch_with_tactus_disables_the_competing_codex_controllers()
    {
        using var storage = new StorageScratchFolder();
        using var scratch = new ScratchFolder();
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        var configuration = BuiltInServerFixtures.Config(ProviderKind.Codex, Guid.NewGuid()) with
        {
            Sealed = new SealedLaunch(Guid.NewGuid()),
            Environment = new ValueMap<string, string>(new Dictionary<string, string> { ["CODEX_HOME"] = scratch.Child("empty-codex-home") }),
        };
        var session = new CodexSession(configuration);
        var parameters = (Dictionary<string, JSONValue>)typeof(CodexSession).GetProperty("ThreadParameters", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var config = parameters["config"];
        Assert.NotNull(config["mcp_servers"]?["nth-pc"]);
        foreach (var plugin in PcProviderWiring.CodexComputerUsePlugins)
            Assert.False(config["plugins." + plugin + ".enabled"]?.JSONValueBool ?? true);
    }
}
