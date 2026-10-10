using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Core.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17, OD-0099 Sections C): "two Settings pages, Plugins and Skills ... no switcher, no mixed page". The rows of one page, what each carries (ONDEMAND-PLAN.md 2.5: title, Marrow's
// one-liner, source and licence as text, size, needs, the action) and the per-skill switches inside an installed set (section 6).
public sealed class LibraryRowsTests
{
    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Plugins_lists_only_plugins_and_Skills_only_skill_sets_and_never_a_tool_or_a_package_set()
    {
        Assert.Equal(["PA", "PB"], PageFixtures.Rows(LibrarySection.Plugins).Select(r => r.Id));
        Assert.Equal(["SA", "SB", "SC"], PageFixtures.Rows(LibrarySection.Skills).Select(r => r.Id));
        var catalogue = Marrow.Core.Tests.Port.Skills.Catalogue.CatalogueFixtures.Real;
        foreach (var section in new[] { LibrarySection.Plugins, LibrarySection.Skills })
        {
            var real = LibraryRows.Build(section, catalogue, AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches());
            Assert.NotEmpty(real);
            Assert.Equal(catalogue.Items.Where(i => i.Kind == section.Kind()).Select(i => i.Id), real.Select(r => r.Id));
        }
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_row_carries_marrow_s_title_and_one_liner_the_size_and_the_source_and_licence_as_text()
    {
        var row = PageFixtures.Rows(LibrarySection.Skills).Single(r => r.Id == "SA");
        Assert.Equal("Fixture skills A", row.Title);
        Assert.Equal("Nth-made summary of Fixture skills A", row.Summary);
        Assert.Equal(3.5, row.SizeMb);
        Assert.Equal("2 skills \u00B7 3.5 MB \u00B7 example/sa \u00B7 Apache-2.0", row.Meta);
        Assert.Equal("github.com/example/sa at 0123456", row.Source);
        Assert.Equal("Apache-2.0", row.Licence);
        Assert.Null(row.TermsLabel);
        Assert.False(row.TermsOwed);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_row_counts_its_folders_and_their_skills()
    {
        var row = PageFixtures.Rows(LibrarySection.Plugins).Single(r => r.Id == "PA");
        Assert.Equal("2 plugins \u00B7 45 skills \u00B7 8.1 MB \u00B7 example/pa \u00B7 Apache-2.0", row.Meta);
        Assert.Equal(["sales", "legal"], row.Switches.Select(s => s.Label));
        Assert.Equal(["36 skills", "9 skills"], row.Switches.Select(s => s.Detail));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void An_item_under_a_vendor_s_terms_says_so_on_its_row_until_and_after_the_terms_are_accepted()
    {
        var owed = PageFixtures.Rows(LibrarySection.Skills).Single(r => r.Id == "SC");
        Assert.Equal("Under Anthropic's terms", owed.TermsLabel);
        Assert.True(owed.TermsOwed);
        Assert.Equal("Under OpenAI's terms", PageFixtures.Rows(LibrarySection.Plugins).Single(r => r.Id == "PB").TermsLabel);
        var accepted = AddonsState.Empty.AcceptTerms(PageFixtures.LicenceHash, "SC", new DateTimeOffset(2026, 10, 5, 9, 0, 0, TimeSpan.Zero));
        var after = PageFixtures.Rows(LibrarySection.Skills, accepted).Single(r => r.Id == "SC");
        Assert.Equal("Under Anthropic's terms", after.TermsLabel);
        Assert.False(after.TermsOwed);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_needs_are_named_and_a_tool_found_on_this_PC_says_so_with_its_path()
    {
        var found = AddonsState.Empty.MergeFound(PageFixtures.Catalogue(), ["TP"], [new FoundOnPc("TP", @"C:\Python\python.exe")]);
        var row = PageFixtures.Rows(LibrarySection.Skills, found).Single(r => r.Id == "SA");
        Assert.Equal(["Python", "LibreOffice"], row.Needs.Select(n => n.Title));
        Assert.True(row.Needs[0].FoundOnThisPc);
        Assert.Equal(@"C:\Python\python.exe", row.Needs[0].FoundPath);
        Assert.False(row.Needs[1].FoundOnThisPc);
        Assert.Equal("Found on this PC", LibraryWords.FoundOnThisPc);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Each_state_of_the_pipeline_reaches_the_row()
    {
        var failed = PageFixtures.Rec("SA", ItemState.Failed, null, PageFixtures.Pin, "Couldn\u2019t reach github.com.");
        var rows = PageFixtures.Rows(LibrarySection.Skills, null, null, failed);
        Assert.Equal(ItemState.Failed, rows[0].State);
        Assert.Equal("Couldn\u2019t reach github.com.", rows[0].Failure);
        Assert.Equal(ItemState.Available, rows[1].State);
        var update = PageFixtures.Rows(LibrarySection.Skills, null, null, PageFixtures.Rec("SA", ItemState.UpdateAvailable, "old", "new"));
        Assert.Equal("old \u2192 new", update[0].Update);
        Assert.True(update[0].OnThisPc);
        Assert.False(PageFixtures.Rows(LibrarySection.Skills, null, null, PageFixtures.Rec("SA", ItemState.Queued, null, PageFixtures.Pin))[0].OnThisPc);
        Assert.True(PageFixtures.Rows(LibrarySection.Skills, null, null, PageFixtures.Rec("SA", ItemState.Downloading, null, PageFixtures.Pin))[0].Busy);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_set_that_is_not_downloaded_lists_its_skills_with_dead_switches_and_says_nothing_is_listed()
    {
        var row = PageFixtures.Rows(LibrarySection.Skills, null, new Switches("SA/skills/docx")).Single(r => r.Id == "SA");
        Assert.Equal(["/docx", "/xlsx"], row.Switches.Select(s => s.Label));
        Assert.All(row.Switches, s => Assert.False(s.Enabled));
        Assert.All(row.Switches, s => Assert.False(s.On));
        Assert.Equal(0, row.ListedOn);
        Assert.Equal("Not listed: not downloaded", row.ListedText);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void The_switches_of_an_installed_set_follow_enabled_json_and_count_what_is_listed()
    {
        var installed = PageFixtures.Rec("SA", ItemState.Installed, PageFixtures.Pin);
        var row = PageFixtures.Rows(LibrarySection.Skills, null, new Switches("SA/skills/docx"), installed).Single(r => r.Id == "SA");
        Assert.All(row.Switches, s => Assert.True(s.Enabled));
        Assert.Equal([true, false], row.Switches.Select(s => s.On));
        Assert.Equal(["SA/skills/docx", "SA/skills/xlsx"], row.Switches.Select(s => s.Key));
        Assert.All(row.Switches, s => Assert.Equal(SwitchKind.Skill, s.Kind));
        Assert.Equal(1, row.ListedOn);
        Assert.Equal(2, row.ListedOf);
        Assert.Equal("1 of 2 listed", row.ListedText);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_row_counts_the_skills_of_its_switched_on_folders()
    {
        var installed = PageFixtures.Rec("PA", ItemState.Installed, PageFixtures.Pin);
        var row = PageFixtures.Rows(LibrarySection.Plugins, null, new Switches("PA/sales"), installed).Single(r => r.Id == "PA");
        Assert.All(row.Switches, s => Assert.Equal(SwitchKind.PluginFolder, s.Kind));
        Assert.Equal(["PA/sales", "PA/legal"], row.Switches.Select(s => s.Key));
        Assert.Equal(36, row.ListedOn);
        Assert.Equal("36 skills listed", row.ListedText);
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void A_skill_with_no_path_in_the_catalogue_has_no_switch_because_nothing_could_key_it()
    {
        var catalogue = PageFixtures.Catalogue();
        var broken = catalogue with { Items = catalogue.Items.Select(i => i.Id == "SA" ? i with { Skills = [i.Skills[0] with { Path = null }, i.Skills[1]] } : i).ToList() };
        var row = LibraryRows.Build(LibrarySection.Skills, broken, AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches()).Single(r => r.Id == "SA");
        Assert.Equal(["/xlsx"], row.Switches.Select(s => s.Label));
    }

    [Fact]
    [Trait("Feature", "SK-15")]
    [Trait("Rule", "R-TEST-4")]
    public void Progress_reaches_the_row_from_the_run()
    {
        var rows = LibraryRows.Build(LibrarySection.Skills, PageFixtures.Catalogue(), AddonsState.Empty, PageFixtures.Records(PageFixtures.Rec("SA", ItemState.Downloading, null, PageFixtures.Pin)),
            id => id == "SA" ? 0.43 : null, new Switches());
        Assert.Equal(0.43, rows[0].Progress);
        Assert.Null(rows[1].Progress);
    }
}
