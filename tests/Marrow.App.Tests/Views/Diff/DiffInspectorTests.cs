using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Marrow.App.Runtime;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Views.Diff;

[Collection("Headless")]
public sealed class DiffInspectorTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "UI-GIT-009")]
    [Trait("Req", "UI-GIT-010")]
    public void Bulk_collapse_action_updates_every_card_without_a_runtime_change(bool expand)
    {
        headless.OnUiThread(() =>
        {
            using var store = new AppModelHarness();
            var project = AppModelSamples.Project("Bulk diff", store.NewFolder("bulk-diff"));
            var thread = AppModelSamples.Thread(project);
            store.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            var app = store.MakeModel();
            var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
            store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            Assert.Empty(runtime.Turns);

            var inspector = store.Main.OnMain(() => new DiffInspector(runtime));
            store.Main.OnMain(() => runtime.ShowDiff(new Border(), turn: null, focusEdits:
            [
                new FileEdit("First.swift", "before\n", "after\n"),
                new FileEdit("Second.swift", "old\n", "new\n"),
            ]));
            var load = Assert.IsAssignableFrom<TaskHandle>(typeof(DiffInspector)
                .GetField("loadTask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inspector));
            store.Main.Advance(System.TimeSpan.FromMilliseconds(180));
            store.Main.Loop.RunUntil(() => load.Completion.IsCompleted, AppModelHarness.Bound);
            var cards = Descendants<DiffFileCard>(inspector).ToArray();
            Assert.Equal(2, cards.Length);
            Assert.All(cards, card => Assert.Single(Descendants<DiffLinesView>(card)));

            if (expand)
            {
                // Each toggle rebuilds all cards: click the current card, not a detached predecessor.
                foreach (var path in new[] { "First.swift", "Second.swift" })
                    store.Main.OnMain(() =>
                    {
                        var current = Assert.Single(Descendants<DiffFileCard>(inspector),
                            candidate => System.Object.Equals(candidate.Tag, path));
                        Assert.Single(Descendants<Button>(current)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                Assert.Empty(Descendants<DiffLinesView>(inspector));
            }

            var runtimeChanges = 0;
            System.ComponentModel.PropertyChangedEventHandler observed = (_, _) => runtimeChanges++;
            runtime.PropertyChanged += observed;
            try
            {
                store.Main.OnMain(() =>
                {
                    var menu = Assert.IsAssignableFrom<Control>(typeof(DiffInspector)
                        .GetMethod("BuildMoreMenu", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inspector, null));
                    var title = expand ? "Expand all" : "Collapse all";
                    var button = Assert.Single(Descendants<Button>(menu), candidate =>
                        Avalonia.Automation.AutomationProperties.GetName(candidate) == title);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                Assert.Equal(0, runtimeChanges);
                cards = Descendants<DiffFileCard>(inspector).ToArray();
                Assert.Equal(2, cards.Length);
                Assert.All(cards, card =>
                {
                    Assert.Single(Descendants<Button>(card));
                    if (expand) Assert.Single(Descendants<DiffLinesView>(card));
                    else Assert.Empty(Descendants<DiffLinesView>(card));
                });
            }
            finally { runtime.PropertyChanged -= observed; }
            System.GC.KeepAlive(app);
        });
    }

    private static ValueList<T> List<T>(params T[] items) => new(items);

    private static DiffLine Line(
        DiffLine.Kind kind,
        string text,
        long? oldNumber = null,
        long? newNumber = null,
        long id = 0) =>
        new(id, kind, text, oldNumber, newNumber);

    private static DiffFile File(string path, params DiffHunk[] hunks) =>
        new(path, DiffFile.Change.Modified, false, List(hunks));

    private static DiffHunk Hunk(string header, params DiffLine[] lines) =>
        new(1, header, List(lines));

    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(Control root) where T : class
    {
        if (root is T match) yield return match;

        switch (root)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    foreach (var found in Descendants<T>(child))
                        yield return found;
                break;
            case Decorator decorator when decorator.Child is not null:
                foreach (var found in Descendants<T>(decorator.Child))
                    yield return found;
                break;
            case ContentControl content when content.Content is Control contentChild:
                foreach (var found in Descendants<T>(contentChild))
                    yield return found;
                break;
        }
    }

    private static TextBlock? Text(Control root, string text) =>
        Descendants<TextBlock>(root).FirstOrDefault(block => block.Text == text);

    [Fact]
    [Trait("Req", "UI-GIT-016")] // App/Views/Diff/DiffInspector.swift:194-213
    public void Card_header_toggles_collapse_and_shows_its_path_and_change_symbol()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File(
                "Sources/App/LongNamedFile.swift",
                Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Addition, "added", 1, 1)));
            var toggled = false;
            var card = new DiffFileCard(file, isCollapsed: false, toggle: () => toggled = true);
            var button = Assert.Single(Descendants<Button>(card));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(toggled);

            // The File() fixture defaults to DiffFile.Change.Modified, whose symbol is pencil.circle.fill
            // (Card_header_uses_the_change_symbol_for_each_change_kind covers the other three kinds).
            Assert.Contains(Descendants<SwiftImage>(card), image =>
                image.SymbolName == "pencil.circle.fill");

            var headerText = Descendants<TextBlock>(button).Select(block => block.Text).ToArray();
            Assert.Contains(file.Directory, headerText);
            Assert.Contains(file.Name, headerText);
            Assert.Contains(Descendants<DiffStatLabel>(button), _ => true);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-016")] // App/Views/Diff/DiffInspector.swift:196-213
    public void Card_header_uses_the_chevron_symbol_and_source_geometry()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Addition, "added", 1, 1)));
            foreach (var isCollapsed in new[] { true, false })
            {
                var card = new DiffFileCard(file, isCollapsed, () => { });
                var button = Assert.Single(Descendants<Button>(card));
                Assert.Equal(new Thickness(12, 9), button.Padding);

                var row = Descendants<StackPanel>(button)
                    .First(panel => panel.Orientation == Orientation.Horizontal);
                Assert.Equal(8, row.Spacing);

                Assert.Contains(Descendants<SwiftImage>(card), image =>
                    image.SymbolName == "chevron.right");

                var rotations = Descendants<Control>(card)
                    .Select(control => control.RenderTransform)
                    .OfType<RotateTransform>()
                    .ToArray();
                Assert.Contains(rotations, transform =>
                    transform.Angle == (isCollapsed ? 0 : 90));
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-016")] // App/Views/Diff/DiffInspector.swift:233-249
    public void Card_header_uses_the_change_symbol_for_each_change_kind()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var expected = new[]
            {
                (DiffFile.Change.Added, "plus.circle.fill"),
                (DiffFile.Change.Deleted, "minus.circle.fill"),
                (DiffFile.Change.Renamed, "arrow.right.circle.fill"),
                (DiffFile.Change.Modified, "pencil.circle.fill"),
            };

            foreach (var (change, symbol) in expected)
            {
                var file = File("Sources/App/File.swift",
                    Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Context, "line", 1, 1))) with
                {
                    FileChange = change,
                };
                var card = new DiffFileCard(file, isCollapsed: false, () => { });
                Assert.Contains(Descendants<SwiftImage>(card), image => image.SymbolName == symbol);
            }
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-017")] // App/Views/Diff/DiffInspector.swift:215-221
    public void Expanded_binary_file_shows_the_binary_caption()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Assets/image.bin") with { IsBinary = true };
            var expanded = new DiffFileCard(file, isCollapsed: false, () => { });
            var caption = Assert.IsType<TextBlock>(Text(expanded, "Binary file"));
            Assert.Equal(10, caption.FontSize);
            Assert.Equal(new Thickness(12, 0, 12, 10), caption.Margin);

            var collapsed = new DiffFileCard(file, isCollapsed: true, () => { });
            Assert.Null(Text(collapsed, "Binary file"));
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-018")] // App/Views/Diff/DiffInspector.swift:215-221
    public void Expanded_file_without_hunks_shows_the_unavailable_details_caption()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/Generated.swift");
            var expanded = new DiffFileCard(file, isCollapsed: false, () => { });
            var caption = Assert.IsType<TextBlock>(Text(expanded, "Diff details unavailable"));
            Assert.Equal(10, caption.FontSize);
            Assert.Equal(new Thickness(12, 0, 12, 10), caption.Margin);

            var collapsed = new DiffFileCard(file, isCollapsed: true, () => { });
            Assert.Null(Text(collapsed, "Diff details unavailable"));
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-019")] // App/Views/Diff/DiffInspector.swift:390-411
    public void Diff_lines_use_the_11_5_point_monospaced_font()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Addition, "added line", 1, 1)));
            var view = new DiffLinesView(file);
            // The line text renders as SelectableTextBlock (Diff_line_text_is_selectable), a TextBlock subclass.
            var rowText = Assert.IsAssignableFrom<TextBlock>(Text(view, "added line"));

            Assert.Equal(11.5, rowText.FontSize);
            Assert.Contains("Cascadia Mono", rowText.FontFamily.Name, System.StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-019")] // App/Views/Diff/DiffInspector.swift:406-407
    public void Diff_line_text_is_selectable()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Addition, "select this", 1, 1)));
            var view = new DiffLinesView(file);

            Assert.Contains(Descendants<SelectableTextBlock>(view), block => block.Text == "select this");
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-020")] // App/Views/Diff/DiffInspector.swift:313-355
    public void Hunk_header_is_raw_and_is_omitted_when_line_numbers_are_hidden()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            const string rawHeader = "@@ -2,3 +4,5 @@ function";
            var file = File("Sources/App/File.swift",
                Hunk(rawHeader, Line(DiffLine.Kind.Context, "context", 2, 4)));

            var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);
            var header = Assert.Single(sections.OfType<DiffLinesView.Section.Header>());
            Assert.Equal(rawHeader, header.Text);
            Assert.NotNull(Text(new DiffLinesView(file), rawHeader));

            var hiddenSections = DiffLinesView.MakeSections(file, showsLineNumbers: false, limit: 200);
            Assert.Empty(hiddenSections.OfType<DiffLinesView.Section.Header>());
            Assert.Null(Text(new DiffLinesView(file, showsLineNumbers: false), rawHeader));
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-020")] // App/Views/Diff/DiffInspector.swift:320-326
    public void Hunk_header_is_dropped_when_a_tinted_run_continues_across_hunks()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var first = new DiffHunk(1, "@@ -1,1 +1,1 @@",
                List(Line(DiffLine.Kind.Addition, "first", 1, 1, id: 1)));
            var second = new DiffHunk(2, "@@ -5,1 +5,1 @@",
                List(Line(DiffLine.Kind.Addition, "second", 5, 5, id: 2)));
            var file = File("Sources/App/File.swift", first, second);

            var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);
            // continues (:320) requires pending to be non-empty already, which is never true for the first hunk, so
            // the first hunk always gets its header; only the second hunk's header is dropped, because its first run
            // is tinted and the first hunk's tinted line is still pending.
            var header = Assert.Single(sections.OfType<DiffLinesView.Section.Header>());
            Assert.Equal(first.Header, header.Text);
            Assert.Equal(0, header.Index);
            var tinted = Assert.Single(sections.OfType<DiffLinesView.Section.Tinted>());
            Assert.Equal(2, tinted.Lines.Count);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-020")] // App/Views/Diff/DiffInspector.swift:348-355
    public void Hunk_header_uses_the_source_padding_and_fills_the_available_width()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            const string rawHeader = "@@ -1,1 +1,1 @@";
            var file = File("Sources/App/File.swift",
                Hunk(rawHeader, Line(DiffLine.Kind.Context, "context", 1, 1)));
            var view = new DiffLinesView(file);
            // The hunk header renders as SelectableTextBlock (AddSection's Section.Header case), a TextBlock subclass.
            var label = Assert.IsAssignableFrom<TextBlock>(Text(view, rawHeader));

            Assert.Equal(new Thickness(10, 3), label.Margin);
            Assert.Equal(HorizontalAlignment.Stretch, label.HorizontalAlignment);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-021")] // App/Views/Diff/DiffInspector.swift:448-473
    public void Diff_rows_use_two_30_wide_number_columns_and_a_14_wide_marker_column()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -12,1 +13,1 @@", Line(DiffLine.Kind.Addition, "added", 12, 13)));
            var view = new DiffLinesView(file);
            var blocks = Descendants<TextBlock>(view).ToArray();

            var numbers = blocks.Where(block => block.Width == 30).ToArray();
            Assert.Equal(2, numbers.Length);
            Assert.Equal("12", numbers[0].Text);
            Assert.Equal("13", numbers[1].Text);

            var marker = Assert.Single(blocks, block => block.Width == 14);
            Assert.Equal("+", marker.Text);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-021")] // App/Views/Diff/DiffInspector.swift:460-461, :490-495
    public void Diff_rows_use_the_source_markers_and_keep_empty_line_text_visible()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,3 +1,2 @@",
                    Line(DiffLine.Kind.Addition, "added", 1, 1, id: 1),
                    Line(DiffLine.Kind.Deletion, "deleted", 2, id: 2),
                    Line(DiffLine.Kind.Context, "", 3, 2, id: 3),
                    Line(DiffLine.Kind.Note, "note", id: 4)));
            var view = new DiffLinesView(file);
            var markers = Descendants<TextBlock>(view)
                .Where(block => block.Width == 14)
                .Select(block => block.Text)
                .ToArray();

            Assert.Equal(new[] { "+", "\u2212", "", "" }, markers);
            Assert.Equal(" ", Text(view, " ")?.Text);
            Assert.NotNull(Text(view, "note"));
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-021")] // App/Views/Diff/DiffInspector.swift:464-488
    public void Tinted_run_rounds_only_its_first_and_last_rows()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var top = DiffLineRow.BlockRounding(index: 0, count: 3);
            var middle = DiffLineRow.BlockRounding(index: 1, count: 3);
            var bottom = DiffLineRow.BlockRounding(index: 2, count: 3);

            Assert.Equal(8, top.TopLeft);
            Assert.Equal(8, top.TopRight);
            Assert.Equal(0, top.BottomLeft);
            Assert.Equal(0, top.BottomRight);
            Assert.Equal(0, middle.TopLeft);
            Assert.Equal(0, middle.BottomRight);
            Assert.Equal(8, bottom.BottomLeft);
            Assert.Equal(8, bottom.BottomRight);
            Assert.Equal(0, bottom.TopLeft);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-021")] // App/Views/Diff/DiffInspector.swift:464-468
    public void Diff_row_uses_the_source_padding()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,1 +1,1 @@", Line(DiffLine.Kind.Context, "context", 1, 1)));
            var view = new DiffLinesView(file);
            var row = Descendants<Border>(view)
                .First(border => Descendants<TextBlock>(border).Any(block => block.Text == "context"));

            Assert.Equal(new Thickness(6, 1, 8, 1), row.Padding);
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-022")] // App/Views/Diff/DiffInspector.swift:274, :372-387
    public void Show_all_button_appears_only_when_the_diff_exceeds_200_lines()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var atLimit = File("Sources/App/File.swift",
                Hunk("@@ -1,200 +1,200 @@", Enumerable.Range(1, 200)
                    .Select(index => Line(DiffLine.Kind.Addition, "line", index, index, index))
                    .ToArray()));
            Assert.Null(Text(new DiffLinesView(atLimit), "Show all 200 lines"));

            var aboveLimit = File("Sources/App/File.swift",
                Hunk("@@ -1,201 +1,201 @@", Enumerable.Range(1, 201)
                    .Select(index => Line(DiffLine.Kind.Addition, "line", index, index, index))
                    .ToArray()));
            Assert.NotNull(Text(new DiffLinesView(aboveLimit), "Show all 201 lines"));
        });
    }

    [Fact]
    [Trait("Req", "UI-GIT-022")] // App/Views/Diff/DiffInspector.swift:372-387
    public void Show_all_button_names_the_total_line_count_and_renders_every_line_when_clicked()
    {
        headless.OnUiThread(() =>
        {
            using var runtime = new TestRuntime();
            var file = File("Sources/App/File.swift",
                Hunk("@@ -1,201 +1,201 @@", Enumerable.Range(1, 201)
                    .Select(index => Line(DiffLine.Kind.Addition, "line " + index, index, index, index))
                    .ToArray()));
            var view = new DiffLinesView(file);
            var label = Assert.IsType<TextBlock>(Text(view, "Show all 201 lines"));
            Assert.Equal(10, label.FontSize);

            var button = Assert.Single(Descendants<Button>(view));
            Assert.Equal(new Thickness(10, 6), button.Padding);
            Assert.Equal(new Thickness(10, 8), button.Margin);
            Assert.Equal(200, Descendants<TextBlock>(view)
                .Count(block => block.Text is { } text && text.StartsWith("line ", System.StringComparison.Ordinal)));

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Equal(201, Descendants<TextBlock>(view)
                .Count(block => block.Text is { } text && text.StartsWith("line ", System.StringComparison.Ordinal)));
            Assert.Null(Text(view, "Show all 201 lines"));
        });
    }

    [Fact]
    [Trait("Req", "PR-STORE-175")] // App/Views/Diff/DiffInspector.swift:162-173, :165
    public void A_focused_edit_with_no_diff_adds_no_file_and_one_with_a_patch_adds_its_file()
    {
        var unchanged = UnchangedEdit("Sources/App/Same.swift");

        // :165 skips a nil diff and an empty one alike.
        Assert.Empty(DiffInspector.FocusedFiles([unchanged], ValueList<DiffFile>.Empty));
        Assert.Empty(DiffInspector.FocusedFiles([new FileEdit("Sources/App/Same.swift", "")], ValueList<DiffFile>.Empty));

        // The negative control: a patch on the same kind of edit does add its file (:169-170).
        const string text = "first line\nsecond line\n";
        var changed = new FileEdit("Sources/App/Changed.swift", text, "first line\nchanged line\n");
        Assert.NotNull(changed.Diff);
        var file = Assert.Single(DiffInspector.FocusedFiles([unchanged, changed], ValueList<DiffFile>.Empty));
        Assert.Equal("Sources/App/Changed.swift", file.Path);
        Assert.NotEmpty(file.Hunks);
    }

    [Fact]
    [Trait("Req", "PR-STORE-175")] // App/Views/Diff/DiffInspector.swift:71-83, :110-146, :165, :215-226; App/Runtime/ThreadRuntime.swift:378-385, :393-398, :2020
    public void Opening_the_changes_on_an_edit_with_no_diff_shows_no_file_while_a_patch_opens_its_card()
    {
        headless.OnUiThread(() =>
        {
            using var store = new AppModelHarness();
            var project = AppModelSamples.Project("Diff inspector", store.NewFolder("diff-inspector"));
            var thread = AppModelSamples.Thread(project);
            store.WriteLibrary([project], [thread]);
            AppModelHarness.WriteDocument(thread.Id);
            var app = store.MakeModel();
            var runtime = store.Main.OnMain(() => new ThreadRuntime(thread.Id, app));
            store.Main.Loop.Run(() => runtime.EnsureLoaded(), AppModelHarness.Bound);
            // The thread has no turn, so the diff the inspector parses is empty (ThreadRuntime.swift:2020) and the
            // focused edit is the only thing that could put a file in it (DiffInspector.swift:136-138).
            Assert.Empty(runtime.Turns);

            var inspector = store.Main.OnMain(() => new DiffInspector(runtime));

            OpenChanges(store, runtime, inspector, UnchangedEdit("Sources/App/Same.swift"));
            Assert.Empty(Descendants<DiffFileCard>(inspector));
            Assert.Empty(Descendants<DiffLinesView>(inspector));
            Assert.NotNull(Text(inspector, "No changes yet"));

            // The negative control: the same path with a patch opens its card, expanded because it is focused
            // (:143-146), with its lines (:222-225), so the empty result above is the nil diff's doing and not an
            // inspector that never loaded.
            var changed = new FileEdit("Sources/App/Same.swift", "first line\nsecond line\n", "first line\nchanged line\n");
            OpenChanges(store, runtime, inspector, changed);
            var card = Assert.Single(Descendants<DiffFileCard>(inspector));
            Assert.Equal("Sources/App/Same.swift", card.Tag);
            Assert.Single(Descendants<DiffLinesView>(card));
            Assert.Null(Text(inspector, "No changes yet"));
            // The runtime holds its model weakly (ThreadRuntime.swift:409); the parsed diff reads it (:2015).
            System.GC.KeepAlive(app);
        });
    }

    // The edit SimpleDiff.unified makes of two identical texts: FileEdit.Stats(diff: nil, additions: 0, deletions: 0)
    // (Core/Support/Text.swift:105-115). Built as that value, not through the port's SimpleDiff, so these tests pin the
    // consumers of a nil diff on their own; SimpleDiffTests pins the producer.
    private static FileEdit UnchangedEdit(string path) => new(path, Diff: null, Additions: 0, Deletions: 0);

    // A tool row's tap (TimelineRows.swift:1692) is runtime.showDiff with the row's edits as the focus; the inspector
    // reloads on the new focus revision after its 180 ms wait (DiffInspector.swift:110-119). The load is the private
    // loadTask, read by reflection so the test waits for it rather than for a text that is on screen before it too.
    private static void OpenChanges(AppModelHarness store, ThreadRuntime runtime, DiffInspector inspector, FileEdit edit)
    {
        store.Main.OnMain(() => runtime.ShowDiff(new Border(), turn: null, focusEdits: [edit]));
        var load = Assert.IsAssignableFrom<TaskHandle>(typeof(DiffInspector)
            .GetField("loadTask", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(inspector));
        store.Main.Advance(System.TimeSpan.FromMilliseconds(180));
        store.Main.Loop.RunUntil(() => load.Completion.IsCompleted, AppModelHarness.Bound);
    }
}
