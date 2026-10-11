using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Composer;

// ED-NEW-lean-9 (lean pass L9), the "stop when not drawn" guard the field copied from NthMotionHandler: the next frame update is
// asked for by a draw, so a field that is not drawn stops asking, and it must start again, at once and at every refresh, when it is
// drawn again (no new message) or sent a new frame. Not drawn here: moved outside the window, where the compositor still ticks the
// field's frame updates (asserted: the update its last draw asked for comes) but draws nothing, as for a window that is not
// composed; and hidden (IsVisible false), where it is ticked and not drawn too. Disabling the guard (asking at every update) fails
// all four tests (checked once in the review round). Headless and deterministic
// (R-TEST-5): the field's clock is the test's ManualTimeProvider, advanced 1/240 s before each forced compositor tick. A field that
// stops and never starts again is the risk this guards.
[Collection("Headless")]
public sealed class ComposerWorkingDotsStopGuardTests(HeadlessSession headless)
{
    private const double Rate = 240;

    private static void Run(Action<TestRuntime, DotFieldControl, DotFieldCompositionHandler> body)
    {
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
            AssertDrawsEveryTick(runtime, field, handler, 10, "while shown");
            body(runtime, field, handler);
        }
        finally
        {
            window.Content = null; window.Close(); Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal(0, frames.Pending); // never a UI-thread frame
    }

    private static void Tick(TestRuntime runtime)
    {
        runtime.Advance(TimeSpan.FromSeconds(1 / Rate));
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    private static long Drawn(DotFieldControl field) => field.LastRenderedFrame?.Now.Ticks ?? -1;

    // Each tick a frame update and a draw at the time the clock shows.
    private static void AssertDrawsEveryTick(TestRuntime runtime, DotFieldControl field, DotFieldCompositionHandler handler, int ticks, string when)
    {
        for (var i = 0; i < ticks; i++)
        {
            var updates = handler.Updates;
            Tick(runtime);
            Assert.True(handler.Updates == updates + 1, $"{when}: tick {i} had {handler.Updates - updates} frame updates, not 1");
            Assert.True(Drawn(field) == SwiftClock.Now.Ticks, $"{when}: tick {i} did not draw the field at the clock's time");
        }
    }

    private static void Place(DotFieldControl field, bool inside)
    {
        Avalonia.Controls.Canvas.SetLeft(field, inside ? 0 : 1000);
        Dispatcher.UIThread.RunJobs();
    }

    // Outside the window: the update the last draw asked for comes, with no draw after it, and the field asks for no more.
    private static void MoveOutUntilStopped(TestRuntime runtime, DotFieldControl field, DotFieldCompositionHandler handler)
    {
        var updates = handler.Updates;
        var drawn = Drawn(field);
        Place(field, inside: false);
        Tick(runtime);
        Tick(runtime);
        Tick(runtime);
        Assert.True(handler.Updates - updates == 1, $"a field outside the window had {handler.Updates - updates} frame updates in three ticks, not 1 (the setup must still tick it)");
        Assert.Equal(drawn, Drawn(field)); // not drawn
        Stays(runtime, field, handler, drawn);
    }

    // Hidden: the field is not drawn, and after the update its last draw asked for it asks for no more.
    private static void HideUntilStopped(TestRuntime runtime, DotFieldControl field, DotFieldCompositionHandler handler)
    {
        var drawn = Drawn(field);
        field.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Tick(runtime);
        Tick(runtime);
        Assert.Equal(drawn, Drawn(field));
        Stays(runtime, field, handler, drawn);
    }

    private static void Stays(TestRuntime runtime, DotFieldControl field, DotFieldCompositionHandler handler, long drawn)
    {
        var stopped = handler.Updates;
        for (var i = 0; i < 20; i++) Tick(runtime);
        Assert.True(handler.Updates == stopped, $"a field that is not drawn kept asking for frames: {handler.Updates - stopped} updates in 20 ticks");
        Assert.Equal(drawn, Drawn(field)); // and it was not drawn
        Assert.False(handler.IsStopped);
    }

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_field_that_is_not_drawn_stops_asking_for_frames_and_starts_again_when_drawn_with_no_new_message() => headless.OnUiThread(() => Run((runtime, field, handler) =>
    {
        MoveOutUntilStopped(runtime, field, handler);
        // Back inside: no message is sent (the size is the same), the compositor draws the field, and that draw asks again.
        Place(field, inside: true);
        Tick(runtime);
        Assert.True(Drawn(field) == SwiftClock.Now.Ticks, "the field was not drawn on the first refresh after it came back");
        AssertDrawsEveryTick(runtime, field, handler, 20, "back inside");
        // and it stops and starts again as often as it leaves and comes back
        MoveOutUntilStopped(runtime, field, handler);
        Place(field, inside: true);
        Tick(runtime);
        AssertDrawsEveryTick(runtime, field, handler, 10, "back inside a second time");
    }));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_new_frame_restarts_a_field_that_stopped_while_not_drawn() => headless.OnUiThread(() => Run((runtime, field, handler) =>
    {
        MoveOutUntilStopped(runtime, field, handler);
        // A new frame (an accent change, eased) while still not drawn: the field asks for an update again, and with no draw after
        // it stops again.
        var updates = handler.Updates;
        var drawn = Drawn(field);
        field.UpdateColor(new SwiftColor(0, 0.4, 1), ease: true);
        Dispatcher.UIThread.RunJobs();
        // the frame reaches the render server in the first tick and the update it asks for comes at the next
        Tick(runtime);
        Tick(runtime);
        Assert.True(handler.Updates == updates + 1, $"a new frame for a stopped field asked for {handler.Updates - updates} frame updates in two ticks, not 1");
        Stays(runtime, field, handler, drawn);
        // Back inside with a new frame: it draws at once and at every refresh, in the blend the new frame started.
        Place(field, inside: true);
        field.UpdateColor(new SwiftColor(0, 1, 0.4), ease: true);
        Dispatcher.UIThread.RunJobs();
        Tick(runtime);
        Assert.True(Drawn(field) == SwiftClock.Now.Ticks, "the field was not drawn on the first refresh after it came back with a new frame");
        Assert.True(field.LastRenderedFrame!.Blending);
        AssertDrawsEveryTick(runtime, field, handler, 20, "back inside with a new frame");
    }));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_field_outside_the_window_for_a_long_stop_draws_at_the_clock_time_when_back() => headless.OnUiThread(() => Run((runtime, field, handler) =>
    {
        MoveOutUntilStopped(runtime, field, handler);
        runtime.Advance(TimeSpan.FromSeconds(3));
        Place(field, inside: true);
        Tick(runtime);
        Assert.True(Drawn(field) == SwiftClock.Now.Ticks, "the field back after 3 s was not drawn at the clock's time");
        AssertDrawsEveryTick(runtime, field, handler, 5, "back after 3 s");
    }));

    [Fact]
    [Trait("Decision", "ED-NEW-lean-9")]
    public void A_hidden_field_starts_again_when_shown_after_a_long_stop_at_the_clock_time() => headless.OnUiThread(() => Run((runtime, field, handler) =>
    {
        HideUntilStopped(runtime, field, handler);
        runtime.Advance(TimeSpan.FromSeconds(3));
        field.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Tick(runtime);
        Assert.True(Drawn(field) == SwiftClock.Now.Ticks, "the field shown again after 3 s was not drawn at the clock's time");
        AssertDrawsEveryTick(runtime, field, handler, 20, "shown again after 3 s");
    }));
}
