namespace Marrow.UI.Port;

// PORT-ONLY: the nth boot and hero motion as pure functions of time.
// Boot: OD-0142 (owner, 2026-10-11) replaces OD-0136's boot motion (gather, draw, drop, hot sweep, breath, the hold's slow
// sweeps): the shine is gone. The silver tile pops toward the viewer, the n draws inside it, then the bead appears:
//   tile    0s     .62s  a damped spring (zeta .62): scale .86 to 1 (peak about 1.012), opacity 0 to 1 over .30 s
//                        (cubic-bezier(.2,.7,.3,1)); the drop shadow lifts with the same spring (grows, softens, falls further)
//   light   0s    1.36s  the tile's own gradient turns from -22 degrees to its rest angle, cubic-bezier(.45,0,.25,1)
//                        (owner follow-up 2026-10-11: the gradient inside the tile moves; no band, no streak)
//   n draw  .40s   .56s  trim path from the left foot over the arch to the right foot, cubic-bezier(.55,0,.3,1); a round head
//                        leads and lands flush on the butt foot of the finished glyph
//   bead    .96s   .40s  a damped spring (zeta .6): scale .4 to 1 (peak about 1.06), opacity 0 to 1 over .18 s
// Settled at 1.36 s; the tile then holds still, exactly the app icon's stage tile, until the unchanged hand-over (at least
// 1.8 s, AuroraBootSequence.HandOver). Reduced motion: the finished mark (the stage cross-fades it in).
// Hero: the empty chat's 44 px tile (.hero .mk) sweeps once with the soft band of .mk::after (OD-0136, unchanged; ENGINEERING
// DECISION: one pass of the page's once-only sweep (@keyframes sweepOnce, .6 s, cubic-bezier(.45,0,.2,1)) starting .3 s after
// the tile appears, as the prompt's .3 s fade-in ends (NewThreadPrompt.swift:50-53), instead of the page's demo loop: the
// tile is still .9 s after it appears, so the idle GPU warm-up sees a quiet window (ED-252)).
public static class NthMotion
{
    /// <summary>The tile's spring: its length, its first scale, its damping, and how long its fade-in takes.</summary>
    public const double TileSeconds = .62, TileFrom = .86, TileDamping = .62, TileFadeSeconds = .30;
    /// <summary>The n's trim path: when it starts (about 65% through the tile's spring) and how long it takes.</summary>
    public const double DrawStart = .40, DrawSeconds = .56, DrawEnd = DrawStart + DrawSeconds;
    /// <summary>The bead's spring: it starts when the n is complete.</summary>
    public const double BeadStart = DrawEnd, BeadSeconds = .40, BeadFrom = .4, BeadDamping = .6, BeadFadeSeconds = .18;
    /// <summary>When the boot mark's last move (the bead's spring) ends; the tile's gradient reaches its rest angle here too.</summary>
    public const double Settled = BeadStart + BeadSeconds;
    /// <summary>The gradient's starting turn (degrees, clockwise) away from the tile's rest angle.</summary>
    public const double LightFrom = -22;
    /// <summary>The empty chat's one sweep: its delay and its end.</summary>
    public const double HeroSweepDelay = .3, HeroSweepSeconds = .6, HeroDone = HeroSweepDelay + HeroSweepSeconds;

    // omega * T for each spring: e^(-zeta omega T) is below .006, so the closing correction is too small to see.
    private const double TileOmega = 9.4, BeadOmega = 9.0;

    public static NthPose Boot(double seconds, bool reduceMotion)
    {
        if (reduceMotion || !double.IsFinite(seconds) || seconds >= Settled) return NthPose.Still;
        var s = Math.Max(0, seconds);
        var tile = Spring(s / TileSeconds, TileDamping, TileOmega);
        var draw = Ease((s - DrawStart) / DrawSeconds, .55, 0, .3, 1);
        var bead = Spring((s - BeadStart) / BeadSeconds, BeadDamping, BeadOmega);
        var beadOpacity = s < BeadStart ? 0 : Ease((s - BeadStart) / BeadFadeSeconds, .2, .7, .3, 1);
        var light = LightFrom * (1 - Ease(s / Settled, .45, 0, .25, 1));
        return new NthPose(draw, beadOpacity, BeadFrom + (1 - BeadFrom) * bead, 0,
            TileFrom + (1 - TileFrom) * tile, Ease(s / TileFadeSeconds, .2, .7, .3, 1), tile, light);
    }

    /// <summary>The empty chat's tile, <paramref name="seconds"/> after the chat opened: still, with one soft sweep.</summary>
    public static NthPose Hero(double seconds, bool reduceMotion) =>
        reduceMotion || !double.IsFinite(seconds) || seconds >= HeroDone ? NthPose.Still
            : NthPose.Still with { Sweep = Ease((seconds - HeroSweepDelay) / HeroSweepSeconds, .45, 0, .2, 1) };

    /// <summary>The CSS cubic-bezier at linear progress <paramref name="x"/> (clamped to 0..1; y may overshoot).</summary>
    public static double Ease(double x, double x1, double y1, double x2, double y2) =>
        x <= 0 ? 0 : x >= 1 ? 1 : CometEasing.Bezier(x, x1, y1, x2, y2).Value;

    /// <summary>An under-damped spring from 0 to 1 over linear progress <paramref name="u"/> (clamped to 0..1): exactly 0 at the
    /// start and exactly 1 from the end on (the tiny remainder at the end is closed by a cubic that is flat at the start).</summary>
    public static double Spring(double u, double damping, double omega)
    {
        if (!(u > 0)) return 0;
        if (u >= 1) return 1;
        return Raw(u, damping, omega) + (1 - Raw(1, damping, omega)) * u * u * u;
        static double Raw(double u, double z, double w)
        {
            var wd = w * Math.Sqrt(1 - z * z);
            return 1 - Math.Exp(-z * w * u) * (Math.Cos(wd * u) + z * w / wd * Math.Sin(wd * u));
        }
    }
}
