using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Marrow.UI.NthUi;
using Marrow.UI.NthUi.Charts;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.NthUi;

// DESIGN.md 12 "nth-ui streaming: each node under 2 ms; a 300-node view never takes more than 4 ms of a frame" (web surfaces W16b;
// ED-NEW-web-w16b-1). A chart is one node however many points it has: placing a three-series 200-point chart, changing the state
// behind it, painting it and gliding the readout over it all stay far inside a frame. As in NthUiPerformanceTests these run on a
// shared machine in a debug build, so they hold the processor time (what the work costs whatever else the machine is doing)
// against budgets that catch a quadratic or a rebuild, not jitter; the design's own wall-clock budgets are held by
// NthUiChartPerformanceNativeTests for a Release run on the real machine. Each run writes what it measured to
// nthui-snapshots/chart-performance.txt next to the test binaries.
[Collection("Headless")]
public sealed class NthUiChartPerformanceTests(HeadlessSession headless)
{
    private const string Heavy = "card \"Perf\"\n  state a = 2\n  slider a label=\"A\" min=0 max=10 step=1\n"
        + "  chart line series=\"A,B,C\" y=[(map(range(1, 201), i => a * i)), (map(range(1, 201), i => i * 3)), (map(range(1, 201), i => 700 - i))]\n";

    private static double ProcessorMilliseconds()
    {
        using var process = Process.GetCurrentProcess();
        return process.TotalProcessorTime.TotalMilliseconds;
    }

    internal static void Record(string line)
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "nthui-snapshots");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "chart-performance.txt"), line + "\n");
    }

    /// <summary>Places a three-series chart of 200 points, changes the state behind it 21 times, paints it 200 times and walks
    /// a readout across it for 300 frames, and returns the average of each.</summary>
    internal static NthUiChartTimings Measure()
    {
        // The first chart of a process pays for loading fonts and compiling the code; what is measured is the ones after it.
        using (var warm = new NthUiRig())
        {
            warm.View.Append(Heavy);
            warm.View.Complete();
            warm.Settle();
        }
        // The processor clock ticks about every 16 ms, so each figure is a total over enough repeats to be many ticks.
        var wall = Stopwatch.StartNew();
        var cpu = ProcessorMilliseconds();
        for (var i = 0; i < 12; i++)
        {
            using var each = new NthUiRig();
            each.View.Append(Heavy);
            each.View.Complete();
            each.Pump();
        }
        var placeWall = wall.Elapsed.TotalMilliseconds / 12;
        var placeCpu = (ProcessorMilliseconds() - cpu) / 12;

        using var rig = new NthUiRig();
        rig.View.Append(Heavy);
        rig.View.Complete();
        rig.Pump();
        rig.Settle();
        var chart = rig.Chart();
        Assert.Equal(200, chart.Model.Count);
        Assert.Equal(3, chart.Model.Series.Count);

        var slider = rig.ViewOf<NthUiSliderView>().Slider;
        var times = new List<double>();
        cpu = ProcessorMilliseconds();
        for (var i = 0; i < 21; i++)
        {
            wall.Restart();
            rig.Key(slider, i % 2 == 0 ? Key.Up : Key.Down);
            times.Add(wall.Elapsed.TotalMilliseconds);
            rig.Tick();
        }
        var changeCpu = (ProcessorMilliseconds() - cpu) / 21;
        times.Sort();
        var changeWall = times[times.Count / 2];
        rig.Settle();

        var plot = chart.Plot!;
        wall.Restart();
        cpu = ProcessorMilliseconds();
        for (var i = 0; i < 200; i++)
        {
            using var bitmap = new RenderTargetBitmap(new PixelSize(528, 240));
            bitmap.Render(plot);
        }
        var paintWall = wall.Elapsed.TotalMilliseconds / 200;
        var paintCpu = (ProcessorMilliseconds() - cpu) / 200;

        // The readout following a walking cursor: the key moves the point, the frame glides the readout and repaints.
        plot.Focus(NavigationMethod.Tab);
        rig.Pump();
        const int Frames = 300;
        wall.Restart();
        cpu = ProcessorMilliseconds();
        for (var i = 0; i < Frames; i++)
        {
            rig.Key(plot, i % 120 < 60 ? Key.Right : Key.Left);
            rig.Tick();
        }
        var glideWall = wall.Elapsed.TotalMilliseconds / Frames;
        var glideCpu = (ProcessorMilliseconds() - cpu) / Frames;
        Assert.NotNull(plot.Active);
        return new NthUiChartTimings(placeWall, placeCpu, changeWall, changeCpu, paintWall, paintCpu, glideWall, glideCpu);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-web-w16b-1")]
    public void A_three_series_chart_of_two_hundred_points_is_placed_changed_painted_and_hovered_inside_a_frame() => headless.OnUiThread(() =>
    {
        var t = Measure();
        Record(string.Create(CultureInfo.InvariantCulture,
            $"processor ms: placed {t.PlaceCpu:0.0}; a state change {t.ChangeCpu:0.0}; a paint {t.PaintCpu:0.00}; a readout frame {t.GlideCpu:0.00} (debug build, 3 x 200 points)"));
        Assert.True(t.PlaceCpu < 60, $"placing the chart took {t.PlaceCpu:0.0} ms");
        Assert.True(t.ChangeCpu < 16, $"changing the state behind it took {t.ChangeCpu:0.0} ms a change");
        Assert.True(t.PaintCpu < 20, $"painting it took {t.PaintCpu:0.0} ms");
        Assert.True(t.GlideCpu < 8, $"gliding the readout over it took {t.GlideCpu:0.0} ms a frame");
    });
}
