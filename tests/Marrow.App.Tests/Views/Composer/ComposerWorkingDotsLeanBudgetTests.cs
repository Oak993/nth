using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-9 (lean pass L9): the composer's working dots for one simulated second on a 240 Hz display. Headless and deterministic
// (R-TEST-5): the field's clock is the test's ManualTimeProvider, advanced 1/240 s before each forced compositor tick, read through
// ThreadCountingTimeProvider, and the display's refresh period is 1/240 s (RenderFrameSource.PeriodSource). A pill-wide field: 700 x 24
// points, 3 rows of 89 dots. 60 warm ticks first. Debug build (the test project builds Debug).
// What is counted, with no hook in the field: draws (ticks after which the field's last drawn frame shows a new time), the field's clock
// reads (one per render-server callback that samples: the frame update and the draw), and the bytes the test thread allocates inside
// ForceRenderTimerTick, where headless runs the compositor tick and with it the field's handler (asserted: every clock read of the
// field is on the test thread, none elsewhere). The bytes include Avalonia's own work for the tick.
// Numbers: see ED-NEW-lean-9 in porting/engineering_decisions.md (before on 1fdf47b43, after the L9 fix).
[Collection("Headless")]
public sealed class ComposerWorkingDotsLeanBudgetTests(HeadlessSession headless)
{
    private const double Rate = 240;
    private const int Warm = 60;

    private static (int Draws, int Reads, double Bytes) OneSimulatedSecond()
    {
        var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / Rate);
        using var runtime = new TestRuntime();
        var clock = new ThreadCountingTimeProvider(runtime.Time, Environment.CurrentManagedThreadId);
        using var provider = SwiftClock.Provider.Override(clock);
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1, 0, 0), animated: true, sweeps: false);
        var window = new Avalonia.Controls.Window { Width = 700, Height = 24, Background = Brushes.Black, Content = field };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(267, field.LastRenderedFrame is { } first ? first.Rows * first.Columns : 0);
            for (var i = 0; i < Warm; i++) Tick(runtime, out _);
            var last = field.LastRenderedFrame!.Now.Ticks;
            var reads = clock.OnThread;
            var draws = 0;
            long bytes = 0;
            for (var i = 0; i < Rate; i++)
            {
                Tick(runtime, out var allocated);
                bytes += allocated;
                if (field.LastRenderedFrame is { } frame && frame.Now.Ticks != last) { draws++; last = frame.Now.Ticks; }
            }
            Assert.Equal(0, clock.OffThread); // the field's render-server work ran on this thread, so its allocations are counted
            Assert.Equal(0, frames.Pending); // never a UI-thread frame
            return (draws, clock.OnThread - reads, bytes / Rate);
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
            RenderFrameSource.PeriodSource = period;
        }
    }

    private static void Tick(TestRuntime runtime, out long allocated)
    {
        runtime.Advance(TimeSpan.FromSeconds(1 / Rate));
        Dispatcher.UIThread.RunJobs();
        var before = GC.GetAllocatedBytesForCurrentThread();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_dots_draw_within_the_budget_in_a_simulated_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var (draws, _, _) = OneSimulatedSecond();
        Assert.True(draws > 0, "the field stopped drawing");
        Assert.True(draws <= DrawsLimit, $"the dots drew {draws} times in one simulated second at {Rate} Hz (limit {DrawsLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_dots_sample_their_clock_within_the_budget_in_a_simulated_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var (_, reads, _) = OneSimulatedSecond();
        Assert.True(reads > 0, "the field stopped sampling");
        Assert.True(reads <= ReadsLimit, $"the dots read their clock {reads} times in one simulated second at {Rate} Hz (limit {ReadsLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_warm_compositor_tick_with_the_dots_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var (_, _, bytes) = OneSimulatedSecond();
        Assert.True(bytes <= BytesLimit, $"a warm compositor tick with the dots allocates {bytes:F1} B (limit {BytesLimit})");
    });

    private const int DrawsLimit = 360; // before 240 (1.5x)
    private const int ReadsLimit = 720; // before 480 (1.5x)
    private const double BytesLimit = 6890; // before 4593 B (1.5x)
}
