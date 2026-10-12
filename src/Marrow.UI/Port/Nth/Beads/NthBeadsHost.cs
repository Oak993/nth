// PORT-ONLY: Calculi (ED-NEW-calculi-wu4-1; DESIGN.md sections 5.1, 8, 9 and 11). The beads' render-thread host: the UI thread
// posts an immutable NthBeadsFrame only when something changes, every event in it stamped with its own time; the driver applies
// those events at their times and advances the engine only on the handler's pacer slots (60 Hz, or every second slot at 30 Hz);
// OnRender only paints the sample the last slot made. A paused host (window unavailable, or the visual not drawn) asks for no
// frames, freezes the engine's clock, cuts an exit in progress to gone, and re-anchors the clock when it resumes, so the beads go
// on exactly where they were with no catch-up burst. The driver outlives its visual: a control that leaves the tree and comes back
// (a virtualised list, a reparent) hands the same driver to its new painter, so a turn goes on where it was and a turn that ended
// stays ended; nothing is replayed. After warm-up a slot allocates nothing.
using Avalonia;
using SkiaSharp;
using static Marrow.UI.Port.Nth.Beads.NthBeadsGeometry;

namespace Marrow.UI.Port.Nth.Beads;

/// <summary>How a turn ended (the exit the engine plays; the UI never waits for it, section 5.1).</summary>
public enum NthBeadsExit : byte { None, Finish, Fail, Stop }

/// <summary>
/// Everything the beads' render thread needs, as one immutable value, sent only when it changes. Counters (turn, steps) and the
/// running reflow total make it safe to coalesce: the driver applies the difference between the last frame it applied and this one.
/// Times are clock seconds (the host's <see cref="NthFrame.Clock"/>); reflow totals are in n units.
/// </summary>
public sealed record NthBeadsFrame(
    double Size, SKColor? Contrast, bool Quiet, int Every, bool Shown,
    bool Calm, double CalmAt,
    NthBeadsState State, double StateAt,
    int Steps, double StepAt,
    int Turn, NthBeadsEntrance Entrance, uint Seed, double EnterAt,
    NthBeadsExit Exit, double ExitAt,
    int Reflows, double ReflowX, double ReflowY, double ReflowAt)
{
    /// <summary>Nothing has happened yet: no turn, thinking, not calm, an 18 DIP glyph at 60 Hz.</summary>
    public static readonly NthBeadsFrame Blank = new(18, null, false, 1, true, false, 0, NthBeadsState.Thinking, 0, 0, 0,
        0, NthBeadsEntrance.Ortus, 0, 0, NthBeadsExit.None, 0, 0, 0, 0, 0);

    /// <summary>The frame rate rule (section 11): 60 Hz from 16 DIP, 30 Hz below it and at ambient sites. A site the section 9 table
    /// names 60 Hz below 16 DIP (the helper capsule and the Tactus pill, 14 DIP) asks for <paramref name="fullRate"/>.</summary>
    public static int EveryFor(double size, bool ambient, bool fullRate = false) => fullRate && !ambient ? 1 : ambient || size < 16 ? 2 : 1;
}

/// <summary>
/// The beads' engine on the render thread: applies the UI's timestamped frames, advances on slots, pauses and resumes. Pure (no
/// Avalonia), so its clock rules are tested directly; the handler calls it from OnMessage, OnAnimationFrameUpdate and OnRender.
/// </summary>
public sealed class NthBeadsDriver
{
    /// <summary>A gap between two slots of a moving engine longer than this is a stall (a pause the host did not see): the clock
    /// is re-anchored and advances one slot, never the whole gap at once.</summary>
    public const double StallSeconds = .25;

    private const int Capacity = 16;
    private enum Kind : byte { Calm, State, Enter, Step, Exit, Reflow }
    private struct Ev { public Kind Kind; public double At; }

    private readonly NthBeadsFrame?[] pending = new NthBeadsFrame?[Capacity];
    private readonly Ev[] evs = new Ev[6];
    private int count;
    private NthBeadsFrame applied = NthBeadsFrame.Blank;
    private volatile NthBeadsFrame? latest;
    private NthBeadsEngine? engine;
    private NthBeadsSample sample;
    private double shift, last = double.NaN;
    private bool hidden, unseen, wasMoving;
    private volatile bool moving;
    private volatile bool gone = true;
    private volatile object? owner;

    public NthBeadsDriver() => sample.Glint = -1;

    /// <summary>The engine (render thread only; null before the first frame).</summary>
    public NthBeadsEngine? Engine => engine;

    /// <summary>The sample the last slot (or event while paused) made: what OnRender paints.</summary>
    public ref readonly NthBeadsSample Sample => ref sample;

    /// <summary>The frame received last.</summary>
    public NthBeadsFrame? Latest => latest;

    /// <summary>60 Hz slots per frame for the latest frame.</summary>
    public int Every => latest?.Every ?? 1;

    /// <summary>True while the clock is stopped: the window is unavailable, or the visual was not drawn.</summary>
    public bool Paused => hidden || unseen || engine is null;

    /// <summary>Whether frames are wanted: running and not still. Safe to read from the UI thread.</summary>
    public bool Moving => moving;

    /// <summary>Whether the beads draw nothing: no engine yet, or no turn, or an exit that has ended (reference <c>gone</c>). Safe to read from the
    /// UI thread, where it moves the beads only (a hand-over to a far slot waits for it, ED-NEW-calculi-wu5-2); no UI waits for it.</summary>
    public bool Gone => gone;

    /// <summary>The painter that drives this driver now (the last to <see cref="Adopt"/> it).</summary>
    public object? Owner => owner;

    /// <summary>The painter <paramref name="by"/> takes this driver over at <paramref name="now"/> (render thread). A driver handed
    /// on from a visual that left the tree pauses as one not drawn (an exit in progress is cut to gone, the clock re-anchors when
    /// it is drawn again), unless the hand-off came within a stall (a reparent in one frame: it simply goes on).</summary>
    public void Adopt(object by, double now)
    {
        ArgumentNullException.ThrowIfNull(by);
        if (ReferenceEquals(owner, by)) return;
        var handed = owner is not null;
        owner = by;
        if (handed && !(now - last <= StallSeconds)) Unseen();
    }

    /// <summary>Engine time for the clock time <paramref name="now"/> while running.</summary>
    public double EngineTime(double now) => now - shift;

    /// <summary>A frame from the UI thread arrived at <paramref name="now"/>. Its events wait for the next slot, unless the host is
    /// paused: then they apply at once at the frozen clock and an exit is cut to gone (nothing waits on a frame that never comes).</summary>
    public void Receive(NthBeadsFrame frame, double now)
    {
        ArgumentNullException.ThrowIfNull(frame);
        latest = frame;
        if (engine is null || engine.Quiet != frame.Quiet)
        {
            engine = new NthBeadsEngine(frame.Seed, frame.Quiet);
            // a new engine has had no turn: a turn still running enters it; a turn that has ended is not replayed
            applied = frame.Exit == NthBeadsExit.None ? applied with { Turn = 0, Exit = NthBeadsExit.None }
                : applied with { Turn = frame.Turn, Exit = frame.Exit, Entrance = frame.Entrance, Seed = frame.Seed, EnterAt = frame.EnterAt, ExitAt = frame.ExitAt };
            shift = now; last = double.NaN; wasMoving = false;
        }
        if (count == Capacity) Apply(Paused ? engine.Clock : Math.Max(engine.Clock, EngineTime(now)));
        pending[count++] = frame;
        if (!frame.Shown) hidden = true;
        if (Paused)
        {
            Apply(engine.Clock);
            if (frame.Shown && hidden) { hidden = false; if (!unseen) Resume(now); }
            if (Paused) engine.Cut();
            Resample();
        }
        Publish();
    }

    /// <summary>A pacer slot at clock time <paramref name="now"/>: apply what arrived at its own times, then advance to the slot.</summary>
    public void Advance(double now)
    {
        if (engine is null || Paused) return;
        if (wasMoving && !double.IsNaN(last) && now - last > StallSeconds) shift = now - (engine.Clock + Every / 60.0);
        last = now;
        var t = Math.Max(engine.Clock, EngineTime(now));
        Apply(t);
        engine.Update(Math.Max(t, engine.Clock));
        Resample();
        Publish();
        wasMoving = moving;
    }

    /// <summary>The last slot's frame was not drawn (scrolled away, hidden by an ancestor, window covered): pause.</summary>
    public void Unseen()
    {
        if (engine is null || unseen) return;
        unseen = true;
        if (engine.Cut()) Resample();
        Publish();
    }

    /// <summary>The visual is drawn again at <paramref name="now"/>: resume from the frozen pose if it paused for not being drawn.</summary>
    public void Seen(double now)
    {
        if (!unseen) return;
        unseen = false;
        if (!Paused) Resume(now);
        Publish();
    }

    // The clock goes on from where it stopped: engine time now is the engine's own clock.
    private void Resume(double now)
    {
        if (engine is null) return;
        shift = now - engine.Clock;
        last = now; wasMoving = false;
    }

    private void Publish()
    {
        moving = engine is { IsStill: false } && !Paused;
        gone = engine is not { Mode: not NthBeadsMode.Gone };
    }

    private void Resample() => sample = engine!.Frame();

    private double At(double at, double limit) => Math.Clamp(at - shift, engine!.Clock, Math.Max(limit, engine.Clock));

    // Applies every pending frame in order, each as its difference from the frame applied before it, at the events' own times
    // (never before the engine's clock, never past the slot).
    private void Apply(double limit)
    {
        for (var i = 0; i < count; i++)
        {
            var next = pending[i]!;
            pending[i] = null;
            Diff(applied, next, Paused ? engine!.Clock : limit);
            applied = next;
        }
        count = 0;
    }

    private void Diff(NthBeadsFrame was, NthBeadsFrame f, double limit)
    {
        var e = engine!;
        var n = 0;
        if (was.Calm != f.Calm) evs[n++] = new Ev { Kind = Kind.Calm, At = f.CalmAt };
        if (was.State != f.State) evs[n++] = new Ev { Kind = Kind.State, At = f.StateAt };
        if (was.Turn != f.Turn) evs[n++] = new Ev { Kind = Kind.Enter, At = f.EnterAt };
        if (was.Steps != f.Steps) evs[n++] = new Ev { Kind = Kind.Step, At = f.StepAt };
        if (f.Exit != NthBeadsExit.None && (was.Exit != f.Exit || was.Turn != f.Turn)) evs[n++] = new Ev { Kind = Kind.Exit, At = f.ExitAt };
        if (was.Reflows != f.Reflows) evs[n++] = new Ev { Kind = Kind.Reflow, At = f.ReflowAt };
        // stable insertion sort by time (the order above breaks ties: the state before the turn it starts in)
        for (var i = 1; i < n; i++)
        {
            var v = evs[i];
            var j = i - 1;
            while (j >= 0 && evs[j].At > v.At) { evs[j + 1] = evs[j]; j--; }
            evs[j + 1] = v;
        }
        for (var i = 0; i < n; i++)
        {
            var at = At(evs[i].At, limit);
            switch (evs[i].Kind)
            {
                case Kind.Calm: e.SetCalm(f.Calm, at); break;
                case Kind.State: if (Settable(f.State)) e.SetState(f.State, at); break;
                case Kind.Enter:
                    // after an error or a stop the engine's state is that end; the new turn starts in the UI's state
                    if (e.State != f.State && Settable(f.State)) e.SetState(f.State, at);
                    e.Enter(f.Entrance, at, f.Seed);
                    break;
                case Kind.Step: e.Step(at); break;
                case Kind.Exit:
                    if (f.Exit == NthBeadsExit.Finish) e.Finish(at);
                    else if (f.Exit == NthBeadsExit.Fail) e.Fail(at);
                    else e.Stop(at);
                    break;
                case Kind.Reflow: e.Reflow(f.ReflowX - was.ReflowX, f.ReflowY - was.ReflowY, at); break;
            }
        }
    }

    private static bool Settable(NthBeadsState s) => s is not (NthBeadsState.Error or NthBeadsState.Stopped);

    /// <summary>The area the beads draw in a control of <paramref name="control"/> size with the n box at <paramref name="size"/>
    /// DIP: the envelope box, widened by the live flow offset while the beads glide after a reflowed glyph (section 9).</summary>
    public Rect Bounds(Size control, double size)
    {
        var box = new Rect(control);
        if (sample.FlowX == 0 && sample.FlowY == 0) return box;
        var u = NthBeadsPainter.Unit(size);
        return box.Union(box.Translate(new Vector(sample.FlowX * u, sample.FlowY * u)));
    }
}

/// <summary>The beads' painter on the composition host: the driver's engine, painted by <see cref="NthBeadsPainter"/>. A painter
/// whose driver a newer painter has taken over (its visual left the tree) does nothing: it asks for no frames and draws nothing.</summary>
public sealed class NthBeadsHostPainter(NthBeadsDriver? driver = null) : NthAdvancingPainter
{
    private readonly NthBeadsPainter ink = new();

    /// <summary>The render thread's engine and clock rules (the control's own, kept across detach and re-attach).</summary>
    public NthBeadsDriver Driver { get; } = driver ?? new();

    private bool Owns => ReferenceEquals(Driver.Owner, this);

    public override bool ClipsToSize => false;

    public override bool Moving(NthFrame frame, double now) => Owns && Driver.Moving;

    public override Rect Bounds(NthFrame frame) => frame.State is NthBeadsFrame f ? Driver.Bounds(frame.Size, f.Size) : new(frame.Size);

    internal override int Every(NthFrame frame) => frame.State is NthBeadsFrame f ? f.Every : 1;
    internal override bool Paused => !Owns || Driver.Paused;

    internal override void Receive(NthFrame frame, double now)
    {
        if (frame.State is not NthBeadsFrame f) return;
        Driver.Adopt(this, now);
        Driver.Receive(f, now);
    }

    internal override void Advance(NthFrame frame, double now) { if (Owns) Driver.Advance(now); }
    internal override void Unseen() { if (Owns) Driver.Unseen(); }
    internal override void Seen(double now) { if (Owns) Driver.Seen(now); }

    /// <summary>Paints the last slot's sample, the envelope box centred in the control.</summary>
    public override void Draw(SKCanvas canvas, NthFrame frame, double now)
    {
        if (frame.State is not NthBeadsFrame f || !Owns || !Driver.Sample.Present) return;
        var box = NthBeadsPainter.EnvelopeSize(f.Size);
        float dx = (float)Math.Max(0, (frame.Size.Width - box.Width) / 2), dy = (float)Math.Max(0, (frame.Size.Height - box.Height) / 2);
        if (dx == 0 && dy == 0) { ink.Paint(canvas, Driver.Sample, f.Size, frame.Dark, f.Contrast); return; }
        var saved = canvas.Save();
        canvas.Translate(dx, dy);
        ink.Paint(canvas, Driver.Sample, f.Size, frame.Dark, f.Contrast);
        canvas.RestoreToCount(saved);
    }

    public override void Dispose()
    {
        ink.Dispose();
        base.Dispose();
    }
}
