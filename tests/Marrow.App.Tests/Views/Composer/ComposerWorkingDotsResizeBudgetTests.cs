using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-9 (hidden grid): the still-dot grid (an Ellipse per dot, shown under Reduce Motion and read as the field's layout
// record) is laid out again at every new size, also while the dots animate and the grid is hidden. A width change of one point at a
// time (a window or composer resized at one size a frame) on a 700 x 24 animated field (3 rows of 89 or 90 dots): the bytes the UI
// thread allocates for one layout pass at the new size, after 10 warm passes, the median of 40. Debug, headless. The grid after each
// pass is the grid a fresh field of that size lays out: the same dots in the same order at the same places, fills and opacities.
[Collection("Headless")]
public sealed class ComposerWorkingDotsResizeBudgetTests(HeadlessSession headless)
{
    private const int Warm = 10;
    private const int Passes = 40;

    private static string Grid(DotFieldControl field) => string.Join(";", ((Avalonia.Controls.Canvas)field.Layer!).Children.Cast<Ellipse>().Select(dot =>
        $"{Avalonia.Controls.Canvas.GetLeft(dot):R},{Avalonia.Controls.Canvas.GetTop(dot):R},{dot.Width:R},{dot.Height:R},{dot.Opacity:R},{((ISolidColorBrush)dot.Fill!).Color}"));

    private static (double Bytes, string Grid, string Fresh) Resize(bool animated)
    {
        using var runtime = new TestRuntime();
        runtime.Advance(TimeSpan.FromSeconds(10));
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1, 0, 0), animated, sweeps: false) { Width = 700, Height = 24 };
        var window = new Avalonia.Controls.Window { Width = 760, Height = 40, Background = Brushes.Black, Content = new Avalonia.Controls.Canvas { Children = { field } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            void Pass(int k) { field.Width = 700 + k % 12; Dispatcher.UIThread.RunJobs(); }
            for (var k = 1; k <= Warm; k++) Pass(k);
            var passes = new long[Passes];
            for (var k = Warm + 1; k <= Warm + Passes; k++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                Pass(k);
                passes[k - Warm - 1] = GC.GetAllocatedBytesForCurrentThread() - before;
            }
            // the median pass: a job of a test running beside this one, run by this thread's RunJobs, lands in a few passes only
            Array.Sort(passes);
            var grid = Grid(field);
            // a fresh field at the last size, in the same window
            var fresh = new DotFieldControl(new SwiftColor(1, 0, 0), animated, sweeps: false) { Width = field.Width, Height = 24 };
            ((Avalonia.Controls.Canvas)window.Content!).Children.Add(fresh);
            Dispatcher.UIThread.RunJobs();
            return (passes[Passes / 2], grid, Grid(fresh));
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_one_point_resize_of_an_animated_field_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var (bytes, grid, fresh) = Resize(animated: true);
        Assert.Equal(fresh, grid);
        Assert.True(bytes <= ResizeBytesLimit, $"a one-point resize of an animated field allocates {bytes:F0} B on the UI thread (limit {ResizeBytesLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_resized_still_field_lays_out_the_grid_a_fresh_field_of_that_size_does() => headless.OnUiThread(() =>
    {
        var (_, grid, fresh) = Resize(animated: false);
        Assert.Equal(fresh, grid);
        Assert.Equal(3 * 89, grid.Split(';').Length); // 702 points: 3 rows of ceil(702 / 8) + 1 = 89
    });

    // Measured first on the unchanged grid (claude/lean-r1 d723dd5f8, every dot cleared and made again): 1,869,630 B; after the
    // grid laid out in place: 4,957 B (Avalonia's layout pass, the field's frame message and its brush); limit 1.3x.
    private const double ResizeBytesLimit = 6_444; // before 1,869,630 B, after 4,957 B
}
