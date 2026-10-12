using System.Diagnostics;
using System.Globalization;
using System.Text;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4, 12 and 13.3 (web surfaces W16a; ED-NEW-web-w16a-1): a long answer streams with each line placed well inside
// a frame on average, and a state change that recomputes a large table lands inside one frame. These headless checks run on
// a shared machine in a debug build, so their budgets catch a quadratic or a per-line rebuild, not jitter; the design's own
// budgets (2 ms a node, 4 ms of a frame for a 300-node view) are held by NthUiPerformanceNativeTests on the real machine.
// Each run writes what it measured to nthui-snapshots/performance.txt next to the test binaries.
[Collection("Headless")]
public sealed class NthUiPerformanceTests(HeadlessSession headless)
{
    /// <summary>Streams a 301-line answer (a 200-row table that reads a state, then 100 texts) one line a frame and returns
    /// the average and the worst time to place one line, layout included, in milliseconds.</summary>
    internal static (double Average, double Worst) StreamLongAnswer()
    {
        using var rig = new NthUiRig();
        var lines = new List<string> { "card \"Inventory\"", "  state n = 2", "  stepper n label=\"Multiplier\" min=1 max=9", "  table cols=\"Item,Count,Total\"" };
        for (var i = 1; i <= 200; i++) lines.Add($"    row \"Item {i}\" \"{i}\" = fmt(n * {i}, \"0\")");
        for (var i = 1; i <= 100; i++) lines.Add($"  text \"Note {i}: every line here is placed as it completes.\"");
        rig.View.Append(lines[0] + "\n");
        rig.Pump();
        var watch = new Stopwatch();
        var worst = 0.0;
        var all = 0.0;
        foreach (var line in lines.Skip(1))
        {
            watch.Restart();
            rig.View.Append(line + "\n");
            rig.Pump();
            watch.Stop();
            all += watch.Elapsed.TotalMilliseconds;
            worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
            rig.Tick();
        }
        Assert.Equal(200, rig.View.Document.Nodes.Count(n => n.Kind == "row"));
        return (all / (lines.Count - 1), worst);
    }

    /// <summary>Changes a state that 200 table rows of a 300-node view read, nine times, and returns the median time from the
    /// change to the laid-out view, in milliseconds.</summary>
    internal static double RecomputeMedian()
    {
        var text = new StringBuilder("card \"Inventory\"\n  state n = 2\n  stepper n label=\"Multiplier\" min=1 max=9\n  table cols=\"Item,Total\"\n");
        for (var i = 1; i <= 200; i++) text.Append(CultureInfo.InvariantCulture, $"    row \"Item {i}\" = fmt(n * {i}, \"0\")\n");
        for (var i = 1; i <= 96; i++) text.Append(CultureInfo.InvariantCulture, $"  text \"Note {i}\"\n");
        using var rig = new NthUiRig(NthUiView.Show(text.ToString(), culture: NthUiRig.English));
        var stepper = rig.ViewOf<NthUiStepperView>().Stepper;
        stepper.Press(1);
        rig.Pump();
        var times = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            var watch = Stopwatch.StartNew();
            stepper.Press(i % 2 == 0 ? 1 : -1);
            rig.Pump();
            times.Add(watch.Elapsed.TotalMilliseconds);
        }
        Assert.Contains("800", rig.TextsOf("table"));
        times.Sort();
        return times[times.Count / 2];
    }

    /// <summary>Adds one measured line to nthui-snapshots/performance.txt.</summary>
    internal static void Record(string what, double milliseconds)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "nthui-snapshots");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "performance.txt"), string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:O} {what} {milliseconds:0.00} ms\n"));
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_long_answer_places_each_line_well_inside_a_frame_on_average() => headless.OnUiThread(() =>
    {
        var (average, worst) = StreamLongAnswer();
        Record("headless stream average", average);
        Record("headless stream worst", worst);
        Assert.True(average < 16, $"average {average:0.0} ms per line, worst {worst:0} ms");
        Assert.True(worst < 500, $"worst {worst:0} ms");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_state_change_that_recomputes_two_hundred_rows_lands_inside_a_frame() => headless.OnUiThread(() =>
    {
        var median = RecomputeMedian();
        Record("headless recompute median", median);
        Assert.True(median < 16, $"median {median:0.0} ms per change");
    });
}
