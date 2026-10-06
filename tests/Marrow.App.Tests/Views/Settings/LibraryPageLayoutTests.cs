using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;
using Marrow.App.Port.Skills.Pages;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port.Skills.Pages;
using Marrow.Core.Port.Skills.State;

namespace Marrow.App.Tests.Views.Settings;

// SK-15 (DL-17, OD-0069, OD-0071): no overlap, no clipping of a part by its neighbour, a steady page while a state changes, and scrolling areas that clip what they scroll.
[Collection("Headless")]
public sealed class LibraryPageLayoutTests(HeadlessSession headless)
{
    private static Rect In(Visual of, Visual space) => new(of.TranslatePoint(default, space) ?? default, of.Bounds.Size);

    private static void NoOverlap(IReadOnlyList<Rect> rects, string what)
    {
        for (var i = 0; i < rects.Count; i++)
            for (var j = i + 1; j < rects.Count; j++)
            {
                var overlap = rects[i].Intersect(rects[j]);
                Assert.True(overlap.Width < 0.5 || overlap.Height < 0.5, what + ": " + rects[i] + " meets " + rects[j]);
            }
    }

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0069: the rows of the list never overlap one another and the list never reaches the detail
    public void The_rows_the_list_and_the_detail_do_not_overlap() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var rows = harness.Page.RowStack.Children.OfType<LibraryRowView>().Select(r => In(r, harness.Window)).ToList();
        NoOverlap(rows, "rows");
        Assert.All(rows, r => Assert.Equal(LibraryRowView.RowHeight, r.Height, 3));
        var list = In(harness.Page.RowStack.GetVisualAncestors().OfType<Border>().First(b => b.Width == LibraryPage.ListWidth), harness.Window);
        var detail = In(harness.Page.DetailSlot, harness.Window);
        Assert.True(list.Right <= detail.Left + 0.5, "the list " + list + " reaches the detail " + detail);
        Assert.All(rows, r => Assert.True(list.Contains(r.TopLeft) && list.Contains(r.BottomRight - new Vector(0.5, 0.5)), "a row leaves the list: " + r + " in " + list));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0069: the parts of a detail follow one another and never meet, in every state of the item
    public void The_parts_of_a_detail_never_overlap_in_any_state() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        void Check(string id, string state)
        {
            harness.Controller.Select(id);
            harness.Settle();
            var detail = harness.Page.Details[id];
            var parts = ((StackPanel)detail.Child!).Children.Where(c => c.IsVisible && c.Bounds.Height > 0.5).Select(c => In(c, harness.Window)).ToList();
            NoOverlap(parts, id + " " + state);
            for (var i = 1; i < parts.Count; i++) Assert.True(parts[i].Top >= parts[i - 1].Bottom - 0.5, id + " " + state + ": a part rises into the one above it");
        }
        Check("S1", "available");
        Check("S4", "terms");
        harness.Backend.Install("S2", LibraryFixtures.Pin);
        Check("S2", "installed");
        harness.Press("action-trash");
        Check("S2", "remove asked");
        harness.Press("action-remove-cancel");
        harness.Install.FailOn("S3", "Couldn’t reach github.com.");
        harness.Controller.Select("S3");
        harness.Settle();
        harness.Wait(harness.Controller.Download("S3"));
        Check("S3", "failed");
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0069: the page keeps its height whatever the state, so the footer never jumps into the panes or away from them
    public void The_page_keeps_its_height_whatever_the_state_of_the_selected_item() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var height = harness.Page.Bounds.Height;
        var footer = harness.Page.Reset!;
        var top = In(footer, harness.Window).Top;
        foreach (var id in new[] { "S4", "S2", "S5" })
        {
            harness.Controller.Select(id);
            harness.Settle();
            Assert.Equal(height, harness.Page.Bounds.Height, 1);
            Assert.Equal(top, In(footer, harness.Window).Top, 1);
        }
        harness.Press("reset");
        Assert.True(harness.Page.Bounds.Height >= height);
        var panes = In(harness.Page.DetailSlot, harness.Window);
        Assert.True(In(footer, harness.Window).Top >= panes.Bottom - 0.5 || In(footer, harness.Window).Top >= panes.Top + LibraryPage.PaneHeight - 0.5);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0069: the confirmation fits in the window at its smallest and never covers the caption row
    public void The_dialog_fits_in_the_window_and_its_list_scrolls_inside_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Press("download-all");
        var dialog = harness.Page.Sheet!.Dialog;
        harness.Settle();
        var rect = In(dialog, harness.Window);
        var window = new Rect(harness.Window.Bounds.Size);
        Assert.True(window.Contains(rect.TopLeft) && window.Contains(rect.BottomRight), "the dialog " + rect + " leaves the window " + window);
        Assert.Equal(DownloadAllDialog.Width440, dialog.Bounds.Width, 1);
        var scrollers = dialog.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        Assert.NotEmpty(scrollers);
        Assert.All(scrollers, s => Assert.True(s.MaxHeight > 0 && s.MaxHeight < 400));
        var lines = dialog.Lines.Values.Select(l => In(l, harness.Window)).ToList();
        NoOverlap(lines, "dialog lines");
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0071: the scrolling areas of the page are the app's soft scrollers and they clip their content
    public void The_scrolling_areas_use_the_apps_soft_scrolling_and_clip_what_they_scroll() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        var scrollers = harness.Page.GetVisualDescendants().OfType<ScrollViewer>().ToList();
        Assert.True(scrollers.Count >= 3, "the list, the detail and the switches");
        Assert.All(scrollers, s => Assert.True(Marrow.UI.Port.SoftScroll.GetIsEnabled(s)));
        Assert.All(scrollers, s => Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, s.VerticalScrollBarVisibility));
        Assert.All(scrollers.SelectMany(s => s.GetVisualDescendants().OfType<ScrollContentPresenter>()), p => Assert.True(p.ClipToBounds || p.Parent is ScrollViewer));
    });

    [Fact]
    [Trait("Decision", "ED-326")] // OD-0069: nothing of the detail is drawn outside its pane while it scrolls
    public void The_detail_scrolls_inside_its_pane_and_its_content_is_clipped_to_it() => LibraryPageHarness.Run(headless, LibrarySection.Skills, harness =>
    {
        harness.Backend.Install("S1", LibraryFixtures.Pin);
        harness.Controller.Select("S4");
        harness.Controller.Select("S1");
        harness.Settle();
        var pane = harness.Page.DetailSlot.GetVisualAncestors().OfType<Border>().First(b => b.ClipToBounds);
        var scroller = pane.GetVisualDescendants().OfType<ScrollViewer>().First();
        Assert.True(pane.ClipToBounds);
        Assert.Equal(LibraryPage.PaneHeight, pane.Bounds.Height, 1);
        scroller.Offset = new Vector(0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        harness.Settle();
        Assert.True(scroller.Offset.Y >= 0);
        var paneRect = In(pane, harness.Window);
        var footer = In(harness.Page.Reset!, harness.Window);
        Assert.True(footer.Top >= paneRect.Bottom - 0.5, "the footer " + footer + " meets the pane " + paneRect);
    });

    [Fact]
    [Trait("Decision", "ED-326")] // the plugin folders of a plugin item stay in their box
    public void The_Plugins_page_lays_out_without_overlap() => LibraryPageHarness.Run(headless, LibrarySection.Plugins, harness =>
    {
        harness.Backend.Install("P1", LibraryFixtures.Pin);
        harness.Controller.Select("P2");
        harness.Controller.Select("P1");
        harness.Settle();
        var rows = harness.Page.RowStack.Children.OfType<LibraryRowView>().Select(r => In(r, harness.Window)).ToList();
        NoOverlap(rows, "plugin rows");
        var switches = harness.Page.Details["P1"].Switches.Rows.Select(r => In(r.Row, harness.Window)).ToList();
        NoOverlap(switches, "plugin switches");
    });
}
