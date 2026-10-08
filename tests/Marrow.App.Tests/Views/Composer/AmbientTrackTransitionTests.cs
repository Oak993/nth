using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

[Collection("Headless")]
[Trait("Decision", "ED-248")]
public sealed class AmbientTrackTransitionTests(HeadlessSession headless)
{
    [Fact]
    public void Delayed_attach_and_idle_do_not_skip_or_hold_a_colour_fade_or_fast_transition() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var stamp = RenderFrameSource.Timestamp; var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => { state = new(w, true); state.Touch(clock.Elapsed.TotalSeconds); return state; };
        RenderFrameSource.Timestamp = () => (long)(clock.Elapsed.TotalSeconds * Stopwatch.Frequency);
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 165);
        var effect = new TrackEffect(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple));
        clock.Advance(TimeSpan.FromSeconds(2));
        var window = new Avalonia.Controls.Window { Width = 300, Height = 100 };
        using var source = FrameClock.Source.Override(new AvaloniaFrameSource(window));
        void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Step(int count) { for (var i = 0; i < count; i++) { clock.Advance(TimeSpan.FromSeconds(1.0 / 165)); Tick(); } }
        try
        {
            window.Content = effect; window.Show(); window.Activate(); Tick();
            effect.PrepareScene(new Size(240, 32)); Step(3500);
            var held = effect.Time; Step(4950); Assert.Equal(held, effect.Time);
            state!.Touch(clock.Elapsed.TotalSeconds);
            effect.FadeFrom(LiquidSpec.For(effect.Look), StrikeTint.For(effect.Look.Brand), SwiftDate.Now, .6);
            effect.SetLook(new TrackLook(TrackLook.Kind.Fast, EffortBrand.Claude));
            effect.PrepareScene(new Size(240, 32)); Tick();
            Assert.InRange(effect.FadeAt(effect.Time), 0, .02);
            Step(50); Assert.InRange(effect.FadeAt(effect.Time), .4, .65);
            Step(55); Assert.Equal(1, effect.FadeAt(effect.Time));
            Assert.InRange(effect.TimeAt(SwiftDate.Now) - effect.Time, 0, .02);
            held = effect.Time; window.Content = null; Tick(); clock.Advance(TimeSpan.FromSeconds(30));
            effect.FadeFrom(LiquidSpec.For(effect.Look), StrikeTint.For(effect.Look.Brand), SwiftDate.Now, .6);
            effect.SetLook(new TrackLook(TrackLook.Kind.Plain, EffortBrand.Purple));
            effect.PrepareScene(new Size(240, 32));
            window.Content = effect; Tick(); Assert.Equal(held, effect.Time, 6);
            Assert.InRange(effect.FadeAt(effect.Time), 0, .02);
            Step(50); Assert.InRange(effect.FadeAt(effect.Time), .4, .65);
        }
        finally
        {
            window.Close(); Tick(); AmbientWindowState.TestFactory = null;
            RenderFrameSource.Timestamp = stamp; RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
        Assert.Equal(0, clock.ActiveTimers);
    });
}
