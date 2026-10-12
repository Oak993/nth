using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// ED-NEW-dl-13 (the lead's 2026-10-11 decision): nth shows each plugin's own licence on its switch line, and the Download-all list names every skill a set leaves out, with the reason, under
// the set's own line. Marrow-made fixtures only.
public sealed class PluginSetPageTests
{
    private static CatalogueItem Plugins() => PageFixtures.Plugin("PA", "Fixture plugins A", 8.1, folders: [("plugins/sales", 2), ("partner-built/legal", 1)]) with
    {
        LicenceName = "Apache-2.0, MIT",
        Folders =
        [
            new CatalogueFolder { Name = "plugins/sales", SkillCount = 2, LicenceName = "Apache-2.0", LicenceFile = "plugins/sales/LICENSE" },
            new CatalogueFolder { Name = "partner-built/legal", SkillCount = 1, LicenceName = "MIT", LicenceFile = "partner-built/legal/LICENSE" },
        ],
        Skills =
        [
            new CatalogueSkill { Folder = "deck", Name = "deck", Path = "plugins/sales/skills/deck", Needs = [] },
            new CatalogueSkill { Folder = "forecast", Name = "forecast", Path = "plugins/sales/skills/forecast", Needs = [], LicenceName = "MIT", LicenceFile = "plugins/sales/skills/forecast/LICENSE.txt" },
            new CatalogueSkill { Folder = "contracts", Name = "contracts", Path = "partner-built/legal/skills/contracts", Needs = [] },
        ],
        SkillsListed = true,
        Skipped =
        [
            new CatalogueSkipped { Path = "plugins/sales/skills/quota", Name = "quota", Reason = "No clear licence of its own" },
            new CatalogueSkipped { Path = "partner-built/legal/skills/Bad_Name", Name = null, Reason = "Its name breaks the skill name rule" },
        ],
    };

    private static SkillsCatalogue Catalogue() => PageFixtures.Catalogue() with { Items = PageFixtures.Catalogue().Items.Select(i => i.Id == "PA" ? Plugins() : i).ToList() };

    private static IReadOnlyList<LibraryRow> Rows(params ItemRecord[] records) =>
        LibraryRows.Build(LibrarySection.Plugins, Catalogue(), AddonsState.Empty, PageFixtures.Records(records), _ => null, new Switches("PA/plugins/sales"));

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_line_shows_the_plugin_s_name_its_skills_and_its_own_licences_and_keeps_its_path_as_the_switch_key()
    {
        var row = Rows(PageFixtures.Rec("PA", ItemState.Installed, PageFixtures.Pin)).Single(r => r.Id == "PA");
        Assert.Equal(["sales", "legal"], row.Switches.Select(s => s.Label));
        Assert.Equal(["2 skills · Apache-2.0 · MIT", "1 skill · MIT"], row.Switches.Select(s => s.Detail));
        Assert.Equal(["PA/plugins/sales", "PA/partner-built/legal"], row.Switches.Select(s => s.Key));
        Assert.Equal([true, false], row.Switches.Select(s => s.On));
        Assert.Equal((2, 3), (row.ListedOn, row.ListedOf));
        Assert.Equal("Apache-2.0, MIT", row.Licence);
        // A plugin whose catalogue entry records no licence per plugin keeps the plain count.
        var plain = PageFixtures.Rows(LibrarySection.Plugins, null, new Switches(), PageFixtures.Rec("PA", ItemState.Installed, PageFixtures.Pin)).Single(r => r.Id == "PA");
        Assert.Equal(["36 skills", "9 skills"], plain.Switches.Select(s => s.Detail));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void Download_all_lists_each_skill_a_set_leaves_out_under_the_set_with_its_reason_and_neither_fetches_nor_counts_it()
    {
        var plan = DownloadAllPlan.Build(LibrarySection.Plugins, Rows(), Catalogue(), null);
        Assert.Equal(
            [("PA", PlanLineKind.Download, null), ("PA/plugins/sales/skills/quota", PlanLineKind.SkippedSkill, "No clear licence of its own"),
                ("PA/partner-built/legal/skills/Bad_Name", PlanLineKind.SkippedSkill, "Its name breaks the skill name rule"), ("PB", PlanLineKind.NeedsTerms, (string?)null)],
            plan.Lines.Select(l => (l.Id, l.Kind, l.Reason)));
        Assert.Equal(["/quota", "/Bad_Name"], plan.Lines.Where(l => l.Kind == PlanLineKind.SkippedSkill).Select(l => l.Title));
        Assert.All(plan.Lines.Where(l => l.Kind == PlanLineKind.SkippedSkill), l => Assert.Null(l.SizeMb));
        Assert.Equal(["PA"], plan.ToDownload);
        Assert.Equal(8.1, plan.DownloadMb, 6);
        Assert.Equal(["github.com"], plan.Hosts);
        Assert.Equal("Included · 2 · 1 needs its terms first, skipped · 2 skills are left out of their sets, skipped", plan.IncludedHeading);
        Assert.Equal("Included · 1 · 1 skill is left out of its set, skipped", LibraryWords.Included(1, 0, 0, 0, 0, 1));
        Assert.Equal("Skipped: No clear licence of its own", LibraryWords.SkippedSkill("No clear licence of its own"));
    }

    [Fact]
    [Trait("Feature", "RR-14")]
    [Trait("Spec", "DL-17")]
    [Trait("Rule", "R-TEST-4")]
    public void A_set_already_here_lists_no_skipped_skills_and_one_this_build_cannot_fetch_still_says_what_it_would_leave_out()
    {
        var here = DownloadAllPlan.Build(LibrarySection.Plugins, Rows(PageFixtures.Rec("PA", ItemState.Installed, PageFixtures.Pin)), Catalogue(), null);
        Assert.DoesNotContain(here.Lines, l => l.Kind == PlanLineKind.SkippedSkill);
        var cant = DownloadAllPlan.Build(LibrarySection.Plugins, Rows(), Catalogue(), null, id => id != "PA");
        Assert.Equal([PlanLineKind.CantFetch, PlanLineKind.SkippedSkill, PlanLineKind.SkippedSkill, PlanLineKind.NeedsTerms], cant.Lines.Select(l => l.Kind));
        Assert.Empty(cant.ToDownload);
    }
}
