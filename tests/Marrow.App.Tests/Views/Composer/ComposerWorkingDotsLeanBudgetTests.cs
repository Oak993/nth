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
// points, 3 rows of 89 dots, the clock 10 s in (Settled). 60 warm ticks first. Debug build (the test project builds Debug).
// What is counted, with no hook in the field: draws (ticks after which the field's last drawn frame shows a new time), the field's clock
// reads (one per render-server callback that samples: the frame update and the draw), and the bytes the test thread allocates inside
// ForceRenderTimerTick, where headless runs the compositor tick and with it the field's handler (asserted: every clock read of the
// field is on the test thread, none elsewhere). Those bytes include Avalonia's own work for a tick that draws (about 4.2 KB, the same for
// 9 or 89 columns; a still window's tick allocates nothing), so the field's own share is measured too: the same ticks of the same window
// with InvalidatingCompositionHandler (invalidated at every refresh over the field's area, drawing nothing) in place of the field are
// Avalonia's share, and the dots' share is the difference of the two runs' median ticks (the reference must draw at every tick, as the
// dots do: asserted). The
// reference pushes one opacity layer per draw, as the dots do: Avalonia's ImmediateDrawingContext allocates its state stack (56 B)
// at a draw's first push, which is Avalonia's share, not the field's.
// Numbers: see ED-NEW-lean-9 in porting/engineering_decisions.md (before on 1fdf47b43, after the L9 fix) and ED-NEW-lean-9 (pacing):
// the dots draw on the 60 Hz slots of the compositor's shared pacer, every 4th refresh at 240 Hz, so 60 draws a second; every frame
// update samples the clock for the pacer (240) and every draw samples it again (60).
// The own share is a difference of two runs' median ticks, so it needs each run's steady tick: more than half of the ticks of a run
// allocate the same bytes (Avalonia's one-off growth moves a few ticks only), and a regression of the dots that allocates at most
// draws moves the median. Paced, the dots' run has two kinds of tick: the drawing ticks (one in four) are compared with the reference
// (which draws at every tick), and the paced ticks (the other three, an update that asks for the next one and draws nothing) have
// their own budget, so the mixed median (a paced tick) is never compared with a drawing reference.
[Collection("Headless")]
public sealed class ComposerWorkingDotsLeanBudgetTests(HeadlessSession headless)
{
    private const double Rate = 240;
    private const int Warm = 60;
    // The composer loop's time is the clock's own seconds (production: seconds since boot), so a clock at 0 would leave most dots
    // before their first pulse. 10 s in, every dot of the loop is in its wave, as in a running app.
    private const double Settled = 10;

    private static (int Draws, int Reads, double Bytes, long Median, long PacedMax, List<int> DrawnAt) OneSimulatedSecond(bool blend = false)
    {
        var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / Rate);
        using var runtime = new TestRuntime();
        runtime.Advance(TimeSpan.FromSeconds(Settled));
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
            for (var i = 0; i < Warm; i++) Tick(runtime, out _);
            Assert.Equal(267, field.LastRenderedFrame is { } first ? first.Rows * first.Columns : 0);
            // An accent change eases the dots over 0.32 s (ED-253): the ticks measured are those of the blend, every one inside it.
            if (blend)
            {
                field.UpdateColor(new SwiftColor(0, 0.4, 1), ease: true);
                // ED-NEW-lean-9 (pacing): the new frame is drawn at once when it reaches the render server, at the next tick or the
                // one after (when headless applies the UI's commit); the measured ticks start after that draw, all inside the blend
                var reached = 0;
                while (field.LastRenderedFrame is not { Blending: true } && reached++ < 2) Tick(runtime, out _);
                Assert.True(field.LastRenderedFrame!.Blending, "the new frame's blend was not drawn within two ticks");
            }
            var count = blend ? BlendTicks : (int)Rate;
            var last = field.LastRenderedFrame!.Now.Ticks;
            var reads = clock.OnThread;
            var draws = 0;
            long bytes = 0;
            var drawing = new List<long>();
            var drawnAt = new List<int>();
            long pacedMax = 0;
            for (var i = 0; i < count; i++)
            {
                Tick(runtime, out var allocated);
                bytes += allocated;
                if (field.LastRenderedFrame is { } frame && frame.Now.Ticks != last) { draws++; last = frame.Now.Ticks; drawing.Add(allocated); drawnAt.Add(i); }
                else pacedMax = Math.Max(pacedMax, allocated);
                if (blend) Assert.True(field.LastRenderedFrame!.Blending, $"the blend ended at tick {i}");
            }
            Assert.Equal(0, clock.OffThread); // the field's render-server work ran on this thread, so its allocations are counted
            Assert.Equal(0, frames.Pending); // never a UI-thread frame
            return (draws, clock.OnThread - reads, bytes / count, Median([.. drawing]), pacedMax, drawnAt);
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
            RenderFrameSource.PeriodSource = period;
        }
    }

    // The same window and ticks with a visual that is invalidated at every refresh and draws nothing: Avalonia's own bytes per tick.
    private static long ReferenceBytes()
    {
        var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / Rate);
        using var runtime = new TestRuntime();
        runtime.Advance(TimeSpan.FromSeconds(Settled));
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var host = new Avalonia.Controls.Border();
        var window = new Avalonia.Controls.Window { Width = 700, Height = 24, Background = Brushes.Black, Content = host };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var handler = new InvalidatingCompositionHandler(new Avalonia.Rect(0, 0, 712, 24));
            var visual = Avalonia.Rendering.Composition.ElementComposition.GetElementVisual(host)!.Compositor.CreateCustomVisual(handler);
            visual.Size = new Avalonia.Vector(700, 24);
            Avalonia.Rendering.Composition.ElementComposition.SetElementChildVisual(host, visual);
            visual.SendHandlerMessage(handler);
            for (var i = 0; i < Warm; i++) Tick(runtime, out _);
            var draws = handler.Draws;
            var ticks = new long[(int)Rate];
            for (var i = 0; i < Rate; i++) Tick(runtime, out ticks[i]);
            Assert.Equal((int)Rate, handler.Draws - draws);
            Assert.Equal(0, frames.Pending);
            return Median(ticks);
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
            RenderFrameSource.PeriodSource = period;
        }
    }

    // The median tick: a one-off allocation of Avalonia's inside the second (a pool growing, about 16 KB once) moves a mean by tens of
    // bytes a tick, in either run; the median tick of each run is the steady cost of a tick.
    private static long Median(long[] ticks)
    {
        var sorted = (long[])ticks.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
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
        var (draws, _, _, _, _, _) = OneSimulatedSecond();
        Assert.True(draws > 0, "the field stopped drawing");
        Assert.True(draws <= DrawsLimit, $"the dots drew {draws} times in one simulated second at {Rate} Hz (limit {DrawsLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_dots_sample_their_clock_within_the_budget_in_a_simulated_second_on_a_240_Hz_display() => headless.OnUiThread(() =>
    {
        var (_, reads, _, _, _, _) = OneSimulatedSecond();
        Assert.True(reads > 0, "the field stopped sampling");
        Assert.True(reads <= ReadsLimit, $"the dots read their clock {reads} times in one simulated second at {Rate} Hz (limit {ReadsLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_warm_compositor_tick_with_the_dots_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var (_, _, bytes, _, _, _) = OneSimulatedSecond();
        Assert.True(bytes <= BytesLimit, $"a warm compositor tick with the dots allocates {bytes:F1} B (limit {BytesLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_dots_own_share_of_a_warm_compositor_tick_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var reference = ReferenceBytes();
        var (_, _, _, median, _, drawnAt) = OneSimulatedSecond();
        EverySlotDrew(drawnAt, (int)Rate); // the median is of those drawing ticks
        var own = median - reference;
        Assert.True(own <= OwnBytesLimit, $"the dots' own work allocates {own} B a warm compositor tick (median tick {median} B with the dots, {reference} B without) (limit {OwnBytesLimit})");
    });

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void The_dots_own_share_of_a_warm_compositor_tick_while_the_accent_blends_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var reference = ReferenceBytes();
        var (_, _, _, median, _, drawnAt) = OneSimulatedSecond(blend: true);
        // the new frame's own draw is before the measured ticks; the median is of the drawing ticks
        EverySlotDrew(drawnAt, BlendTicks);
        var own = median - reference;
        Assert.True(own <= BlendOwnBytesLimit, $"while the accent blends, the dots' own work allocates {own} B a warm compositor tick (median tick {median} B with the dots, {reference} B without) (limit {BlendOwnBytesLimit})");
    });

    // The dots drew on every 60 Hz slot of the measured ticks (every 4th tick, one phase), as the reference drew at every tick; one draw
    // more at most, at once on a frame sent from main (a theme change of a test running beside this one draws at once, as before the
    // pacing, where that tick drew anyway).
    private static void EverySlotDrew(List<int> drawnAt, int ticks)
    {
        var at = string.Join(", ", drawnAt);
        Assert.True(Enumerable.Range(0, 4).Any(phase => Enumerable.Range(0, ticks).Where(i => i % 4 == phase).All(drawnAt.Contains)),
            $"the dots missed a 60 Hz slot in {ticks} ticks at 240 Hz (drew at {at})");
        Assert.True(drawnAt.Count <= ticks / 4 + 1, $"the dots drew {drawnAt.Count} times in {ticks} ticks at 240 Hz, more than one a slot (at {at})");
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_paced_compositor_tick_with_the_dots_allocates_within_the_budget() => headless.OnUiThread(() =>
    {
        var (_, _, _, _, pacedMax, drawnAt) = OneSimulatedSecond();
        EverySlotDrew(drawnAt, (int)Rate);
        Assert.True(pacedMax <= PacedBytesLimit, $"a paced compositor tick (an update that draws nothing) with the dots allocates up to {pacedMax} B (limit {PacedBytesLimit})");
    });

    // 0.32 s of blend is 76 ticks at 240 Hz (complete 1 ms early); the 60 after the new frame's first draw (at most 2 ticks in) are
    // all inside it.
    private const int BlendTicks = 60;

    // The first limits (cc75a34e4, measured on the unchanged field before the fix, the clock at 0, the mean tick): draws 360 (240),
    // reads 720 (480), bytes 6890 (4593 B). The fix commit (c92e5ca66) changed the method (the clock 10 s in, the median tick, the
    // reference run); the new method's "before" numbers were measured on the base handler and frame after the fix was written (the
    // review round): bytes 19472 B, own share 15384 B, own share while blending 15512 B. Every limit below is under its first limit.
    // The draws limit is the brief's <=61 since the pacing (ED-NEW-lean-9 (pacing)); the reads limit stays (paced: 300).
    private const int DrawsLimit = 61; // before 240 (first limit 360, 1.5x); after 240 (312, 1.3x); paced 60 (the brief's 61)
    private const int ReadsLimit = 312; // before 480 (first limit 720, 1.5x); after 240 (1.3x); paced 300 (240 updates, 60 draws)
    private const double BytesLimit = 5314; // before 4593 B on the first method (first limit 6890, 1.5x), 19472 B on this one; after 4088 B (1.3x)
    private const long OwnBytesLimit = 0; // before 15384 B; after 0 B
    // Added in the review round, measured first on the unchanged L9 field (c92e5ca66): 64 B, one new immutable brush a draw for each
    // new colour of the blend (first limit 96, 1.5x); before L9 15512 B; after the review round 64 B (1.3x).
    private const long BlendOwnBytesLimit = 83;
    // Added with the pacing: the most a paced tick (the dots' update asks for the next one and draws nothing) allocates; measured 0 B (ED-NEW-lean-9 (pacing) item 3).
    private const long PacedBytesLimit = 0;
}
