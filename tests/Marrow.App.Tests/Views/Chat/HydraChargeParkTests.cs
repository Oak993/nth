using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Chat;

// ED-NEW-perf-idle-4: the Hydra head's charge (the turning ring and the two breaths) asked the frame clock for a frame at every
// display refresh for as long as a head ran, minimised or not. It is a draw loop at 60 a second now: the request is held back to
// the next slot, none is made while no window of the app can be seen, and the first frame after the return draws the clock's
// phase. A manual clock and a manual frame source with a 240 Hz refresh.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-8")]
public sealed class HydraChargeParkTests(HeadlessSession headless)
{
    private const int Refresh = 240;

    private static void Tick(ManualTimeProvider time, ManualFrameSource frames)
    {
        time.Advance(TimeSpan.FromSeconds(1.0 / Refresh));
        frames.Advance(TimeSpan.FromSeconds(1.0 / Refresh));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void The_charge_draws_sixty_a_second_asks_for_fewer_frames_than_the_display_makes_and_parks_while_unseen() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource { WakePeriod = TimeSpan.FromSeconds(1.0 / Refresh) };
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var charge = new HydraChargeMotion { Width = 28, Height = 28 };
        var window = new Avalonia.Controls.Window { Width = 48, Height = 48, Content = charge, Background = Brushes.Black };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            for (var i = 0; i < 60; i++) Tick(time, frames);
            var delivered = frames.Delivered;
            var drawn = charge.LayerView.Invalidations;
            for (var i = 0; i < Refresh; i++) Tick(time, frames);
            Assert.InRange(charge.LayerView.Invalidations - drawn, 59, 66);
            Assert.True(frames.Delivered - delivered <= 140, $"frames delivered to the charge in a simulated second: {frames.Delivered - delivered} (240 before)");

            frames.Unseen = true;
            Tick(time, frames); // the request already made is answered; it makes no new one while unseen
            delivered = frames.Delivered;
            drawn = charge.LayerView.Invalidations;
            for (var i = 0; i < Refresh * 10; i++) Tick(time, frames);
            Assert.Equal(delivered, frames.Delivered);
            Assert.Equal(drawn, charge.LayerView.Invalidations);

            frames.Unseen = false;
            for (var i = 0; i < 8; i++) Tick(time, frames);
            Assert.True(charge.LayerView.Invalidations > drawn, "the charge draws again once a window can be seen");
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
