using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Armilla;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port.Armilla;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-dextra-smooth-2")]
public sealed class DextraSmoothAvailabilityTests(HeadlessSession headless)
{
    [Fact]
    public void A_covered_window_stops_marks_immediately_and_wakes_on_availability() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        AmbientWindowState? availability = null;
        AmbientWindowState.TestFactory = window => availability = new AmbientWindowState(window, test: true);
        var mark = new ArmillaMark { MarkSize = 176, State = ArmillaState.Thinking, Kind = ArmillaKind.Live };
        var window = new Window { Width = 220, Height = 220, Content = mark };
        try
        {
            window.Show(); Pump();
            Assert.NotNull(availability);
            availability.SetAvailable(false); Pump();
            var frames = mark.FramesDrawn;
            for (var i = 0; i < 60; i++) Pump();
            Assert.False(mark.Ticking);
            Assert.Equal(frames, mark.FramesDrawn);
            mark.State = ArmillaState.Working; Pump(); Pump();
            Assert.Equal(frames, mark.FramesDrawn);
            availability.SetAvailable(true); Pump(); Pump();
            Assert.True(mark.FramesDrawn > frames);
            Assert.True(mark.Ticking);
        }
        finally { window.Close(); Pump(); AmbientWindowState.TestFactory = null; }
        void Pump()
        {
            clock.Advance(TimeSpan.FromMilliseconds(17));
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
        }
    });

    [Fact]
    public void Astur_marks_use_the_same_event_driven_window_availability() => headless.OnUiThread(() =>
    {
        AmbientWindowState? availability = null;
        AmbientWindowState.TestFactory = window => availability = new AmbientWindowState(window, test: true);
        var window = new Window { Width = 220, Height = 220, Content = new AsturMark { MarkSize = 20, State = AsturMarkState.Thinking } };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.NotNull(availability);
            availability.SetAvailable(false); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.False(((AsturMark)window.Content).Ticking);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); AmbientWindowState.TestFactory = null; }
    });

    [Fact]
    public void Detaching_the_last_mark_releases_the_window_sleep_timer() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider();
        using var time = SwiftClock.Provider.Override(clock);
        var window = new Window { Width = 100, Height = 100, Content = new ArmillaMark() };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            ArmillaWindowWatch.For(window).Focused(false);
            Assert.Equal(1, clock.ActiveTimers);
            window.Content = null; Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, clock.ActiveTimers);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
