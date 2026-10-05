using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-17"), Trait("Rule", "R-TEST-4")]
public sealed class CursorSkillsMcpTests
{
    [Fact]
    public void The_MCP_contract_exposes_read_only_discovery_and_pinned_skill_reads()
    {
        using var f = new Generations.GenerationFixture();
        var pdf = f.Add("O3", "pdf");
        var explicitOnly = f.Add("O1", "screenshot");
        using var g = f.Open().Start("cursor", "chat", [pdf, explicitOnly], f.Switches((pdf, true), (explicitOnly, true)), f.Budget);
        using var mcp = new SkillsMcpReflection(g);
        var initialized = mcp.Call("initialize", new { protocolVersion = "2025-06-18" })["result"]!;
        Assert.Equal("2025-06-18", initialized["protocolVersion"]!.GetValue<string>());
        Assert.Contains("list_skills", initialized["instructions"]!.GetValue<string>(), StringComparison.Ordinal);
        var tools = mcp.Call("tools/list")["result"]!["tools"]!.AsArray();
        Assert.Equal(new[] { "list_skills", "read_skill" }, tools.Select(t => t!["name"]!.GetValue<string>()));
        Assert.All(tools, t => Assert.True(t!["annotations"]!["readOnlyHint"]!.GetValue<bool>()));
        var text = mcp.Tool("list_skills")["result"]!["content"]![0]!["text"]!.GetValue<string>();
        var listed = JsonNode.Parse(text)!.AsArray();
        Assert.Equal("pdf", Assert.Single(listed)!["name"]!.GetValue<string>());
        Assert.DoesNotContain("BODY SENTINEL", text, StringComparison.Ordinal);
        Assert.DoesNotContain("screenshot", text, StringComparison.Ordinal);
        Assert.Contains("BODY SENTINEL", mcp.Tool("read_skill", new { name = "pdf" })["result"]!["content"]![0]!["text"]!.GetValue<string>(), StringComparison.Ordinal);
        Assert.True(mcp.Tool("read_skill", new { name = "screenshot" })["result"]!["isError"]!.GetValue<bool>());
    }
    [Theory]
    [InlineData("../other", "SKILL.md")]
    [InlineData("pdf", "../SKILL.md")]
    [InlineData("pdf", "C:/owner/private.txt")]
    public void Unknown_names_and_paths_outside_the_pinned_tree_are_refused(string name, string path)
    {
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        using var mcp = new SkillsMcpReflection(g);
        Assert.True(mcp.Tool("read_skill", new { name, path })["result"]!["isError"]!.GetValue<bool>());
        Assert.True(g.Verify());
    }
    [Fact]
    public void Damage_blocks_both_discovery_and_read_and_disposal_stops_new_reads()
    {
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        using var mcp = new SkillsMcpReflection(g);
        f.Trees.Unlock(g.Folder);
        File.AppendAllText(Assert.Single(g.Skills).File, "damage");
        Assert.True(mcp.Tool("list_skills")["result"]!["isError"]!.GetValue<bool>());
        Assert.True(mcp.Tool("read_skill", new { name = "pdf" })["result"]!["isError"]!.GetValue<bool>());
        mcp.Dispose();
        Assert.True(mcp.Tool("read_skill", new { name = "pdf" })["result"]!["isError"]!.GetValue<bool>());
    }
    [Fact]
    public async Task The_owned_pipe_serves_MCP_without_exporting_a_disk_trust_manifest_and_closes_on_stop()
    {
        using var f = new Generations.GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start("cursor", "chat", [item], f.Switches((item, true)), f.Budget);
        var type = typeof(Generation).Assembly.GetType("Marrow.Services.Port.Skills.Providers.SkillsMcpBridge");
        Assert.NotNull(type);
        using var bridge = (IDisposable)Activator.CreateInstance(type, g, "C:/fake/Marrow.App.exe")!;
        var pipeName = (string)type.GetProperty("PipeName")!.GetValue(bridge)!;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/call\",\"params\":{\"name\":\"read_skill\",\"arguments\":{\"name\":\"pdf\"}}}");
        var reply = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("BODY SENTINEL", reply!, StringComparison.Ordinal);
        bridge.Dispose();
        await ((Task)type.GetProperty("Completion")!.GetValue(bridge)!).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(g.Verify()); // Bridge never steals/disposes the runtime's generation lease.
    }
}
