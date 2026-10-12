using System.Text;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 7.2 and 13.3 "NthUiView: progressive streaming renders ... degrade-to-text" (web surfaces W16b; ED-NEW-web-w16b-1):
// two hundred random charts (every type, any number of series given as arguments or as lines, gaps, extremes, fixed or
// reversed ranges, editable or not) are streamed in pieces of random size, painted, walked with the pointer and the keys and
// dragged; nothing throws and nothing that is drawn or reported is not a finite number. The seed is fixed, so a failure is
// the same failure again.
[Collection("Headless")]
public sealed class NthUiChartFuzzTests(HeadlessSession headless)
{
    private static readonly string[] Types = ["line", "bar", "area", "pie", "scatter", ""];

    private static readonly string[] Numbers = ["0", "1", "2", "3.5", "10", "-4", "100", "250", "0.001", "1e6", "-1e6", "1e300", "7"];

    private static string Values(Random random, int count) =>
        "[" + string.Join(", ", Enumerable.Range(0, count).Select(_ => random.Next(12) == 0 ? "\"x\"" : Numbers[random.Next(Numbers.Length)])) + "]";

    private static string Spec(Random random)
    {
        var type = Types[random.Next(Types.Length)];
        var count = random.Next(0, 9);
        var text = new StringBuilder();
        var editable = random.Next(5) == 0 && type is "line" or "bar" or "area";
        if (editable) text.Append("card \"F\"\n  state v = ").Append(Values(random, Math.Max(1, count))).Append('\n').Append("  ");
        text.Append("chart ").Append(type);
        if (random.Next(3) == 0) text.Append(" title=\"A title ").Append(random.Next(100)).Append('"');
        if (random.Next(2) == 0) text.Append(" x=\"").Append(string.Join(",", Enumerable.Range(0, count).Select(i => "L" + i + new string('x', random.Next(0, 12))))).Append('"');
        if (random.Next(6) == 0) text.Append(" unit=\"u\"");
        if (random.Next(5) == 0) text.Append(" min=").Append(Numbers[random.Next(Numbers.Length)]);
        if (random.Next(5) == 0) text.Append(" max=").Append(Numbers[random.Next(Numbers.Length)]);
        if (random.Next(5) == 0) text.Append(" stacked");
        if (editable) text.Append(" editable=points bind=v");
        else
        {
            var series = random.Next(0, 4);
            if (random.Next(2) == 0 && series > 0)
            {
                text.Append(" series=\"").Append(string.Join(",", Enumerable.Range(0, series).Select(i => "S" + i))).Append("\" y=[").Append(string.Join(", ", Enumerable.Range(0, series).Select(_ => Values(random, count)))).Append(']');
                text.Append('\n');
            }
            else
            {
                text.Append('\n');
                for (var i = 0; i < series; i++)
                {
                    text.Append("  series \"S").Append(i).Append("\" y=").Append(Values(random, count));
                    if (type == "scatter" || random.Next(4) == 0) text.Append(" x=").Append(Values(random, count));
                    text.Append('\n');
                }
            }
            return text.ToString();
        }
        text.Append('\n');
        return text.ToString();
    }

    private static void Check(NthUiRig rig)
    {
        var chart = rig.Chart();
        foreach (var control in new Avalonia.Controls.Control?[] { chart.Plot, chart.Pie, chart.Bars })
        {
            if (control is null) continue;
            using var bitmap = new RenderTargetBitmap(new PixelSize(Math.Max(1, (int)control.Bounds.Width), Math.Max(1, (int)control.Bounds.Height)));
            bitmap.Render(control);
        }
        if (chart.Plot is { } plot)
        {
            var (lo, hi) = plot.ShownRange;
            Assert.True(double.IsFinite(lo) && double.IsFinite(hi) && hi > lo, $"range {lo} {hi}");
            for (var s = 0; s < plot.Model.Series.Count; s++)
            {
                for (var i = 0; i < plot.Model.Series[s].Values.Count; i++)
                {
                    if (plot.PointOf(s, i) is { } p) Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y), $"point {s},{i}: {p}");
                }
            }
            if (plot.Readout is { } readout) Assert.DoesNotContain("NaN", readout.Spoken, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("NaN", chart.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", chart.Summary, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void Two_hundred_random_charts_stream_paint_and_take_the_pointer_and_the_keys_without_a_bad_number() => headless.OnUiThread(() =>
    {
        var random = new Random(16);
        int plots = 0, pies = 0, lists = 0, editable = 0;
        for (var round = 0; round < 200; round++)
        {
            var text = Spec(random);
            try
            {
                using var rig = new NthUiRig();
                rig.Stream(text, random.Next(3, 40));
                rig.View.Complete();
                rig.Settle();
                if (!rig.View.Document.Nodes.Any(n => n.Kind == "chart" && !n.Degraded)) continue;
                var chart = rig.Chart();
                Check(rig);
                if (chart.Plot is not null) plots++;
                if (chart.Pie is not null) pies++;
                if (chart.Bars is not null) lists++;
                if (chart.Model.Editable) editable++;
                if (chart.Plot is { } plot)
                {
                    for (var i = 0; i < 6; i++)
                    {
                        var at = new Point(random.NextDouble() * plot.Bounds.Width, random.NextDouble() * plot.Bounds.Height);
                        rig.Window.MouseMove(rig.InWindow(plot, at), RawInputModifiers.None);
                        rig.Tick();
                    }
                    if (plot.Model.Editable)
                    {
                        var at = new Point(random.NextDouble() * plot.Bounds.Width, random.NextDouble() * plot.Bounds.Height);
                        rig.Window.MouseDown(rig.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
                        for (var i = 0; i < 4; i++)
                        {
                            rig.Window.MouseMove(rig.InWindow(plot, new Point(at.X + random.Next(-30, 30), random.NextDouble() * plot.Bounds.Height)), RawInputModifiers.LeftMouseButton);
                            rig.Tick();
                        }
                        rig.Window.MouseUp(rig.InWindow(plot, at), MouseButton.Left, RawInputModifiers.None);
                    }
                    else
                    {
                        plot.Focus(NavigationMethod.Tab);
                        foreach (var key in new[] { Key.Right, Key.Down, Key.End, Key.Left, Key.Up, Key.Home, Key.Escape, Key.Right }) rig.Key(plot, key);
                    }
                }
                if (chart.Pie is { } pie)
                {
                    pie.Focus(NavigationMethod.Tab);
                    foreach (var key in new[] { Key.Right, Key.End, Key.Left, Key.Home, Key.Escape }) rig.Key(pie, key);
                    rig.Window.MouseMove(rig.InWindow(pie, new Point(random.NextDouble() * pie.Bounds.Width, random.NextDouble() * pie.Bounds.Height)), RawInputModifiers.None);
                }
                rig.Click(chart.TableButton);
                rig.Settle();
                Check(rig);
                rig.Click(chart.TableButton);
                rig.Settle();
                Check(rig);
            }
            catch (Exception exception) when (exception is not Xunit.Sdk.XunitException)
            {
                throw new InvalidOperationException($"round {round} threw on:\n{text}", exception);
            }
        }
        Assert.True(plots >= 60 && pies >= 15 && lists >= 10 && editable >= 5, $"the fuzz must reach every drawing: {plots} plots, {pies} pies, {lists} bar lists, {editable} editable");
    });
}
