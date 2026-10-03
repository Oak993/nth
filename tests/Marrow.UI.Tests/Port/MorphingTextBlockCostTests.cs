using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// Owner bar "zero stutter" and OD-0094's "cheap" (a brush transform, no per-frame allocation): what a gradient title
// costs frame by frame, against the same title in a plain colour, measured on the UI thread with the renderer ticking.
[Collection("Headless")]
public sealed class MorphingTextBlockCostTests(HeadlessSession headless)
{
    private static LinearGradientBrush Pair() => new()
    {
        StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Colors.OrangeRed, 0), new GradientStop(Colors.DeepPink, 0.5), new GradientStop(Colors.MediumPurple, 1) },
    };

    private static void Pump(ManualFrameSource frames, ManualTimeProvider clock, int count)
    {
        for (var i = 0; i < count; i++)
        {
            clock.Advance(TimeSpan.FromMilliseconds(16));
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    // A change draws each letter with one small brush pinned to the text's box (its blur copies share it), on stops
    // copied once. "Extra High" -> "Max" in a gradient, measured with the headless renderer drawing on this thread: about
    // 19 KB a frame more than in white, nearly all of it the renderer making a gradient shader for each drawn copy (round 1:
    // a new mutable brush and stop list for every letter's seventeen blur copies, about 395 KB more; a small brush per copy
    // instead of per letter, about 32 KB more).
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void A_gradient_change_costs_a_small_brush_a_letter_beyond_a_plain_one() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        (long PerFrame, int Frames) Change(IBrush brush)
        {
            var label = new MorphingTextBlock { Text = "Extra High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = brush, MaxLines = 1 };
            var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = label };
            window.Show();
            try
            {
                Pump(frames, clock, 10);
                label.SetText("Max", Animation.Snappy(0.2));
                Pump(frames, clock, 1); // the first frame builds the change's layouts and letters once
                var before = GC.GetAllocatedBytesForCurrentThread();
                var count = 0;
                while (label.IsMorphing && count < 100) { Pump(frames, clock, 1); count++; }
                return ((GC.GetAllocatedBytesForCurrentThread() - before) / Math.Max(1, count), count);
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        Change(Pair()); // warm the code paths
        var plain = Change(Brushes.White);
        var gradient = Change(Pair());
        Assert.True(plain.Frames >= 10, $"the change ran {plain.Frames} frames");
        Assert.True(gradient.PerFrame - plain.PerFrame <= 24 * 1024, $"a gradient change allocates {gradient.PerFrame} bytes a frame, a plain one {plain.PerFrame}");
    });

    // OD-0094: the drift is "cheap (a shader or brush transform on the frame clock, no per-frame allocation, frames only
    // while the title is visible)". At rest the label is drawn once; each frame only moves its brushes' two ends, which the
    // compositor carries to what it already draws. Measured three ways: (1) the label is never drawn again while the
    // colours move; (2) the UI thread's own work per frame, renderer held still, is the two ends' change notices; (3) with
    // the headless renderer drawing on this thread, a drifting title costs what any changed pixel costs the renderer (a
    // Border whose brush changes colour each frame): about 6.6 KB a frame here, all of it the renderer's frame (measured
    // 2026-10-03: the drift's own work 64 B a frame; with the renderer 6.9 KB against 6.6 KB for the border).
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")] // as ruled by OD-0094
    public void The_drift_moves_the_brushes_and_never_draws_the_label_again() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        void Step(bool render, Action? each)
        {
            each?.Invoke();
            clock.Advance(TimeSpan.FromMilliseconds(16));
            frames.Advance(TimeSpan.FromMilliseconds(16));
            Dispatcher.UIThread.RunJobs();
            if (render) Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
        long PerFrame(Control content, bool render, Action? each = null)
        {
            var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = content };
            window.Show();
            try
            {
                for (var i = 0; i < 30; i++) Step(render, each);
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < 120; i++) Step(render, each);
                return (GC.GetAllocatedBytesForCurrentThread() - before) / 120;
            }
            finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        CountedMorphingTextBlock Title(double period) => new() { Text = "Extra High", FontSize = 17, FontWeight = FontWeight.Medium, Foreground = Pair(), DriftPeriod = period };

        // (1) Drawn once, then only its brushes move: the label is not drawn again, frame after frame.
        var counted = Title(7);
        var window = new Window { Width = 300, Height = 60, Background = Brushes.Black, Content = counted };
        window.Show();
        try
        {
            for (var i = 0; i < 5; i++) Step(true, null);
            var drawn = counted.Renders;
            var start = RenderedPixels.Capture(window);
            for (var i = 0; i < 60; i++) Step(true, null);
            Assert.True(counted.DriftPhase > 0.1, $"phase {counted.DriftPhase}");
            Assert.Equal(drawn, counted.Renders);
            var moved = RenderedPixels.Capture(window);
            var most = 0;
            for (var y = 0; y < start.Height; y++)
                for (var x = 0; x < start.Width; x++)
                    most = Math.Max(most, Math.Abs(start.At(x, y).Red - moved.At(x, y).Red));
            Assert.True(most > 20, $"the colours moved on screen without the label being drawn again ({most} levels)");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }

        // (2) The UI thread's own work, the renderer held still: the two ends' change notices, no more.
        var stillAlone = PerFrame(Title(0), render: false);
        var driftAlone = PerFrame(Title(7), render: false);
        Assert.True(driftAlone - stillAlone <= 256, $"the drift's own work allocates {driftAlone - stillAlone} bytes a frame ({driftAlone} drifting, {stillAlone} still)");

        // (3) With the renderer drawing here: what any changed pixel costs it, no more.
        var solid = new SolidColorBrush(Colors.Red);
        var n = 0;
        var anyChange = PerFrame(new Border { Width = 50, Height = 20, Background = solid }, render: true, () => solid.Color = n++ % 2 == 0 ? Colors.Red : Colors.Blue);
        var drifting = PerFrame(Title(7), render: true);
        Assert.True(drifting - anyChange <= 1024, $"a drifting title costs {drifting} bytes a frame with the renderer; a border changing colour {anyChange}");
    });

    // Every letter's brush shares its colour's one copy of the stops.
    [Fact]
    [Trait("Req", "PR-COMPOSER-111")]
    public void Every_letters_brush_shares_one_copy_of_the_stops() => headless.OnUiThread(() =>
    {
        var label = new MorphingTextBlock { Text = "Max" };
        var pair = Pair();
        var frame = new Rect(0, 0, 60, 20);
        var first = label.Pinned(pair, frame, Matrix.Identity);
        var second = label.Pinned(pair, frame, Matrix.CreateTranslation(-5, 0));
        Assert.Same(first.GradientStops, second.GradientStops);
        Assert.Equal(pair.GradientStops.Select(stop => (stop.Offset, stop.Color)), first.GradientStops.Select(stop => (stop.Offset, stop.Color)));
        Assert.NotSame(first.GradientStops, label.Pinned(Pair(), frame, Matrix.Identity).GradientStops); // another colour, its own
    });
}
