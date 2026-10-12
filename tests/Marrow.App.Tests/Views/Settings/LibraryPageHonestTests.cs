using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pages;

namespace Marrow.App.Tests.Views.Settings;

// PORT-ONLY: ED-NEW-dlhonest-1, from the owner's run of the installed release (2026-10-11): every item read "Didn't download", Download all ended "0 of 16 downloaded" in a dialog over the middle of the detail,
// the foot read "Download all tools · 1.08 Gl" and the list cut every title ("Anthropic know…"). A page now offers a download only for an item that can be fetched and says why for the others, the dialog is
// centred on the window it dims, and the list is wide enough for its foot with titles that wrap. A fake backend and a scripted install: no network.
[Collection("Headless")]
public sealed class LibraryPageHonestTests(HeadlessSession headless)
{
    private static Rect In(Visual of, Visual space) => new(of.TranslatePoint(default, space) ?? default, of.Bounds.Size);

    private static IEnumerable<string> Words(Control root) => root.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible && !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!);

    [Fact]
    [Trait("Decision", "ED-NEW-dlhonest-1")]
    public void An_item_that_cannot_be_fetched_says_so_before_a_press_and_is_left_out_of_Download_all() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.Unfetchable.Add("S1");
        harness.Controller.Select("S1");
        harness.Settle();
        var detail = harness.Page.Details["S1"];
        var download = detail.GetVisualDescendants().OfType<Button>().Single(b => LibraryUi.IdOf(b) == "action-download");
        Assert.False(download.IsEnabled);
        var reason = detail.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == LibraryWords.DownloadsUnavailable && t.IsEffectivelyVisible);
        var pane = In(harness.Page.DetailSlot, harness.Window);
        Assert.True(In(reason, harness.Window).Right <= pane.Right + 0.5, "the reason " + In(reason, harness.Window) + " runs past the detail " + pane);
        Assert.DoesNotContain("S1", harness.Controller.Plan.ToDownload);
        Assert.Contains("S2", harness.Controller.Plan.ToDownload);
        harness.Wait(harness.Controller.Download("S1"));
        Assert.Equal((ItemState.Available, null), (harness.Controller.Rows.Single(r => r.Id == "S1").State, harness.Controller.Rows.Single(r => r.Id == "S1").Failure));
        Assert.Empty(harness.Install.Calls);
        harness.Controller.Select("S2");
        harness.Settle();
        Assert.True(harness.Page.Details["S2"].GetVisualDescendants().OfType<Button>().Single(b => LibraryUi.IdOf(b) == "action-download").IsEnabled);
        Assert.DoesNotContain(LibraryWords.DownloadsUnavailable, Words(harness.Page.Details["S2"]));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-dl-6")]
    public void Download_all_lists_what_this_build_cannot_fetch_as_skipped_with_the_reason_instead_of_dropping_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.Unfetchable.UnionWith(["S1", "S3"]);
        harness.Controller.Select("S2"); // a change the page draws again
        harness.Settle();
        var plan = harness.Controller.Plan;
        Assert.Equal(PlanLineKind.CantFetch, plan.Lines.Single(l => l.Id == "S1").Kind);
        Assert.Equal(PlanLineKind.CantFetch, plan.Lines.Single(l => l.Id == "S3").Kind);
        Assert.DoesNotContain("S1", plan.ToDownload);
        Assert.DoesNotContain("S3", plan.ToDownload);
        Assert.Contains("S2", plan.ToDownload);
        Assert.True(harness.Press("download-all"));
        harness.Settle();
        var dialog = harness.Page.Sheet!.Dialog;
        Assert.Equal(["S1", "S2", "S3", "S4", "S5"], dialog.Lines.Keys.Order()); // nothing on the page is dropped from the list
        Assert.Contains("2 can’t be downloaded in this build, skipped", dialog.IncludedShown, StringComparison.Ordinal);
        foreach (var id in new[] { "S1", "S3" })
        {
            var line = dialog.Lines[id];
            Assert.Equal(StatusMark.Mark.Words, line.Mark.Current);
            Assert.Contains(LibraryWords.NotAvailable, Words(line));
        }
        Assert.DoesNotContain(LibraryWords.NotAvailable, Words(dialog.Lines["S2"]));
        Assert.Empty(harness.Install.Calls);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-dl-6")]
    public void When_nothing_left_can_be_fetched_the_off_Download_all_button_says_why() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, harness =>
    {
        harness.Backend.Unfetchable.UnionWith(["P1", "P2"]);
        harness.Controller.Select("P1");
        harness.Settle();
        Assert.False(harness.Page.DownloadAllButton.IsEnabled);
        Assert.Equal(LibraryWords.DownloadsUnavailable, ToolTip.GetTip(harness.Page.DownloadAllButton));
        Assert.All(harness.Controller.Plan.Lines, l => Assert.Equal(PlanLineKind.CantFetch, l.Kind));
        Assert.Equal(LibraryWords.NothingToDownload, harness.Controller.Plan.Statement);
        Assert.Contains("2 can’t be downloaded in this build, skipped", harness.Controller.Plan.IncludedHeading, StringComparison.Ordinal);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-dlhonest-1")]
    public void With_nothing_on_the_page_fetchable_Download_all_is_off_and_does_not_say_everything_is_here() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, harness =>
    {
        harness.Backend.Unfetchable.UnionWith(["P1", "P2"]);
        harness.Controller.Select("P1"); // a change the page draws again
        harness.Settle();
        Assert.True(harness.Controller.Plan.IsEmpty);
        Assert.False(harness.Page.DownloadAllButton.IsEnabled);
        Assert.Equal("Download all plugins", harness.Page.DownloadAllWords);
    });

    [Fact]
    [Trait("Decision", "ED-NEW-dlhonest-1")]
    public void The_Download_all_dialog_is_centred_on_the_window_it_dims_not_on_the_page() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        ((Border)harness.Page.Parent!).Padding = new Thickness(24, 400, 24, 24); // the page runs on below the window, as the Tools library does far down Settings › Updates
        harness.Settle();
        Assert.True(harness.Press("download-all"));
        harness.Settle();
        var layer = harness.Root.Children.OfType<WindowSheetLayer>().Single();
        var sheet = Assert.IsType<DialogSheet>(layer.Sheet);
        var window = new Rect(harness.Root.Bounds.Size);
        var page = In(harness.Page, harness.Window);
        Assert.True(Math.Abs(page.Center.Y - window.Center.Y) > 10, "the fixture's page and window share a centre, so this cannot tell them apart");
        var dialog = In(sheet.Dialog, harness.Root);
        Assert.True(dialog.Height < window.Height, "the dialog " + dialog + " does not fit the window " + window);
        Assert.InRange(dialog.Center.X, window.Center.X - 1, window.Center.X + 1); // layout rounding: half a point
        Assert.InRange(dialog.Center.Y, window.Center.Y - 1, window.Center.Y + 1);
        var wash = In(sheet.Wash, harness.Root);
        Assert.True(wash.Contains(window.TopLeft) && wash.Contains(window.BottomRight), "the wash " + wash + " leaves part of the window " + window + " undimmed");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-dlhonest-1")]
    public void Long_titles_wrap_instead_of_being_cut_and_the_foot_shows_its_whole_label() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, harness =>
    {
        var rows = harness.Page.RowStack.Children.OfType<LibraryRowView>().ToList();
        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
        {
            var title = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == row.Title);
            Assert.All(title.TextLayout.TextLines, line => Assert.False(line.HasCollapsed, row.Title + " is cut"));
            Assert.InRange(title.TextLayout.TextLines.Count, 1, 3);
            Assert.True(In(title, harness.Window).Bottom <= In(row, harness.Window).Bottom + 0.5, row.Title + " leaves its row");
        }
        Assert.Equal(2, rows.Single(r => r.Title == "OpenAI proprietary plugins").GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "OpenAI proprietary plugins").TextLayout.TextLines.Count);
        var label = harness.Page.DownloadAllWords;
        Assert.Equal("Download all plugins · 132 MB", label);
        var button = In(harness.Page.DownloadAllButton, harness.Window);
        var words = harness.Page.DownloadAllButton.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label && t.IsEffectivelyVisible);
        var drawn = In(words, harness.Window);
        Assert.True(words.TextLayout.Width <= words.Bounds.Width + 0.5, "the label needs " + words.TextLayout.Width + " and has " + words.Bounds.Width);
        Assert.True(drawn.Left >= button.Left - 0.5 && drawn.Right <= button.Right + 0.5, "the label " + drawn + " leaves its button " + button);
    }, catalogue: LibraryFixtures.WithSize(Titled(LibraryFixtures.Catalogue(), ("P1", "Anthropic knowledge-work plugins"), ("P2", "OpenAI proprietary plugins")), "P1", 132));

    [Fact]
    [Trait("Decision", "ED-NEW-dlhonest-1")]
    public void The_Tools_page_names_a_tools_own_file_in_its_fallback_and_asks_the_backend_about_the_tool_itself() => headless.OnUiThread(() =>
    {
        var python = new CatalogueItem
        {
            Id = "T1", Kind = ItemKind.Tool, Title = "Python", Summary = "A fixture tool", Vendor = "Fixture", CountState = CountState.Plan, PinState = PinState.Unpinned, Pin = "3.14.8",
            SourceUrl = "https://www.python.org/ftp/python/3.14.8/", LicenceName = "PSF-2.0", LicenceUrl = null, LicenceFile = null, TermsClass = TermsClass.Open, ApproxSizeMb = 12.6, Needs = [], Provides = [], Files = [],
            Artifacts = [new CatalogueArtifact { Name = "python-3.14.8-embed-amd64.zip", Url = "https://www.python.org/ftp/python/3.14.8/python-3.14.8-embed-amd64.zip", Size = 12601679, Sha256 = null }],
        };
        var backend = new FakeLibraryBackend(LibraryFixtures.Catalogue() with { Items = [python] }, new ScriptedInstall());
        backend.Unfetchable.Add("T1");
        var tools = new ToolsLibraryBackend(backend);
        Assert.Equal(ItemKind.Skillset, tools.Catalogue.Items.Single().Kind); // the page draws tools as sets
        using var controller = new LibraryController(LibrarySection.Skills, tools);
        var fallback = controller.ManualFallbackOf("T1")!;
        Assert.Equal(("python-3.14.8-embed-amd64.zip", "python.org/ftp/python/3.14.8/", true), (fallback.File, fallback.Source, fallback.ForTool));
        Assert.DoesNotContain("commit", fallback.File, StringComparison.Ordinal);
        Assert.False(controller.CanFetchItem("T1"));
        Assert.True(controller.CanFetch);
    });

    private static SkillsCatalogue Titled(SkillsCatalogue catalogue, params (string Id, string Title)[] titles) =>
        catalogue with { Items = catalogue.Items.Select(i => titles.FirstOrDefault(t => t.Id == i.Id) is { Title: { } title } ? i with { Title = title } : i).ToList() };
}
