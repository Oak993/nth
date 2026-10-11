using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Port;

// ED-NEW-lean-6 (d): render frame sources ticking in the same refresh read DWM's timing once and share it. Two sources run
// every refresh of a 240 Hz display for one simulated second; the budget is DWM reads per refresh.
[Collection("Headless")]
[Trait("Decision", "ED-NEW-lean-6")]
public sealed class RenderFrameSourceSharedTimingTests(HeadlessSession headless)
{
    // DWM timing reads per refresh with two sources ticking. Measured (Debug, headless): 2.0 before the fix (one
    // per source), so the first limit was 3.0; 1.0 after, the limit now 1.3.
    private const double ReadsPerRefresh = 1.3;

    [Fact]
    public void Two_sources_in_one_refresh_share_one_timing_read() => headless.OnUiThread(() =>
    {
        var clock = new ManualTimeProvider(); using var time = SwiftClock.Provider.Override(clock);
        var stamp = RenderFrameSource.Timestamp; var refresh = RenderFrameSource.RefreshSource; var period = RenderFrameSource.PeriodSource;
        var reads = 0;
        RenderFrameSource.Timestamp = () => (long)(clock.Elapsed.TotalSeconds * Stopwatch.Frequency);
        RenderFrameSource.RefreshSource = () => { reads++; return null; };
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1.0 / 240);
        var left = new Border { Width = 20, Height = 20 }; var right = new Border { Width = 20, Height = 20 };
        var window = new Window { Width = 100, Height = 100, Content = new StackPanel { Children = { left, right } } };
        RenderFrameSource? a = null, b = null;
        void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
        void Step(int count) { for (var i = 0; i < count; i++) { clock.Advance(TimeSpan.FromSeconds(1.0 / 240)); Tick(); } }
        try
        {
            window.Show(); Tick();
            a = RenderFrameSource.Create(left)!; b = RenderFrameSource.Create(right)!;
            Action<TimeSpan>? onA = null, onB = null;
            onA = _ => a.RequestFrame(onA!); onB = _ => b.RequestFrame(onB!);
            a.RequestFrame(onA); b.RequestFrame(onB);
            Step(12);
            var readsBefore = reads; var ticksBefore = a.Ticks; var otherBefore = b.Ticks;
            Step(240);
            var refreshes = a.Ticks - ticksBefore;
            Assert.True(refreshes >= 200, $"the sources ticked every refresh ({refreshes})");
            Assert.Equal(refreshes, b.Ticks - otherBefore);
            var perRefresh = (reads - readsBefore) / (double)refreshes;
            Assert.InRange(perRefresh, 0, ReadsPerRefresh);
        }
        finally
        {
            a?.Dispose(); b?.Dispose(); window.Close(); Tick();
            RenderFrameSource.Timestamp = stamp; RenderFrameSource.RefreshSource = refresh; RenderFrameSource.PeriodSource = period;
        }
    });
}
