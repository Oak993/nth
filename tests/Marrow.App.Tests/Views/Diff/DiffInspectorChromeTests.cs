using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :22-121 (the panel's body) and :27-64 (its chrome row).
[Collection("Headless")]
public sealed class DiffInspectorChromeTests(HeadlessSession headless)
{
    private static Grid Root(DiffInspector inspector) => Assert.IsType<Grid>(inspector.Child);

    private static Control ChromeRow(DiffInspector inspector) => Root(inspector).Children[0];

    private static Rect Box(Control control, Control within) =>
        new(control.TranslatePoint(new Point(0, 0), within)!.Value, control.Bounds.Size);

    private static Control PrivateMenu(DiffInspector inspector, string method) => Assert.IsAssignableFrom<Control>(typeof(DiffInspector)
        .GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inspector, null));

    private static Button ButtonOf(Control control) => Assert.Single(Descendants<Button>(control));

    private static string[] ItemTitles(Control menu) =>
        Descendants<PopoverItem>(menu).Select(item => AutomationProperties.GetName(ButtonOf(item)) ?? "").ToArray();

    private static PopoverItem Item(Control menu, string title) =>
        Assert.Single(Descendants<PopoverItem>(menu), item => AutomationProperties.GetName(ButtonOf(item)) == title);

    private static void Press(DiffInspectorRig rig, Control menu, string title)
    {
        rig.Store.Main.OnMain(() => ButtonOf(Item(menu, title)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Dispatcher.UIThread.RunJobs();
        rig.Store.Main.Loop.RunUntilIdle();
    }

    // The check column of a row built with isChecked: a 14 wide checkmark, shown at full opacity or hidden (Popovers.swift:276-281).
    private static double[] CheckOpacities(Control menu) => Descendants<PopoverItem>(menu)
        .Select(item => Assert.Single(Descendants<SwiftImage>(item), image => image.SymbolName == "checkmark"))
        .Select(image => { Assert.Equal(14, image.Width); return image.Opacity; })
        .ToArray();

    private static Button ButtonTipped(Control scope, string tip) =>
        Assert.Single(Descendants<Button>(scope), button => ToolTip.GetTip(button) as string == tip);

    [Fact]
    [Trait("Req", "UI-GIT-001")] // App/Views/Diff/DiffInspector.swift:22-26, :71-109 (640 by 540; the chrome row, then the empty state or the scroll view; spacing 0)
    public void The_panel_is_640_by_540_with_the_chrome_row_above_the_empty_state_or_the_list() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.NewInspector();

        Assert.Equal(640, inspector.Width);
        Assert.Equal(540, inspector.Height);
        Assert.Equal(2, Root(inspector).Children.Count);
        Assert.NotEmpty(Descendants<ChromeTextMenu>(ChromeRow(inspector)));
        Assert.NotEmpty(Descendants<ChromeCapsule>(ChromeRow(inspector)));
        Assert.NotNull(Text(Root(inspector).Children[1], "No changes yet"));

        rig.Reload(inspector);

        Assert.Equal(2, Root(inspector).Children.Count);
        Assert.NotEmpty(Descendants<ChromeTextMenu>(ChromeRow(inspector)));
        Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
        Assert.Null(Text(inspector, "No changes yet"));
    });

    [Fact]
    [Trait("Req", "UI-GIT-002")] // App/Views/Diff/DiffInspector.swift:25-69 (padding 14, 12 and 10; spacing 10; the spacer pushes the counts and capsule to the trailing edge)
    public void The_chrome_row_is_padded_and_spaced_and_holds_the_counts_and_capsule_at_the_trailing_edge() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "b.txt"));
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);

        var row = ChromeRow(inspector);
        Assert.Equal(2, SwiftGlass.GetContainerSpacing(row)); // GlassEffectContainer(spacing: 2)
        var menu = Box(Assert.Single(Descendants<ChromeTextMenu>(row)), inspector);
        var count = Box(Assert.Single(Descendants<TextBlock>(row), block => block.Text == "2 files"), inspector);
        var stat = Box(Assert.Single(Descendants<DiffStatLabel>(row)), inspector);
        var capsule = Box(Assert.Single(Descendants<ChromeCapsule>(row)), inspector);

        Assert.Equal(12, Box(row, inspector).Y, 0.5); // .padding(.top, 12)
        Assert.Equal(14, menu.X, 0.5); // .padding(.horizontal, 14)
        Assert.Equal(640 - 14, capsule.Right, 0.5);
        // HStack(alignment: .center): every item sits on the row's centre line, whatever its own height.
        Assert.True(count.Height < capsule.Height && stat.Height < capsule.Height);
        foreach (var item in new[] { menu, count, stat }) Assert.Equal(capsule.Y + capsule.Height / 2, item.Y + item.Height / 2, 0.5);
        Assert.Equal(10, stat.X - count.Right, 0.5); // HStack(spacing: 10)
        Assert.Equal(10, capsule.X - stat.Right, 0.5);
        Assert.True(count.X - menu.Right >= 8 + 2 * 10 - 0.5, "the spacer keeps at least 8 points between its neighbours' spacing");
        Assert.True(count.X - menu.Right > 100, "the spacer takes the free width, so the counts do not sit against the menu");
    });

    [Fact]
    [Trait("Req", "UI-GIT-002")] // App/Views/Diff/DiffInspector.swift:46-54 (no files: the count label and the statistics are absent, the menu and the capsule remain)
    public void Without_files_the_chrome_row_has_no_count_label_or_statistics() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open();
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);

        var row = ChromeRow(inspector);
        Assert.NotEmpty(Descendants<ChromeTextMenu>(row));
        Assert.NotEmpty(Descendants<ChromeCapsule>(row));
        Assert.Empty(Descendants<DiffStatLabel>(row));
        Assert.Equal(Box(row, inspector).Bottom + 10, Box(Root(inspector).Children[1], inspector).Y, 0.5); // .padding(.bottom, 10)
        Assert.DoesNotContain(Texts(row), text => text.EndsWith("file", StringComparison.Ordinal) || text.EndsWith("files", StringComparison.Ordinal));
    });

    [Fact]
    [Trait("Req", "UI-GIT-003")] // App/Views/Diff/DiffInspector.swift:27-33 (the plusminus symbol, the tooltip, and the title All changes with nothing selected)
    public void The_selection_menu_button_reads_All_changes_with_its_symbol_and_tooltip() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.NewInspector();
        var menu = Assert.Single(Descendants<ChromeTextMenu>(ChromeRow(inspector)));

        Assert.NotNull(Text(menu, "All changes"));
        Assert.Contains(Descendants<SwiftImage>(menu), image => image.SymbolName == "plusminus");
        Assert.Equal("Choose which changes to show", ToolTip.GetTip(ButtonOf(menu)));
    });

    [Fact]
    [Trait("Req", "UI-GIT-003")] // App/Views/Diff/DiffInspector.swift:29-32 (a selected turn reads Turn n, the stored index plus one; an unknown selection falls back to All changes)
    public void The_selection_menu_button_names_the_selected_turn_at_once() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"), null);
        var inspector = rig.NewInspector();

        // The title follows the runtime's selection as it changes, not once the diff has loaded.
        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[1].Id);
        Assert.NotNull(Text(ChromeRow(inspector), "Turn 2"));
        Assert.Null(Text(ChromeRow(inspector), "All changes"));

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[2].Id); // a turn with no changes still has its index
        Assert.NotNull(Text(ChromeRow(inspector), "Turn 3"));

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = Guid.NewGuid());
        Assert.NotNull(Text(ChromeRow(inspector), "All changes"));
    });

    [Fact]
    [Trait("Req", "UI-GIT-004")] // App/Views/Diff/DiffInspector.swift:34-37 (All changes is checked when nothing is selected; a click clears the focus and the selection)
    public void The_All_changes_row_is_checked_without_a_selection_and_clears_the_focus_and_selection() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"));
        var inspector = rig.NewInspector();
        rig.Reload(inspector);

        var menu = PrivateMenu(inspector, "BuildSelectionMenu");
        Assert.Equal(1, CheckOpacities(menu)[0]);

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), rig.Turns[1].Id, focusEdits: [new FileEdit("b.txt", "old\n", "new\n")]));
        rig.FinishLoad(inspector);
        menu = PrivateMenu(inspector, "BuildSelectionMenu");
        Assert.Equal(0, CheckOpacities(menu)[0]);
        Assert.NotEmpty(rig.Runtime.DiffFocusEdits);

        Press(rig, menu, "All changes");

        Assert.Empty(rig.Runtime.DiffFocusEdits);
        Assert.Null(rig.Runtime.DiffSelection);
    });

    [Fact]
    [Trait("Req", "UI-GIT-005")] // App/Views/Diff/DiffInspector.swift:38-43 (one row per turn with changes, Turn n, checked when selected; a click clears the focus and selects it)
    public void Each_turn_with_changes_has_a_row_that_is_checked_when_selected_and_selects_it() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"), null);
        var inspector = rig.NewInspector();
        rig.Reload(inspector);

        var menu = PrivateMenu(inspector, "BuildSelectionMenu");
        Assert.Equal(new[] { "All changes", "Turn 1", "Turn 2" }, ItemTitles(menu)); // the third turn changed nothing
        Assert.Equal(new double[] { 1, 0, 0 }, CheckOpacities(menu));

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits: [new FileEdit("a.txt", "old\n", "new\n")]));
        Assert.NotEmpty(rig.Runtime.DiffFocusEdits);
        Press(rig, menu, "Turn 2");

        Assert.Empty(rig.Runtime.DiffFocusEdits);
        Assert.Equal(rig.Turns[1].Id, rig.Runtime.DiffSelection);
        menu = PrivateMenu(inspector, "BuildSelectionMenu");
        Assert.Equal(new double[] { 0, 0, 1 }, CheckOpacities(menu));
    });

    [Theory]
    [InlineData(1, "1 file")]
    [InlineData(2, "2 files")]
    [Trait("Req", "UI-GIT-006")] // App/Views/Diff/DiffInspector.swift:46-49 (exactly "1 file" at one file, otherwise the count and " files"; 11 point, secondary text)
    public void The_file_count_label_reads_1_file_or_the_count_and_files(int files, string expected) => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Enumerable.Range(1, files).Select(index => "f" + index + ".txt").ToArray()));
        var inspector = rig.NewInspector();
        rig.Reload(inspector);

        var label = Assert.Single(Descendants<TextBlock>(ChromeRow(inspector)), block => block.Text is "1 file" || block.Text is { } text && text.EndsWith(" files", StringComparison.Ordinal));
        Assert.Equal(expected, label.Text);
        Assert.Equal(11, label.FontSize);
        Assert.Equal(((ISolidColorBrush)Chrome.SecondaryText.Brush(label)).Color, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
    });

    [Fact]
    [Trait("Req", "UI-GIT-006")] // App/Views/Diff/DiffInspector.swift:46 (the label is hidden when the file list is empty)
    public void The_file_count_label_is_hidden_without_files() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var inspector = rig.NewInspector();
        Assert.DoesNotContain(Texts(ChromeRow(inspector)), text => text.EndsWith("file", StringComparison.Ordinal));

        rig.Reload(inspector);
        Assert.Contains(Texts(ChromeRow(inspector)), text => text == "1 file");

        // The selected turn has no changes: the file list is empty again.
        using var empty = DiffInspectorRig.Open((string?)null);
        var emptyInspector = empty.NewInspector();
        empty.Store.Main.OnMain(() => empty.Runtime.DiffSelection = empty.Turns[0].Id);
        empty.FinishLoad(emptyInspector);
        Assert.DoesNotContain(Texts(ChromeRow(emptyInspector)), text => text.EndsWith("file", StringComparison.Ordinal) || text.EndsWith("files", StringComparison.Ordinal));
    });

    [Fact]
    [Trait("Req", "UI-GIT-008")] // App/Views/Diff/DiffInspector.swift:55-64 (an ellipsis menu button, tooltip More, then a divider and the close button, in one capsule)
    public void The_more_button_sits_first_in_the_trailing_capsule_before_a_divider_and_the_close_button() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open();
        var inspector = rig.NewInspector();
        var capsule = Assert.Single(Descendants<ChromeCapsule>(ChromeRow(inspector)));

        var parts = Descendants<Control>(capsule)
            .Where(control => control is ChromeMenuButton or ChromeDivider or ChromeIconButton)
            .Select(control => control.GetType())
            .ToArray();
        Assert.Equal(new[] { typeof(ChromeMenuButton), typeof(ChromeDivider), typeof(ChromeIconButton) }, parts);

        var more = Assert.Single(Descendants<ChromeMenuButton>(capsule));
        Assert.Contains(Descendants<SwiftImage>(more), image => image.SymbolName == "ellipsis");
        Assert.Equal("More", ToolTip.GetTip(ButtonOf(more)));
    });

    [Fact]
    [Trait("Req", "UI-GIT-008")] // App/Views/Diff/DiffInspector.swift:56-59 (the menu lists Expand all, then Collapse all, each with its symbol)
    public void The_more_menu_lists_Expand_all_then_Collapse_all_with_their_symbols() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open();
        var inspector = rig.NewInspector();

        var menu = PrivateMenu(inspector, "BuildMoreMenu");

        Assert.Equal(new[] { "Expand all", "Collapse all" }, ItemTitles(menu));
        Assert.Contains(Descendants<SwiftImage>(Item(menu, "Expand all")), image => image.SymbolName == "arrow.down.right.and.arrow.up.left");
        Assert.Contains(Descendants<SwiftImage>(Item(menu, "Collapse all")), image => image.SymbolName == "arrow.up.left.and.arrow.down.right");
    });

    [Fact]
    [Trait("Req", "UI-GIT-011")] // App/Views/Diff/DiffInspector.swift:60-63 (an xmark icon button, tooltip Hide changes plus the shortcut's hint; a click hides the changes)
    public void The_close_button_hides_the_changes_and_its_tooltip_carries_the_shortcut_hint() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open();
        var inspector = rig.NewInspector();
        var close = Assert.Single(Descendants<ChromeIconButton>(ChromeRow(inspector)));
        Assert.Contains(Descendants<SwiftImage>(close), image => image.SymbolName == "xmark");
        var hint = ShortcutStore.Hint(AppShortcut.ToggleChanges);
        Assert.NotEmpty(hint); // the default chord is set
        Assert.Equal("Hide changes" + hint, ToolTip.GetTip(ButtonOf(close)));

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null));
        Assert.True(rig.Runtime.IsDiffVisible);
        rig.Store.Main.OnMain(() => ButtonOf(close).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

        Assert.False(rig.Runtime.IsDiffVisible);
    });

    [Fact]
    [Trait("Req", "UI-GIT-012")] // App/Views/Diff/DiffInspector.swift:71-77 (while loading with no files: the named progress label, centred; OD-0016 puts the 64 point mark above it)
    public void While_the_diff_is_read_the_named_loading_mark_fills_the_space_below_the_chrome_row() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"));
        var pending = new TaskCompletionSource<ValueList<DiffFile>>();
        rig.SeedDiff(pending.Task, rig.Turns[0].Id);
        var inspector = rig.NewInspector();

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[0].Id); // cached: no wait
        rig.Store.Main.Loop.RunUntilIdle();

        var state = Assert.IsType<Border>(Root(inspector).Children[1]);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, state.HorizontalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, state.VerticalAlignment);
        var label = Assert.IsAssignableFrom<NthShineText>(Text(state, "Reading the diff…")); // the light passes through it (step 5)
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, label.HorizontalAlignment);
        // OD-0113 supersedes OD-0016: loader 5 is the stretchable 88 DIP placeholder; OD-0136 amendment 2 (step 5) makes it the diff skeleton, which comes in after the 400 ms wait.
        var loading = Assert.Single(Descendants<AuroraDiffLoadingState>(state));
        Assert.True(loading.Label.IsShining);
        var place = Assert.IsType<Panel>(loading.Children[0]);
        Assert.Equal(88, place.Height);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, place.HorizontalAlignment);
        Assert.Equal(NthSkeletonKind.Diff, loading.Skeleton.Kind);
        Assert.Null(Text(state, "No changes yet"));

        pending.SetResult(ValueList<DiffFile>.Empty);
        Assert.True(rig.PumpUntil(() => Text(Root(inspector), "Reading the diff…") is null));
        Assert.NotNull(Text(Root(inspector), "No changes yet"));
    });

    [Fact]
    [Trait("Req", "UI-GIT-012")] // App/Views/Diff/DiffInspector.swift:71-73 (shown only while loading and the file list is still empty)
    public void The_loading_mark_is_not_shown_while_files_are_already_listed() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt"), DiffInspectorRig.Patches("b.txt"));
        var inspector = rig.NewInspector();
        rig.Reload(inspector);
        Assert.Single(Descendants<DiffFileCard>(inspector), card => (string)card.Tag! == "a.txt");
        var pending = new TaskCompletionSource<ValueList<DiffFile>>();
        rig.SeedDiff(pending.Task, rig.Turns[1].Id);

        rig.Store.Main.OnMain(() => rig.Runtime.DiffSelection = rig.Turns[1].Id);
        rig.Store.Main.Loop.RunUntilIdle();

        Assert.Null(Text(inspector, "Reading the diff…"));
        Assert.Empty(Descendants<AuroraDiffLoadingState>(inspector));
        Assert.NotEmpty(Descendants<DiffFileCard>(inspector)); // the previous list stays until the new one arrives
        pending.SetResult(ValueList<DiffFile>.Empty);
        rig.PumpUntil(() => Text(Root(inspector), "No changes yet") is not null);
    });

    [Fact]
    [Trait("Req", "UI-GIT-013")] // App/Views/Diff/DiffInspector.swift:77-83 (the unavailable view: the title, the doc.text.magnifyingglass image and the description, centred and filling both axes)
    public void With_no_files_and_no_load_the_panel_says_there_are_no_changes_yet() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open();
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);

        var state = Assert.IsType<Border>(Root(inspector).Children[1]);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Stretch, state.HorizontalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Stretch, state.VerticalAlignment);
        Assert.Equal(Avalonia.Layout.HorizontalAlignment.Center, Assert.IsType<StackPanel>(state.Child).HorizontalAlignment);
        Assert.Equal(Avalonia.Layout.VerticalAlignment.Center, Assert.IsType<StackPanel>(state.Child).VerticalAlignment);
        Assert.NotNull(Text(state, "No changes yet"));
        Assert.NotNull(Text(state, "Files the agent changes appear here after each turn."));
        Assert.Null(Text(state, "Reading the diff…"));
        var image = Assert.Single(Descendants<Control>(state), control => control.GetType().Name == "SwiftImageContentUnavailable");
        var symbol = Assert.IsType<SwiftImage>(image.GetType().GetField("image", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(image));
        Assert.Equal("doc.text.magnifyingglass", symbol.SymbolName);
    });

    [Fact]
    [Trait("Req", "UI-GIT-014")] // App/Views/Diff/DiffInspector.swift:87-99 (a leading-aligned list with spacing 10 and 12 points of padding on every side, inside the scroll view)
    public void The_file_list_scrolls_with_cards_spaced_10_apart_and_12_points_inside_the_scroll_view() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches("a.txt", "b.txt", "c.txt"));
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);

        var scroll = Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
        var cards = Descendants<DiffFileCard>(scroll).Select(card => Box(card, scroll)).ToArray();
        Assert.Equal(3, cards.Length);

        // The scroll view is the whole pane under the chrome row; the padding belongs to what scrolls inside it.
        Assert.Equal(640, scroll.Bounds.Width, 0.5);
        Assert.Equal(Box(ChromeRow(inspector), inspector).Bottom + 10, Box(scroll, inspector).Y, 0.5);
        Assert.Equal(12, cards[0].X, 0.5);
        Assert.Equal(12, cards[0].Y, 0.5);
        Assert.Equal(640 - 24, cards[0].Width, 0.5);
        Assert.Equal(10, cards[1].Y - cards[0].Bottom, 0.5);
        Assert.Equal(10, cards[2].Y - cards[1].Bottom, 0.5);
    });

    [Fact]
    [Trait("Req", "UI-GIT-014")] // App/Views/Diff/DiffInspector.swift:87-98 (a list longer than the pane scrolls, and ends 12 points below its last card)
    public void A_long_file_list_scrolls_and_ends_12_points_below_its_last_card() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Enumerable.Range(10, 12).Select(index => "f" + index + ".txt").ToArray()));
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);
        var scroll = Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height, "twelve cards are taller than the pane");
        Assert.Equal(540 - Box(scroll, inspector).Y, scroll.Viewport.Height, 0.5); // the scroll view fills what the chrome row leaves

        scroll.ScrollToEnd();
        scroll.UpdateLayout();

        var last = Descendants<DiffFileCard>(scroll).Last();
        Assert.Equal(scroll.Viewport.Height - 12, Box(last, scroll).Bottom, 0.5);
    });

    [Fact]
    [Trait("Req", "UI-GIT-014")] // App/Views/Diff/DiffInspector.swift:100-106 (a tapped tool row's file is scrolled to, its card's top at the top of the view)
    public void A_focused_file_is_scrolled_to_the_top_of_the_list() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Enumerable.Range(10, 20).Select(index => "f" + index + ".txt").ToArray()));
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);

        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits: [new FileEdit("f20.txt", "old\n", "new\n")]));
        rig.FinishLoad(inspector);
        rig.Store.Main.Loop.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        var scroll = Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
        scroll.UpdateLayout();

        var card = Assert.Single(Descendants<DiffFileCard>(scroll), candidate => (string)candidate.Tag! == "f20.txt");
        Assert.True(scroll.Offset.Y > 100, "the list scrolled");
        Assert.Equal(0, Box(card, scroll).Y, 1.0);
    });

    [Fact]
    [Trait("Req", "PR-GIT-134")] // App/Views/Diff/DiffInspector.swift:100-106 (the scroll runs when the request counter changes, not on every later redraw)
    public void The_scroll_to_a_focused_file_happens_once_per_request_and_not_on_every_redraw() => headless.OnUiThread(() =>
    {
        using var rig = DiffInspectorRig.Open(DiffInspectorRig.Patches(Enumerable.Range(10, 20).Select(index => "f" + index + ".txt").ToArray()));
        var inspector = rig.NewInspector();
        rig.Present(inspector);
        rig.FinishLoad(inspector);
        rig.Store.Main.OnMain(() => rig.Runtime.ShowDiff(new Border(), turn: null, focusEdits: [new FileEdit("f20.txt", "old\n", "new\n")]));
        rig.FinishLoad(inspector);
        var scroll = Assert.IsType<ScrollViewer>(Root(inspector).Children[1]);
        Assert.True(scroll.Offset.Y > 100, "the focused file was scrolled to");

        // The reader scrolls back to the top and opens a card there: that is a redraw, not a new request.
        scroll.Offset = new Vector(0, 0);
        scroll.UpdateLayout();
        var first = Assert.Single(Descendants<DiffFileCard>(scroll), card => (string)card.Tag! == "f10.txt");
        rig.Store.Main.OnMain(() => ButtonOf(first).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        rig.Store.Main.Loop.RunUntilIdle();
        Dispatcher.UIThread.RunJobs();
        scroll.UpdateLayout();

        Assert.Equal(0, scroll.Offset.Y, 0.5);
    });
}
