using Marrow.Core.Models;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Port.Skills.Import;

namespace Marrow.Services.Tests.Port.Skills.Pages;

public sealed class SyncedProviderRemovalTests
{
    [Fact]
    [Trait("Feature", "SK-16")]
    [Trait("Rule", "R-TEST-4")]
    public void A_removed_synced_skill_is_not_an_account_claim_even_when_the_last_listing_had_its_path()
    {
        using var home = new ForeignHome();
        var folder = home.Skill(".claude/skills/synced", "pdf", "pdf", "Synced fixture.");
        var reports = new ProviderSkillReports(() => home.Home, Path.Combine(home.Root, "marrow-data"));
        reports.Observe("claude", "Claude", home.Root, [new SlashCommand { Name = "pdf", Detail = "Synced fixture.", SkillPath = Path.Combine(folder, "SKILL.md") }]);
        Assert.True(Assert.Single(Assert.Single(reports.Report()).Skills).AccountProvided);
        Directory.Delete(folder, true);
        Assert.Empty(reports.Report());
    }
}
