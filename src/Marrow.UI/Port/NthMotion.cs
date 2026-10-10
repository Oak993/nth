namespace Marrow.UI.Port;

// PORT-ONLY: OD-0136, the nth boot and hero motion as pure functions of time. Source: work/design/nth-brand/nth-brand.html,
// the .stage.play rules and their keyframes (a CSS timing function eases each keyframe interval of each property on its own):
//   .bi gather    0s    .6s  cubic-bezier(.2,.8,.2,1)  opacity 0 to 1 by 55%; scale .8 to 1 and blur(14px) to 0 over the whole
//   .nB draw     .38s  .72s  cubic-bezier(.65,0,.35,1) stroke-dashoffset 100 to 0
//   .bd drop     1.0s   .5s  cubic-bezier(.34,1.56,.64,1) opacity 0 to 1, translateY(-36) scale(.3) to none (its one overshoot)
//   .sw sweep    1.18s  .6s  cubic-bezier(.45,0,.2,1)  background-position 100% to 0
//   .bs settle   1.36s  .5s  ease-in-out               scale 1, 1.035 at 45%, 1 (one breath)
// The window then opens at the existing hand-over (AuroraBootSequence: at least 1.8 s, unchanged). "If it needs longer, the tile
// holds and the sweep repeats slowly": ENGINEERING DECISION, the hold repeats the page's slow loop sweep (.mk.loop::after: the soft
// band, a 1.2 s pass, cubic-bezier(.45,0,.2,1)) every 3 s from 4.18 s. Reduced motion: the finished mark (the stage fades it in).
// The empty chat's 44 px tile (.hero .mk) sweeps once with the soft band of .mk::after. ENGINEERING DECISION: one pass of the
// page's once-only sweep (@keyframes sweepOnce, .6 s, cubic-bezier(.45,0,.2,1)) starting .3 s after the tile appears, as the
// prompt's .3 s fade-in ends (NewThreadPrompt.swift:50-53), instead of the page's demo loop (1 s delay, a 1.2 s pass every 6 s):
// the tile is still .9 s after it appears, so the opening's motion has run out and the idle GPU warm-up sees a quiet window
// (ED-252; EffortSliderJewelWarmupTests' window at rest, where the prompt mounts about .5 s into its 2 s).
public static class NthMotion
{
    /// <summary>When the boot mark's last move (the breath) ends.</summary>
    public const double Settled = 1.86;
    /// <summary>The hold's slow sweeps: the first starts here, then one every <see cref="HoldPeriod"/>.</summary>
    public const double HoldSweepStart = 4.18, HoldPeriod = 3, SoftSweepSeconds = 1.2;
    /// <summary>The empty chat's one sweep: its delay and its end.</summary>
    public const double HeroSweepDelay = .3, HeroSweepSeconds = .6, HeroDone = HeroSweepDelay + HeroSweepSeconds;

    public static NthPose Boot(double seconds, bool reduceMotion)
    {
        if (reduceMotion || !double.IsFinite(seconds)) return NthPose.Still;
        var s = Math.Max(0, seconds);
        var gather = Ease(s / .6, .2, .8, .2, 1);
        var gatherOpacity = Ease(s / (.6 * .55), .2, .8, .2, 1);
        var draw = Ease((s - .38) / .72, .65, 0, .35, 1);
        var drop = Ease((s - 1.0) / .5, .34, 1.56, .64, 1);
        var dropped = s >= 1.0;
        double sweep; var kind = NthSweep.Hot;
        if (s < HoldSweepStart) sweep = Ease((s - 1.18) / .6, .45, 0, .2, 1);
        else { sweep = Ease(((s - HoldSweepStart) % HoldPeriod) / SoftSweepSeconds, .45, 0, .2, 1); kind = NthSweep.Soft; }
        var settle = Math.Clamp((s - 1.36) / .5, 0, 1);
        var breath = 1 + .035 * (settle < .45 ? Ease(settle / .45, .42, 0, .58, 1) : 1 - Ease((settle - .45) / .55, .42, 0, .58, 1)); // exactly 1 when done
        return new NthPose(draw, dropped ? Math.Clamp(drop, 0, 1) : 0, -36 * (1 - drop), .3 + .7 * drop, sweep, kind,
            breath, gatherOpacity, .8 + .2 * gather, NthMark.GatherSigma * (1 - gather));
    }

    /// <summary>The empty chat's tile, <paramref name="seconds"/> after the chat opened: still, with one soft sweep.</summary>
    public static NthPose Hero(double seconds, bool reduceMotion) =>
        reduceMotion || !double.IsFinite(seconds) || seconds >= HeroDone ? NthPose.Still
            : NthPose.Still with { Sweep = Ease((seconds - HeroSweepDelay) / HeroSweepSeconds, .45, 0, .2, 1), SweepKind = NthSweep.Soft };

    /// <summary>The CSS cubic-bezier at linear progress <paramref name="x"/> (clamped to 0..1; y may overshoot).</summary>
    public static double Ease(double x, double x1, double y1, double x2, double y2) =>
        x <= 0 ? 0 : x >= 1 ? 1 : CometEasing.Bezier(x, x1, y1, x2, y2).Value;
}
