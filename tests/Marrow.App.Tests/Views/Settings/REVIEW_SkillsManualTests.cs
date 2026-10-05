using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Tests.Views.Settings;

// Locked sd-page.js fbBlock/cond/checkAgain and skills-downloads.html CSS 567-575.
// Fake backend, scripted install, manual clock: no download, network, opener or installer.
[Collection("Headless")]
[Trait("Feature", "SK-14")]
[Trait("Spec", "DL-17")]
[Trait("Review", "SK-14")]
[Trait("Rule", "R-TEST-4")]
public sealed class REVIEW_SkillsManualTests(HeadlessSession headless)
{
    private static ManualBlock Failed(LibraryPageHarness h)
    {
        h.Controller.Select("S1");
        h.Install.FailOn("S1", "Couldn’t reach github.com.");
        h.Wait(h.Controller.Download("S1"));
        return h.Page.Details["S1"].GetVisualDescendants().OfType<ManualBlock>().Single();
    }

    [Fact]
    public void REVIEW_Check_again_is_disabled_when_the_install_seam_is_unbound() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        _ = Failed(h);
        h.Backend.CanFetch = false;
        h.Controller.Select("S2");
        h.Controller.Select("S1");
        h.Settle();
        Assert.False(h.Find<Button>("manual-check-again").IsEffectivelyEnabled);
        Assert.False(h.Press("manual-check-again"));
        Assert.DoesNotContain("place S1", h.Backend.Calls);
    });

    [Fact]
    public void REVIEW_Checking_manual_files_stays_in_an_open_visible_fold() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var block = Failed(h);
        h.Install.StopFailing("S1");
        h.Install.HoldOn("S1");
        try
        {
            Assert.True(h.Press("manual-check-again"));
            h.Wait(h.Install.Started("S1"));
            h.Install.Report("S1", 1);
            h.Until(() => h.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Checking);
            var fold = block.GetVisualAncestors().OfType<CzFoldRow>().First();
            Assert.True(fold.Open, "The enclosing failure fold clips the Checking… fallback to zero height.");
            Assert.True(fold.Bounds.Height > 0);
            Assert.True(fold.Opacity > 0);
        }
        finally { h.Install.Release("S1"); h.Until(() => h.Controller.Rows.Single(r => r.Id == "S1").State == ItemState.Installed); }
    });

    [Fact]
    public void REVIEW_Fallback_spacing_and_padding_match_the_locked_CSS() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var block = Failed(h);
        Assert.Equal(new Thickness(11, 9), block.Padding);
        var rows = Assert.IsType<StackPanel>(block.Child);
        Assert.Equal(7, rows.Spacing);
        var step = Assert.IsType<Grid>(rows.Children[1]);
        Assert.Equal(18, step.ColumnDefinitions[0].Width.Value);
    });

    [Fact]
    public void REVIEW_Fallback_buttons_match_the_locked_height_and_font() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        _ = Failed(h);
        foreach (var id in new[] { "manual-open-page", "manual-open-folder", "manual-check-again" })
        {
            var button = h.Find<Button>(id);
            Assert.Equal(24, button.Bounds.Height, 3);
            Assert.Equal(11.5, Assert.IsType<TextBlock>(button.Content).FontSize);
        }
    });

    [Fact]
    public void REVIEW_Manual_folder_text_wraps_inside_its_step_without_covering_Open_folder() => LibraryPageHarness.Run(headless, LibrarySection.Skills, h =>
    {
        var block = Failed(h);
        var folder = block.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == ManualFallbackWords.Folder);
        Assert.Equal(TextWrapping.Wrap, folder.TextWrapping);
        var text = folder.TranslatePoint(default, block)!.Value;
        var open = h.Find<Button>("manual-open-folder");
        var action = open.TranslatePoint(default, block)!.Value;
        Assert.True(text.X + folder.Bounds.Width <= action.X, "The manual path overlaps the Open folder button.");
    });
}
