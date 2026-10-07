using Marrow.Core.Models;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Pages;

// RR-16: observed provider skills are distinct from catalogue availability and local imports.
public sealed class ProviderOriginReportsTests
{
    [Fact]
    [Trait("Feature", "SK-16")]
    [Trait("Rule", "R-TEST-4")]
    public void Only_a_synced_source_claims_account_origin_and_equivalence_uses_observed_names()
    {
        using var home = new ForeignHome();
        home.Skill(".claude/skills/synced", "docx", "docx", "Synced fixture.");
        home.Skill(".claude/skills", "local", "local", "Local fixture.");
        var reports = new ProviderSkillReports(() => home.Home, Path.Combine(home.Root, "marrow-data"));
        Assert.Empty(reports.Report()); // a folder alone does not establish a provider report
        var before = ForeignHome.Fingerprint(home.Home);
        reports.Observe("claude", "Claude", home.Root, [new SlashCommand { Name = "ordinary-command", Detail = "A command." }]);
        var skill = Assert.Single(Assert.Single(reports.Report()).Skills);
        Assert.Equal(("docx", "Synced fixture.", SkillOrigin.AccountSynced), (skill.Name, skill.Description, skill.Origin));
        Assert.True(skill.AccountProvided);
        Assert.Equal(["doc", "documents", "docx"], skill.EquivalentNames);
        Assert.True(GenerationFilters.Clashes("claude", "documents", reports.Existing("claude", home.Root)));
        Assert.Equal(before, ForeignHome.Fingerprint(home.Home));
        Directory.Delete(Path.Combine(home.Home, ".claude/skills/synced/docx"), true);
        Assert.Empty(reports.Report()); // removed sync is not reported from stale cache
    }

    [Fact]
    [Trait("Feature", "SK-16")]
    [Trait("Rule", "R-TEST-4")]
    public void Provider_lists_exclude_commands_disabled_snapshots_and_marrows_generations()
    {
        using var home = new ForeignHome();
        var data = Path.Combine(home.Root, "marrow-data");
        var reports = new ProviderSkillReports(() => home.Home, data);
        var user = home.Skill(".codex/skills", "user", "user", "User fixture.");
        reports.Observe("codex", "Codex", home.Root,
        [
            new SlashCommand { Name = "user", Detail = "User fixture.", SkillPath = Path.Combine(user, "SKILL.md") },
            new SlashCommand { Name = "plugin:Presentations", Detail = "Provider fixture.", SkillPath = Path.Combine(home.Home, ".codex/plugins/cache/runtime/slides/SKILL.md") },
            new SlashCommand { Name = "compact", Detail = "A command." },
            new SlashCommand { Name = "marrow:pdf", Detail = "Owned fixture.", SkillPath = Path.Combine(user, "SKILL.md") },
            new SlashCommand { Name = "hidden-owned", Detail = "Owned fixture.", SkillPath = Path.Combine(data, "skills/views/codex/chat-x/hidden-owned/SKILL.md") },
        ]);
        var skills = Assert.Single(reports.Report()).Skills;
        Assert.Equal(2, skills.Count);
        Assert.Equal(SkillOrigin.User, skills.Single(s => s.Name == "user").Origin);
        var plugin = skills.Single(s => s.Name == "plugin:Presentations");
        Assert.Equal(SkillOrigin.ProviderPlugin, plugin.Origin);
        Assert.False(plugin.AccountProvided);
        Assert.Equal(["pptx", "presentations", "slides"], plugin.EquivalentNames);
        Assert.Empty(reports.Existing("codex", Path.Combine(home.Root, "another-project")));
        reports.Observe("codex", "Codex", home.Root, []);
        Assert.Empty(reports.Report()); // a later empty listing replaces the earlier one
    }
}
