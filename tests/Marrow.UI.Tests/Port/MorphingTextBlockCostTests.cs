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
