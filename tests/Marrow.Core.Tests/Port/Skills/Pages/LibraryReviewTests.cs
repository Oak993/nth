using Marrow.Core.Port.Skills.Pages;

namespace Marrow.Core.Tests.Port.Skills.Pages;

public sealed class LibraryReviewTests
{
    [Fact, Trait("Rule", "R-TEST-4")]
    public void Running_and_partial_words_match_the_copied_Sections_source_byte_for_byte()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Marrow.sln"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "porting", "first-release", "design-src", "skills-sections", "sd-page.js"));
        Assert.Contains("dl: '" + LibraryWords.RunningSub + "'", source, StringComparison.Ordinal);
        Assert.Contains("partial: '" + LibraryWords.PartialFoot + "'", source, StringComparison.Ordinal);
        foreach (var words in new[] { LibraryWords.Waiting, LibraryWords.Checking, LibraryWords.Installed, LibraryWords.DidNotDownload, LibraryWords.RemoveWhy, LibraryWords.SwitchNote, LibraryWords.ConfirmFoot, LibraryWords.RunningFoot, LibraryWords.DoneFoot, LibraryWords.PausedSub, LibraryWords.EverythingHere })
            Assert.Contains(words, source, StringComparison.Ordinal);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void Needs_rounds_up_so_it_never_understates_required_space()
    {
        var catalogue = PageFixtures.Catalogue();
        catalogue = catalogue with { Items = catalogue.Items.Select(i => i.Id == "SA" ? i with { ApproxSizeMb = 5663.636363 } : i).ToList() };
        var rows = LibraryRows.Build(LibrarySection.Skills, catalogue, Marrow.Core.Port.Skills.State.AddonsState.Empty, PageFixtures.Records(), _ => null, new Switches());
        var plan = DownloadAllPlan.Build(LibrarySection.Skills, rows, catalogue, new DriveSpace("C:", 12_200_000_000));
        Assert.False(plan.CanContinue);
        Assert.Equal("Needs 13 GB free on C:.", plan.RoomNote);
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void An_exact_fit_allows_Continue_but_one_byte_less_does_not()
    {
        var rows = PageFixtures.Rows(LibrarySection.Skills);
        var initial = DownloadAllPlan.Build(LibrarySection.Skills, rows, PageFixtures.Catalogue(), null);
        foreach (var extra in new long[] { -1, 0, 1 })
        {
            var plan = DownloadAllPlan.Build(LibrarySection.Skills, rows, PageFixtures.Catalogue(), new DriveSpace("C:", initial.NeedBytes + extra));
            Assert.Equal(extra >= 0, plan.EnoughRoom);
            Assert.Equal(extra >= 0, plan.CanContinue);
        }
    }

    [Fact, Trait("Rule", "R-TEST-4")]
    public void An_undownloaded_plugin_folder_stays_off_even_with_a_saved_on_switch()
    {
        var row = PageFixtures.Rows(LibrarySection.Plugins, null, new Switches("PA/sales")).Single(r => r.Id == "PA");
        Assert.All(row.Switches, line => { Assert.False(line.On); Assert.False(line.Enabled); });
        Assert.Equal(0, row.ListedOn);
    }
}
