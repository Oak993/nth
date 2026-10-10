// PORT-ONLY: OD-0136 amendment 2 (ED-NEW-nth-motion). The geometry and timing of every nth primitive as pure functions of
// time, transcribed from the review page's build notes (nth-motion-review.html, STEPS[].build) and, where a note leaves a
// number out, from the page's own canvas engine (SETUP / DRAW). Seconds are measured from the primitive's start. Nothing
// here allocates; the painters and the tests read the same functions.
using SkiaSharp;

namespace Marrow.UI.Port.Nth;

/// <summary>Where the spinner's line is on its tile track: from <see cref="Start"/> along the track for <see cref="Length"/>.</summary>
public readonly record struct NthTrace(double Start, double Length, double Alpha);

/// <summary>The thinking glyph's three arcs: the first one's centre angle, the angle between arcs, each arc's sweep (radians).</summary>
public readonly record struct NthArcs(double Rotation, double Spacing, double Sweep, double Alpha);

/// <summary>A segment of a bar between <see cref="X0"/> and <see cref="X1"/> drawn at <see cref="Alpha"/> (0: nothing).</summary>
public readonly record struct NthSegment(double X0, double X1, double Alpha);

/// <summary>A ring's done stage at one moment: the ring's opacity, the disc's radius scale and opacity, and how much of the tick is drawn.</summary>
public readonly record struct NthDone(double RingAlpha, double DiscScale, double DiscAlpha, double Tick);

public static class NthMotion
{
    // ---- step 1, spinner option B: tile trace ----

    /// <summary>Step 1 "Stroke": 1.6 px at 12, 2.0 at 16, 2.4 at 20 (the page: up to 12, up to 16, above).</summary>
    public static double SpinnerStroke(double size) => size <= 12 ? 1.6 : size <= 16 ? 2 : 2.4;

    /// <summary>Points on the tile track (superellipse n = 4, side = size - stroke - 1), from 12 o'clock clockwise, centred on 0.</summary>
    public const int TilePoints = 128;

    /// <summary>Point <paramref name="i"/> of <see cref="TilePoints"/> on the tile track of half-side <paramref name="half"/>.</summary>
    public static SKPoint TilePoint(double half, int i)
    {
        var theta = -Math.PI / 2 + 2 * Math.PI * i / TilePoints;
        double c = Math.Cos(theta), s = Math.Sin(theta);
        return new SKPoint((float)(half * Math.Sign(c) * Math.Pow(Math.Abs(c), .5)), (float)(half * Math.Sign(s) * Math.Pow(Math.Abs(s), .5)));
    }

    /// <summary>The tile track's half side for a spinner of <paramref name="size"/>.</summary>
    public static double TileHalf(double size) => (size - SpinnerStroke(size) - 1) / 2;

    /// <summary>
    /// Step 1 option B at <paramref name="seconds"/> on a track of length <paramref name="length"/>: a 1.6 s cycle where the
    /// head runs L*ease(u/0.62) and the tail L*ease((u-0.38)/0.62) (ease .65,0,.35,1), never shorter than 4 % of L, plus a drift
    /// of L per 4.5 s; it fades in over 0.3 s (.2,.8,.2,1). Calm: still, 30 % of the tile from 12 o'clock, opacity 0.6 to 1 every 2.4 s.
    /// </summary>
    public static NthTrace Spinner(double seconds, double length, bool calm)
    {
        if (calm) return new NthTrace(0, .3 * length, NthEase.CalmPulse(seconds));
        var u = NthEase.Phase(seconds, 1.6);
        var head = length * NthEase.InOut.Ease(u / .62);
        var tail = length * NthEase.InOut.Ease((u - .38) / .62);
        var start = tail + length * seconds / 4.5;
        start %= length;
        if (start < 0) start += length;
        return new NthTrace(start, Math.Max(.04 * length, head - tail), NthEase.Out.Ease(seconds / .3));
    }

    // ---- step 2, thinking option B: three arcs that gather and spread ----

    public const double GlyphStroke = 1.7;

    /// <summary>
    /// Step 2 "B glyph": three arcs on one circle turning 360 degrees per 2.2 s; every 1.4 s they gather and spread (gap 0.2 to
    /// 0.333 of a turn, arc 0.11 to 0.18), eased .65,0,.35,1 there and back; fade in 0.3 s. Calm: still and spread, the calm pulse.
    /// </summary>
    public static NthArcs Glyph(double seconds, bool calm)
    {
        double rotation, e, alpha;
        if (calm) { rotation = -Math.PI / 2; e = 1; alpha = NthEase.CalmPulse(seconds); }
        else
        {
            alpha = NthEase.Out.Ease(seconds / .3);
            rotation = 2 * Math.PI * seconds / 2.2 - Math.PI / 2;
            var u = NthEase.Phase(seconds, 1.4);
            e = NthEase.InOut.Ease(u < .5 ? u * 2 : 2 - u * 2);
        }
        return new NthArcs(rotation, 2 * Math.PI * (.2 + .1333 * e), 2 * Math.PI * (.11 + .07 * e), alpha);
    }

    /// <summary>
    /// Step 2 "B letters": letter <paramref name="index"/> lifts -1.5 px and back on a 2.6 s cycle, 55 ms after the one before
    /// (keyframes 0 %, 30 %, 100 % at rest and 11 % at -1.5 px, each part eased .45,0,.2,1).
    /// </summary>
    public static double LetterLift(double seconds, int index, bool calm)
    {
        if (calm) return 0;
        var local = seconds - index * .055;
        if (local < 0) return 0;
        var p = NthEase.Phase(local, 2.6);
        if (p < .11) return -1.5 * NthEase.Glide.Ease(p / .11);
        if (p < .30) return -1.5 * (1 - NthEase.Glide.Ease((p - .11) / .19));
        return 0;
    }

    /// <summary>A letter of a new word, <paramref name="seconds"/> after the change: (y offset, opacity). Rises from 5 px over 0.36 s
    /// (.2,.8,.2,1), 18 ms after the letter before. Calm: fades in over 0.25 s, linear.</summary>
    public static (double Y, double Alpha) LetterIn(double seconds, int index, bool calm)
    {
        if (calm) { var f = NthEase.Clamp01(seconds / .25); return (0, f); }
        var k = NthEase.Out.Ease((seconds - index * .018) / .36);
        return (5 * (1 - k), k);
    }

    /// <summary>A letter of the old word: leaves up 4 px over 0.24 s (.4,0,1,1), 12 ms after the letter before. Calm: fades out in 0.2 s.</summary>
    public static (double Y, double Alpha) LetterOut(double seconds, int index, bool calm)
    {
        if (calm) return (0, 1 - NthEase.Clamp01(seconds / .2));
        var k = NthEase.Accelerate.Ease((seconds - index * .012) / .24);
        return (-4 * k, 1 - k);
    }

    /// <summary>How long a word change lasts for a word of <paramref name="letters"/> letters (the last letter's end).</summary>
    public static double WordChangeSeconds(int letters, bool calm) => calm ? .25 : Math.Max(.24 + .012 * letters, .36 + .018 * letters);

    /// <summary>Step 2 "Timer": a changed digit rolls out (0.3 s, .4,0,1,1, up 75 % of the slot) and in (0.42 s, .2,.8,.2,1, from 75 %
    /// below). Returns (old y, old opacity, new y, new opacity) as fractions of the slot height. Calm: a 0.2 s cross-fade.</summary>
    public static (double OldY, double OldAlpha, double NewY, double NewAlpha) Roll(double seconds, bool calm)
    {
        if (calm) { var f = NthEase.Clamp01(seconds / .2); return (0, 1 - f, 0, f); }
        var o = NthEase.Accelerate.Ease(seconds / .3);
        var n = NthEase.Out.Ease(seconds / .42);
        return (-.75 * o, 1 - o, .75 * (1 - n), n);
    }

    public const double RollSeconds = .42;

    // ---- text light (step 2 A, used by "Reading the diff", tool rows, connecting) ----

    /// <summary>The light's half window (a 60 px window with a linear ramp to its centre).</summary>
    public const double ShineHalf = 30;

    /// <summary>
    /// The text light's centre over a word <paramref name="width"/> wide: it travels from -w to +w of the word's own box in 1.8 s
    /// (.45,0,.2,1) and rests 0.6 s (a 2.4 s cycle; <paramref name="period"/> lets a site use the page's own period). Null while it
    /// rests or in calm (no light).
    /// </summary>
    public static double? Shine(double seconds, double width, bool calm, double period = 2.4)
    {
        if (calm || width <= 0) return null;
        var a = NthEase.Phase(seconds, period) / .75;
        if (a >= 1) return null;
        // CSS: the window box (the word's own width) moves translateX(-100% to 100%); its centre is the box centre.
        return -width / 2 + 2 * width * NthEase.Glide.Ease(a);
    }

    // ---- step 3, progress bar option B ----

    /// <summary>The fill's opacity: it fades in over the first 1.5 * h px of its width.</summary>
    public static double BarFillAlpha(double fillWidth, double height) => fillWidth > .3 ? NthEase.Clamp01(fillWidth / (1.5 * height)) : 0;

    /// <summary>Step 3 "Light": the band's half width, min(60, 0.3 W).</summary>
    public static double BarLightHalf(double width) => Math.Min(60, width * .3);

    /// <summary>Step 3 "Light": the band centre inside a fill <paramref name="fillWidth"/> wide (2.6 s: travel 72 %, .45,0,.2,1; rest 28 %),
    /// or null while it rests, in calm, or once the bar is full.</summary>
    public static double? BarLight(double seconds, double fillWidth, double width, double value, bool calm)
    {
        if (calm || value >= .999 || fillWidth <= .3) return null;
        var a = NthEase.Phase(seconds, 2.6) / .72;
        if (a >= 1) return null;
        var half = BarLightHalf(width);
        return -half + (fillWidth + 2 * half) * NthEase.Glide.Ease(a);
    }

    /// <summary>
    /// Step 3 option B, unknown length: over 1.8 s the head runs W*ease(u/0.66) and the tail W*ease((u-0.34)/0.66) (.4,0,.2,1).
    /// The segment is clipped by the track and slides out through its round ends, fading when less than 1.5 * h is visible.
    /// Calm: a still middle segment (35 % to 65 %) whose opacity eases 0.55 to 1 every 2.4 s.
    /// </summary>
    public static NthSegment BarIndeterminate(double seconds, double width, double height, bool calm)
    {
        double x0, x1, alpha = 1;
        if (calm) { x0 = width * .35; x1 = width * .65; alpha = .55 + .45 * (.5 + .5 * Math.Cos(2 * Math.PI * seconds / 2.4)); }
        else
        {
            var u = NthEase.Phase(seconds, 1.8);
            x1 = width * NthEase.Standard.Ease(u / .66);
            x0 = width * NthEase.Standard.Ease((u - .34) / .66);
        }
        var visible = Math.Min(width, x1) - Math.Max(0, x0);
        if (visible <= .3) return new NthSegment(0, 0, 0);
        alpha *= NthEase.Clamp01(visible / (1.5 * height));
        if (x0 <= .5) x0 = Math.Min(x0, 0) - height;
        if (x1 >= width - .5) x1 = Math.Max(x1, width) + height;
        return new NthSegment(x0, Math.Max(x1, x0 + height), alpha);
    }

    // ---- step 4, progress ring ----

    /// <summary>Step 4 "Stroke": 1.75 / 2 / 2.25 px at 14 / 18 / 20 (and the 24 px puck ring, 2.25).</summary>
    public static double RingStroke(double size) => size <= 14 ? 1.75 : size <= 18 ? 2 : 2.25;

    public static double RingRadius(double size) => (size - RingStroke(size)) / 2 - .25;

    /// <summary>The done disc's radius (the page: s/2 - 0.4).</summary>
    public static double DiscRadius(double size) => size / 2 - .4;

    /// <summary>
    /// Step 4 "Done", <paramref name="seconds"/> after the value reached 100 %: the disc grows 0 to r in 0.36 s (.3,1.3,.5,1), the ring
    /// fades out in 0.15 s from 0.18 s, the tick draws in over 0.3 s from 0.16 s (.4,0,.2,1). Calm: disc and tick fade in.
    /// </summary>
    public static NthDone RingDone(double seconds, bool calm)
    {
        var ring = 1 - NthEase.Clamp01((seconds - .18) / .15);
        if (calm) return new NthDone(ring, 1, NthEase.Clamp01(seconds / .2), NthEase.Clamp01((seconds - .1) / .2));
        return new NthDone(ring, Math.Max(0, NthEase.Pop.Ease(seconds / .36)), 1, NthEase.Standard.Ease((seconds - .16) / .3));
    }

    public const double RingDoneSeconds = .46;

    /// <summary>Step 4 "Tick" on a disc of radius 1: (-0.42, 0.02), (-0.13, 0.31), (0.43, -0.29); stroke 0.24 r (at least 1.3 px).</summary>
    public static readonly SKPoint[] DiscTick = [new(-.42f, .02f), new(-.13f, .31f), new(.43f, -.29f)];

    public static double DiscTickStroke(double radius) => Math.Max(1.3, radius * .24);

    // ---- step 8, the done tick of a tool row ----

    /// <summary>Step 8 "Tick" in the 14 px box: (3.0, 7.2), (5.6, 9.8), (11.0, 4.6); stroke 1.8 px, round caps.</summary>
    public static readonly SKPoint[] RowTick = [new(3f, 7.2f), new(5.6f, 9.8f), new(11f, 4.6f)];

    public const double RowTickStroke = 1.8;

    /// <summary>Step 8 "Done": the tick draws in over 0.32 s (.4,0,.2,1). Calm: the whole tick fades in over 0.2 s. (drawn, opacity)</summary>
    public static (double Drawn, double Alpha) RowTickIn(double seconds, bool calm) =>
        calm ? (1, NthEase.Clamp01(seconds / .2)) : (NthEase.Standard.Ease(seconds / .32), seconds >= 0 ? 1 : 0);

    // ---- the status slot cross-fade (steps 8, 11, 15) ----

    /// <summary>A status mark leaving (the spinner fades 0.18 s; waiting/working/done cross-fade 0.2 s, CSS ease).</summary>
    public static double FadeOut(double seconds, double length = .2) => 1 - NthEase.Css.Ease(seconds / length);

    public static double FadeIn(double seconds, double length = .2) => NthEase.Css.Ease(seconds / length);

    // ---- steps 5 to 7, skeletons ----

    /// <summary>
    /// The skeleton light (step 5): a 2.2 s cycle, travelling for 70 % (.45,0,.2,1) and resting 30 %. Returns the travel fraction
    /// (0..1) or null while it rests or before <paramref name="seconds"/> reaches 0 (a row's delay).
    /// </summary>
    public static double? SkeletonLight(double seconds, bool calm, double period = 2.2, double travel = .7)
    {
        if (calm || seconds < 0) return null;
        var a = NthEase.Phase(seconds, period) / travel;
        return a >= 1 ? null : NthEase.Glide.Ease(a);
    }

    /// <summary>Step 5/7 "Enter": rows fade in top to bottom, 50 ms apart, over 0.35 s (.2,.8,.2,1).</summary>
    public static double RowEnter(double seconds, int row, bool calm) => calm ? 1 : NthEase.Out.Ease((seconds - row * .05) / .35);

    /// <summary>Step 5: the diff rows (indent, width as a share of what is left, sign: -1 minus, 1 plus, 0 none).</summary>
    public static readonly (int Indent, double Share, int Sign)[] DiffRows =
        [(0, .62, 0), (1, .78, 0), (1, .5, -1), (1, .66, 1), (2, .42, 1), (2, .71, 1), (1, .3, 0), (0, .55, 0)];

    /// <summary>Step 7: each list row's title bar share of the width less 40.</summary>
    public static readonly double[] ListShares = [.72, .55, .8, .62, .48];

    // ---- step 14, model colours tube ----

    /// <summary>Step 14 "Dots": dot <paramref name="index"/> (0..2) swells to r 2.8 in turn: 1.2 s cycle, 0.18 s apart, a sine in the first half. 0..1.</summary>
    public static double TubeDot(double seconds, int index, bool calm)
    {
        if (calm) return 0;
        var p = ((seconds / 1.2 - index * .18 + 3) % 1 + 1) % 1;
        return p < .5 ? Math.Sin(Math.PI * p / .5) : 0;
    }

    /// <summary>Step 14 "Light": the band centre (1.6 s cycle, .45,0,.2,1 across the whole cycle, band +-30 px).</summary>
    public static double? TubeLight(double seconds, double width, bool calm) =>
        calm ? null : -30 + (width + 60) * NthEase.Glide.Ease(NthEase.Phase(seconds, 1.6));

    // ---- step 11, the Codex link ----

    /// <summary>Step 11 "Codex link": dash 2.5 / gap 4.5 flowing 14 px/s toward Codex; connected: the gap closes in 0.35 s (.4,0,.2,1).
    /// Returns (dash, gap, phase).</summary>
    public static (double Dash, double Gap, double Phase) Link(double seconds, double? connectedSeconds, bool calm)
    {
        var g = connectedSeconds is { } c ? NthEase.Standard.Ease(c / .35) : 0;
        var phase = calm ? 0 : seconds * 14 % 7;
        return (2.5 + 4.5 * g, 4.5 * (1 - g), phase);
    }

    // ---- step 9, the send button ----

    /// <summary>Step 9 "Send": arrow y 0 to -20 (scaled), opacity to 0, 0.3 s (.5,0,.75,0). (y fraction of 20, opacity)</summary>
    public static (double Y, double Alpha) ArrowLaunch(double seconds, bool calm)
    {
        if (calm) return (0, 1 - NthEase.Clamp01(seconds / .2));
        var k = NthEase.Launch.Ease(seconds / .3);
        return (-20 * k, 1 - k);
    }

    /// <summary>Step 9 "Stop": the arrow drops back from -14, 0.42 s (.2,.9,.3,1.2), 0.08 s after the stop. (y, opacity)</summary>
    public static (double Y, double Alpha) ArrowDrop(double seconds, bool calm)
    {
        if (calm) return (0, NthEase.Clamp01(seconds / .2));
        var k = NthEase.Drop.Ease((seconds - .08) / .42);
        return (-14 * (1 - k), NthEase.Clamp01(k));
    }

    /// <summary>Step 9 "Send" + "Working": the stop square pops in (scale 0.3 to 1, 0.42 s, .3,1.4,.5,1, 0.1 s later), then breathes
    /// (1, 0.84, 1 every 2.2 s, ease-in-out, from 0.62 s). (scale, opacity)</summary>
    public static (double Scale, double Alpha) StopIn(double seconds, bool calm)
    {
        if (calm) return (1, NthEase.Clamp01(seconds / .2));
        if (seconds < .62)
        {
            var k = NthEase.PopIn.Ease((seconds - .1) / .42);
            return (.3 + .7 * k, NthEase.Clamp01(k));
        }
        var p = NthEase.Phase(seconds - .62, 2.2);
        var e = p < .5 ? NthEase.EaseInOut.Ease(p / .5) : 1 - NthEase.EaseInOut.Ease((p - .5) / .5);
        return (1 - .16 * e, 1);
    }

    /// <summary>Step 9 "Stop": the square shrinks to 0.2 and fades in 0.2 s (.4,0,1,1). (scale, opacity)</summary>
    public static (double Scale, double Alpha) StopOut(double seconds, bool calm)
    {
        if (calm) return (1, 1 - NthEase.Clamp01(seconds / .2));
        var k = NthEase.Accelerate.Ease(seconds / .2);
        return (1 - .8 * k, 1 - k);
    }

    /// <summary>Step 9 "Press": scale 0.9 over 0.16 s (.2,.8,.2,1).</summary>
    public static double Press(double seconds, bool pressed) =>
        pressed ? 1 - .1 * NthEase.Out.Ease(seconds / .16) : .9 + .1 * NthEase.Out.Ease(seconds / .16);
}
