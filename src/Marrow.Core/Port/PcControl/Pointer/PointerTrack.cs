// PORT-ONLY: Tactus pointer, one app's drawn pointer (OD-0144, OD-0145, ED-NEW-pcctl-21; DESIGN.md 3.1 overlay row, 4.5
// "The Tactus pointer", 10.1 "The paused app's Tactus pointer", 10.8; canvas 01, 02 and 10). Each active app has its own
// silver pointer that shows where Tactus acts, in the monitor-local DIP of the monitor it is on:
// - It arrives by growing out of its tip on a critically damped spring and leaves by shrinking back into its tip with an
//   accelerating ease: designed poses, never a fade (owner's motion rule). A move to another monitor leaves on the old one
//   and arrives on the new one (the pointer window never straddles two DPIs).
// - Between targets it glides (PointerGlide, 180 to 260 ms); a press shows as a 32 DIP ripple that grows from 30 % and
//   fades over 450 ms, starting when the glide lands, so the press lands on the pointer.
// - Where the target cannot be seen (covered, minimized, the workspace) the pointer leaves and is parked: the pill's app
//   row shows it as "working behind" until it is aimed at a visible point again.
// - Paused: it stays where it is, dims to 40 % over 200 ms and wears a pause badge; it never glides while paused, and a
//   target given meanwhile is glided to on Resume.
// - During a borrow of the real mouse it glows.
// - Reduce Motion: arrivals, leaves and moves become 150 ms fades (a move fades through), the ripple only fades, and the
//   pause badge appears at once.
// Its window exists only while it is visible and 1 s after it left (so a quick return reuses it), sized to the pointer
// and its ripple at rest and to the span of a glide while gliding, never resized per frame. Pure; times in seconds.
using Marrow.Core.Port.PcControl.Pill;

namespace Marrow.Core.Port.PcControl.Pointer;

public enum PointerPhase
{
    Hidden,
    Shown,
    Leaving,
}

/// <summary>What to draw at one moment: the tip, the body's scale and opacity, the badges, the glow and the ripple.</summary>
public readonly record struct PointerPose(
    PillVector Tip,
    double Scale,
    double Opacity,
    double PauseBadge,
    double Glow,
    bool Rippling,
    double RippleScale,
    double RippleOpacity)
{
    public static PointerPose None { get; } = new(PillVector.Zero, 0, 0, 0, 0, false, 0, 0);
}

public sealed class PointerTrack
{
    /// <summary>The arrival spring's response (seconds, damping ratio 1).</summary>
    public const double ArriveResponse = 0.30;

    /// <summary>The leave: the body shrinks into its tip on an accelerating ease in this long.</summary>
    public const double LeaveSeconds = 0.18;

    public const double RippleSeconds = 0.45;
    public const double RippleFrom = 0.3;

    /// <summary>The ripple's diameter at full size (DIP).</summary>
    public const double RippleSize = 32;

    public const double PausedOpacity = 0.4;
    public const double PauseSeconds = 0.2;
    public const double PauseBadgeResponse = 0.2;
    public const double GlowSeconds = 0.15;
    public const double ReducedSeconds = 0.15;

    /// <summary>The most the arrive and badge springs may pass their end when they start with speed (a fraction).</summary>
    public const double SpringCap = 0.02;

    /// <summary>The window stays this long after the pointer left, then goes.</summary>
    public const double KeepWindowSeconds = 1.0;

    /// <summary>
    /// What the pointer draws around its tip (DIP): the ripple ring (16) and its glow reach about 36 on every side; the
    /// arrow, its shadow and glow, and the app badge reach about 36 right and down; the pause badge 17 left. 88 square.
    /// </summary>
    public static readonly PillRect Extent = new(-40, -40, 88, 88);

    private PointerGlide glide = PointerGlide.Still(0, PillVector.Zero);
    private Scale scale = Scale.Gone;
    private PillCurve dim = PillCurve.Still(1, 0);
    private PillCurve badge = PillCurve.Still(0, 0);
    private PillCurve glow = PillCurve.Still(0, 0);
    private double rippleAt = double.NaN;
    private double fadeThrough = double.NaN;
    private PillVector fadeFrom;
    private (int Monitor, PillVector Tip)? hop;
    private PillVector? heldTarget;
    private double goneAt = double.NegativeInfinity;

    public PointerTrack(bool reduceMotion = false) => ReduceMotion = reduceMotion;

    public bool ReduceMotion { get; set; }

    /// <summary>The monitor it is drawn on (-1 before its first aim).</summary>
    public int Monitor { get; private set; } = -1;

    /// <summary>True while its target cannot be seen: the pill's row shows it "working behind".</summary>
    public bool Parked { get; private set; }

    public bool Paused { get; private set; }

    public bool Real { get; private set; }

    public PointerPhase Phase(double now)
    {
        Advance(now);
        if (scale.Kind == ScaleKind.Gone) return PointerPhase.Hidden;
        return scale.Kind == ScaleKind.Leave ? PointerPhase.Leaving : PointerPhase.Shown;
    }

    /// <summary>Tactus acts at <paramref name="tip"/> on <paramref name="monitor"/>, a point the user can see.</summary>
    public void Aim(double now, int monitor, PillVector tip)
    {
        Advance(now);
        Parked = false;
        if (scale.Kind == ScaleKind.Gone)
        {
            Monitor = monitor;
            glide = PointerGlide.Still(now, tip);
            heldTarget = null;
            Arrive(now);
            return;
        }
        if (monitor != Monitor)
        {
            // Another monitor: leave here, arrive there.
            hop = (monitor, tip);
            if (scale.Kind != ScaleKind.Leave) Leave(now);
            return;
        }
        hop = null;
        if (scale.Kind == ScaleKind.Leave) Arrive(now);
        if (Paused)
        {
            heldTarget = tip;
            return;
        }
        MoveTo(now, tip);
    }

    /// <summary>A press (a click, a command): the ripple starts when the glide lands. Returns when it starts.</summary>
    public double Press(double now)
    {
        Advance(now);
        if (scale.Kind is ScaleKind.Gone or ScaleKind.Leave) return now;
        var lands = Fading(now) ? fadeThrough + (ReducedSeconds / 2) : glide.LandsAt();
        rippleAt = Math.Max(now, lands);
        return rippleAt;
    }

    /// <summary>The target cannot be seen: the pointer leaves and parks on the app's row ("working behind").</summary>
    public void Park(double now)
    {
        Advance(now);
        Parked = true;
        hop = null;
        if (scale.Kind is ScaleKind.Arrive or ScaleKind.Still) Leave(now);
    }

    /// <summary>The app's stream ended: the pointer leaves.</summary>
    public void Hide(double now)
    {
        Advance(now);
        Parked = false;
        hop = null;
        if (scale.Kind is ScaleKind.Arrive or ScaleKind.Still) Leave(now);
    }

    /// <summary>The app paused or resumed.</summary>
    public void Pause(double now, bool paused)
    {
        Advance(now);
        if (paused == Paused) return;
        Paused = paused;
        var to = paused ? PausedOpacity : 1;
        dim = PillCurve.Ease(dim.Value(now), to, now, PauseSeconds);
        badge = ReduceMotion
            ? PillCurve.Still(paused ? 1 : 0, now)
            : PillCurve.Spring(badge.Value(now), paused ? 1 : 0, badge.Velocity(now), PauseBadgeResponse, now, cap: SpringCap);
        if (!paused && heldTarget is { } held)
        {
            heldTarget = null;
            MoveTo(now, held);
        }
    }

    /// <summary>A borrow of the real mouse started or ended: the pointer glows while it runs.</summary>
    public void Borrow(double now, bool real)
    {
        Advance(now);
        if (real == Real) return;
        Real = real;
        glow = PillCurve.Ease(glow.Value(now), real ? 1 : 0, now, GlowSeconds);
    }

    public PointerPose Pose(double now)
    {
        Advance(now);
        if (scale.Kind == ScaleKind.Gone) return PointerPose.None;
        var tip = glide.Presented(now);
        var through = 1.0;
        if (Fading(now))
        {
            var mid = fadeThrough + (ReducedSeconds / 2);
            tip = now < mid ? fadeFrom : glide.Target;
            through = Math.Clamp(Math.Abs(now - mid) / (ReducedSeconds / 2), 0, 1);
        }
        var (bodyScale, bodyOpacity) = scale.At(now, ReduceMotion);
        var rippling = !double.IsNaN(rippleAt) && now >= rippleAt && now < rippleAt + RippleSeconds;
        var progress = rippling ? PillEase.CubicBezier(0.2, 0.7, 0.2, 1, (now - rippleAt) / RippleSeconds) : 0;
        var rippleScale = ReduceMotion ? 1 : RippleFrom + ((1 - RippleFrom) * progress);
        return new PointerPose(tip, bodyScale, bodyOpacity * through * Rest(dim, now), Rest(badge, now), Rest(glow, now),
            rippling, rippling ? rippleScale : 0, rippling ? 1 - progress : 0);
    }

    /// <summary>True while anything about it moves: the host asks for frames only then.</summary>
    public bool Moving(double now)
    {
        Advance(now);
        if (scale.Kind == ScaleKind.Gone) return false;
        return !glide.Ended(now) || !scale.Done(now) || Fading(now) || (!double.IsNaN(rippleAt) && now < rippleAt + RippleSeconds)
            || !Rests(dim, now) || !Rests(badge, now) || !Rests(glow, now);
    }

    /// <summary>True while its window must exist: visible, leaving, or within 1 s after it left.</summary>
    public bool NeedsWindow(double now) => Phase(now) != PointerPhase.Hidden || now < goneAt + KeepWindowSeconds;

    /// <summary>
    /// Where its window goes, in monitor-local DIP: the rest extent around the tip, or while gliding the span of the glide
    /// (both ends, plus the excursion), so the window is set once per glide rather than moved every frame.
    /// </summary>
    public PillRect WindowRect(double now)
    {
        Advance(now);
        if (Fading(now)) return Union(Box(fadeFrom), Box(glide.Target));
        if (glide.Landed(now)) return Box(glide.Target);
        return Union(Union(Box(glide.From), Box(glide.Target)).Inflate(PointerGlide.MaxExcursion + 2), Box(glide.Value(now)));
    }

    /// <summary>A spring at rest: within 0.001 of its end and slower than 0.01 per second; a timed curve that ran its time.</summary>
    internal static bool Rests(PillCurve curve, double now)
    {
        if (curve.Kind != PillCurveKind.Spring) return now - curve.Start >= curve.Duration;
        if (curve.From == curve.To && curve.Velocity0 == 0) return true;
        return now > curve.Start && Math.Abs(curve.Value(now) - curve.To) < 0.001 && Math.Abs(curve.Velocity(now)) < 0.01;
    }

    /// <summary>The curve's value, exactly its end once it rests (no lingering 99.9 %).</summary>
    internal static double Rest(PillCurve curve, double now) => Rests(curve, now) ? curve.To : curve.Value(now);

    private bool Fading(double now) => !double.IsNaN(fadeThrough) && now < fadeThrough + ReducedSeconds;

    private static PillRect Box(PillVector tip) => new(tip.X + Extent.X, tip.Y + Extent.Y, Extent.Width, Extent.Height);

    private static PillRect Union(PillRect a, PillRect b)
    {
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        return new PillRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }

    private void MoveTo(double now, PillVector tip)
    {
        if (ReduceMotion)
        {
            fadeFrom = Pose(now).Tip;
            fadeThrough = now;
            glide = PointerGlide.Still(now, tip);
            return;
        }
        fadeThrough = double.NaN;
        glide = glide.Retarget(now, tip);
    }

    private void Arrive(double now) => scale = Scale.Arriving(now, scale, ReduceMotion);

    private void Leave(double now) => scale = Scale.Leaving(now, scale, ReduceMotion);

    // A finished leave becomes hidden (or arrives on the other monitor of a hop) at the moment it ended.
    private void Advance(double now)
    {
        if (scale.Kind != ScaleKind.Leave || now < scale.EndsAt(ReduceMotion)) return;
        var ended = scale.EndsAt(ReduceMotion);
        goneAt = ended;
        scale = Scale.Gone;
        rippleAt = double.NaN;
        fadeThrough = double.NaN;
        if (hop is { } next)
        {
            hop = null;
            Monitor = next.Monitor;
            glide = PointerGlide.Still(ended, next.Tip);
            heldTarget = null;
            scale = Scale.Arriving(ended, Scale.Gone, ReduceMotion);
        }
    }

    private enum ScaleKind
    {
        Gone,
        Arrive,
        Still,
        Leave,
    }

    // The body's arrive and leave: a critically damped spring to full size, or an accelerating ease into the tip; with
    // Reduce Motion an opacity ease at full size instead.
    private readonly record struct Scale(ScaleKind Kind, PillCurve Curve, double From, double Start)
    {
        public static Scale Gone { get; } = new(ScaleKind.Gone, PillCurve.Still(0, 0), 0, 0);

        public static Scale Arriving(double now, Scale was, bool reduce)
        {
            var (value, opacity) = was.At(now, reduce);
            if (reduce) return new Scale(ScaleKind.Arrive, PillCurve.Ease(was.Kind == ScaleKind.Gone ? 0 : opacity, 1, now, ReducedSeconds), 0, now);
            var velocity = was.Kind == ScaleKind.Leave ? was.LeaveVelocity(now) : was.Curve.Velocity(now);
            return new Scale(ScaleKind.Arrive, PillCurve.Spring(was.Kind == ScaleKind.Gone ? 0 : value, 1, velocity, ArriveResponse, now, cap: SpringCap), 0, now);
        }

        public static Scale Leaving(double now, Scale was, bool reduce)
        {
            var (value, opacity) = was.At(now, reduce);
            return new Scale(ScaleKind.Leave, PillCurve.Still(0, now), reduce ? opacity : value, now);
        }

        public double EndsAt(bool reduce) => Start + (reduce ? ReducedSeconds : LeaveSeconds);

        /// <summary>The body's scale and opacity at <paramref name="now"/>.</summary>
        public (double Scale, double Opacity) At(double now, bool reduce)
        {
            switch (Kind)
            {
                case ScaleKind.Gone:
                    return (0, 0);
                case ScaleKind.Leave:
                    var duration = reduce ? ReducedSeconds : LeaveSeconds;
                    var left = From * (1 - PillEase.CubicBezier(0.4, 0, 1, 1, Math.Clamp((now - Start) / duration, 0, 1)));
                    return reduce ? (1, left) : (left, 1);
                default:
                    var value = Rest(Curve, now);
                    return reduce ? (1, value) : (value, 1);
            }
        }

        public bool Done(double now) => Kind != ScaleKind.Leave && Rests(Curve, now);

        public double LeaveVelocity(double now)
        {
            const double Step = 0.001;
            return (At(now, false).Scale - At(now - Step, false).Scale) / Step;
        }
    }
}
