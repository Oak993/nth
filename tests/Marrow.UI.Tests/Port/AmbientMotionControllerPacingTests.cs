using Avalonia.Controls;
using Marrow.Testing;
using Marrow.Core.Port;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-frame-pacing (3): the controller waits on refreshes, never on a timer; interactions run at the display's rate; it stops when idle
// or unseen. (Formerly AmbientRefreshPacerTests.Controller: a test class must be top level, R-TEST-2.)
[Collection("Headless")]
[Trait("Decision", "ED-NEW-framepacing-1")]
public sealed class AmbientMotionControllerPacingTests(HeadlessSession headless)
{
    [Fact]
    public void Pacing_waits_on_refreshes_never_a_timer_runs_interactions_at_display_rate_and_stops_when_idle_or_unseen() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var state = new AmbientWindowState(new Window(), true);
        state.Touch(0);
        using var ambient = new AmbientMotionController(state) { Pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / 240.0)) };
        var paced = new List<AmbientMotionController.Paced>();
        for (var k = 0; k < 16; k++) paced.Add(ambient.Pace(k + 1, k / 240.0, false));
        Assert.Equal(4, paced.Count(p => p == AmbientMotionController.Paced.Go));
        Assert.Equal(12, paced.Count(p => p == AmbientMotionController.Paced.Wait));
        Assert.Equal(0, clock.ActiveTimers);
        Assert.True(ambient.Request(16 / 240.0, false));
        for (var k = 16; k < 24; k++) Assert.Equal(AmbientMotionController.Paced.Go, ambient.Pace(k + 1, k / 240.0, true));
        // after the interaction the slots go on from the count: refreshes 24 and 28 are slots, 25-27 are not
        Assert.Equal(AmbientMotionController.Paced.Go, ambient.Pace(25, 24 / 240.0, false));
        Assert.Equal(AmbientMotionController.Paced.Wait, ambient.Pace(26, 25 / 240.0, false));
        Assert.Equal(AmbientMotionController.Paced.Stop, ambient.Pace(27, 20.7, false));
        Assert.False(ambient.Request(20.7, false));
        state.Touch(21);
        Assert.True(ambient.Request(21, false));
        state.SetAvailable(false);
        Assert.Equal(AmbientMotionController.Paced.Stop, ambient.Pace(28, 21.1, true));
        Assert.Equal(0, clock.ActiveTimers);
    });
}
