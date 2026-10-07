// PORT-ONLY: SK-9 composed sets, shared probes, restart and budget fixtures.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Feature", "SK-9")]
[Trait("Spec", "DL-9")]
public sealed class GenerationCompositionTests
{
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-plugin-disabled-skill
    public void Whole_plugin_preserves_assets_but_disabled_skill_is_absent()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O4", "pdf", plugin: true, secondName: "doc");
        var switches = f.Switches((item, true)).WithSkill(item.Skills[1], false).WithPlugin("O4", true);
        using var g = f.Open().Start("claude", "a", [item], switches, f.Budget);
        Assert.Equal("pdf", Assert.Single(g.Skills).Name);
        Assert.DoesNotContain(Directory.GetFiles(g.Folder, "SKILL.md", SearchOption.AllDirectories), p => p.Contains("doc", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Assert.Single(g.PluginFolders), ".mcp.json")));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-multiple-plugin-folders
    public void A_downloaded_set_preserves_each_whole_plugin_folder_for_plugin_providers()
    {
        using var f = new GenerationFixture();
        var item = f.Add("O4", "pdf", plugin: true, secondName: "doc", nestedPlugins: true);
        var s = f.Switches((item, true)).WithSkill(item.Skills[1], true).WithPlugin("O4", true);
        var service = f.Open();
        using var claude = service.Start("claude", "a", [item], s, f.Budget);
        using var codex = service.Start("codex", "b", [item], s, f.Budget);
        Assert.Equal(2, claude.PluginFolders.Count);
        Assert.All(claude.PluginFolders, p => Assert.True(File.Exists(Path.Combine(p, ".claude-plugin", "plugin.json"))));
        Assert.Equal(2, claude.Skills.Count);
        Assert.Empty(codex.PluginFolders);
        Assert.Equal(2, codex.Skills.Count);
        Assert.DoesNotContain(Directory.GetFiles(codex.Folder, "*", SearchOption.AllDirectories), p => p.EndsWith(".mcp.json", StringComparison.Ordinal));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-21-smallest-window
    public void Budget_uses_smallest_offered_model_and_user_skills_keep_their_descriptions()
    {
        Assert.Equal(2000, GenerationBudget.ForModels([8000, 2000]).Characters);
        Assert.Equal(2000, GenerationBudget.ForModels([2000, 8000]).Characters);
        Assert.Throws<ArgumentException>(() => GenerationBudget.ForModels([]));
        foreach (var provider in new[] { "claude", "codex", "native" })
        {
            using var f = new GenerationFixture();
            var a = f.Add("A1", "pdf");
            var b = f.Add("O3", "doc", priority: 10);
            var own = new ExistingSkill("own", new string('x', 1500));
            using var g = f.Open().Start(provider, "a", [a, b], f.Switches((a, true), (b, true)), new(1720), [own]);
            Assert.Equal("pdf", Assert.Single(g.Skills).Name);
            Assert.Equal(1500, own.Description.Length);
            Assert.Contains(b.Skills[0].Id, g.LeftOut);
        }
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-shared-listing
    public void Listing_generation_is_shared_by_probes_and_paths_are_rewritten_for_each_chat()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var s = f.Switches((item, true));
        using var first = service.Listing("codex", [item], s, f.Budget);
        using var second = service.Listing("codex", [item], s, f.Budget);
        using var chat = service.Start("codex", "a", [item], s, f.Budget);
        Assert.Equal(first.Folder, second.Folder);
        Assert.NotEqual(first.Folder, chat.Folder);
        Assert.Equal(chat.Skills[0].File, first.ForChat(first.Skills[0].File, chat));
        Assert.Equal("user path", first.ForChat("user path", chat));
        first.Dispose();
        service.Collect();
        Assert.True(Directory.Exists(second.Folder));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-restart-stable-path
    public void A_restart_reuses_the_chat_path_and_complete_references()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var s = f.Switches((item, true));
        var first = f.Open();
        var g = first.Start("codex", "a", [item], s, f.Budget);
        var path = g.Folder;
        var reference = g.Skills[0].Folder + Path.DirectorySeparatorChar + "references" + Path.DirectorySeparatorChar + "example.txt";
        g.Dispose();
        first.Dispose();
        var restarted = f.Open();
        using var resumed = restarted.Start("codex", "a", [item], s, f.Budget);
        Assert.Equal(path, resumed.Folder);
        Assert.True(File.Exists(reference));
        Assert.Equal("REFERENCE SENTINEL", resumed.ReadReference("pdf", "references/example.txt"));
    }
}
