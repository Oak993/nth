using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Services.Git;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorSamples;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;

namespace Marrow.App.Tests.Views.Diff;

// Source: DroppyCode/App/Views/Diff/DiffInspector.swift (B3 52bc792), :252-412 (a file's rows) and :423-434.
[Collection("Headless")]
public sealed class DiffInspectorRowsTests(HeadlessSession headless)
{
    private const DiffLine.Kind Add = DiffLine.Kind.Addition;
    private const DiffLine.Kind Del = DiffLine.Kind.Deletion;
    private const DiffLine.Kind Ctx = DiffLine.Kind.Context;
    private const DiffLine.Kind Note = DiffLine.Kind.Note;

    private static int RowCount(Control view) => Descendants<DiffLineRow>(view).Count();

    [Fact]
    [Trait("Req", "PR-GIT-135")] // App/Views/Diff/DiffInspector.swift:274, :313-317, :390-394 (the budget of 200 lines runs across the hunks)
    public void Only_the_first_200_lines_across_the_hunks_are_grouped()
    {
        var file = FileOf(Hunk("@@ -1,150 +1,150 @@", Lines(Add, 150, "first")), Hunk("@@ -200,150 +200,150 @@", Lines(Add, 150, "second")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        var lines = sections.OfType<DiffLinesView.Section.Tinted>().SelectMany(section => section.Lines).ToArray();
        Assert.Equal(200, lines.Length);
        Assert.Equal(150, lines.Count(line => line.Text.StartsWith("first", StringComparison.Ordinal)));
        Assert.Equal(50, lines.Count(line => line.Text.StartsWith("second", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Req", "PR-GIT-135")] // App/Views/Diff/DiffInspector.swift:314-317 (a huge hunk costs its first 200 lines; a hunk past the budget adds nothing, not even its header)
    public void A_huge_hunk_costs_its_first_200_lines_and_a_hunk_past_the_budget_is_left_out()
    {
        var file = FileOf(Hunk("@@ -1,5000 +1,5000 @@", Lines(Ctx, 5000)), Hunk("@@ -9000,1 +9000,1 @@", Lines(Add, 3, "late")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.Equal(200, sections.OfType<DiffLinesView.Section.Plain>().Sum(section => section.Value.Count));
        Assert.Equal(new[] { "@@ -1,5000 +1,5000 @@" }, sections.OfType<DiffLinesView.Section.Header>().Select(header => header.Text));
        Assert.Empty(sections.OfType<DiffLinesView.Section.Tinted>());
    }

    [Fact]
    [Trait("Req", "PR-GIT-135")] // App/Views/Diff/DiffInspector.swift:372-387, :391, :409 (the button names the whole file's lines; a click shows them all)
    public void The_show_all_button_counts_every_hunk_and_a_click_renders_every_line() => headless.OnUiThread(() =>
    {
        var file = FileOf(Hunk("@@ -1,150 +1,150 @@", Lines(Add, 150, "first")), Hunk("@@ -200,150 +200,150 @@", Lines(Add, 150, "second")));
        var view = new DiffLinesView(file);

        Assert.Equal(200, RowCount(view));
        var button = Assert.Single(Descendants<Button>(view));
        Assert.Equal("Show all 300 lines", Assert.Single(Descendants<TextBlock>(button)).Text);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Equal(300, RowCount(view));
        Assert.Empty(Descendants<Button>(view));
        Assert.Contains(Descendants<TextBlock>(view), block => block.Text == "second 150");
    });

    [Fact]
    [Trait("Req", "PR-GIT-135")] // App/Views/Diff/DiffInspector.swift:372 (the button shows only when the total exceeds 200: 200 across two hunks does not)
    public void Exactly_200_lines_across_two_hunks_show_no_button_and_all_rows() => headless.OnUiThread(() =>
    {
        var file = FileOf(Hunk("@@ -1,100 +1,100 @@", Lines(Add, 100, "first")), Hunk("@@ -300,100 +300,100 @@", Lines(Del, 100, "second")));
        var view = new DiffLinesView(file);

        Assert.Equal(200, RowCount(view));
        Assert.Empty(Descendants<Button>(view));
    });

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:305-311 (a tinted run is emitted in chunks of 40, each with its offset and the run's length)
    public void A_tinted_run_is_emitted_in_chunks_of_forty_carrying_offset_and_run_length()
    {
        var file = FileOf(Hunk("@@ -1,100 +1,100 @@", Lines(Add, 100)));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.IsType<DiffLinesView.Section.Header>(sections[0]);
        var chunks = sections.OfType<DiffLinesView.Section.Tinted>().ToArray();
        Assert.Equal(new[] { 40, 40, 20 }, chunks.Select(chunk => chunk.Lines.Count));
        Assert.Equal(new long[] { 0, 40, 80 }, chunks.Select(chunk => chunk.Offset));
        Assert.Equal(new long[] { 100, 100, 100 }, chunks.Select(chunk => chunk.Count));
        Assert.Equal(4, sections.Count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:477-488, :356-365 (the radius 8 is on the run's very first and very last rows only, not at each chunk's ends)
    public void Corners_are_rounded_only_at_the_first_and_last_row_of_the_whole_run() => headless.OnUiThread(() =>
    {
        var view = new DiffLinesView(FileOf(Hunk("@@ -1,100 +1,100 @@", Lines(Add, 100))));

        var rows = Descendants<DiffLineRow>(view).ToArray();

        Assert.Equal(100, rows.Length);
        var radii = rows.Select(row => ((Border)row.Child!).CornerRadius).ToArray();
        Assert.Equal(new CornerRadius(8, 8, 0, 0), radii[0]);
        Assert.Equal(new CornerRadius(0, 0, 8, 8), radii[99]);
        Assert.All(radii.Skip(1).Take(98), radius => Assert.Equal(new CornerRadius(0), radius));
    });

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:477-488 (a run of one row is both its first and its last: all four corners)
    public void A_run_of_one_row_is_rounded_on_all_four_corners_and_plain_rows_are_square() => headless.OnUiThread(() =>
    {
        var view = new DiffLinesView(FileOf(Hunk("@@ -1,3 +1,3 @@", Line(Ctx, "before", 1, 1), Line(Add, "only", null, 2), Line(Ctx, "after", 2, 3))));

        var radii = Descendants<DiffLineRow>(view).Select(row => ((Border)row.Child!).CornerRadius).ToArray();

        Assert.Equal(new[] { new CornerRadius(0), new CornerRadius(8), new CornerRadius(0) }, radii);
    });

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:423-434, :300-343 (runs share tinting: additions and deletions are one run, context and notes another)
    public void Additions_and_deletions_form_one_tinted_run_and_context_and_notes_one_plain_run()
    {
        var file = FileOf(Hunk("@@ -1,7 +1,7 @@",
            Line(Ctx, "c1"), Line(Add, "a1"), Line(Add, "a2"), Line(Del, "d1"), Line(Ctx, "c2"), Line(Note, "n1"), Line(Add, "a3")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.Equal(5, sections.Count);
        Assert.IsType<DiffLinesView.Section.Header>(sections[0]);
        Assert.Equal(new[] { "c1" }, Assert.IsType<DiffLinesView.Section.Plain>(sections[1]).Value.Select(line => line.Text));
        var first = Assert.IsType<DiffLinesView.Section.Tinted>(sections[2]);
        Assert.Equal(new[] { "a1", "a2", "d1" }, first.Lines.Select(line => line.Text));
        Assert.Equal((0L, 3L), (first.Offset, first.Count));
        Assert.Equal(new[] { "c2", "n1" }, Assert.IsType<DiffLinesView.Section.Plain>(sections[3]).Value.Select(line => line.Text));
        var last = Assert.IsType<DiffLinesView.Section.Tinted>(sections[4]);
        Assert.Equal(new[] { "a3" }, last.Lines.Select(line => line.Text));
        Assert.Equal((0L, 1L), (last.Offset, last.Count));
    }

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:318-326 (a hunk opening with a tinted run joins the pending run and loses its header)
    public void A_tinted_run_continues_across_hunks_without_the_second_hunks_header()
    {
        var file = FileOf(
            Hunk("@@ -1,2 +1,2 @@", Line(Ctx, "c1"), Line(Add, "a1")),
            Hunk("@@ -9,3 +9,3 @@", Line(Add, "a2"), Line(Del, "d1"), Line(Ctx, "c2")),
            Hunk("@@ -20,1 +20,1 @@", Line(Ctx, "c3")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.Equal(6, sections.Count);
        Assert.Equal("@@ -1,2 +1,2 @@", Assert.IsType<DiffLinesView.Section.Header>(sections[0]).Text);
        Assert.Equal(new[] { "c1" }, Assert.IsType<DiffLinesView.Section.Plain>(sections[1]).Value.Select(line => line.Text));
        var merged = Assert.IsType<DiffLinesView.Section.Tinted>(sections[2]);
        Assert.Equal(new[] { "a1", "a2", "d1" }, merged.Lines.Select(line => line.Text));
        Assert.Equal(3L, merged.Count);
        Assert.Equal(new[] { "c2" }, Assert.IsType<DiffLinesView.Section.Plain>(sections[3]).Value.Select(line => line.Text));
        var third = Assert.IsType<DiffLinesView.Section.Header>(sections[4]);
        Assert.Equal("@@ -20,1 +20,1 @@", third.Text);
        Assert.Equal(2, third.Index);
        Assert.Equal(new[] { "c3" }, Assert.IsType<DiffLinesView.Section.Plain>(sections[5]).Value.Select(line => line.Text));
    }

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:320-326 (a hunk that opens with a plain run flushes the pending run first and gets its header)
    public void A_hunk_opening_plain_flushes_the_pending_tinted_run_before_its_header()
    {
        var file = FileOf(
            Hunk("@@ -1,1 +1,1 @@", Line(Add, "a1")),
            Hunk("@@ -9,2 +9,2 @@", Line(Ctx, "c1"), Line(Add, "a2")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.Equal(5, sections.Count);
        Assert.IsType<DiffLinesView.Section.Header>(sections[0]);
        Assert.Equal(new[] { "a1" }, Assert.IsType<DiffLinesView.Section.Tinted>(sections[1]).Lines.Select(line => line.Text));
        Assert.Equal("@@ -9,2 +9,2 @@", Assert.IsType<DiffLinesView.Section.Header>(sections[2]).Text);
        Assert.IsType<DiffLinesView.Section.Plain>(sections[3]);
        Assert.Equal(new[] { "a2" }, Assert.IsType<DiffLinesView.Section.Tinted>(sections[4]).Lines.Select(line => line.Text));
    }

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:300-311 (a run merged across hunks is chunked as one: 40 then the rest, offset and count of the whole)
    public void A_run_merged_across_hunks_is_chunked_as_one_run()
    {
        var file = FileOf(Hunk("@@ -1,25 +1,25 @@", Lines(Add, 25, "first")), Hunk("@@ -50,25 +50,25 @@", Lines(Add, 25, "second")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 200);

        Assert.Single(sections.OfType<DiffLinesView.Section.Header>());
        var chunks = sections.OfType<DiffLinesView.Section.Tinted>().ToArray();
        Assert.Equal(new[] { 40, 10 }, chunks.Select(chunk => chunk.Lines.Count));
        Assert.Equal(new long[] { 0, 40 }, chunks.Select(chunk => chunk.Offset));
        Assert.Equal(new long[] { 50, 50 }, chunks.Select(chunk => chunk.Count));
    }

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:323-325 (headers only when line numbers are shown; the merging does not depend on it)
    public void No_headers_are_emitted_without_line_numbers_but_runs_still_merge() => headless.OnUiThread(() =>
    {
        var file = FileOf(Hunk("@@ -1,1 +1,1 @@", Line(Add, "a1")), Hunk("@@ -9,1 +9,1 @@", Line(Add, "a2")));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: false, limit: 200);

        var only = Assert.IsType<DiffLinesView.Section.Tinted>(Assert.Single(sections));
        Assert.Equal(new[] { "a1", "a2" }, only.Lines.Select(line => line.Text));
        var view = new DiffLinesView(file, showsLineNumbers: false);
        Assert.Null(Text(view, "@@ -1,1 +1,1 @@"));
        Assert.DoesNotContain(Descendants<TextBlock>(view), block => block.Width == 30);
        Assert.Equal(2, RowCount(view));
    });

    [Fact]
    [Trait("Req", "PR-GIT-136")] // App/Views/Diff/DiffInspector.swift:316-317, :330-337 (a run the budget cuts is taken only as far as it reaches; its count is what was taken)
    public void A_run_cut_by_the_budget_carries_the_count_of_what_was_taken()
    {
        var file = FileOf(Hunk("@@ -1,10 +1,10 @@", Lines(Add, 10)));

        var sections = DiffLinesView.MakeSections(file, showsLineNumbers: true, limit: 5);

        var cut = Assert.IsType<DiffLinesView.Section.Tinted>(sections[1]);
        Assert.Equal(5, cut.Lines.Count);
        Assert.Equal(5L, cut.Count);
        Assert.Equal(2, sections.Count);
    }

    [Fact]
    [Trait("Req", "PR-GIT-135")] // App/Views/Diff/DiffInspector.swift:390-411 (the rows are built and laid out, in the embedded monospaced face Cascadia Mono)
    public void Rows_lay_out_in_the_embedded_monospaced_face() => headless.OnUiThread(() =>
    {
        var view = new DiffLinesView(FileOf(Hunk("@@ -1,2 +1,2 @@", Line(Ctx, "context", 1, 1), Line(Add, "added", null, 2))));
        var window = new Avalonia.Controls.Window { Width = 500, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            window.UpdateLayout();

            var text = Assert.IsAssignableFrom<TextBlock>(Text(view, "added"));
            Assert.True(text.Bounds.Width > 0);
            Assert.True(FontManager.Current.TryGetGlyphTypeface(new Typeface(text.FontFamily), out var face));
            Assert.Equal("Cascadia Mono", face.FamilyName);
        }
        finally { window.Close(); }
    });
}
