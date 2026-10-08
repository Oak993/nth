using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.ComposerLights;
using Marrow.UI.Port.Voice.Orb;
using Marrow.UI.Tests.Fixtures;
using Marrow.UI.Tests.Port.Voice;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-248")]
public sealed class AmbientMotionViewTests(HeadlessSession headless)
{
    [Fact]
    public void Orb_and_light_stop_requesting_when_the_window_is_unavailable() => Check(hidden: true);
    [Fact]
    public void Orb_and_light_stop_after_twenty_seconds_and_resume_on_input() => Check(hidden: false);

    [Fact]
    public void Disposing_one_controller_twice_keeps_the_other_windows_activity_source_alive() => headless.OnUiThread(() =>
    {
        AmbientWindowState.TestFactory = w => new(w, true);
        var host = new Border(); var window = new Window { Content = host };
        try
        {
            window.Show(); Tick();
            var state = AmbientWindowState.Acquire(host)!;
            var first = new AmbientMotionController(state);
            using var second = new AmbientMotionController(AmbientWindowState.Acquire(host)!);
            first.Dispose(); first.Dispose();
            var remaining = AmbientWindowState.Acquire(host)!;
            using var third = new AmbientMotionController(remaining);
            Assert.Same(state, remaining);
        }
        finally { window.Close(); Tick(); AmbientWindowState.TestFactory = null; }
    });

    [Fact]
    public void Returning_to_foreground_preserves_the_last_actual_input() => headless.OnUiThread(() =>
    {
        var window = new Window();
        var state = new AmbientWindowState(window, true);
        state.Touch(123);
        state.SetAvailable(false);
        state.SetAvailable(true);
        Assert.Equal(123, state.LastInput);
    });
    private void Check(bool hidden) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        using var orbMotion = new VoiceOrbMotion(); using var lightMotion = new ComposerLightMotion();
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => { state = new(w, true); state.Touch(0); return state; };
        var orb = new VoiceOrbView();
        var light = new ComposerLightView { ComposerRect = new Rect(20, 100, 160, 50) };
        var window = new Window { Width = 220, Height = 180, Content = new Panel { Children = { light, orb } } };
        try
        {
            window.Show(); Tick();
            Assert.NotNull(state);
            Assert.True(orb.Handler!.IsAnimating); Assert.True(light.Handler!.IsAnimating);
            if (hidden) state!.SetAvailable(false);
            else { clock.Advance(TimeSpan.FromSeconds(20.7)); state!.Touch(0); }
            Tick();
            Assert.False(orb.Handler.IsAnimating); Assert.False(light.Handler.IsAnimating);
            Assert.Equal(0, orb.Handler.AmbientSpeed); Assert.Equal(0, light.Handler.AmbientSpeed);
            var frames = (orb.Handler.Frames, light.Handler.Frames);
            clock.Advance(TimeSpan.FromSeconds(30)); Tick();
            Assert.Equal(frames, (orb.Handler.Frames, light.Handler.Frames));
            if (hidden) state!.SetAvailable(true);
            state!.Touch(clock.GetTimestamp() / (double)clock.TimestampFrequency);
            Tick();
            Assert.True(orb.Handler.IsAnimating); Assert.True(light.Handler.IsAnimating);
            Assert.Equal(0, orb.Handler.AmbientSpeed); Assert.Equal(0, light.Handler.AmbientSpeed);
            var resumed = clock.GetTimestamp() / (double)clock.TimestampFrequency;
            clock.Advance(TimeSpan.FromSeconds(.3)); state.Touch(resumed); Tick();
            Assert.Equal(.5, orb.Handler.AmbientSpeed, 8); Assert.Equal(.5, light.Handler.AmbientSpeed, 8);
        }
        finally { window.Close(); Tick(); AmbientWindowState.TestFactory = null; }
    });
    private static void Tick()
    {
        for (var i = 0; i < 4; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
        Dispatcher.UIThread.RunJobs();
    }
}
