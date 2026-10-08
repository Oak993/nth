using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

[Collection("Headless")]
[Trait("Decision", "ED-248")]
public sealed class AmbientTimelineTests(HeadlessSession headless)
{
    [Fact]
    public void Decorative_render_timeline_caps_requests_stops_idle_preserves_phase_and_exempts_real_transitions() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var stamp = RenderFrameSource.Timestamp; var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => { state = new(w, true); state.Touch(clock.Elapsed.TotalSeconds); return state; };
        RenderFrameSource.Timestamp = () => (long)(clock.Elapsed.TotalSeconds * Stopwatch.Frequency);
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 165);
        var host = new Border(); var window = new Window { Width = 100, Height = 100, Content = host };
        var interaction = false; var draws = 0;
        using var frames = FrameClock.Source.Override(new AvaloniaFrameSource(window));
        var timeline = new AnimationTimeline(() => new(MinimumInterval: .001, Ambient: true), _ => draws++, host, () => interaction);
        void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Step(int count) { for (var i = 0; i < count; i++) { clock.Advance(TimeSpan.FromSeconds(1.0 / 165)); Tick(); } }
        try
        {
            window.Show(); Tick(); timeline.Appear(); Tick();
            var source = Assert.IsType<RenderFrameSource>(timeline.Source);
            Step(330); Assert.InRange(source.Ticks, 50, 121);
            Step(3300); var held = source.Ticks; var phase = timeline.Date;
            Step(165); Assert.Equal(held, source.Ticks); Assert.Equal(phase, timeline.Date);
            interaction = true; state!.Touch(clock.Elapsed.TotalSeconds); Tick();
            held = source.Ticks; Step(165); Assert.InRange(source.Ticks - held, 150, 166);
            interaction = false;
            timeline.Disappear(); var carry = timeline.Date;
            Step(4950); timeline.Appear(); Tick(); Assert.Equal(carry, timeline.Date);
            state!.SetAvailable(false); Tick(); held = Assert.IsType<RenderFrameSource>(timeline.Source).Ticks;
            Step(165); Assert.Equal(held, Assert.IsType<RenderFrameSource>(timeline.Source).Ticks);
        }
        finally
        {
            timeline.Disappear(); window.Close(); Tick(); AmbientWindowState.TestFactory = null;
            RenderFrameSource.Timestamp = stamp; RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
        Assert.Equal(0, clock.ActiveTimers);
    });
}
