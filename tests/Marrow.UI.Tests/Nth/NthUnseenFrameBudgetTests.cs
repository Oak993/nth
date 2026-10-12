using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Nth;

// ED-NEW-lean-6 (a): an nth primitive in a window that cannot be seen asks the compositor for no frames, and the first frame
// after the window is seen again is the painter at the current time (painters are pure functions of time: no phase is lost).
// Headless, Debug; a 240 Hz display and a manual clock, no wall clock.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class NthUnseenFrameBudgetTests(HeadlessSession headless)
{
    // Frame requests in one simulated second at 240 Hz while the window is unseen. Measured (Debug, headless): 240 before the fix (one
    // per refresh), so the first limit was 360; 0 after, the limit now.
    private const int UnseenRequestsPerSecond = 0;

    private static void Step(ManualTimeProvider time, int refreshes)
    {
        for (var i = 0; i < refreshes; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1.0 / 240));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        }
    }

    [Fact]
    public void An_unseen_window_asks_no_frames_for_its_moving_primitive_at_240_hz() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 240);
        WindowPresence.TestFactory = w => new WindowPresence(w, true);
        var spinner = new NthSpinner { Width = 16, Height = 16 };
        var window = new Window { Width = 60, Height = 60, Content = spinner };
        try
        {
            window.Show(); Step(time, 24);
            var presence = WindowPresence.For(spinner)!;
            Assert.True(spinner.FrameRequests > 0, "a seen spinner asks for frames");
            presence.SetSeen(false); Step(time, 24);
            var before = spinner.FrameRequests;
            Step(time, 240);
            var requests = spinner.FrameRequests - before;
            Assert.InRange(requests, 0, UnseenRequestsPerSecond);
        }
        finally
        {
            window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null;
            RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
    });

    // ED-NEW-calcmerge-1: the working beads (an advancing painter: the engine keeps state) go idle in an unseen window like every other
    // nth primitive: no frame requests, the engine's clock frozen, a turn that ends meanwhile cut to gone; seen again they go on from
    // where they were, with no catch-up burst.
    [Fact]
    [Trait("Decision", "ED-NEW-calcmerge-1")]
    public void An_unseen_window_asks_no_frames_for_its_working_beads_and_they_go_on_where_they_were() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 240);
        WindowPresence.TestFactory = w => new WindowPresence(w, true);
        var beads = new NthBeadsControl { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 80, Height = 80, Content = beads, Background = Avalonia.Media.Brushes.Black };
        try
        {
            window.Show(); Step(time, 8);
            beads.Enter(NthBeadsEntrance.Ortus, "t1");
            Step(time, 240);
            var driver = beads.Painter!.Driver;
            Assert.Equal(NthBeadsMode.Running, driver.Engine!.Mode);
            Assert.True(driver.Moving, "a seen turn moves");
            var presence = WindowPresence.For(beads)!;
            presence.SetSeen(false); Step(time, 24);
            var before = beads.FrameRequests;
            var frozen = driver.Engine.Clock;
            Step(time, 240);
            Assert.InRange(beads.FrameRequests - before, 0, UnseenRequestsPerSecond);
            Assert.Equal(frozen, driver.Engine.Clock); // the clock did not run
            Assert.False(beads.IsMoving);
            presence.SetSeen(true); Step(time, 8);
            Assert.True(beads.FrameRequests > before, "seen again, the beads ask for frames");
            Assert.True(driver.Engine.Clock > frozen, "the clock runs again");
            Assert.InRange(driver.Engine.Clock, frozen, frozen + 5 / 60.0); // went on from the frozen clock, no 1 s catch-up
            Assert.Equal(NthBeadsMode.Running, driver.Engine.Mode);
            Assert.True(beads.IsMoving);
            presence.SetSeen(false); Step(time, 24);
            before = beads.FrameRequests;
            beads.Finish(); // the turn ends while unseen: its exit is cut to gone at once, nothing waits on a frame that never comes
            Step(time, 24);
            Assert.Equal(NthBeadsMode.Gone, driver.Engine.Mode);
            Assert.True(beads.IsGone);
            Assert.Equal(before, beads.FrameRequests);
        }
        finally
        {
            window.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null;
            RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
    });

    [Fact]
    public void After_hidden_then_seen_the_first_frame_is_the_painter_at_the_current_time() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 240);
        WindowPresence.TestFactory = w => new WindowPresence(w, true);
        var hiddenSpinner = new NthSpinner { Width = 16, Height = 16 };
        var seenSpinner = new NthSpinner { Width = 16, Height = 16 };
        var hiddenWindow = new Window { Width = 60, Height = 60, Content = hiddenSpinner, Background = Avalonia.Media.Brushes.Black };
        var seenWindow = new Window { Width = 60, Height = 60, Content = seenSpinner, Background = Avalonia.Media.Brushes.Black };
        try
        {
            hiddenWindow.Show(); seenWindow.Show(); Step(time, 24);
            Assert.Equal(hiddenSpinner.Started, seenSpinner.Started);
            var presence = WindowPresence.For(hiddenSpinner)!;
            presence.SetSeen(false); Step(time, 24);
            Step(time, 3 * 240 + 77);
            var drawn = hiddenSpinner.FramesDrawn;
            presence.SetSeen(true); Step(time, 1);
            Assert.True(hiddenSpinner.FramesDrawn > drawn, "the restored window draws on the next refresh");
            Assert.Equal(NthFrame.Seconds(time), hiddenSpinner.LastDrawnAt);
            Assert.Equal(hiddenSpinner.Snapshot(), hiddenSpinner.Frame);
            // the windows share the compositor's 60 Hz slots: compare the two when both last drew at the same time
            for (var i = 0; i < 8 && (hiddenSpinner.LastDrawnAt != NthFrame.Seconds(time) || seenSpinner.LastDrawnAt != NthFrame.Seconds(time)); i++) Step(time, 1);
            Assert.Equal(NthFrame.Seconds(time), seenSpinner.LastDrawnAt);
            Assert.Equal(NthFrame.Seconds(time), hiddenSpinner.LastDrawnAt);
            var restored = RenderedPixels.Capture(hiddenWindow); var reference = RenderedPixels.Capture(seenWindow);
            var differing = 0;
            for (var y = 0; y < restored.Height; y++)
                for (var x = 0; x < restored.Width; x++)
                    if (restored.At(x, y) != reference.At(x, y)) differing++;
            Assert.Equal(0, differing);
            Assert.True(hiddenSpinner.FrameRequests > 0);
        }
        finally
        {
            hiddenWindow.Close(); seenWindow.Close(); Dispatcher.UIThread.RunJobs(); WindowPresence.TestFactory = null;
            RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
    });
}
