using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-frame-pacing: two ambient render timelines of one window (the effort track and its knob) draw once per 60 Hz
// slot, on the same refreshes, evenly, at 60/120/144/165/240 Hz, through the real compositor ticks; no timer.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-frame-pacing")]
public sealed class AmbientPacedTimelineTests(HeadlessSession headless)
{
    [Theory]
    [InlineData(60.0)]
    [InlineData(120.0)]
    [InlineData(144.0)]
    [InlineData(165.0)]
    [InlineData(240.0)]
    public void Track_and_knob_timelines_update_together_once_per_sixty_hertz_slot(double rate) => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var stamp = RenderFrameSource.Timestamp; var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => { state = new(w, true); state.Touch(clock.Elapsed.TotalSeconds); return state; };
        RenderFrameSource.Timestamp = () => (long)(clock.Elapsed.TotalSeconds * Stopwatch.Frequency);
        RenderFrameSource.RefreshSource = () => null; RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / rate);
        var trackHost = new Border { Width = 200, Height = 20 }; var knobHost = new Border { Width = 20, Height = 20 };
        var window = new Window { Width = 240, Height = 60, Content = new Panel { Children = { trackHost, knobHost } } };
        long index = -1; var track = new List<long>(); var knob = new List<long>();
        using var frames = FrameClock.Source.Override(new AvaloniaFrameSource(window));
        var trackLine = new AnimationTimeline(() => new(MinimumInterval: 1.0 / 60, Ambient: true), _ => track.Add(index), trackHost, () => false);
        var knobLine = new AnimationTimeline(() => new(MinimumInterval: 1.0 / 60, Ambient: true), _ => knob.Add(index), knobHost, () => false);
        void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            window.Show(); Tick(); trackLine.Appear();
            for (index = 0; index < 3; index++) { clock.Advance(TimeSpan.FromSeconds(1 / rate)); Tick(); }
            knobLine.Appear(); // the knob appears a few refreshes into a slot
            var refreshes = (long)Math.Round(rate * 3);
            for (index = 3; index < 3 + rate + refreshes; index++) { clock.Advance(TimeSpan.FromSeconds(1 / rate)); Tick(); }
            var from = 3 + (long)rate; // after a second of warm-up
            var trackSlots = track.Where(k => k >= from).ToList(); var knobSlots = knob.Where(k => k >= from).ToList();
            Assert.Equal(trackSlots, knobSlots); // the same refreshes: one render for both
            Assert.Equal(180, trackSlots.Count);
            var perSlot = rate / 60;
            Assert.All(trackSlots.Zip(trackSlots.Skip(1), (a, b) => b - a), gap => Assert.InRange(gap, (long)Math.Floor(perSlot), (long)Math.Ceiling(perSlot)));
            for (var i = 0; i + 5 < trackSlots.Count; i++)
                Assert.InRange(trackSlots[i + 5] - trackSlots[i], Math.Floor(5 * perSlot), Math.Ceiling(5 * perSlot));
            Assert.Equal(0, clock.ActiveTimers); // paced on the refreshes, never on a timer
        }
        finally
        {
            trackLine.Disappear(); knobLine.Disappear(); window.Close(); Tick(); AmbientWindowState.TestFactory = null;
            RenderFrameSource.Timestamp = stamp; RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
    });
}
