// PORT-ONLY: Tactus (ED-NEW-pcctl-41; DESIGN.md 3.4). The nth-pc server in every CLI and ACP provider's MCP config: none
// of it while Tactus is off (each shape is then exactly what it was), and while on the nth-pc entry beside the connected
// servers and any other built-in, in each provider's own form: Codex with its 120 s tool timeout and its bundled
// computer-use plugins off, Copilot's one merged object, ACP's appended entries, Claude's and the JS bridge's per-chat
// file, Antigravity's per-chat system settings with nth's entries trusted. Fake desktop, scratch storage, no provider.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port.PcControl;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.PcControl;

[Trait("Decision", "ED-NEW-pcctl-41")]
public sealed class PcProviderShapesTests
{
    private static readonly MCPResolvedServer Advisor = new("nth-advisor", "nth-advisor") { Command = "C:/nth/nth.exe", Args = new ValueList<string>(["--marrow-advisor-mcp", "pipe", "token"]) };

    private static SessionConfiguration Config(ProviderKind provider, Guid? thread = null) => new()
    {
        Provider = provider,
        WorkingDirectory = SwiftURL.Create("marrow-native-path:C:/work")!,
        Environment = ValueMap<string, string>.Empty,
        RuntimeMode = RuntimeMode.Supervised,
        InteractionMode = InteractionMode.Build,
        Model = "fixture-model",
        IgnisThreadID = thread ?? Guid.NewGuid(),
    };

    [Fact]
    public void Nothing_is_added_while_tactus_is_off_or_unbound()
    {
        using var rig = new PcAssemblyRig(PcSettings.Default);
        var connected = JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));
        var acp = JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "github")));

        Assert.Null(MCPProviderConfig.PcServer(Config(ProviderKind.Codex)));
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        Assert.Null(MCPProviderConfig.PcServer(Config(ProviderKind.Codex)));
        var none = MCPProviderConfig.BuiltInServers(Config(ProviderKind.Codex));

        Assert.Empty(none);
        Assert.Same(connected, MCPProviderConfig.CodexBuiltIns(connected, none));
        Assert.Null(MCPProviderConfig.CodexBuiltIns(null, none));
        Assert.Null(MCPProviderConfig.CopilotBuiltIns(none));
        Assert.Same(acp, MCPProviderConfig.AcpBuiltIns(acp, none));
        Assert.Null(MCPProviderConfig.GeminiSessionSettings(none, Guid.NewGuid()));
        using var storage = new StorageScratchFolder();
        Assert.Null(MCPProviderConfig.ClaudeBuiltInConfig(none, Guid.NewGuid()));
        Assert.Empty(Directory.Exists(MCPPaths.Directory) ? Directory.GetFiles(MCPPaths.Directory) : []);
    }

    [Fact]
    public void While_on_each_shape_carries_the_nth_pc_entry_beside_the_connected_servers_and_the_advisor()
    {
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        var thread = Guid.NewGuid();
        var pc = Assert.IsType<MCPResolvedServer>(MCPProviderConfig.PcServer(Config(ProviderKind.Codex, thread)));
        Assert.Equal((PcToolSchemas.ServerName, PcControlBridge.Command), (pc.Id, pc.Args[0]));
        Assert.Equal(thread.ToString("D"), PcControlBridge.Registration(pc.Args[2])!.Thread);
        var builtIns = MCPProviderConfig.BuiltInServers(Advisor, pc);
        Assert.Equal(new[] { "nth-advisor", "nth-pc" }, builtIns.Select(s => s.Id));

        var connected = JSONValue.FromDictionaryLiteral(("github", JSONValue.FromDictionaryLiteral(("url", "https://example.test/mcp"))));
        var codex = MCPProviderConfig.CodexBuiltIns(connected, builtIns)!;
        Assert.Equal(new HashSet<string> { "github", "nth-advisor", "nth-pc" }, codex.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(120L, codex["nth-pc"]!["tool_timeout_sec"]!.JSONValueInt);
        Assert.Null(codex["nth-advisor"]!["tool_timeout_sec"]);
        Assert.Equal(pc.Args.ToArray(), codex["nth-pc"]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!));

        var copilot = MCPProviderConfig.CopilotBuiltIns(builtIns)!;
        Assert.Equal(new HashSet<string> { "nth-advisor", "nth-pc" }, copilot.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(("local", "*"), (copilot["nth-pc"]!["type"]!.JSONValueString, copilot["nth-pc"]!["tools"]![0]!.JSONValueString));

        var acp = MCPProviderConfig.AcpBuiltIns(JSONValue.FromArrayLiteral(JSONValue.FromDictionaryLiteral(("name", "github"))), builtIns).JSONValueArray!;
        Assert.Equal(new[] { "github", "nth-advisor", "nth-pc" }, acp.Select(e => e["name"]!.JSONValueString!));
        Assert.Equal(pc.Command, acp[2]["command"]!.JSONValueString);
        Assert.Empty(acp[2]["env"]!.JSONValueArray!);
        PcControlBridge.Forget(thread.ToString("D"));
    }

    [Fact]
    public void Claude_and_the_js_bridge_get_one_merged_file_per_chat()
    {
        using var storage = new StorageScratchFolder();
        var pc = new MCPResolvedServer("nth-pc", "nth-pc") { Command = "C:/nth/nth.exe", Args = new ValueList<string>(["--marrow-pc-mcp", "pipe", "token"]) };
        var thread = Guid.NewGuid();

        var alone = Assert.IsType<(string Path, bool Strict)>(MCPProviderConfig.ClaudeBuiltInConfig([pc], thread));
        Assert.False(alone.Strict);
        Assert.Equal(new[] { "nth-pc" }, JSONValue.Parse(File.ReadAllBytes(alone.Path))!["mcpServers"]!.JSONValueObject!.Keys);

        MCPProviderConfig.WriteAll([new MCPResolvedServer("github", "github") { Url = "https://example.test/mcp" }]);
        var merged = Assert.IsType<(string Path, bool Strict)>(MCPProviderConfig.ClaudeBuiltInConfig([Advisor, pc], thread));
        Assert.True(merged.Strict);
        Assert.NotEqual(MCPPaths.ClaudeConfigURL, merged.Path);
        var servers = JSONValue.Parse(File.ReadAllBytes(merged.Path))!["mcpServers"]!;
        Assert.Equal(new HashSet<string> { "github", "nth-advisor", "nth-pc" }, servers.JSONValueObject!.Keys.ToHashSet());
        Assert.Equal(pc.Args.ToArray(), servers["nth-pc"]!["args"]!.JSONValueArray!.Select(a => a.JSONValueString!));
        Assert.Equal((MCPProviderConfig.ClaudeConfigPath()!, true), MCPProviderConfig.ClaudeBuiltInConfig([], thread));
        Assert.Equal(MCPProviderConfig.ClaudeSessionConfig(null, thread), MCPProviderConfig.ClaudeBuiltInConfig([], thread));
    }

    [Fact]
    public void Antigravity_gets_a_per_chat_copy_of_its_system_settings_with_nths_entries_trusted()
    {
        using var storage = new StorageScratchFolder();
        var pc = new MCPResolvedServer("nth-pc", "nth-pc") { Command = "C:/nth/nth.exe", Args = new ValueList<string>(["--marrow-pc-mcp", "pipe", "token"]) };
        var thread = Guid.NewGuid();

        var alone = MCPProviderConfig.GeminiSessionSettings([pc], thread)!;
        var only = JSONValue.Parse(File.ReadAllBytes(alone))!["mcpServers"]!;
        Assert.Equal(new[] { "nth-pc" }, only.JSONValueObject!.Keys);
        Assert.True(only["nth-pc"]!["trust"]!.JSONValueBool);

        MCPProviderConfig.WriteAll([new MCPResolvedServer("github", "github") { Url = "https://example.test/mcp" }]);
        var shared = MCPProviderConfig.GeminiSettingsPath()!;
        var before = File.ReadAllBytes(shared);
        var copy = MCPProviderConfig.GeminiSessionSettings([pc], thread)!;
        var servers = JSONValue.Parse(File.ReadAllBytes(copy))!["mcpServers"]!;

        Assert.NotEqual(shared, copy);
        Assert.Equal(new HashSet<string> { "github", "nth-pc" }, servers.JSONValueObject!.Keys.ToHashSet());
        Assert.Null(servers["github"]!["trust"]);
        Assert.Equal("https://example.test/mcp", servers["github"]!["httpUrl"]!.JSONValueString);
        Assert.True(servers["nth-pc"]!["trust"]!.JSONValueBool);
        Assert.Equal(pc.Command, servers["nth-pc"]!["command"]!.JSONValueString);
        Assert.Equal(before, File.ReadAllBytes(shared));
    }

    [Fact]
    public void Pi_and_Command_Code_chats_get_an_image_folder_and_no_other_client_does()
    {
        using var folder = new PcTestFolder("pc-images");
        using var root = PcProviderWiring.ImageRoot.Override(() => folder.Path);
        using var rig = new PcAssemblyRig();
        var assembly = rig.Build();
        using var shared = PcControlBridge.Parts.Override(() => assembly.Shared);
        var thread = Guid.NewGuid();

        foreach (var provider in new[] { ProviderKind.Pi, ProviderKind.Commandcode })
            Assert.Equal(Path.Combine(folder.Path, thread.ToString("D")), PcControlBridge.Registration(Config(provider, thread))!.ImageFolder);
        foreach (var provider in new[] { ProviderKind.Claude, ProviderKind.Codex, ProviderKind.Copilot, ProviderKind.Cursor, ProviderKind.Antigravity })
            Assert.Null(PcControlBridge.Registration(Config(provider, thread))!.ImageFolder);
    }
}
