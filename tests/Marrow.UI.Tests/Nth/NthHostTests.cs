using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Nth;

// OD-0136 amendment 2 (ED-NEW-nth-motion): the primitives draw on the render thread through their composition visual, show up
// in a real rendered frame, and stop counting as moving when hidden, finished or removed (the idle GPU warm-up waits on them).
[Collection("Headless")]
public sealed class NthHostTests(HeadlessSession headless)
{
    private static void Pump(int ticks = 4)
    {
        for (var i = 0; i < ticks; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_shown_spinner_draws_through_its_composition_visual_and_stops_when_hidden_or_removed() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var spinner = new NthSpinner { Width = 16, Height = 16 };
        var window = new Window { Width = 60, Height = 60, Content = spinner, Background = Avalonia.Media.Brushes.Black };
        window.Show();
        try
        {
            time.Advance(TimeSpan.FromSeconds(.5));
            Pump();
            Assert.NotNull(spinner.Frame);
            Assert.Equal(16, spinner.Frame!.Size.Width);
            Assert.True(spinner.FramesDrawn > 0, "the render thread drew it");
            Assert.True(spinner.IsMoving);
            Assert.True(NthVisualHost.AnyAnimating());
            var pixels = RenderedPixels.Capture(window);
            var ink = 0;
            for (var y = 0; y < pixels.Height; y++) for (var x = 0; x < pixels.Width; x++) if (pixels.Level(x, y) > 40) ink++;
            Assert.True(ink > 20, $"the spinner is in the rendered frame ({ink} px)");
            // (other tests' windows may hold primitives of their own, so "nothing else moves" is read on this control)
            spinner.IsVisible = false;
            Assert.False(spinner.IsMoving);
            spinner.IsVisible = true;
            Assert.True(spinner.IsMoving);
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.Null(spinner.Frame);
            Assert.False(spinner.IsMoving);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_finished_ring_stops_moving_and_the_activity_check_allocates_nothing() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var ring = new NthRing { Width = 18, Height = 18 };
        var window = new Window { Width = 60, Height = 60, Content = ring };
        window.Show();
        try
        {
            Pump();
            ring.Value = .5;
            Assert.True(ring.IsMoving, "the value springs");
            Assert.True(NthVisualHost.AnyAnimating());
            time.Advance(TimeSpan.FromSeconds(2));
            Assert.False(ring.IsMoving, "settled");
            ring.IsDone = true;
            Assert.True(ring.IsMoving, "the disc and tick");
            time.Advance(TimeSpan.FromSeconds(3));
            Assert.False(ring.IsMoving);
            for (var i = 0; i < 2000; i++) { NthVisualHost.AnyAnimating(); _ = ring.IsMoving; }
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) { NthVisualHost.AnyAnimating(); _ = ring.IsMoving; }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void A_status_slot_created_done_shows_its_tick_in_place_and_a_live_change_animates() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var created = new NthStatusMark { Status = NthStatus.Done };
        var live = new NthStatusMark { Status = NthStatus.Working };
        var window = new Window { Width = 80, Height = 40, Content = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { created, live } } };
        window.Show();
        try
        {
            Pump();
            var first = Assert.IsType<NthStatusState>(created.Frame!.State);
            Assert.True(double.IsNegativeInfinity(first.ChangedAt), "no replay for a row shown already done");
            time.Advance(TimeSpan.FromSeconds(1));
            live.Status = NthStatus.Done;
            var changed = Assert.IsType<NthStatusState>(live.Frame!.State);
            Assert.Equal(NthStatus.Working, changed.Previous);
            Assert.False(double.IsNegativeInfinity(changed.ChangedAt));
            Assert.True(live.IsMoving);
            time.Advance(TimeSpan.FromSeconds(1));
            Assert.False(live.IsMoving, "the tick has drawn");
            Assert.False(created.IsMoving);
        }
        finally { window.Close(); }
    });

    [Fact]
    [Trait("Decision", "OD-0136")]
    public void The_shine_text_captures_its_own_glyphs_and_the_light_stops_when_calm_or_done() => headless.OnUiThread(() =>
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        var label = new NthShineText { Text = "Reading the diff" };
        var window = new Window { Width = 200, Height = 40, Content = label };
        window.Show();
        try
        {
            Pump();
            var state = Assert.IsType<NthShineState>(label.Frame!.State);
            Assert.NotEmpty(state.Runs);
            Assert.True(state.Box.Width > 40, $"the word's box {state.Box}");
            Assert.True(label.IsMoving);
            label.IsShining = false;
            Assert.False(label.IsMoving);
            label.IsShining = true;
            Assert.True(label.IsMoving);
            EnvironmentValues.SetAccessibilityReduceMotion(label, true);
            Assert.False(label.IsMoving, "calm: no light");
        }
        finally { window.Close(); }
    });
}
