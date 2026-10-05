// PORT-ONLY: SK-17. Layouts observed in the supervised live check, plus explicit-only confinement.
using System.Text.Json;
using Marrow.Services.Tests.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Providers;

[Trait("Feature", "SK-17"), Trait("Rule", "R-TEST-4")]
public sealed class SkillOverlayLayoutTests
{
    [Theory]
    [InlineData("agy", ".agents/skills/pdf/SKILL.md")]
    [InlineData("cursor", "skills/pdf/SKILL.md")]
    public void Enabled_skills_use_the_live_proven_provider_layout(string provider, string relative)
    {
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf");
        using var g = f.Open().Start(provider, "chat", [item], f.Switches((item, true)), f.Budget);
        Assert.Equal(Path.Combine(g.Folder, relative.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(Assert.Single(g.Skills).Folder, "SKILL.md"));
        Assert.Contains("BODY SENTINEL", File.ReadAllText(Path.Combine(g.Folder, relative)), StringComparison.Ordinal);
        Assert.True(g.Verify());
        if (provider == "cursor")
        {
            using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(g.Folder, ".cursor-plugin", "plugin.json")));
            Assert.Equal("marrow", manifest.RootElement.GetProperty("name").GetString());
            Assert.Equal("fixture", manifest.RootElement.GetProperty("version").GetString());
        }
    }

    [Theory]
    [InlineData("agy", ".agents/skills")]
    [InlineData("cursor", "skills")]
    public void Explicit_only_skills_are_kept_out_of_automatic_discovery(string provider, string discovery)
    {
        using var f = new GenerationFixture();
        var named = f.Add("O1", "screenshot");
        var automatic = f.Add("O3", "pdf");
        using var g = f.Open().Start(provider, "chat", [named, automatic], f.Switches((named, true), (automatic, true)), f.Budget);
        Assert.Contains("BODY SENTINEL", g.Invoke("screenshot"), StringComparison.Ordinal);
        var discovered = Directory.GetFiles(Path.Combine(g.Folder, discovery), "SKILL.md", SearchOption.AllDirectories);
        Assert.Single(discovered);
        Assert.DoesNotContain(discovered, p => p.Contains("screenshot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("agy")]
    [InlineData("cursor")]
    public void Plugin_hooks_and_mcp_are_not_copied_into_a_skills_only_overlay(string provider)
    {
        using var f = new GenerationFixture();
        var item = f.Add("O3", "pdf", plugin: true);
        using var g = f.Open().Start(provider, "chat", [item], f.Switches((item, true)).WithPlugin("O3", true), f.Budget);
        Assert.Single(g.Skills);
        Assert.Empty(g.PluginFolders);
        var files = Directory.GetFiles(g.Folder, "*", SearchOption.AllDirectories);
        Assert.DoesNotContain(files, p => p.EndsWith("hooks.json", StringComparison.Ordinal) || p.EndsWith(".mcp.json", StringComparison.Ordinal));
    }
}
