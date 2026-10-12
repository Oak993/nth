using Avalonia.Controls;
using Avalonia.Interactivity;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Diff;
using Marrow.Services.Git;
using Xunit;
using static Marrow.App.Tests.Fixtures.DiffInspectorSamples;
using static Marrow.App.Tests.Fixtures.DiffInspectorTree;

namespace Marrow.App.Tests.Views.Diff;

// ED-NEW-lean-17: what a 1,000-line diff costs to build. The file is shown whole (the "Show all" click), so every line
// is a row. Controls are counted through panels, decorators and content (before templating); bytes are the managed
// allocation on the UI thread for building the view and the click, after a warm-up build (JIT, static fields).
// Deterministic: no layout, no clock. Measured in Debug (the test build).
[Collection("Headless")]
public sealed class DiffLinesLeanBudgetTests(HeadlessSession headless)
{
    // Measured on the unchanged base (Debug): 7,207 controls (7 per line), 45,067,560 bytes; limits were set at 1.5x
    // (10,800 and 67,600,000). After one shared font per face and one brush per line kind (Debug): 7,207 controls (the
    // controls per line stay, so selection and copy stay per column), 36,129,656 bytes; limits tightened to 1.3x.
    private const int ControlLimit = 9_400;
    private const long ByteLimit = 47_000_000;

    private static DiffFile ThousandLines()
    {
        var hunks = new List<DiffHunk>();
        for (var h = 0; h < 4; h++)
        {
            var lines = new List<DiffLine>();
            for (var i = 0; i < 250; i++)
            {
                var number = h * 250 + i + 1;
                var kind = (i % 5) switch { 1 => DiffLine.Kind.Addition, 2 => DiffLine.Kind.Deletion, 3 => DiffLine.Kind.Addition, _ => DiffLine.Kind.Context };
                lines.Add(Line(kind, "    let value" + number + " = compute(" + number + ")", kind == DiffLine.Kind.Addition ? null : number, kind == DiffLine.Kind.Deletion ? null : number));
            }
            hunks.Add(Hunk("@@ -" + (h * 250 + 1) + ",250 +" + (h * 250 + 1) + ",250 @@", lines.ToArray()));
        }
        return FileOf(hunks.ToArray());
    }

    private static DiffLinesView BuildWhole(DiffFile file)
    {
        var view = new DiffLinesView(file);
        Descendants<Button>(view).Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return view;
    }

    private static void Log(string line)
    {
        if (Environment.GetEnvironmentVariable("MARROW_LEAN_LOG") is { Length: > 0 } path)
            File.AppendAllText(path, line + Environment.NewLine);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-17")]
    public void A_thousand_line_diff_builds_within_its_control_budget() => headless.OnUiThread(() =>
    {
        var view = BuildWhole(ThousandLines());
        Assert.Equal(1000, Descendants<DiffLineRow>(view).Count());
        var controls = Descendants<Control>(view).Count();
        Log("L17 controls=" + controls);
        Assert.True(controls <= ControlLimit, $"{controls} controls for 1,000 lines (limit {ControlLimit})");
    });

    // The 1.3x byte limit alone would let the per-line fonts and brush holders come back (the base measured under it), so
    // the sharing itself is held: every row's text blocks use one font instance, and the gutter one brush per line kind.
    [Fact]
    [Trait("Decision", "ED-NEW-lean-17")]
    public void Every_row_shares_one_font_and_one_gutter_brush_per_line_kind() => headless.OnUiThread(() =>
    {
        var first = BuildWhole(ThousandLines());
        var second = BuildWhole(ThousandLines());
        var blocks = Descendants<DiffLineRow>(first).Concat(Descendants<DiffLineRow>(second)).SelectMany(row => Descendants<TextBlock>(row)).ToArray();
        Assert.Equal(8000, blocks.Length);
        Assert.Single(blocks.Select(block => block.FontFamily).Distinct(ReferenceEqualityComparer.Instance));
        // Per row: old number, new number, marker, text. The numbers share one brush; the markers one per kind (+, -, other).
        var numbers = Descendants<DiffLineRow>(first).Concat(Descendants<DiffLineRow>(second)).SelectMany(row => Descendants<TextBlock>(row).Take(2)).Select(block => block.Foreground);
        Assert.Single(numbers.Distinct(ReferenceEqualityComparer.Instance));
        var markers = Descendants<DiffLineRow>(first).Concat(Descendants<DiffLineRow>(second)).Select(row => Descendants<TextBlock>(row).ElementAt(2).Foreground);
        Assert.Equal(3, markers.Distinct(ReferenceEqualityComparer.Instance).Count());
        var tints = Descendants<DiffLineRow>(first).Concat(Descendants<DiffLineRow>(second)).Select(row => ((Border)row.Child!).Background);
        Assert.Equal(3, tints.Distinct(ReferenceEqualityComparer.Instance).Count());
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-17")]
    public void A_thousand_line_diff_builds_within_its_byte_budget() => headless.OnUiThread(() =>
    {
        var file = ThousandLines();
        for (var i = 0; i < 2; i++) BuildWhole(file);
        var best = long.MaxValue;
        for (var i = 0; i < 3; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(BuildWhole(file));
            best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Log("L17 bytes=" + best);
        Assert.True(best <= ByteLimit, $"{best} bytes for 1,000 lines (limit {ByteLimit})");
    });
}
