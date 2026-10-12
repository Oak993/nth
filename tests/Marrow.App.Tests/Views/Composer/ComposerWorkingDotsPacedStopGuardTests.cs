using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-9 (pacing): the working dots on a 240 Hz display (RenderFrameSource.PeriodSource 1/240 s, the field's clock the test's
// ManualTimeProvider advanced 1/240 s before each forced compositor tick). The field has a frame update at every refresh and draws on
// exactly one refresh of each 60 Hz slot of the shared pacer (every 4th refresh, one phase), each draw at the clock's time of its
// refresh. The "stop when not drawn" guard holds through the pacing: moved outside the window, the field's updates stop within one
// slot (the paced updates ask for the next one until the slot's update, which is not drawn), and back inside it is drawn on the first
// refresh and paced again.
[Collection("Headless")]
public sealed class ComposerWorkingDotsPacedStopGuardTests(HeadlessSession headless)
{
    private const double Rate = 240;

    private static void Tick(TestRuntime runtime)
    {
        runtime.Advance(TimeSpan.FromSeconds(1 / Rate));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static long Drawn(DotFieldControl field) => field.LastRenderedFrame?.Now.Ticks ?? -1;

    // For `ticks` refreshes: one frame update each, and a draw on one refresh of each 60 Hz slot, each at the clock's time: the first
    // within the first four refreshes (after a pause the pacer's first refresh is a slot of its own, then the slots of the shared
    // refresh count follow), every later one 4 refreshes after the one before, and none missed at the end.
    private static void AssertPacedDraws(TestRuntime runtime, DotFieldControl field, DotFieldCompositionHandler handler, int ticks, string when)
    {
        var drawnAt = new List<int>();
        for (var i = 0; i < ticks; i++)
        {
            var updates = handler.Updates;
            var before = Drawn(field);
            Tick(runtime);
            Assert.True(handler.Updates == updates + 1, $"{when}: tick {i} had {handler.Updates - updates} frame updates, not 1");
            if (Drawn(field) == before) continue;
            Assert.True(Drawn(field) == SwiftClock.Now.Ticks, $"{when}: tick {i} drew the field at another time than the clock's");
            drawnAt.Add(i);
        }
        var at = string.Join(", ", drawnAt);
        Assert.True(drawnAt.Count >= 2 && drawnAt[0] <= 3, $"{when}: the field drew on refreshes {at} of {ticks}");
        Assert.True(drawnAt[1] - drawnAt[0] is >= 1 and <= 4, $"{when}: the first two draws are not within one slot (at {at})");
        Assert.True(drawnAt.Skip(2).Select((i, k) => i - drawnAt[k + 1]).All(gap => gap == 4), $"{when}: the draws are not every 4th refresh (at {at})");
        Assert.True(drawnAt[^1] >= ticks - 4, $"{when}: the field missed the last slot (at {at})");
    }

    private static void Place(DotFieldControl field, bool inside)
    {
        Avalonia.Controls.Canvas.SetLeft(field, inside ? 0 : 1000);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_paced_field_draws_once_a_60_Hz_slot_stops_when_not_drawn_and_starts_again_when_drawn() => headless.OnUiThread(() =>
    {
        var period = RenderFrameSource.PeriodSource;
        RenderFrameSource.PeriodSource = () => TimeSpan.FromSeconds(1 / Rate);
        using var runtime = new TestRuntime();
        runtime.Advance(TimeSpan.FromSeconds(10));
        using var provider = SwiftClock.Provider.Override(runtime.Time);
        using var executor = MainActor.Executor.Override(AvaloniaMainExecutor.Instance);
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        var field = new DotFieldControl(new SwiftColor(1, 0, 0), animated: true, sweeps: false) { Width = 240, Height = 24 };
        var canvas = new Avalonia.Controls.Canvas { Children = { field } };
        var window = new Avalonia.Controls.Window { Width = 240, Height = 24, Background = Brushes.Black, Content = canvas };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            for (var i = 0; i < 10; i++) Tick(runtime);
            var handler = Assert.IsType<DotFieldCompositionHandler>(field.CompositionHandler);
            AssertPacedDraws(runtime, field, handler, 24, "while shown");
            for (var round = 0; round < 2; round++)
            {
                // outside: the paced updates go on to the slot's update at most (4), which is not drawn, and then stop
                var updates = handler.Updates;
                var drawn = Drawn(field);
                Place(field, inside: false);
                for (var i = 0; i < 4; i++) Tick(runtime);
                Assert.True(handler.Updates - updates is >= 1 and <= 4, $"round {round}: a field outside the window had {handler.Updates - updates} frame updates in its first four ticks, not 1 to 4");
                var stopped = handler.Updates;
                for (var i = 0; i < 20; i++) Tick(runtime);
                Assert.True(handler.Updates == stopped, $"round {round}: a field that is not drawn kept asking for frames: {handler.Updates - stopped} updates in 20 ticks");
                Assert.Equal(drawn, Drawn(field));
                Assert.False(handler.IsStopped);
                // back inside (no new message): drawn on the first refresh, then paced again
                Place(field, inside: true);
                Tick(runtime);
                Assert.True(Drawn(field) == SwiftClock.Now.Ticks, $"round {round}: the field was not drawn on the first refresh after it came back");
                AssertPacedDraws(runtime, field, handler, 24, $"round {round}: back inside");
            }
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
            RenderFrameSource.PeriodSource = period;
        }
        Assert.Equal(0, frames.Pending); // never a UI-thread frame
    });
}
