using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth.Beads;
using Marrow.UI.Tests.Fixtures;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Tests.Nth;

// The control on a real (headless) composition visual: it measures to the envelope, posts only on change, and asks for no frames
// while still, hidden or minimised (the window state's Available is false when the window is minimised or not shown).
[Collection("Headless")]
[Trait("Decision", "ED-NEW-calcmerge-2")] // ED-NEW-calculi-wu4-1
public sealed class NthBeadsControlTests(HeadlessSession headless)
{
    private static void Pump(ManualTimeProvider time, int ticks)
    {
        for (var i = 0; i < ticks; i++)
        {
            time.Advance(TimeSpan.FromSeconds(1 / 60.0));
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Shown(Action<NthBeadsControl, Window, AmbientWindowState, ManualTimeProvider> body, bool calm = false)
    {
        var time = new ManualTimeProvider(); using var clock = SwiftClock.Provider.Override(time);
        AmbientWindowState? state = null;
        AmbientWindowState.TestFactory = w => state = new(w, true);
        var beads = new NthBeadsControl { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 80, Height = 80, Content = beads, Background = Avalonia.Media.Brushes.Black };
        if (calm) EnvironmentValues.SetAccessibilityReduceMotion(window, true);
        window.Show();
        try
        {
            Pump(time, 2);
            body(beads, window, state!, time);
        }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); AmbientWindowState.TestFactory = null; }
    }

    [Fact]
    public void The_control_measures_to_the_motion_envelope() => headless.OnUiThread(() => Shown((beads, _, _, _) =>
    {
        // 18.9 x 22.1 DIP at 18 (layout rounding takes it up to the next whole pixel; the beads are centred in it)
        var envelope = NthBeadsPainter.EnvelopeSize(18);
        Assert.Equal(18.93, envelope.Width, 2);
        Assert.Equal(22.11, envelope.Height, 2);
        Assert.InRange(beads.DesiredSize.Width, envelope.Width, envelope.Width + 1);
        Assert.InRange(beads.DesiredSize.Height, envelope.Height, envelope.Height + 1);
        Assert.Equal(beads.DesiredSize, beads.Bounds.Size);
        beads.GlyphSize = 12;
        beads.UpdateLayout();
        Assert.InRange(beads.DesiredSize.Width, NthBeadsPainter.EnvelopeSize(12).Width, NthBeadsPainter.EnvelopeSize(12).Width + 1);
        Assert.Equal(2, beads.Current.Every); // 30 Hz at 12 DIP
    }));

    [Fact]
    public void Frames_are_posted_only_when_something_changes() => headless.OnUiThread(() => Shown((beads, _, state, time) =>
    {
        var posted = beads.FramesPosted;
        beads.SetState(NthBeadsState.Thinking); // already thinking
        Assert.Equal(posted, beads.FramesPosted);
        beads.SetState(NthBeadsState.Tool);
        Assert.Equal(posted + 1, beads.FramesPosted);
        beads.SetState(NthBeadsState.Tool);
        Assert.Equal(posted + 1, beads.FramesPosted);
        beads.Step();
        Assert.Equal(posted + 2, beads.FramesPosted);
        state.Touch(5); // input on the window is not a change of the beads
        Pump(time, 6); // nor are frames: nothing is posted per frame
        Assert.Equal(posted + 2, beads.FramesPosted);
        beads.Finish(); // no turn yet: nothing to end
        Assert.Equal(posted + 2, beads.FramesPosted);
    }));

    [Fact]
    public void A_turn_draws_on_slots_and_a_hidden_or_minimised_window_asks_for_nothing() => headless.OnUiThread(() => Shown((beads, _, state, time) =>
    {
        beads.Enter(NthBeadsEntrance.Ortus, "t1");
        Pump(time, 40);
        var driver = beads.Painter!.Driver;
        Assert.True(beads.FramesDrawn > 10, $"drawn {beads.FramesDrawn}");
        Assert.True(driver.Moving);
        Assert.True(beads.IsMoving);
        // advanced on the slots, at the slots' times
        Assert.Equal(driver.EngineTime(time.GetTimestamp() / (double)time.TimestampFrequency), driver.Engine!.Clock, 9);
        state.SetAvailable(false); // minimised, hidden, covered or not in front
        Pump(time, 2);
        var requests = beads.FrameRequests;
        var clock = driver.Engine.Clock;
        Pump(time, 60);
        Assert.Equal(requests, beads.FrameRequests);
        Assert.Equal(clock, driver.Engine.Clock);
        Assert.False(beads.IsMoving);
        beads.Finish(); // the turn ends while hidden: gone at once
        Pump(time, 2);
        Assert.Equal(NthBeadsMode.Gone, driver.Engine.Mode);
        Assert.Equal(requests, beads.FrameRequests);
        state.SetAvailable(true);
        beads.Enter(NthBeadsEntrance.Ortus, "t2");
        Pump(time, 20);
        Assert.True(beads.FrameRequests > requests);
        Assert.Equal(NthBeadsMode.Running, driver.Engine.Mode);
        beads.IsVisible = false;
        Pump(time, 2);
        requests = beads.FrameRequests;
        Pump(time, 30);
        Assert.Equal(requests, beads.FrameRequests);
    }));

    [Fact]
    public void A_still_formation_asks_for_no_frames() => headless.OnUiThread(() => Shown((beads, _, _, time) =>
    {
        beads.SetState(NthBeadsState.Tool);
        beads.Enter(NthBeadsEntrance.Ortus, "t1");
        Pump(time, 40); // Reduce Motion: a 0.24 s pop, then Pedes held still
        Assert.True(beads.Current.Calm);
        Assert.False(beads.Painter!.Driver.Moving);
        var requests = beads.FrameRequests;
        Pump(time, 30);
        Assert.Equal(requests, beads.FrameRequests);
        Assert.False(beads.IsMoving);
    }, calm: true));

    [Fact]
    // ED-NEW-calculi-wu4-2
    public void A_control_that_leaves_the_tree_and_comes_back_goes_on_and_never_replays() => headless.OnUiThread(() => Shown((beads, window, _, time) =>
    {
        beads.Enter(NthBeadsEntrance.Scriptio, "t1");
        Pump(time, 240); // past the entrance: working
        var first = beads.Painter!;
        var driver = first.Driver;
        var engine = driver.Engine!;
        Assert.Equal(NthBeadsMode.Running, engine.Mode);
        var clock = engine.Clock;
        var entrances = 0;
        engine.MoveBegan += _ => { if (engine.MoveId is NthBeadsMoveId.Scriptio or NthBeadsMoveId.Ortus) entrances++; };
        window.Content = null; // scrolled out of a virtualised list
        Pump(time, 60);
        window.Content = beads; // and back
        Pump(time, 4);
        Assert.NotSame(first, beads.Painter);
        Assert.Same(driver, beads.Painter!.Driver);
        Assert.Same(engine, driver.Engine);
        Assert.Equal(NthBeadsMode.Running, engine.Mode);
        Assert.InRange(engine.Clock, clock, clock + 5 / 60.0); // went on from the frozen clock, no 1 s catch-up
        Assert.True(beads.IsMoving);
        Pump(time, 60);
        Assert.Equal(0, entrances);
        // the turn ends and plays out, then the control leaves and comes back: nothing replays
        beads.Finish();
        Pump(time, 240);
        Assert.Equal(NthBeadsMode.Gone, engine.Mode);
        var began = 0;
        engine.MoveBegan += _ => began++;
        window.Content = null;
        Pump(time, 60);
        window.Content = beads;
        Pump(time, 60);
        Assert.Equal(0, began);
        Assert.Equal(NthBeadsMode.Gone, engine.Mode);
        Assert.False(driver.Sample.Present);
        Assert.False(beads.IsMoving);
    }));
}
