// PORT-ONLY: Tactus's mark in motion (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). Pure and clock-driven, so every surface
// moves the mark the same way and tests step it frame by frame. The mark is still between actions. Each real action is
// one press, at most one per 450 ms, timed to the Tactus pointer's ripple: the wind-up starts 100 ms before contact, the
// press spring starts 65 ms before it so it lands on the ripple frame, and 120 ms after contact the mark springs back with
// no bounce. A typing burst is one press held until the burst ends. Paused springs into the Lift pose (point up, cut open)
// and Resume springs back. The mark arrives and leaves through its own poses (TactusGlyph.Enter and Leave), never a fade.
// With Reduce motion there is no press or hold, and every pose change is immediate.
namespace Marrow.Core.Port.PcControl.Mark;

/// <summary>A critically damped or underdamped spring of mass 1: stiffness k and damping ratio z (damping 2 z sqrt(k)).</summary>
public readonly record struct TactusSpring(double Stiffness, double DampingRatio)
{
    private const double SettledDistance = 1e-3;
    private const double SettledSpeed = 1e-2;

    public double Damping => 2 * Math.Sqrt(Stiffness) * DampingRatio;

    /// <summary>
    /// Moves <paramref name="value"/> toward <paramref name="target"/> for <paramref name="seconds"/> (at most 50 ms, in 240
    /// sub-steps a second, as the design pages do), snapping onto the target once it is within a thousandth and slower than
    /// a hundredth a second. True when it rests on the target.
    /// </summary>
    public bool Advance(ref double value, ref double speed, double target, double seconds)
    {
        seconds = Math.Clamp(seconds, 0, 0.05);
        if (seconds > 0 && (value != target || speed != 0))
        {
            var substeps = Math.Max(1, (int)Math.Ceiling(seconds * 240));
            var h = seconds / substeps;
            var c = Damping;
            for (var s = 0; s < substeps; s++)
            {
                var a = (-Stiffness * (value - target)) - (c * speed);
                speed += a * h;
                value += speed * h;
            }
            if (Math.Abs(value - target) < SettledDistance && Math.Abs(speed) < SettledSpeed)
            {
                value = target;
                speed = 0;
            }
        }
        return value == target && speed == 0;
    }
}

public sealed class TactusMarkMotion
{
    /// <summary>At most one press per this span.</summary>
    public const int PressSpacingMs = 450;

    /// <summary>The wind-up starts this long before contact.</summary>
    public const int WindLeadMs = 100;

    /// <summary>The press spring starts this long before contact, so it arrives on the contact frame.</summary>
    public const int PressLeadMs = 65;

    /// <summary>The spring back starts this long after contact.</summary>
    public const int ReleaseAfterMs = 120;

    /// <summary>A held press goes down this long after its wind-up starts.</summary>
    public const int HoldPressAfterMs = 60;

    public static readonly TactusSpring WindSpring = new(900, 1);
    public static readonly TactusSpring PressSpring = new(2400, 0.9);
    public static readonly TactusSpring HoldSpring = new(1000, 0.8);
    public static readonly TactusSpring ReleaseSpring = new(380, 0.8);

    /// <summary>Into and out of Paused: critically damped, so the lift never bounces.</summary>
    public static readonly TactusSpring PoseSpring = new(420, 1);

    private readonly double[] values;
    private readonly double[] speeds = new double[TactusGlyph.Count];
    private readonly double[] targets;
    private readonly TactusSpring[] springs = new TactusSpring[TactusGlyph.Count];
    private readonly long[] startsAt = new long[TactusGlyph.Count];
    private readonly List<(long At, Action Run)> timers = [];
    private long lastStep;
    private long lastPress = long.MinValue / 2;
    private bool reduceMotion;

    public TactusMarkMotion(bool paused = false, bool reduceMotion = false)
    {
        Base = paused ? TactusPose.Lift : TactusPose.Rest;
        values = TactusGlyph.Pose(Base);
        targets = (double[])values.Clone();
        this.reduceMotion = reduceMotion;
    }

    /// <summary>The pose the mark rests in: Rest, or Lift while paused.</summary>
    public TactusPose Base { get; private set; }

    /// <summary>True while a typing burst holds the press.</summary>
    public bool Held { get; private set; }

    /// <summary>The mark's numbers now.</summary>
    public ReadOnlySpan<double> Values => values;

    /// <summary>The mark's outline now, in 24 units.</summary>
    public TactusPath Path => TactusGlyph.Draw(values);

    /// <summary>True while any number is still moving or a step is scheduled; a surface asks for frames only then.</summary>
    public bool Moving
    {
        get
        {
            if (timers.Count > 0) return true;
            for (var i = 0; i < TactusGlyph.Count; i++)
            {
                if (values[i] != targets[i] || speeds[i] != 0) return true;
            }
            return false;
        }
    }

    /// <summary>Reduce motion: turning it on stops every motion and shows the base pose at once.</summary>
    public bool ReduceMotion
    {
        get => reduceMotion;
        set
        {
            if (reduceMotion == value) return;
            reduceMotion = value;
            if (value) Still();
        }
    }

    /// <summary>
    /// One press for a real action whose contact (the pointer's ripple) comes <paramref name="contactInMs"/> from
    /// <paramref name="now"/>. False when it was skipped: Reduce motion, a held press, or another press under 450 ms ago.
    /// </summary>
    public bool Press(long now, int contactInMs = WindLeadMs)
    {
        if (reduceMotion || Held) return false;
        if (now - lastPress < PressSpacingMs) return false;
        contactInMs = Math.Max(0, contactInMs);
        lastPress = now + contactInMs;
        timers.Clear();
        At(now + Math.Max(0, contactInMs - WindLeadMs), at => To(at, TactusPose.Wind, WindSpring));
        At(now + Math.Max(0, contactInMs - PressLeadMs), at => To(at, TactusPose.Press, PressSpring));
        At(now + contactInMs + ReleaseAfterMs, at =>
        {
            if (!Held) To(at, Base, ReleaseSpring);
        });
        return true;
    }

    /// <summary>A typing burst starts (held press) or ends (spring back). Nothing with Reduce motion.</summary>
    public void Hold(long now, bool on)
    {
        if (reduceMotion) return;
        if (on)
        {
            if (Held) return;
            Held = true;
            timers.Clear();
            lastPress = now;
            To(now, TactusPose.Wind, WindSpring);
            At(now + HoldPressAfterMs, at => To(at, TactusPose.Press, HoldSpring));
        }
        else
        {
            if (!Held) return;
            Held = false;
            timers.Clear();
            To(now, Base, ReleaseSpring);
        }
    }

    /// <summary>Paused morphs into the Lift pose and Resume back to Rest; a held press ends. Immediate with Reduce motion.</summary>
    public void SetPaused(long now, bool paused)
    {
        var pose = paused ? TactusPose.Lift : TactusPose.Rest;
        if (pose == Base && !Held) return;
        Base = pose;
        Held = false;
        timers.Clear();
        if (reduceMotion) Set(Base);
        else To(now, Base, PoseSpring);
    }

    /// <summary>The mark arrives: from its tip (Zero) through the glyph's own arrival. Immediate with Reduce motion.</summary>
    public void Enter(long now)
    {
        timers.Clear();
        Held = false;
        if (reduceMotion)
        {
            Set(Base);
            return;
        }
        Set(TactusPose.Zero);
        lastStep = now;
        var basePose = TactusGlyph.Pose(Base);
        foreach (var move in TactusGlyph.Enter) Apply(now, move, basePose);
    }

    /// <summary>The mark leaves through the glyph's own leaving (to Zero). Immediate with Reduce motion.</summary>
    public void Leave(long now)
    {
        timers.Clear();
        Held = false;
        if (reduceMotion)
        {
            Set(TactusPose.Zero);
            return;
        }
        var basePose = TactusGlyph.Pose(Base);
        foreach (var move in TactusGlyph.Leave) Apply(now, move, basePose);
    }

    /// <summary>Stops every motion and shows the base pose.</summary>
    public void Still()
    {
        timers.Clear();
        Held = false;
        Set(Base);
    }

    /// <summary>
    /// Advances to <paramref name="now"/>: runs the steps that are due, then moves every spring (240 sub-steps a second,
    /// at most 50 ms per call, as the design pages do). True while anything still moves.
    /// </summary>
    public bool Step(long now)
    {
        // Due steps run at their own time, each after the springs moved up to it.
        while (timers.Count > 0)
        {
            var next = timers.MinBy(timer => timer.At);
            if (next.At > now) break;
            Integrate(next.At);
            timers.Remove(next);
            next.Run();
        }
        Integrate(now);
        return Moving;
    }

    private void Integrate(long now)
    {
        var dt = Math.Clamp((now - lastStep) / 1000.0, 0, 0.05);
        var from = lastStep;
        lastStep = Math.Max(lastStep, now);
        if (dt <= 0) return;
        for (var i = 0; i < TactusGlyph.Count; i++)
        {
            if (values[i] == targets[i] && speeds[i] == 0) continue;
            if (startsAt[i] > from + (long)(dt * 1000)) continue;
            springs[i].Advance(ref values[i], ref speeds[i], targets[i], dt);
        }
    }

    private void At(long at, Action<long> run) => timers.Add((at, () => run(at)));

    private void To(long now, TactusPose pose, TactusSpring spring)
    {
        var goal = TactusGlyph.Pose(pose);
        for (var i = 0; i < TactusGlyph.Count; i++) Aim(i, goal[i], spring, now);
    }

    private void Apply(long now, TactusMove move, double[] basePose)
    {
        var spring = new TactusSpring(move.Stiffness, move.DampingRatio);
        foreach (var index in move.Parameters) Aim(index, move.Value ?? basePose[index], spring, now + move.DelayMs);
    }

    private void Aim(int index, double target, TactusSpring spring, long startsAt)
    {
        targets[index] = target;
        springs[index] = spring;
        this.startsAt[index] = startsAt;
    }

    private void Set(TactusPose pose)
    {
        var goal = TactusGlyph.Pose(pose);
        Array.Copy(goal, values, TactusGlyph.Count);
        Array.Copy(goal, targets, TactusGlyph.Count);
        Array.Clear(speeds);
    }
}
