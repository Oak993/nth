using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-181, the cost (SPEC.md 4.5, 5.4). Gating and deterministic: an animation that changes only a colour builds no geometry, for
// 300 continuous shapes of either mechanism (the cache is keyed by size, radius and style, not by colour). Recorded and NOT gating
// (a timing under the shared build load is not evidence): the frame times of the same 300-shape panel, rendered and captured
// headless on Skia's CPU rasteriser, as circular Borders, as Borders cut by the continuous clip and as SwiftShapeBorders; the
// numbers are written to continuous-corner-cost.txt beside the test assembly (or to the path in CC_COST_OUT).
[Collection("Headless")]
public sealed class SwiftShapesCostTests(HeadlessSession headless)
{
    private const int Shapes = 300, Frames = 120;

    private static (Window Window, List<Control> Items, Action<int> Recolour) Panel(Func<int, Control> make)
    {
        var wrap = new WrapPanel { Width = 1200 };
        var items = new List<Control>();
        for (var i = 0; i < Shapes; i++)
        {
            var item = make(i);
            item.Width = 110;
            item.Height = 60;
            item.Margin = new Thickness(5);
            wrap.Children.Add(item);
            items.Add(item);
        }
        var window = new Window { Width = 1200, Height = 700, Background = Brushes.Black, Content = wrap };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        void Recolour(int frame)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var brush = new SolidColorBrush(Color.FromRgb((byte)(40 + (frame * 3 + i) % 200), 90, (byte)(200 - (frame + i) % 150)));
                switch (items[i])
                {
                    case SwiftShapeBorder shape: shape.Background = brush; break;
                    case Border border: border.Background = brush; break;
                }
            }
        }
        return (window, items, Recolour);
    }

    private static Control Plain(RoundedCornerStyle style, int _) => new Border { Background = Brushes.Gray }.RoundedRectangle(12, style);

    private static Control Exact(int _) => new SwiftShapeBorder { Background = Brushes.Gray }.RoundedRectangle(12, RoundedCornerStyle.Continuous);

    [Theory]
    [Trait("Decision", "ED-181")]
    [InlineData("border")]
    [InlineData("shape")]
    public void A_colour_only_animation_over_300_continuous_shapes_builds_no_geometry(string kind) => headless.OnUiThread(() =>
    {
        var (window, items, recolour) = Panel(kind == "border" ? i => Plain(RoundedCornerStyle.Continuous, i) : Exact);
        Assert.Equal(Shapes, items.Count);
        recolour(0);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        if (kind == "border") Assert.All(items, item => Assert.NotNull(item.Clip)); // 110 x 60 at r 12: Apple's corner (2 * 1.528665 * 12 = 36.7 <= 60)
        var builds = SwiftShapes.GeometryBuildCount;
        for (var frame = 1; frame <= Frames; frame++)
        {
            recolour(frame);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(builds, SwiftShapes.GeometryBuildCount);
        window.Close();
    });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // A recorded benchmark: it asserts only that every mode renders; the numbers are the evidence, read by the lead.
    public void The_frame_time_of_a_300_shape_panel_is_recorded_for_the_circular_clipped_and_exact_shapes() => headless.OnUiThread(() =>
    {
        var lines = new List<string>();
        foreach (var (name, make) in new (string, Func<int, Control>)[]
                 { ("circular Border", i => Plain(RoundedCornerStyle.Circular, i)), ("continuous Border (clip)", i => Plain(RoundedCornerStyle.Continuous, i)), ("continuous SwiftShapeBorder", Exact) })
        {
            var (window, _, recolour) = Panel(make);
            for (var warm = 0; warm < 10; warm++) { recolour(warm); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); using var warmFrame = window.CaptureRenderedFrame(); }
            var times = new List<double>();
            for (var frame = 0; frame < Frames; frame++)
            {
                var watch = Stopwatch.StartNew();
                recolour(frame + 10);
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var captured = window.CaptureRenderedFrame();
                times.Add(watch.Elapsed.TotalMilliseconds);
                Assert.NotNull(captured);
            }
            times.Sort();
            lines.Add($"{name}: median {times[times.Count / 2]:F2} ms, p90 {times[times.Count * 9 / 10]:F2} ms, mean {times.Average():F2} ms over {Frames} frames of {Shapes} shapes at 1200 x 700");
            window.Close();
        }
        var path = Environment.GetEnvironmentVariable("CC_COST_OUT") ?? Path.Combine(AppContext.BaseDirectory, "continuous-corner-cost.txt");
        File.WriteAllLines(path, lines);
    });
}
