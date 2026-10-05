using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Pages;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pages;

// Unit SK-15 (DL-17, OD-0099 Sections C): one library page over its backend: rows, selection, the per-skill switches, the terms step, a single item's download, the two-step removal and reset,
// update and repair, "Already available through your providers".
[Trait("Feature", "SK-15")]
[Trait("Spec", "DL-17")]
public sealed class LibraryControllerPageTests
{
    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Each_page_lists_its_own_items_and_the_first_is_selected_until_another_is_chosen()
    {
        using var skills = new LibraryRig();
        using var plugins = new LibraryRig(LibrarySection.Plugins);
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], skills.Page.Rows.Select(r => r.Id));
        Assert.Equal(["P1", "P2"], plugins.Page.Rows.Select(r => r.Id));
        Assert.Equal("S1", skills.Page.SelectedId);
        var changed = 0;
        skills.Page.Changed += () => changed++;
        skills.Page.Select("S3");
        Assert.Equal("S3", skills.Page.SelectedId);
        Assert.Equal("Fixture S3", skills.Page.Selected!.Title);
        Assert.Equal(1, changed);
        skills.Page.Select("S3");
        skills.Page.Select("P1");
        Assert.Equal(1, changed);
        Assert.Equal("S3", skills.Page.SelectedId);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_switch_writes_through_the_backend_for_a_downloaded_set_and_is_ignored_for_one_that_is_not()
    {
        using var rig = new LibraryRig();
        rig.Backend.Install("S1", LibraryFixtures.Pin);
        Assert.Equal("Listed in every session: 0 skills", rig.Page.Listing);
        rig.Page.SetSwitch("S1/skills/docx", true);
        Assert.Equal([("S1/skills/docx", true)], rig.Backend.Writes);
        Assert.Equal([true, false], rig.Row("S1").Switches.Select(s => s.On));
        Assert.Equal("Listed in every session: 1 skill", rig.Page.Listing);
        rig.Page.SetSwitch("S1/skills/docx", true);
        Assert.Single(rig.Backend.Writes);
        rig.Page.SetSwitch("S2/skills/alpha", true);
        Assert.Single(rig.Backend.Writes);
        rig.Page.SetSwitch("S1/skills/docx", false);
        Assert.Equal([("S1/skills/docx", true), ("S1/skills/docx", false)], rig.Backend.Writes);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void A_plugin_folder_switch_counts_its_skills()
    {
        using var rig = new LibraryRig(LibrarySection.Plugins);
        rig.Backend.Install("P1", LibraryFixtures.Pin);
        rig.Page.SetSwitch("P1/sales", true);
        Assert.Equal("3 skills listed", rig.Row("P1").ListedText);
        Assert.Equal("Listed in every session: 3 skills", rig.Page.Listing);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task The_terms_step_gates_the_download_until_the_read_control_is_used_and_then_accepts()
    {
        using var rig = new LibraryRig();
        Assert.True(rig.Row("S4").TermsOwed);
        Assert.Equal("Under Anthropic's terms", rig.Page.Terms("S4")!.Label);
        Assert.Equal("Marrow-made fixture terms for S4", await rig.Page.LicenceText("S4"));
        await LibraryRig.Within(rig.Page.Download("S4"));
        Assert.Empty(rig.Install.Calls);
        Assert.Equal(ItemState.Available, rig.Row("S4").State);
        rig.Page.AcceptTerms("S4");
        Assert.Equal(["accept S4"], rig.Backend.Calls);
        Assert.False(rig.Row("S4").TermsOwed);
        Assert.Null(rig.Page.Terms("S4"));
        await LibraryRig.Within(rig.Page.Download("S4"));
        Assert.Equal(["S4"], rig.Install.Calls);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task One_item_downloads_by_itself_and_ends_installed_or_failed_with_its_reason()
    {
        using var rig = new LibraryRig();
        rig.Install.FailOn("S2", "The file didn’t match its checksum.");
        await LibraryRig.Within(rig.Page.Download("S1"));
        await LibraryRig.Within(rig.Page.Download("S2"));
        Assert.Equal(ItemState.Installed, rig.Row("S1").State);
        Assert.Equal(ItemState.Failed, rig.Row("S2").State);
        Assert.Equal("The file didn’t match its checksum.", rig.Row("S2").Failure);
        await LibraryRig.Within(rig.Page.Download("S1"));
        Assert.Equal(["S1", "S2"], rig.Install.Calls);
        rig.Install.StopFailing("S2");
        await LibraryRig.Within(rig.Page.Download("S2"));
        Assert.Equal(ItemState.Installed, rig.Row("S2").State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Cancelling_one_download_stops_it_and_the_row_goes_back_to_Download()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S1");
        var task = rig.Page.Download("S1");
        await LibraryRig.Within(rig.Install.Started("S1"));
        Assert.Equal(ItemState.Downloading, rig.Row("S1").State);
        rig.Page.CancelItem("S1");
        await LibraryRig.Within(task);
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
        Assert.Equal(["S1"], rig.Install.Cancelled);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_second_download_waits_its_turn_as_Queued()
    {
        using var rig = new LibraryRig();
        rig.Install.HoldOn("S1");
        var first = rig.Page.Download("S1");
        await LibraryRig.Within(rig.Install.Started("S1"));
        var second = rig.Page.Download("S2");
        await LibraryRig.Until(() => rig.Row("S2").State == ItemState.Queued);
        Assert.Equal(["S1"], rig.Install.Calls);
        rig.Install.Release("S1");
        await LibraryRig.Within(first);
        await LibraryRig.Within(second);
        Assert.Equal(["S1", "S2"], rig.Install.Calls);
        Assert.Equal(1, rig.Install.MostAtOnce);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Removal_takes_two_presses_and_the_first_removes_nothing()
    {
        using var rig = new LibraryRig();
        rig.Backend.Install("S1", LibraryFixtures.Pin);
        rig.Page.AskRemove("S1");
        Assert.True(rig.Page.RemoveAsked("S1"));
        Assert.Empty(rig.Backend.Calls);
        Assert.Equal(ItemState.Installed, rig.Row("S1").State);
        rig.Page.CancelRemove("S1");
        Assert.False(rig.Page.RemoveAsked("S1"));
        await LibraryRig.Within(rig.Page.ConfirmRemove("S1"));
        Assert.Empty(rig.Backend.Calls);
        rig.Page.AskRemove("S1");
        await LibraryRig.Within(rig.Page.ConfirmRemove("S1"));
        Assert.Equal(["remove S1"], rig.Backend.Calls);
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
        Assert.False(rig.Page.RemoveAsked("S1"));
        rig.Page.AskRemove("S2");
        Assert.False(rig.Page.RemoveAsked("S2"));
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task Reset_takes_two_presses_and_runs_the_backends_reset_once()
    {
        using var rig = new LibraryRig();
        rig.Backend.Install("S1", LibraryFixtures.Pin);
        rig.Page.AskReset();
        Assert.True(rig.Page.ResetAsked);
        Assert.Empty(rig.Backend.Calls);
        rig.Page.CancelReset();
        await LibraryRig.Within(rig.Page.ConfirmReset());
        Assert.Empty(rig.Backend.Calls);
        rig.Page.AskReset();
        await LibraryRig.Within(rig.Page.ConfirmReset());
        Assert.Equal(["reset"], rig.Backend.Calls);
        Assert.False(rig.Page.ResetAsked);
        Assert.Equal(ItemState.Available, rig.Row("S1").State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task An_update_runs_through_the_pipeline_and_a_failed_one_shows_why_and_tries_again_as_an_update()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.UpdateAvailable, "old", "new", null)], [], [], []);
        Assert.Equal("old → new", rig.Row("S1").Update);
        rig.Backend.UpdateWork = (_, _, _) => Task.FromException(new IOException("The disk is full."));
        await LibraryRig.Within(rig.Page.Update("S1"));
        Assert.Equal(ItemState.Failed, rig.Row("S1").State);
        Assert.Equal("The disk is full.", rig.Row("S1").Failure);
        rig.Backend.UpdateWork = null;
        await LibraryRig.Within(rig.Page.Download("S1"));
        Assert.Equal(["update S1", "update S1"], rig.Backend.Calls);
        Assert.Empty(rig.Install.Calls);
        Assert.Equal(ItemState.UpdateAvailable, rig.Row("S1").State);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_damaged_item_is_repaired_and_nothing_else_is_fetched()
    {
        using var rig = new LibraryRig();
        rig.Backend.State = AddonsState.Restore([new ItemRecord("S1", ItemState.Damaged, "old", null, null)], [], [], []);
        await LibraryRig.Within(rig.Page.Repair("S1"));
        Assert.Equal(["repair S1"], rig.Backend.Calls);
        Assert.Empty(rig.Install.Calls);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_providers_section_lists_only_what_a_provider_reports_and_is_empty_by_default()
    {
        using var rig = new LibraryRig();
        Assert.Empty(rig.Page.Providers);
        rig.Backend.Reports.Add([new ProviderSkillGroup("Claude", [new ExistingSkill("docx", "Word files")]), new ProviderSkillGroup("Codex", [])]);
        Assert.Equal(["Claude"], rig.Page.Providers.Select(g => g.Provider));
        Assert.Equal("docx", rig.Page.Providers[0].Skills[0].Name);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void Every_change_raises_Changed_and_grows_the_revision()
    {
        using var rig = new LibraryRig();
        var seen = new List<int>();
        rig.Page.Changed += () => seen.Add(rig.Page.Revision);
        rig.Page.Select("S2");
        rig.Page.OpenDownloadAll();
        rig.Page.CancelConfirmation();
        Assert.Equal(3, seen.Count);
        Assert.Equal(seen.OrderBy(i => i), seen);
        Assert.Equal(seen.Distinct(), seen);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")]
    public void The_real_catalogue_gives_both_pages_rows_and_a_Download_all_that_starts_nothing()
    {
        var catalogue = Marrow.Core.Port.Skills.Catalogue.SkillsCatalogueLoader.LoadEmbedded();
        foreach (var section in new[] { LibrarySection.Plugins, LibrarySection.Skills })
        {
            using var rig = new LibraryRig(section, catalogue);
            Assert.NotEmpty(rig.Page.Rows);
            rig.Page.OpenDownloadAll();
            Assert.Equal(RunPhase.Confirm, rig.Page.Dialog.Phase);
            rig.Page.CancelConfirmation();
            Assert.Empty(rig.Install.Calls);
        }
    }
}
