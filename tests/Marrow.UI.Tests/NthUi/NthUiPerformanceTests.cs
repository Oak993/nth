using System.Diagnostics;
using System.Text;
using Marrow.UI.NthUi;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.4 and 13.3 (web surfaces W16a; ED-NEW-web-w16a-1): a long answer streams with each line placed well inside a
// frame on average, and a state change that recomputes a large table lands inside one frame. Budgets are generous for a
// shared test machine (headless Skia, a debug build): the checks catch a quadratic or a per-line rebuild, not jitter.
[Collection("Headless")]
public sealed class NthUiPerformanceTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_long_answer_places_each_line_well_inside_a_frame_on_average() => headless.OnUiThread(() =>
    {
        using var rig = new NthUiRig();
        var lines = new List<string> { "card \"Inventory\"", "  state n = 2", "  stepper n label=\"Multiplier\" min=1 max=9", "  table cols=\"Item,Count,Total\"" };
        for (var i = 1; i <= 200; i++) lines.Add($"    row \"Item {i}\" \"{i}\" = fmt(n * {i}, \"0\")");
        for (var i = 1; i <= 100; i++) lines.Add($"  text \"Note {i}: every line here is placed as it completes.\"");
        rig.View.Append(lines[0] + "\n");
        rig.Pump();
        var watch = new Stopwatch();
        var worst = TimeSpan.Zero;
        var all = TimeSpan.Zero;
        foreach (var line in lines.Skip(1))
        {
            watch.Restart();
            rig.View.Append(line + "\n");
            rig.Pump();
            watch.Stop();
            all += watch.Elapsed;
            if (watch.Elapsed > worst) worst = watch.Elapsed;
            rig.Tick();
        }
        var average = all.TotalMilliseconds / (lines.Count - 1);
        Assert.True(average < 16, $"average {average:0.0} ms per line, worst {worst.TotalMilliseconds:0} ms");
        Assert.True(worst.TotalMilliseconds < 500, $"worst {worst.TotalMilliseconds:0} ms");
        Assert.Equal(200, rig.View.Document.Nodes.Count(n => n.Kind == "row"));
    });

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16a-1")]
    public void A_state_change_that_recomputes_two_hundred_rows_lands_inside_a_frame() => headless.OnUiThread(() =>
    {
        var text = new StringBuilder("card \"Inventory\"\n  state n = 2\n  stepper n label=\"Multiplier\" min=1 max=9\n  table cols=\"Item,Total\"\n");
        for (var i = 1; i <= 200; i++) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"    row \"Item {i}\" = fmt(n * {i}, \"0\")\n");
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
        times.Sort();
        var median = times[times.Count / 2];
        Assert.True(median < 16, $"median {median:0.0} ms per change ({string.Join(", ", times.Select(t => t.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)))})");
        Assert.Contains("800", rig.TextsOf("table"));
    });
}
