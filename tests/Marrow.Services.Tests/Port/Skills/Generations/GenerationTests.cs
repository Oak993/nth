// PORT-ONLY: offline acceptance for SK-9 / DL-9. All text is Marrow-made fixture text.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Generations;

[Trait("Feature", "SK-9")]
[Trait("Spec", "DL-9")]
public sealed class GenerationTests
{
    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-installed-enabled
    public void Disabled_and_not_installed_skills_never_appear()
    {
        using var f = new GenerationFixture();
        var installed = f.Add("A1", "pdf");
        var missing = f.Add("O3", "doc", installed: false);
        using var g = f.Open().Start("codex", "a", [installed, missing], f.Switches((installed, false), (missing, true)), f.Budget);
        Assert.Empty(g.Skills);
        Assert.DoesNotContain("SKILL.md", string.Join(',', Directory.GetFiles(g.Folder, "*", SearchOption.AllDirectories)), StringComparison.Ordinal);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: OD-0099-progressive-disclosure
    public void Catalogue_has_only_names_and_descriptions_and_text_requires_invocation()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        using var g = f.Open().Start("codex", "a", [item], f.Switches((item, true)), f.Budget);
        Assert.Contains("pdf", g.Catalogue, StringComparison.Ordinal);
        Assert.Contains("Fixture description", g.Catalogue, StringComparison.Ordinal);
        Assert.DoesNotContain("BODY SENTINEL", g.Catalogue, StringComparison.Ordinal);
        Assert.Contains("BODY SENTINEL", g.Invoke("pdf"), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => g.Invoke("absent"));
        Assert.Throws<ArgumentException>(() => g.ReadReference("pdf", "../outside"));
        Assert.Throws<ArgumentException>(() => g.ReadReference("pdf", "/outside"));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: Q-SKILLS-1-A
    public void Defaults_enable_approved_groups_but_not_external_access_or_large_sets()
    {
        Assert.True(SkillDefaults.Enabled("O3", SkillGroup.Documents, false));
        Assert.True(SkillDefaults.Enabled("A1", SkillGroup.SkillBuilding, false));
        Assert.False(SkillDefaults.Enabled("A1", SkillGroup.Design, true));
        Assert.False(SkillDefaults.Enabled("A3", SkillGroup.Documents, false));
        Assert.False(SkillDefaults.Enabled("A4", SkillGroup.Review, false));
        Assert.False(SkillDefaults.Enabled("O5", SkillGroup.Security, false));
        Assert.False(SkillDefaults.Enabled("O2", SkillGroup.Other, false));
        Assert.False(SkillSwitches.Empty.PluginEnabled("A3"));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.3-provider-filters
    public void Provider_filters_and_explicit_only_are_applied_independently()
    {
        using var f = new GenerationFixture();
        var items = new[] { f.Add("A1", "academy-guide"), f.Add("A2", "claude-api"), f.Add("O1", "review-agent"), f.Add("O2", "plugin-creator") };
        var switches = f.Switches(items.Select(i => (i, true)).ToArray());
        var service = f.Open();
        using var claude = service.Start("claude", "c", items, switches, f.Budget);
        using var grok = service.Start("grok", "g", items, switches, f.Budget);
        using var codex = service.Start("codex", "x", items, switches, f.Budget);
        Assert.Equal(new[] { "academy-guide", "review-agent" }, claude.Skills.Select(s => s.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("review-agent", claude.Catalogue, StringComparison.Ordinal);
        Assert.Equal("claude-api", Assert.Single(grok.Skills).Name);
        Assert.Equal(new[] { "claude-api", "plugin-creator" }, codex.Skills.Select(s => s.Name).Order(StringComparer.Ordinal));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-5-equivalence
    public void Runtime_plugin_names_and_case_match_equivalence_groups()
    {
        using var f = new GenerationFixture();
        var items = new[] { f.Add("O3", "doc"), f.Add("O2", "spreadsheet"), f.Add("O1", "pdf"), f.Add("A1", "slides") };
        using var g = f.Open().Start("codex", "a", items, f.Switches(items.Select(i => (i, true)).ToArray()),
            f.Budget, [new("documents:documents", "user"), new("spreadsheets:Spreadsheets", "user"), new("PDF:PDF", "user"), new("presentations:Presentations", "user")]);
        Assert.Empty(g.Skills);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: WU-21-budget
    public void User_descriptions_stay_whole_and_lowest_priority_is_left_out()
    {
        using var f = new GenerationFixture();
        var high = f.Add("A1", "pdf", priority: 0);
        var low = f.Add("O3", "doc", priority: 20);
        var user = new ExistingSkill("mine", new string('x', 1500));
        using var g = f.Open().Start("codex", "a", [low, high], f.Switches((low, true), (high, true)), new(1720), [user]);
        Assert.Equal("pdf", Assert.Single(g.Skills).Name);
        Assert.Contains(low.Skills[0].Id, g.LeftOut);
        Assert.Equal(1500, user.Description.Length);
        Assert.True(g.Catalogue.Length <= 850);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-enabled-json
    public void Switches_round_trip_carry_over_drop_removed_and_default_new_skills()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("O3", "pdf");
        var doc = f.Add("O2", "doc");
        var state = f.Switches((pdf, false));
        state.Save(f.Paths.EnabledFile);
        var read = SkillSwitches.Load(f.Paths.EnabledFile).Reconcile("next", [pdf, doc]);
        Assert.False(read.Enabled(pdf.Skills[0]));
        Assert.True(read.Enabled(doc.Skills[0]));
        Assert.DoesNotContain(pdf.Skills[0].Id, read.Reconcile("third", [doc]).Skills.Keys);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-frozen-running-resumed
    public void Running_chat_keeps_its_generation_and_resume_uses_current_switches_at_same_path()
    {
        using var f = new GenerationFixture();
        var pdf = f.Add("O3", "pdf");
        var service = f.Open();
        var g = service.Start("codex", "a", [pdf], f.Switches((pdf, true)), f.Budget);
        var path = g.Folder;
        using var other = service.Start("codex", "b", [pdf], f.Switches((pdf, false)), f.Budget);
        Assert.Empty(other.Skills);
        Assert.Single(g.Skills);
        Assert.Contains("BODY SENTINEL", g.Invoke("pdf"), StringComparison.Ordinal);
        g.Dispose();
        using var resumed = service.Start("codex", "a", [pdf], f.Switches((pdf, false)), f.Budget);
        Assert.Equal(path, resumed.Folder);
        Assert.Empty(resumed.Skills);
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-atomic-GC-leases
    public void Published_generations_are_complete_locked_and_lease_protected_from_store_GC()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        var service = f.Open();
        var g = service.Start("codex", "a", [item], f.Switches((item, true)), f.Budget);
        Assert.True(g.Verify());
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(g.Folder)!, "tmp-*"));
        service.Collect();
        Assert.True(Directory.Exists(g.Folder));
        var path = g.Folder;
        g.Dispose();
        service.Collect();
        Assert.False(Directory.Exists(path));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: DL-9-fail-closed
    public void Damaged_store_and_unavailable_protection_are_never_published()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        f.Trees.Unlock(item.Store.Folder);
        File.WriteAllText(Path.Combine(item.Store.Folder, "pdf", "SKILL.md"), "bad");
        f.Trees.Lock(item.Store.Folder);
        using var g = f.Open().Start("codex", "a", [item], f.Switches((item, true)), f.Budget);
        Assert.Empty(g.Skills);
        f.Fake.Refuse = true;
        try { Assert.Throws<IOException>(() => f.Service!.Start("codex", "b", [item], f.Switches((item, true)), f.Budget)); }
        finally { f.Fake.Refuse = false; }
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: BUILD-SK-9-plugin-split
    public void Claude_and_Grok_get_whole_enabled_plugin_other_providers_get_only_skills()
    {
        using var f = new GenerationFixture();
        var plugin = f.Add("O4", "pdf", plugin: true);
        var service = f.Open();
        var on = f.Switches((plugin, true)).WithPlugin("O4", true);
        using var claude = service.Start("claude", "a", [plugin], on, f.Budget);
        using var grok = service.Start("grok", "b", [plugin], on, f.Budget);
        foreach (var g in new[] { claude, grok })
        {
            var root = Assert.Single(g.PluginFolders);
            Assert.True(File.Exists(Path.Combine(root, "hooks", "hooks.json")));
            Assert.True(File.Exists(Path.Combine(root, ".mcp.json")));
            Assert.True(File.Exists(Path.Combine(root, "commands", "sample.md")));
        }
        foreach (var provider in new[] { "codex", "copilot", "opencode", "pi", "commandcode", "cursor", "devin", "native", "agy" })
        {
            using var g = service.Start(provider, provider, [plugin], on, f.Budget);
            Assert.Empty(g.PluginFolders);
            Assert.Single(g.Skills);
            Assert.DoesNotContain(Directory.GetFiles(g.Folder, "*", SearchOption.AllDirectories), p => p.EndsWith("hooks.json", StringComparison.Ordinal) || p.EndsWith(".mcp.json", StringComparison.Ordinal));
        }
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: ED-324-plugin-default
    public void Plugin_hooks_and_MCP_are_off_until_plugin_is_switched_on()
    {
        using var f = new GenerationFixture();
        var plugin = f.Add("O4", "pdf", plugin: true);
        using var g = f.Open().Start("claude", "a", [plugin], f.Switches((plugin, true)), f.Budget);
        Assert.Empty(g.PluginFolders);
        Assert.Single(g.Skills);
        Assert.DoesNotContain(Directory.GetFiles(g.Folder, "*", SearchOption.AllDirectories), p => p.EndsWith("hooks.json", StringComparison.Ordinal) || p.EndsWith(".mcp.json", StringComparison.Ordinal));
    }

    [Fact, Trait("Feature", "SK-9"), Trait("Rule", "R-TEST-4")] // plan rule: PLAN-2.1-verify-on-invocation
    public void Tampered_generation_fails_closed_before_full_text_is_returned()
    {
        using var f = new GenerationFixture();
        var item = f.Add("A1", "pdf");
        using var g = f.Open().Start("codex", "a", [item], f.Switches((item, true)), f.Budget);
        f.Trees.Unlock(g.Folder);
        File.WriteAllText(g.Skills[0].File, "TAMPERED");
        f.Trees.Lock(g.Folder);
        Assert.False(g.Verify());
        Assert.Throws<IOException>(() => g.Invoke("pdf"));
    }
}
