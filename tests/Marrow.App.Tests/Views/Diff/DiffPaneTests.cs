using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;

namespace Marrow.App.Tests.Views.Diff;

// nth web surfaces, unit W4 (work/design/web-surfaces/DESIGN.md 3.1 "Changes | narrow | grid | the diff, now a pane", 3.7 "Changes |
// Ctrl+D (existing ToggleChanges, now the pane)", 3.12 "pane cards ... tinted layers ... not a second blur", the canvas's Changes
// cell "Changes · 3 files"; 14 W4 "DiffPane.cs (from DiffInspector)"). The pane is the thread's diff inspector in pane mode: the same
// load and file cards, in the size its cell gives it, with plain controls under the pane's header and no popover-only parts.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w4-1")]
public sealed class DiffPaneTests(HeadlessSession headless)
{
    private static Grid Root(DiffInspector inspector) => Assert.IsType<Grid>(inspector.Child);

    private static Control ChromeRow(DiffInspector inspector) => Root(inspector).Children[0];

    private static void PressKey(Control target, Key key) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

    [Fact]
    public void The_pane_content_is_titled_Changes_and_counts_its_files_once_they_have_loaded() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "b.txt"));
        var content = rig.Store.Main.OnMain(() => DiffPane.Create(rig.Runtime));
        Assert.Equal("Changes", content.Title);
        Assert.Null(content.Subtitle);
        var inspector = Assert.IsType<DiffInspector>(content.Body);
        var announced = new List<string?>();
        content.Changed += () => announced.Add(content.Subtitle);
        rig.Present(inspector, 360, 420);
        rig.FinishLoad(inspector);
        Assert.Equal("2 files", content.Subtitle);
        Assert.Equal("2 files", announced.Last());
        Assert.Equal("Changes", content.Title);
    });

    [Fact]
    public void One_file_reads_1_file_and_no_files_read_nothing() => headless.OnUiThread(() =>
    {
        using var one = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var content = one.Store.Main.OnMain(() => DiffPane.Create(one.Runtime));
        var inspector = Assert.IsType<DiffInspector>(content.Body);
        one.Present(inspector, 360, 420);
        one.FinishLoad(inspector);
        Assert.Equal("1 file", content.Subtitle);

        using var none = DiffInspectorRig.Open();
        var empty = none.Store.Main.OnMain(() => DiffPane.Create(none.Runtime));
        var emptyInspector = Assert.IsType<DiffInspector>(empty.Body);
        none.Present(emptyInspector, 360, 420);
        none.FinishLoad(emptyInspector);
        Assert.Null(empty.Subtitle);
        Assert.NotNull(Text(emptyInspector, "No changes yet"));
    });

    [Fact]
    public void The_pane_takes_the_size_of_its_cell_and_its_chrome_row_is_plain_with_no_Hide_button() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "b.txt"));
        var inspector = rig.NewPaneInspector();
        rig.Present(inspector, 360, 420);
        rig.FinishLoad(inspector);

        Assert.True(double.IsNaN(inspector.Width) && double.IsNaN(inspector.Height), "no fixed popover size");
        Assert.Equal(new Size(360, 420), inspector.Bounds.Size);
        var row = ChromeRow(inspector);
        Assert.Empty(Descendants<ChromeCapsule>(row)); // glass on glass inside a pane card
        Assert.Empty(Descendants<ChromeTextMenu>(row));
        var tips = Descendants<Button>(row).Select(button => ToolTip.GetTip(button) as string).ToArray();
        Assert.Contains("Choose which changes to show", tips);
        Assert.Contains("More", tips);
        Assert.DoesNotContain(Descendants<Button>(row), button => AutomationProperties.GetName(button)?.StartsWith("Hide changes", StringComparison.Ordinal) == true);
        Assert.Single(Descendants<DiffStatLabel>(row));
        Assert.Null(Text(row, "2 files")); // the count is the pane header's
        Assert.Contains(Descendants<TextBlock>(row), block => block.Text == "All changes");
        Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
    });

    [Fact]
    public void The_pane_chrome_fits_the_narrowest_cell_280_wide() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a/very/long/path/to/some/deeply/nested/file-name.txt"));
        var inspector = rig.NewPaneInspector();
        rig.Present(inspector, 280, 300);
        rig.FinishLoad(inspector);
        var row = ChromeRow(inspector);
        Assert.True(row.Bounds.Right <= 280 + 0.5);
        foreach (var button in Descendants<Button>(row)) Assert.True(button.TranslatePoint(default, inspector)!.Value.X + button.Bounds.Width <= 280 + 0.5, "every control stays inside the cell");
    });

    [Fact]
    public void Esc_in_a_pane_is_left_to_the_workspace_and_never_closes_the_diff() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.NewPaneInspector();
        rig.Present(inspector, 360, 420);
        rig.FinishLoad(inspector);
        rig.Store.Main.OnMain(() => rig.Runtime.IsDiffVisible = true);
        var args = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape };
        inspector.RaiseEvent(args);
        Assert.False(args.Handled);
        Assert.True(rig.Runtime.IsDiffVisible);
    });

    [Fact]
    public void A_pane_rebuilds_only_for_what_the_diff_reads_while_the_popover_rebuilds_for_any_change() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var pane = rig.NewPaneInspector();
        var popover = rig.NewInspector();
        var paneBuilds = 0;
        var popoverBuilds = 0;
        pane.Changed += () => paneBuilds++;
        popover.Changed += () => popoverBuilds++;
        rig.Present(pane, 360, 420);
        rig.Present(popover);
        rig.FinishLoad(pane);
        rig.FinishLoad(popover);
        paneBuilds = popoverBuilds = 0;

        rig.Store.Main.OnMain(() => rig.Runtime.Draft = new ComposerDraft { Text = "typing" }); // streams of unrelated changes
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, paneBuilds);
        Assert.True(popoverBuilds > 0, "the popover keeps rebuilding on any change, as it always has");

        rig.Reload(pane); // a new diff revision
        Assert.True(paneBuilds > 0);
    });

    [Fact]
    public void A_pane_listens_to_the_runtime_only_while_it_is_on_screen() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var pane = rig.NewPaneInspector();
        var builds = 0;
        pane.Changed += () => builds++;
        var window = rig.Present(pane, 360, 420);
        rig.FinishLoad(pane);
        builds = 0;
        rig.Store.Main.OnMain(() =>
        {
            window.Content = null; // off screen: the pane card was removed from the workspace
            Dispatcher.UIThread.RunJobs();
            rig.Runtime.NoteDiffChanged();
        });
        rig.Store.Main.Loop.RunUntilIdle();
        Assert.Equal(0, builds);
        Assert.False(pane.IsLoading);

        rig.Store.Main.OnMain(() =>
        {
            window.Content = pane; // and back: it loads the current diff afresh
            window.UpdateLayout();
        });
        rig.FinishLoad(pane);
        Assert.True(builds > 0);
        Assert.Equal(1, pane.FileCount);
    });

    [Fact]
    public void The_selection_chip_names_the_turn_chosen_in_its_menu_and_the_pane_loads_that_turns_diff() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patch("first.txt"), DiffInspectorRig.Patches("second.txt", "third.txt"));
        var pane = rig.NewPaneInspector();
        rig.Present(pane, 400, 420);
        rig.FinishLoad(pane);
        Assert.Equal(3, pane.FileCount);
        Assert.Contains(Descendants<TextBlock>(ChromeRow(pane)), block => block.Text == "All changes");

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[1].Id);
        rig.FinishLoad(pane);
        Assert.Equal(2, pane.FileCount);
        Assert.Contains(Descendants<TextBlock>(ChromeRow(pane)), block => block.Text == "Turn 2");
        Assert.DoesNotContain(Descendants<TextBlock>(ChromeRow(pane)), block => block.Text == "All changes");
    });
}
