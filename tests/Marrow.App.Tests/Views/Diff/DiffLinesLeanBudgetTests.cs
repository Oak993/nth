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
    // Measured on the unchanged base (Debug): 7,207 controls (7 per line), 45,067,560 bytes; limits set at 1.5x.
    private const int ControlLimit = 10_800;
    private const long ByteLimit = 67_600_000;

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
