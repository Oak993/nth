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

// Calculi WU4 (DESIGN.md sections 5.1, 9 and 11; ED-NEW-calculi-wu4-1): the beads' host and pacing. The engine advances only on
// pacer slots (60 Hz, or every second slot at 30 Hz); frames are posted only on change; nothing is asked for while still, hidden
// or minimised; the clock does not run while paused, an exit in progress is cut, and the clock re-anchors on resume.
public sealed class NthBeadsHostTests
{
    private const double T0 = 100, Slot = 1 / 60.0;

    private static NthBeadsFrame Shown(double at) => NthBeadsFrame.Blank with { Shown = true, CalmAt = at };

    private static NthBeadsFrame Turn(NthBeadsFrame f, double at, NthBeadsEntrance kind = NthBeadsEntrance.Ortus) =>
        f with { Turn = f.Turn + 1, Entrance = kind, Seed = NthBeadsEngine.SeedFrom("turn-" + (f.Turn + 1)), EnterAt = at, Exit = NthBeadsExit.None };

    private static double Travel(in NthBeadsSample a, in NthBeadsSample b)
    {
        var most = 0.0;
        for (var i = 0; i < 3; i++)
        {
            double dx = (a[i].X + a.FlowX) - (b[i].X + b.FlowX), dy = (a[i].Y + a.FlowY) - (b[i].Y + b.FlowY);
            most = Math.Max(most, Math.Sqrt(dx * dx + dy * dy) / BeadWidth);
        }
        return most;
    }

    private static double Run(NthBeadsDriver d, double from, double seconds)
    {
        var t = from;
        for (var i = 1; i <= (int)Math.Round(seconds * 60); i++) { t = from + i * Slot; d.Advance(t); }
        return t;
    }

    // ---------------- the 30 Hz divisor ----------------

    [Theory]
    [InlineData(60.0)]
    [InlineData(120.0)]
    [InlineData(144.0)]
    [InlineData(165.0)]
    [InlineData(240.0)]
    public void A_thirty_hertz_client_gets_every_second_slot(double rate)
    {
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / rate));
        var sixty = new List<long>();
        var thirty = new List<long>();
        for (long k = 0; k < (long)Math.Round(rate * 3); k++)
        {
            var now = T0 + k / rate + .0003 * (k % 3);
            var key = (long)(now * 1e7);
            var full = pacer.Beat(key, now);
            var half = pacer.Beat(key, now, 2);
            Assert.Equal(half, pacer.Beat(key, now, 2)); // every 30 Hz client of the refresh gets the same answer
            Assert.Equal(full, pacer.Beat(key, now, 1));
            if (full) sixty.Add(k);
            if (half) thirty.Add(k);
        }
        Assert.Equal(180, sixty.Count);
        Assert.Equal(90, thirty.Count);
        // exactly the 1st, 3rd, 5th ... 60 Hz slot, so a 30 Hz client updates at that slot's time
        Assert.Equal(sixty.Where((_, i) => i % 2 == 0), thirty);
    }

    [Fact]
    public void A_thirty_hertz_client_alone_still_keeps_the_shared_count()
    {
        var pacer = new AmbientRefreshPacer(() => TimeSpan.FromSeconds(1 / 240.0));
        var beats = 0;
        for (long k = 0; k < 240 * 2; k++)
        {
            var now = T0 + k / 240.0;
            if (pacer.Beat((long)(now * 1e7), now, 2)) beats++;
        }
        Assert.Equal(60, beats);
    }

    [Theory]
    [InlineData(12.0, false, 2)]
    [InlineData(14.0, false, 2)]
    [InlineData(16.0, false, 1)]
    [InlineData(18.0, false, 1)]
    [InlineData(16.0, true, 2)]
    public void The_frame_rate_follows_the_size_and_the_site(double size, bool ambient, int every) =>
        Assert.Equal(every, NthBeadsFrame.EveryFor(size, ambient));

    // ---------------- the driver: events at their own times, pauses, re-anchoring ----------------

    [Fact]
    public void Events_apply_at_their_own_times_exactly_as_the_engine_fed_directly()
    {
        var d = new NthBeadsDriver();
        var f = Shown(T0);
        d.Receive(f, T0);
        var direct = new NthBeadsEngine(f.Seed);
        // (time, change) in the order the UI posts them; the driver gets each when it happens, the engine at its engine time
        var script = new (double At, Func<NthBeadsFrame, double, NthBeadsFrame> Change, Action<NthBeadsEngine, double> Same)[]
        {
            (.004, (x, a) => Turn(x, a, NthBeadsEntrance.Scriptio), (e, a) => e.Enter(NthBeadsEntrance.Scriptio, a, NthBeadsEngine.SeedFrom("turn-1"))),
            (1.207, (x, a) => x with { State = NthBeadsState.Tool, StateAt = a }, (e, a) => e.SetState(NthBeadsState.Tool, a)),
            (3.011, (x, a) => x with { Steps = x.Steps + 1, StepAt = a }, (e, a) => e.Step(a)),
            (5.333, (x, a) => x with { Steps = x.Steps + 1, StepAt = a }, (e, a) => e.Step(a)),
            (6.02, (x, a) => x with { Reflows = 1, ReflowX = 0, ReflowY = 1.8, ReflowAt = a }, (e, a) => e.Reflow(0, 1.8, a)),
            (7.5, (x, a) => x with { State = NthBeadsState.Streaming, StateAt = a }, (e, a) => e.SetState(NthBeadsState.Streaming, a)),
            (9.01, (x, a) => x with { Exit = NthBeadsExit.Finish, ExitAt = a }, (e, a) => e.Finish(a)),
        };
        var next = 0;
        for (var i = 1; i <= 60 * 11; i++)
        {
            var now = T0 + i * Slot;
            while (next < script.Length && T0 + script[next].At <= now)
            {
                f = script[next].Change(f, T0 + script[next].At);
                d.Receive(f, T0 + script[next].At);
                script[next].Same(direct, T0 + script[next].At - T0);
                next++;
            }
            d.Advance(now);
            direct.Update(now - T0);
            var a = d.Sample; var b = direct.Frame();
            Assert.Equal(b.Present, a.Present);
            for (var k = 0; k < 3; k++)
            {
                Assert.Equal(b[k].X, a[k].X, 12); Assert.Equal(b[k].Y, a[k].Y, 12); Assert.Equal(b[k].S, a[k].S, 12);
            }
            Assert.Equal(b.FlowY, a.FlowY, 12);
        }
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        Assert.False(d.Moving);
    }

    [Fact]
    public void The_clock_does_not_advance_while_hidden_and_resumes_exactly_where_it_stopped()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 3.3);
        Assert.True(d.Moving);
        var clock = d.Engine!.Clock;
        var before = d.Sample;
        d.Receive(f = f with { Shown = false }, t + .002);
        Assert.True(d.Paused);
        Assert.False(d.Moving); // the handler asks for nothing
        Run(d, t, 4);
        Assert.Equal(clock, d.Engine.Clock); // the clock did not run
        var back = t + 4.5;
        d.Receive(f with { Shown = true }, back);
        Assert.False(d.Paused);
        Assert.True(d.Moving);
        Assert.Equal(before.B1, d.Sample.B1); // the same pose
        d.Advance(back + Slot);
        Assert.Equal(clock + Slot, d.Engine.Clock, 9); // re-anchored: one slot on, no catch-up
        Assert.True(Travel(before, d.Sample) < .25, $"no burst on resume ({Travel(before, d.Sample):0.###} bead widths)");
    }

    [Fact]
    public void An_exit_in_progress_is_cut_to_gone_when_the_host_pauses()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 4);
        var gone = double.NaN;
        d.Engine!.Gone += at => gone = at;
        d.Receive(f = f with { Exit = NthBeadsExit.Finish, ExitAt = t }, t);
        t = Run(d, t, .2);
        Assert.Equal(NthBeadsMode.Exiting, d.Engine.Mode);
        d.Receive(f with { Shown = false }, t + .001);
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
        Assert.False(double.IsNaN(gone));
        Assert.False(d.Sample.Present); // nothing is left drawn
    }

    [Theory]
    [InlineData(NthBeadsExit.Finish)]
    [InlineData(NthBeadsExit.Fail)]
    [InlineData(NthBeadsExit.Stop)]
    public void An_exit_asked_while_hidden_is_gone_at_once_and_never_waits_for_a_frame(NthBeadsExit exit)
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 2);
        d.Receive(f = f with { Shown = false }, t);
        d.Receive(f with { Exit = exit, ExitAt = t + 1 }, t + 1);
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        Assert.False(d.Moving);
    }

    [Fact]
    public void Not_being_drawn_pauses_cuts_an_exit_and_being_drawn_again_reanchors()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 2.5);
        var clock = d.Engine!.Clock;
        d.Unseen();
        Assert.True(d.Paused);
        Assert.False(d.Moving);
        Run(d, t, 1);
        Assert.Equal(clock, d.Engine.Clock);
        d.Seen(t + 3);
        Assert.True(d.Moving);
        d.Advance(t + 3 + Slot);
        Assert.Equal(clock + Slot, d.Engine.Clock, 9);
        // scrolled away while leaving: gone at once
        d.Receive(f with { Exit = NthBeadsExit.Finish, ExitAt = t + 3.1 }, t + 3.1);
        Run(d, t + 3 + Slot, .2);
        d.Unseen();
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
    }

    [Fact]
    public void A_stall_while_moving_reanchors_instead_of_playing_the_gap_at_once()
    {
        var d = new NthBeadsDriver();
        d.Receive(Turn(Shown(T0), T0), T0);
        var t = Run(d, T0, 3);
        var clock = d.Engine!.Clock;
        var before = d.Sample;
        d.Advance(t + 1.2); // the render thread was held for 1.2 s
        Assert.Equal(clock + Slot, d.Engine.Clock, 9);
        Assert.True(Travel(before, d.Sample) < .25);
    }

    // ---------------- detach and re-attach (the driver outlives its visual) ----------------

    [Fact]
    public void A_driver_handed_to_a_new_painter_goes_on_where_it_was_and_never_replays_the_entrance()
    {
        var d = new NthBeadsDriver();
        object a = new(), b = new();
        var f = Turn(Shown(T0), T0, NthBeadsEntrance.Scriptio);
        d.Adopt(a, T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 4);
        var e = d.Engine!;
        var clock = e.Clock;
        var before = d.Sample;
        var entrances = 0;
        e.MoveBegan += _ => { if (e.MoveId is NthBeadsMoveId.Scriptio or NthBeadsMoveId.Ortus) entrances++; };
        // the control left the tree for 5 s (a virtualised list) and came back with a new visual and painter
        d.Adopt(b, t + 5);
        d.Receive(f, t + 5);
        Assert.Same(b, d.Owner);
        Assert.True(d.Paused); // paused as not drawn: the frozen pose, no clock
        Assert.Equal(clock, e.Clock);
        Assert.Equal(before.B1, d.Sample.B1);
        d.Seen(t + 5);
        d.Advance(t + 5 + Slot);
        Assert.Equal(clock + Slot, e.Clock, 9); // re-anchored: one slot on, no catch-up
        Assert.Equal(NthBeadsMode.Running, e.Mode);
        Assert.True(Travel(before, d.Sample) < .25, $"no snap on re-attach ({Travel(before, d.Sample):0.###} bead widths)");
        Run(d, t + 5 + Slot, 2);
        Assert.Equal(0, entrances);
        Assert.Equal(NthBeadsMode.Running, e.Mode);
    }

    [Theory]
    [InlineData(NthBeadsExit.Finish)]
    [InlineData(NthBeadsExit.Fail)]
    [InlineData(NthBeadsExit.Stop)]
    public void A_turn_that_ended_stays_ended_when_its_control_comes_back(NthBeadsExit exit)
    {
        var d = new NthBeadsDriver();
        object a = new(), b = new(), c = new();
        var f = Turn(Shown(T0), T0);
        d.Adopt(a, T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 3);
        d.Receive(f = f with { Exit = exit, ExitAt = t }, t);
        t = Run(d, t, 4); // the exit (the error's ~2 s hold included) has played out
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        var began = 0;
        d.Engine.MoveBegan += _ => began++;
        d.Adopt(b, t + 10);
        d.Receive(f, t + 10);
        d.Seen(t + 10);
        t = Run(d, t + 10, 3);
        Assert.Equal(0, began); // no entrance, no exit, no hold again
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
        Assert.False(d.Sample.Present);
        Assert.False(d.Moving);
        // an exit in progress when the control leaves the tree is cut to gone, never resumed later
        d.Receive(f = Turn(f, t), t);
        t = Run(d, t, 3);
        d.Receive(f = f with { Exit = exit, ExitAt = t }, t);
        t = Run(d, t, .2);
        Assert.Equal(NthBeadsMode.Exiting, d.Engine.Mode);
        d.Adopt(c, t + 2);
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
        d.Receive(f, t + 2);
        d.Seen(t + 2);
        Run(d, t + 2, 3);
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
        Assert.False(d.Sample.Present);
    }

    [Fact]
    public void A_hand_off_within_one_frame_simply_goes_on()
    {
        var d = new NthBeadsDriver();
        object a = new(), b = new();
        var f = Turn(Shown(T0), T0);
        d.Adopt(a, T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 3);
        d.Receive(f = f with { Exit = NthBeadsExit.Finish, ExitAt = t }, t);
        t = Run(d, t, .1);
        d.Adopt(b, t + Slot); // a reparent: the new visual's first frame comes on the next slot
        Assert.False(d.Paused);
        Assert.Equal(NthBeadsMode.Exiting, d.Engine!.Mode); // the exit is not cut
        var clock = d.Engine.Clock;
        d.Advance(t + Slot);
        Assert.Equal(clock + Slot, d.Engine.Clock, 9);
    }

    [Fact]
    public void A_new_engine_for_a_turn_that_has_ended_replays_nothing()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0, NthBeadsEntrance.Scriptio);
        d.Receive(f, T0);
        var t = Run(d, T0, 3);
        d.Receive(f = f with { Exit = NthBeadsExit.Fail, ExitAt = t }, t);
        t = Run(d, t, 4);
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        var old = d.Engine;
        d.Receive(f with { Quiet = true }, t); // the site switched family after the turn ended: a new engine
        Assert.NotSame(old, d.Engine);
        var began = 0;
        d.Engine.MoveBegan += _ => began++;
        Run(d, t, 4);
        Assert.Equal(0, began);
        Assert.Equal(NthBeadsMode.Gone, d.Engine.Mode);
        Assert.False(d.Sample.Present);
    }

    [Fact]
    public void A_thirty_hertz_driver_matches_the_same_session_at_sixty()
    {
        NthBeadsSample Session(int every)
        {
            var d = new NthBeadsDriver();
            var f = Turn(Shown(T0) with { Every = every }, T0);
            d.Receive(f, T0);
            for (var i = every; i <= 60 * 6; i += every) d.Advance(T0 + i * Slot);
            return d.Sample;
        }
        var a = Session(1); var b = Session(2);
        for (var k = 0; k < 3; k++) { Assert.Equal(a[k].X, b[k].X, 12); Assert.Equal(a[k].Y, b[k].Y, 12); }
    }

    [Fact]
    public void Still_formations_and_gone_ask_for_no_frames()
    {
        var d = new NthBeadsDriver();
        var f = Shown(T0) with { Calm = true, CalmAt = T0, State = NthBeadsState.Tool };
        d.Receive(f = Turn(f, T0), T0);
        var t = Run(d, T0, 1); // the 0.24 s pop, then Pedes held still
        Assert.False(d.Moving);
        d.Receive(f = f with { Steps = 1, StepAt = t }, t); // steps change nothing in Reduce Motion
        t = Run(d, t, .1);
        Assert.False(d.Moving);
        d.Receive(f with { Exit = NthBeadsExit.Finish, ExitAt = t }, t);
        d.Advance(t + Slot);
        Assert.True(d.Moving);
        Run(d, t + Slot, 1);
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        Assert.False(d.Moving);
    }

    [Fact]
    public void A_new_turn_after_an_error_starts_in_the_uis_state()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0) with { State = NthBeadsState.Tool }, T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 2);
        d.Receive(f = f with { Exit = NthBeadsExit.Fail, ExitAt = t }, t);
        t = Run(d, t, 3);
        Assert.Equal(NthBeadsMode.Gone, d.Engine!.Mode);
        d.Receive(Turn(f, t), t);
        d.Advance(t + Slot);
        Assert.Equal(NthBeadsState.Tool, d.Engine.State);
        Assert.Equal(NthBeadsMode.Running, d.Engine.Mode);
    }

    [Fact]
    public void The_render_bounds_widen_with_a_live_reflow_glide()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0), T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 2);
        var control = NthBeadsPainter.EnvelopeSize(18);
        Assert.Equal(new Rect(control), d.Bounds(control, 18));
        d.Receive(f with { Reflows = 1, ReflowY = 26 / NthBeadsPainter.Unit(18), ReflowAt = t }, t);
        d.Advance(t + Slot);
        var wide = d.Bounds(control, 18);
        Assert.True(wide.Top < -20, $"the glide's start is inside the bounds ({wide})");
        Run(d, t + Slot, 1);
        Assert.Equal(new Rect(control), d.Bounds(control, 18)); // the glide has decayed
    }

    [Fact]
    public void A_slot_allocates_nothing_after_warm_up()
    {
        var d = new NthBeadsDriver();
        var f = Turn(Shown(T0) with { State = NthBeadsState.Tool }, T0);
        d.Receive(f, T0);
        var t = Run(d, T0, 30); // warm-up: every move kind of the session has run
        long Slots(int from)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = from + 1; i <= from + 10_000; i++) { d.Advance(t + i * Slot); _ = d.Bounds(new Size(19, 22), 18); }
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        var allocated = Slots(0);
        // ED-NEW-calculi-wu2-1 item 4: the counter's 8 KB quantum noise; a real allocation repeats on both replays
        for (var replay = 1; allocated != 0 && replay <= 2; replay++) allocated = Math.Min(allocated, Slots(replay * 10_000));
        Assert.Equal(0, allocated);
        Assert.Equal(NthBeadsMode.Running, d.Engine!.Mode);
    }
}

// The control on a real (headless) composition visual: it measures to the envelope, posts only on change, and asks for no frames
// while still, hidden or minimised (the window state's Available is false when the window is minimised or not shown).
[Collection("Headless")]
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
