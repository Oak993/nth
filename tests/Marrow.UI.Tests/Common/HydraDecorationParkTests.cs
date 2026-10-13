using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Common;

// ED-NEW-perf-idle-4: the breathing ring and the working spinner used to ask the frame clock for a frame at every display refresh
// (240 a second on a fast panel) and to repaint at 60, and to go on asking while the window was minimised. They are draw loops
// now: the request is held back to the next 60 Hz slot, and none is made while no window of the app can be seen; the first
// frame after the return draws the phase the clock is at. A manual clock and a manual frame source with a 240 Hz refresh.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-8")]
public sealed class HydraDecorationParkTests(HeadlessSession headless)
{
    private const int Refresh = 240;

    private static Window Show(Control content)
    {
        var window = new Window { Width = 48, Height = 48, Content = content, Background = Brushes.Black };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void Tick(ManualTimeProvider time, ManualFrameSource frames)
    {
        time.Advance(TimeSpan.FromSeconds(1.0 / Refresh));
        frames.Advance(TimeSpan.FromSeconds(1.0 / Refresh));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void The_breathing_ring_asks_for_about_half_the_frames_of_a_240_Hz_display_still_draws_sixty_and_parks_while_unseen() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource { WakePeriod = TimeSpan.FromSeconds(1.0 / Refresh) };
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var ring = new HydraBreathRingView { Width = 20, Height = 20 };
        var window = Show(ring);
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames);
            var delivered = frames.Delivered;
            var drawn = ring.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames);
            var asked = frames.Delivered - delivered;
            var draws = ring.Invalidations - drawn;
            Assert.InRange(draws, 59, 66); // the 60 Hz cap, unchanged
            Assert.True(asked <= 140, $"frames delivered to the ring in a simulated second: {asked} (240 before)");

            // no window of the app can be seen: nothing is asked for and nothing is drawn, for as long as that lasts
            frames.Unseen = true;
            Tick(time, frames); // the request already made is answered; it makes no new one while unseen
            delivered = frames.Delivered;
            drawn = ring.Invalidations;
            for (var i = 0; i < Refresh * 10; i++) Tick(time, frames);
            Assert.Equal(delivered, frames.Delivered);
            Assert.Equal(drawn, ring.Invalidations);

            // seen again: the first frame draws, with the phase the clock is at (the pose is a function of the clock)
            frames.Unseen = false;
            for (var i = 0; i < 8; i++) Tick(time, frames);
            Assert.True(ring.Invalidations > drawn, "the ring draws again once a window can be seen");
            var (scale, opacity) = HydraBreathRingView.Phase((SwiftClock.Now - default(ContinuousInstant)).TotalSeconds, false);
            Assert.InRange(Math.Abs(ring.CurrentScale - scale), 0, .04);
            Assert.InRange(Math.Abs(ring.CurrentOpacity - opacity), 0, .04);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void The_working_spinner_draws_sixty_a_second_asks_for_fewer_frames_and_parks_while_unseen() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource { WakePeriod = TimeSpan.FromSeconds(1.0 / Refresh) };
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var spinner = new SpinnerLayerView(MiniSpinner.Cells, 2, 2.4);
        var window = Show(spinner);
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames);
            var delivered = frames.Delivered;
            var drawn = spinner.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames);
            Assert.InRange(spinner.Invalidations - drawn, 59, 66);
            Assert.True(frames.Delivered - delivered <= 140, $"frames delivered to the spinner in a simulated second: {frames.Delivered - delivered}");

            frames.Unseen = true;
            Tick(time, frames);
            delivered = frames.Delivered;
            for (var i = 0; i < Refresh * 10; i++) Tick(time, frames);
            Assert.Equal(delivered, frames.Delivered);
            frames.Unseen = false;
            drawn = spinner.Invalidations;
            for (var i = 0; i < 8; i++) Tick(time, frames);
            Assert.True(spinner.Invalidations > drawn);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
