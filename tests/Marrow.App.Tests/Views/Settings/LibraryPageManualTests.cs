using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY (no Swift twin): SK-14, "Download it yourself" on the Skills and Plugins pages: under a failed item the locked skills-downloads design (sd-page.js fbBlock) draws a heading, three steps (the file and where
// it is from with "Open download page ↗", the folder with "Open folder", and Marrow's check with "Check again") and, beside the last, Checking… and Installed. A fake backend and a scripted install: no network, no browser.
[Collection("Headless")]
public sealed class LibraryPageManualTests(HeadlessSession headless)
{
    private static void FailS1(LibraryPageHarness harness)
    {
        harness.Install.FailOn("S1", "Couldn’t reach github.com.");
        _ = harness.Controller.Download("S1");
        harness.Until(() => harness.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Failed);
    }

    private static ManualBlock Block(LibraryPageHarness harness) => harness.Page.Details["S1"].GetVisualDescendants().OfType<ManualBlock>().Single();

    private static IEnumerable<string> Words(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!);

    [Fact]
    [Trait("Decision", "ED-326")]
    public void A_failed_item_shows_the_designs_three_steps_with_the_files_and_the_folder() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        FailS1(harness);
        var words = Words(Block(harness)).ToList();
        Assert.Contains(ManualFallbackWords.Heading, words);
        // ED-NEW-dlhonest-1 (owner 2026-10-11, over the design's whole phrase in code): step 1 is one line of prose, "Get " and the words plain, only the commit in the code face.
        var step = Block(harness).GetVisualDescendants().OfType<TextBlock>().Single(t => t.Inlines is { Count: > 0 });
        var runs = step.Inlines!.OfType<Avalonia.Controls.Documents.Run>().ToList();
        Assert.Equal(ManualFallbackWords.Get + "the repository at commit 0123456, as a .zip", string.Concat(runs.Select(r => r.Text)));
        Assert.Equal(["0123456"], runs.Where(r => r.FontFamily == LibraryUi.MonoFamily).Select(r => r.Text));
        Assert.Equal(ManualFallbackWords.Get, runs[0].Text);
        Assert.Contains("github.com/example/s1/tree/0123456", words);
        Assert.Contains(ManualFallbackWords.PutIn, words);
        Assert.Contains(ManualFallbackWords.Folder, words);
        Assert.Contains(ManualFallbackWords.CheckingSet, words);
        Assert.NotNull(harness.TryFind("manual-open-page"));
        Assert.NotNull(harness.TryFind("manual-open-folder"));
        Assert.NotNull(harness.TryFind("manual-check-again"));
        Assert.Equal(["1", "2", "3"], words.Where(w => w is "1" or "2" or "3"));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void The_two_openers_go_to_the_backend_and_Check_again_keeps_the_folders_files_and_downloads_the_item_again() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        FailS1(harness);
        Assert.True(harness.Press("manual-open-page"));
        Assert.True(harness.Press("manual-open-folder"));
        Assert.Contains("page https://github.com/example/s1/tree/0123456", harness.Backend.Calls);
        Assert.Contains("folder", harness.Backend.Calls);
        harness.Install.StopFailing("S1");
        Assert.True(harness.Press("manual-check-again"));
        harness.Until(() => harness.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Installed);
        Assert.Contains("place S1", harness.Backend.Calls);
        Assert.Equal(["S1", "S1"], harness.Install.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void While_it_checks_the_button_gives_way_to_Checking_and_when_it_is_here_to_Installed() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        FailS1(harness);
        harness.Install.StopFailing("S1");
        harness.Install.HoldOn("S1");
        Assert.True(harness.Press("manual-check-again"));
        harness.Until(() => harness.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Downloading);
        Assert.Null(harness.TryFind("manual-check-again"));
        Assert.Contains(LibraryWords.Checking, Words(Block(harness)));
        harness.Install.Release("S1");
        harness.Until(() => harness.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Installed);
        Assert.Contains(LibraryWords.Installed, Words(Block(harness)));
    });

    [Fact]
    [Trait("Decision", "ED-326")]
    public void An_item_that_has_not_failed_shows_no_block() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        Assert.False(harness.Page.Details["S1"].FailureOpen); // the fold that holds the block is shut until the item fails
    });
}
