using System.Text.Json.Nodes;
using System.Reflection;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-17"), Trait("Rule", "R-TEST-4")]
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class CursorSkillsAcpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stop_or_damage_during_the_launch_wait_prevents_a_late_Cursor_child(bool stop)
    {
        using var storage = new StorageScratchFolder();
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        using var runtime = new TestRuntime();
        var script = AcpStandIns.CopyFor("sk17-cursor-launch-wait");
        var gate = typeof(ACPSession).GetField("LaunchGate", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        gate.GetType().GetField("nextSlot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(gate, SwiftDate.Now.AddSeconds(5));
        var session = new ACPSession(SkillOverlaySessionTests.Config(ProviderKind.Cursor, script.ExecutablePath, f.Paths.Root, g));
        try
        {
            var start = runtime.OnMain(() => session.Start());
            runtime.Loop.RunUntil(() => runtime.Time.ActiveTimers > 0, TimeSpan.FromSeconds(10));
            if (stop) { runtime.OnMain(session.Stop); g.Dispose(); }
            else { f.Trees.Unlock(g.Folder); File.AppendAllText(Assert.Single(g.Skills).File, "damage"); }
            runtime.Advance(TimeSpan.FromSeconds(5));
            if (stop) Assert.Throws<CancellationError>(() => runtime.Loop.Run(() => start, TimeSpan.FromSeconds(10)));
            else Assert.Throws<IOException>(() => runtime.Loop.Run(() => start, TimeSpan.FromSeconds(10)));
            Assert.Equal(0, script.Launches);
        }
        finally { session.Stop(); AcpStandIns.ResetLaunchGate(); }
    }
    [Theory]
    [InlineData(null, "session/new")]
    [InlineData("saved", "session/load")]
    public void Cursor_attaches_the_owned_server_to_new_and_loaded_sessions_without_replacing_user_MCP(string? resume, string method)
    {
        using var storage = new StorageScratchFolder();
        MCPProviderConfig.WriteAll([new MCPResolvedServer("user-server", "User server") { Command = "C:/fake/user-server.exe" }]);
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        using var runtime = new TestRuntime();
        AcpStandIns.ResetLaunchGate();
        var script = AcpStandIns.CopyFor("sk17-cursor-mcp");
        File.WriteAllText(Path.Combine(script.Folder, "initialize.json"), "{\"protocolVersion\":1,\"agentCapabilities\":{\"loadSession\":true}}");
        var config = SkillOverlaySessionTests.Config(ProviderKind.Cursor, script.ExecutablePath, f.Paths.Root, g, resume);
        var property = typeof(SessionConfiguration).GetProperty("SkillMcpExecutable");
        Assert.NotNull(property);
        property.SetValue(config, "C:/fake/nth.exe");
        var session = new ACPSession(config);
        try
        {
            Assert.Equal(resume ?? "s1", runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60)));
            var request = File.ReadAllLines(Path.Combine(script.Folder, "requests-0.jsonl")).Select(line => JsonNode.Parse(line)!).Single(q => q["method"]!.GetValue<string>() == method);
            var servers = request["params"]!["mcpServers"]!.AsArray();
            Assert.Equal(2, servers.Count);
            Assert.Equal("user-server", servers[0]!["name"]!.GetValue<string>());
            Assert.Equal("C:/fake/user-server.exe", servers[0]!["command"]!.GetValue<string>());
            var owned = servers[1]!;
            Assert.StartsWith("marrow-skills-", owned["name"]!.GetValue<string>(), StringComparison.Ordinal);
            Assert.Equal("C:/fake/nth.exe", owned["command"]!.GetValue<string>());
            Assert.Equal("--marrow-skills-mcp", owned["args"]![0]!.GetValue<string>());
            Assert.Equal(2, owned["args"]!.AsArray().Count);
            Assert.Empty(owned["env"]!.AsArray());
            Assert.Equal(new[] { "acp" }, script.Arguments(0));
            Assert.IsAssignableFrom<ISkillSessionLifetime>(session);
        }
        finally { session.Stop(); }
        runtime.Loop.Run(() => Assert.IsAssignableFrom<ISkillSessionLifetime>(session).SkillReadersExited, TimeSpan.FromSeconds(60));
        Assert.True(g.Verify());
    }
    [Fact]
    public void Cursor_refuses_a_damaged_generation_before_any_CLI_process_starts()
    {
        using var storage = new StorageScratchFolder();
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        f.Trees.Unlock(g.Folder); File.AppendAllText(Assert.Single(g.Skills).File, "damage");
        using var runtime = new TestRuntime();
        var script = AcpStandIns.CopyFor("sk17-cursor-damaged");
        var session = new ACPSession(SkillOverlaySessionTests.Config(ProviderKind.Cursor, script.ExecutablePath, f.Paths.Root, g));
        try { Assert.Throws<IOException>(() => runtime.Loop.Run(() => session.Start(), TimeSpan.FromSeconds(60))); }
        finally { session.Stop(); }
        Assert.Equal(0, script.Launches);
    }
}
