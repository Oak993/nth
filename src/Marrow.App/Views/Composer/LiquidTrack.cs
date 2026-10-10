using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.UI.Port;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// DEVIATION OD-0047 / OD-0048 / OD-0051 (approved and locked by the owner, 2026-10-02): at maximum effort, and for a
// pair, the effort track is a glass tube of living liquid instead of EffortSlider.swift's particle fill and fusion
// (TrackEffect, :1045-1335). The reference is the owner's canvas, copy at
// E:\Droppy\work\design\effort-liquid-v18-gpu.dc.html, whose CSS defines the look and whose WebGL renderer draws it; this
// file is that renderer in SkSL, the same mathematics layer by layer:
//   the liquid: the CSS gradient interpolated in OKLab (computed once per width on the CPU), under the 4.8 s "breathe"
//     (saturate 1 to 1.12, brightness 1 to 1.06, eased in and out);
//   the currents: blurred sine strokes (SVG viewBox 2000 wide, stdDeviation 2, round caps) over twice the track's width,
//     sliding left at a constant pace, soft-light at 0.55 and 0.35; a pair's first current is one coloured strand per
//     model (title colours, the lead's stronger), as on the canvas;
//   the caustic light: four soft white ellipses on a tile twice the track's width, overlay at 0.8, panning to and fro;
//   Grok's oil: a 110-degree iridescent band on a 260 % tile, screen at 0.9, its stretch masked at both ends in a blend;
//   the bubbles: each a CSS bubble (radial highlight, 1 px rim, soft white halo) on the "wander" keyframes, every
//     segment eased cubic-bezier(.35,.1,.55,1), spread evenly along the track;
//   the glass: the displacement map (after rizzytoday/liquid-glass, MIT: red across, blue down, a soft neutral middle)
//     bending each colour channel by -36, -32 and -28 px, so the rim refracts with a faint fringe; both rounded ends
//     sample inward, so the liquid always reaches the ends;
//   the tube's gloss (light above, shade below) and its inset rim lights.
// Everything is a function of continuous time, so nothing loops visibly or jumps. Light on hardware: one pass, one
// small track, at 60 Hz only while the window is visible.
// DEVIATION OD-0073 (ED-248): in fast mode the currents and the caustic run 1.5x (the Strike canvas's --cf 4.67 s,
// --sf 7.33 s, --cp 4 s; the breathe and the bubbles keep their pace), and the Strike lightning (LiquidStrike.cs) is
// screen-blended over the liquid, under the gloss and the rims, unbent by the glass (on the canvas it is a plain layer
// over the liquid): the glass pass samples it once, at the pixel itself.

/// <summary>
/// How far the liquid's moving layers have gone, in passes: the first current (7 s a pass, 4.67 s in fast mode), the
/// slow current (11 s, 7.33 s) and the caustic's sweeps (6 s each way, 4 s).
/// </summary>
public readonly record struct LiquidFlow(double Fast, double Slow, double Caustic)
{
    public static (double Fast, double Slow, double Caustic) Periods(bool fastMode) => fastMode ? (4.67, 7.33, 4) : (7, 11, 6);

    /// <summary>The flow of a track that has run at one pace since time 0.</summary>
    public static LiquidFlow At(double time, bool fastMode)
    {
        var (fast, slow, caustic) = Periods(fastMode);
        return new LiquidFlow(time / fast, time / slow, time / caustic);
    }
}

/// <summary>
/// A track's flow across fast-mode changes: a change of pace keeps every layer where it is and only changes its speed
/// from then on, so a pair's track (which keeps its view when fast mode is toggled) never jumps.
/// </summary>
public sealed class LiquidPace
{
    private bool? fastMode;
    private LiquidFlow offset;

    public LiquidFlow At(double time, bool fast)
    {
        if (fastMode is { } was && was != fast)
        {
            var before = Flow(time, was);
            var after = LiquidFlow.At(time, fast);
            offset = new LiquidFlow(before.Fast - after.Fast, before.Slow - after.Slow, before.Caustic - after.Caustic);
        }
        fastMode = fast;
        return Flow(time, fast);
    }

    private LiquidFlow Flow(double time, bool fast)
    {
        var flow = LiquidFlow.At(time, fast);
        return new LiquidFlow(flow.Fast + offset.Fast, flow.Slow + offset.Slow, flow.Caustic + offset.Caustic);
    }
}

/// <summary>One stretch of the liquid: its colours (left to right) and its share of the track.</summary>
public sealed record LiquidSegment(IReadOnlyList<SwiftColor> Colors, double Weight, bool Oil, SwiftColor Strand, bool Lead);

/// <summary>What the liquid tube draws, built from a <see cref="TrackLook"/>.</summary>
public sealed record LiquidSpec(IReadOnlyList<LiquidSegment> Segments)
{
    /// <summary>OD-0048: a lead's stretch, in workers' stretches.</summary>
    public const double LeadShare = 1.6;

    /// <summary>True when the spec blends more than one model.</summary>
    public bool IsBlend => Segments.Count > 1;

    /// <summary>
    /// The colour stops along the track (position 0..1). One model: its three liquid stops, end to end. A blend (the
    /// canvas formula): each model's colour held over the middle third of its stretch, the first and last also at the
    /// ends, so neighbours mix across the thirds between, in OKLab.
    /// </summary>
    public IReadOnlyList<(SwiftColor Color, double At)> Stops()
    {
        var list = new List<(SwiftColor, double)>();
        if (!IsBlend)
        {
            var only = Segments[0].Colors;
            for (var i = 0; i < only.Count; i++) list.Add((only[i], only.Count == 1 ? 0 : i / (double)(only.Count - 1)));
            return list;
        }
        var total = Segments.Sum(s => s.Weight);
        double at = 0;
        for (var k = 0; k < Segments.Count; k++)
        {
            var s = Segments[k];
            var from = at / total; at += s.Weight; var to = at / total; var w = to - from;
            if (k == 0) list.Add((s.Colors[0], 0));
            var heartFrom = from + w / 3; var heartTo = to - w / 3;
            if (s.Colors.Count == 1) { list.Add((s.Colors[0], heartFrom)); list.Add((s.Colors[0], heartTo)); }
            else for (var i = 0; i < s.Colors.Count; i++) list.Add((s.Colors[i], heartFrom + (heartTo - heartFrom) * i / (s.Colors.Count - 1)));
            if (k == Segments.Count - 1) list.Add((s.Colors[^1], 1));
        }
        return list;
    }

    /// <summary>The oil sheen's stretch along the track (0..1), or null when no model carries it.</summary>
    public (double From, double To)? OilRange()
    {
        // ED-248 review: read every frame, so indexed loops (no enumerator, no LINQ)
        double total = 0;
        for (var i = 0; i < Segments.Count; i++) total += Segments[i].Weight;
        double at = 0;
        for (var i = 0; i < Segments.Count; i++)
        {
            var s = Segments[i];
            var from = at / total; at += s.Weight;
            if (s.Oil) return (from, at / total);
        }
        return null;
    }

    /// <summary>
    /// The spec for a look: a provider's own liquid at maximum, or OD-0048's blend for a pair. A pair is one lead (the
    /// thread's provider, against the knob, 1.6 times the heads' stretch) and its heads: their own provider's colour when
    /// they run elsewhere, else the roster's colours across their stretch.
    /// </summary>
    public static LiquidSpec For(TrackLook look)
    {
        var brand = look.Brand;
        if (look.Pair is not { } pair)
            return new LiquidSpec([new LiquidSegment(brand.LiquidStops.ToArray(), 1, brand.HasOilSheen, brand.TitleColor, true)]);
        var lead = new LiquidSegment([brand.LiquidStops[1]], LeadShare, brand.HasOilSheen, brand.TitleColor, true);
        var heads = pair.HeadsBrand is { } headsBrand
            ? new LiquidSegment([headsBrand.LiquidStops[1]], 1, headsBrand.HasOilSheen, headsBrand.TitleColor, false)
            : new LiquidSegment(pair.Colors.ToArray(), 1, false, pair.TitleColors.Count > 0 ? pair.TitleColors[0] : SwiftColor.White, false);
        return new LiquidSpec([heads, lead]); // workers to the left, the lead at the knob end
    }
}

/// <summary>One rise of one bubble, as the canvas draws it: where its box sits, its "wander" keyframe values, and the
/// pause before the bubble's next rise. A value (ED-248 review: a new rise allocates nothing).</summary>
public sealed record LiquidBubble(double Left, double Bottom, double Size, double Duration, double Opacity,
    double S0, double S1, double S2, Point P1, Point P2, Point P3, double Gap)
{
    /// <summary>The border box's side: the content plus a 1 px rim each side.</summary>
    public double Outer => Size + 2;
}

// Value storage on the frame path; public snapshots retain the existing inspection API.
public readonly record struct LiquidBubbleValues(double Left, double Bottom, double Size, double Duration, double Opacity,
    double S0, double S1, double S2, Point P1, Point P2, Point P3, double Gap)
{
    /// <summary>The border box's side: the content plus a 1 px rim each side.</summary>
    public double Outer => Size + 2;
    public LiquidBubble Snapshot() => new(Left, Bottom, Size, Duration, Opacity, S0, S1, S2, P1, P2, P3, Gap);
}

/// <summary>
/// A track's bubbles, each living rise after rise, every rise new (the canvas's riseOf/bubbleNow). The state only moves
/// forward with time; asking for an earlier time starts the lanes again from their first rise.
/// </summary>
public sealed class BubbleLanes(int seed, double width, double height)
{
    private readonly (int K, LiquidBubbleValues Rise, double Start)?[] lanes = new (int, LiquidBubbleValues, double)?[Math.Clamp((int)Math.Round(width / 32), 4, LiquidTrack.MaxBubbles)];

    // OD-0078 (strike-engine.js laneAt): each lane keeps its last four rises, so a look a little into the past (a contact
    // between two frames) finds the rise as it was; a seek further back starts the lane again
    private readonly (int K, LiquidBubbleValues Rise, double Start)[][] history = Enumerable.Range(0, Math.Clamp((int)Math.Round(width / 32), 4, LiquidTrack.MaxBubbles))
        .Select(_ => new (int, LiquidBubbleValues, double)[4]).ToArray();
    private readonly int[] kept = new int[Math.Clamp((int)Math.Round(width / 32), 4, LiquidTrack.MaxBubbles)];

    private readonly (int K, LiquidBubble Rise)?[] inspected = new (int, LiquidBubble)?[Math.Clamp((int)Math.Round(width / 32), 4, LiquidTrack.MaxBubbles)];

    public (LiquidBubble Rise, double Progress, int K)? At(int i, double time)
    {
        if (AtFrame(i, time) is not { } frame) return null;
        if (inspected[i] is not { } keptRise || keptRise.K != frame.K)
            inspected[i] = (frame.K, frame.Rise.Snapshot());
        return (inspected[i]!.Value.Rise, frame.Progress, frame.K);
    }

    public int Seed => seed;

    /// <summary>How many times a look into the past went further back than a lane's history and started it again (tests:
    /// the history's cost; the bubbles found are the same either way).</summary>
    internal int Restarts { get; private set; }

    /// <summary>How many bubbles the track has: about one per 32 px, between 4 and 24.</summary>
    public int Count => lanes.Length;
    public double Width => width;
    public double Height => height;

    /// <summary>Bubble <paramref name="i"/> at a time: its current rise, how far through it and its number, or null between rises.</summary>
    internal (LiquidBubbleValues Rise, double Progress, int K)? AtFrame(int i, double time)
    {
        var lane = lanes[i];
        if (lane is { } shown && time < shown.Start)
        {
            for (var j = kept[i] - 1; j >= 0; j--)
            {
                var old = history[i][j];
                if (time < old.Start) continue;
                var p = (time - old.Start) / old.Rise.Duration;
                return p is >= 0 and < 1 ? (old.Rise, p, old.K) : null;
            }
            kept[i] = 0; Restarts++;
        }
        if (lane is not { } current || time < current.Start)
        {
            // the first rise is already under way when the track appears, at a random point of it
            var first = LiquidTrack.RiseValues(seed, i, 0, Count, width, height);
            current = (0, first, -LiquidTrack.RiseValues(seed, i, 1000, Count, width, height).Gap / 1.4 * first.Duration);
        }
        while (time >= current.Start + current.Rise.Duration + current.Rise.Gap)
        {
            if (kept[i] == 4) { Array.Copy(history[i], 1, history[i], 0, 3); kept[i] = 3; }
            history[i][kept[i]++] = current;
            var k = current.K + 1;
            current = (k, LiquidTrack.RiseValues(seed, i, k, Count, width, height), current.Start + current.Rise.Duration + current.Rise.Gap);
        }
        lanes[i] = current;
        var progress = (time - current.Start) / current.Rise.Duration;
        return progress is >= 0 and < 1 ? (current.Rise, progress, current.K) : null;
    }
}

/// <summary>The liquid tube's drawing (see the file note).</summary>
public static partial class LiquidTrack
{
    /// <summary>The glass: each channel's displacement scale, px (red, green, blue).</summary>
    public static readonly (double R, double G, double B) Refraction = (-36, -32, -28);

    /// <summary>
    /// Rise <paramref name="rise"/> of bubble <paramref name="bubble"/> on track <paramref name="seed"/>, drawn from its own
    /// seed (the canvas's riseOf): its size (mostly tiny, some small, a few medium), how high it goes, whether it drifts
    /// with the current or wanders up to one side, its three waypoints, its speed, its brightness, where along its stretch
    /// of the track it starts (each bubble keeps one stretch, so they stay spread out; one rise in five starts anywhere),
    /// and the pause before the next rise. Durations and pauses are random, so the pattern never comes round again.
    /// </summary>
    public static LiquidBubble Rise(int seed, int bubble, int rise, int count, double width, double height) => RiseValues(seed, bubble, rise, count, width, height).Snapshot();

    internal static LiquidBubbleValues RiseValues(int seed, int bubble, int rise, int count, double width, double height)
    {
        var x = unchecked((uint)(seed * 73856093) ^ (uint)((bubble + 1) * 19349663) ^ (uint)((rise + 7) * 83492791));
        if (x == 0) x = 1;
        double R() { x ^= x << 13; x ^= x >> 17; x ^= x << 5; return x % 100000 / 100000.0; }
        for (var w = 0; w < 3; w++) R();
        var kind = R();
        var size = kind < 0.6 ? 1.4 + R() * 1.6 : kind < 0.9 ? 3 + R() * 2 : 5 + R() * 3;
        var up = height * (0.55 + R() * 0.8);
        var side = (R() - 0.5) * 2;
        var flow = R() < 0.25; // a quarter drift along with the current; the rest rise and wander
        var x1 = flow ? 14 + R() * 24 : side * (4 + R() * 10);
        var x2 = flow ? 30 + R() * 40 : side * (2 + R() * 14) + (R() - 0.5) * 10;
        var x3 = flow ? 50 + R() * 60 : (R() - 0.5) * 30;
        var y1 = -(up * (0.2 + R() * 0.2));
        var y2 = -(up * (0.45 + R() * 0.25));
        var y3 = -up;
        var duration = (flow ? 3.6 : 1.8) + R() * 3.2;
        var slot = bubble * Stride(count) % count;
        var at = R() < 0.2 ? 3 + R() * 92 : 3 + (slot + R()) / count * 92;
        var left = width * at / 100 - (flow ? x3 / 2 : 0);
        var bottom = -6 + R() * (height * 0.45);
        var opacity = 0.55 + R() * 0.4;
        double s0 = 0.35 + R() * 0.3, s1 = 0.7 + R() * 0.3, s2 = 0.9 + R() * 0.35;
        return new LiquidBubbleValues(left, bottom, size, duration, opacity, s0, s1, s2, new Point(x1, y1), new Point(x2, y2), new Point(x3, y3), R() * 1.4);
    }

    private static readonly int[] Strides = [7, 5, 3, 11, 13];

    /// <summary>
    /// The step that hands each bubble its own stretch in a scattered order: 7 as on the canvas, or the next of 5, 3, 11, 13
    /// that shares no factor with the count, so no two bubbles ever share a stretch (7 bubbles at a step of 7 would).
    /// </summary>
    public static int Stride(int count)
    {
        static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
        foreach (var s in Strides) if (Gcd(s, count) == 1) return s; // (a static array: a new rise allocates nothing, ED-248 review)
        return 1;
    }

    /// <summary>CSS cubic-bezier timing: the eased progress at <paramref name="x"/>.</summary>
    public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        double cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx, cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by;
        double X(double t) => ((ax * t + bx) * t + cx) * t;
        var u = x;
        for (var i = 0; i < 8; i++)
        {
            var fx = X(u) - x; var dx = (3 * ax * u + 2 * bx) * u + cx;
            if (Math.Abs(fx) < 1e-7 || Math.Abs(dx) < 1e-7) break;
            u -= fx / dx;
        }
        if (u < 0 || u > 1 || Math.Abs(X(u) - x) > 1e-6)
        {
            double lo = 0, hi = 1; u = x;
            for (var i = 0; i < 40; i++) { if (X(u) < x) lo = u; else hi = u; u = (lo + hi) / 2; }
        }
        return ((ay * u + by) * u + cy) * u;
    }

    private static readonly double[] TransformKeys = [0, 0.38, 0.70, 1], OpacityKeys = [0, 0.12, 0.70, 1];

    /// <summary>
    /// A rise at a point of its progress (0..1): the bubble's centre, its radius (border box, scaled) and its opacity. The "wander" keyframes: transform
    /// at 0/38/70/100 %, opacity at 0/12/70/100 %, each segment eased cubic-bezier(.35,.1,.55,1).
    /// </summary>
    public static (Point Centre, double Radius, double Opacity) BubbleAt(LiquidBubble b, double height, double progress) => BubbleAt(new LiquidBubbleValues(b.Left, b.Bottom, b.Size, b.Duration, b.Opacity, b.S0, b.S1, b.S2, b.P1, b.P2, b.P3, b.Gap), height, progress);

    internal static (Point Centre, double Radius, double Opacity) BubbleAt(LiquidBubbleValues b, double height, double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        // ED-248 review: no arrays made per bubble per frame: the keyframes' offsets are static arrays (a collection
        // expression here made new ones every call, measured) and the values are picked by index
        static (int I, double F) Segment(double p, ReadOnlySpan<double> keys)
        {
            var i = 0;
            while (i < keys.Length - 2 && p > keys[i + 1]) i++;
            return (i, CubicBezier(0.35, 0.1, 0.55, 1, (p - keys[i]) / (keys[i + 1] - keys[i])));
        }
        var (ti, tf) = Segment(p, TransformKeys);
        Point At(int i) => i switch { 0 => new Point(0, 0), 1 => b.P1, 2 => b.P2, _ => b.P3 };
        double Scale(int i) => i switch { 0 => b.S0, 1 or 2 => b.S1, _ => b.S2 };
        double Opacity(int i) => i is 1 or 2 ? b.Opacity : 0;
        var translate = new Point(At(ti).X + (At(ti + 1).X - At(ti).X) * tf, At(ti).Y + (At(ti + 1).Y - At(ti).Y) * tf);
        var sc = Scale(ti) + (Scale(ti + 1) - Scale(ti)) * tf;
        var (oi, of) = Segment(p, OpacityKeys);
        var o = Opacity(oi) + (Opacity(oi + 1) - Opacity(oi)) * of;
        var centre = new Point(b.Left + b.Outer / 2 + translate.X, height - b.Bottom - b.Outer / 2 + translate.Y);
        return (centre, b.Outer / 2 * sc, Math.Max(0, o));
    }

    /// <summary>The canvas's "breathe": (saturate, brightness) at a time, 4.8 s, each half eased in and out.</summary>
    public static (double Saturate, double Brightness) Breathe(double time)
    {
        var p = time % 4.8 / 4.8;
        var h = p < 0.5 ? CubicBezier(0.42, 0, 0.58, 1, p / 0.5) : 1 - CubicBezier(0.42, 0, 0.58, 1, (p - 0.5) / 0.5);
        return (1 + 0.12 * h, 1 + 0.06 * h);
    }

    /// <summary>An alternating CSS animation's eased progress (ease-in-out, period seconds per direction).</summary>
    public static double Alternate(double time, double period) => AlternateAt(time / period);

    /// <summary>The same, <paramref name="sweeps"/> sweeps in (one sweep each way per period).</summary>
    public static double AlternateAt(double sweeps)
    {
        var q = sweeps % 2;
        return CubicBezier(0.42, 0, 0.58, 1, q <= 1 ? q : 2 - q);
    }

    /// <summary>The currents' and the caustic's offsets (px) for a flow over a track's width: each current slides left by
    /// its pass's fraction of the track, the caustic tile pans by up to the track's width to and fro.</summary>
    public static (double Fast, double Slow, double Caustic) Offsets(LiquidFlow flow, double track) =>
        ((flow.Fast - Math.Floor(flow.Fast)) * track, (flow.Slow - Math.Floor(flow.Slow)) * track, track * AlternateAt(flow.Caustic));

    /// <summary>The gradient across a width, one sRGB texel per device pixel, interpolated in OKLab as CSS does.</summary>
    public static SKColor[] GradientRow(IReadOnlyList<(Color Color, double At)> stops, int pixels)
    {
        var lab = stops.Select(s => (Lab: ToOKLab(s.Color), s.At)).ToArray();
        var row = new SKColor[pixels];
        for (var x = 0; x < pixels; x++)
        {
            var t = (x + 0.5) / pixels;
            (double L, double A, double B) c;
            if (t <= lab[0].At) c = lab[0].Lab;
            else if (t >= lab[^1].At) c = lab[^1].Lab;
            else
            {
                var i = 1;
                while (i < lab.Length - 1 && t > lab[i].At) i++;
                var (a, b) = (lab[i - 1], lab[i]);
                var f = b.At > a.At ? (t - a.At) / (b.At - a.At) : 1;
                c = (a.Lab.L + (b.Lab.L - a.Lab.L) * f, a.Lab.A + (b.Lab.A - a.Lab.A) * f, a.Lab.B + (b.Lab.B - a.Lab.B) * f);
            }
            row[x] = FromOKLab(c);
        }
        return row;
    }

    internal static (double L, double A, double B) ToOKLab(Color color)
    {
        static double Lin(byte v) { var c = v / 255.0; return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
        double r = Lin(color.R), g = Lin(color.G), b = Lin(color.B);
        var l = Math.Cbrt(0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b);
        var m = Math.Cbrt(0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b);
        var s = Math.Cbrt(0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b);
        return (0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s, 1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s, 0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    internal static SKColor FromOKLab((double L, double A, double B) c)
    {
        var l = Math.Pow(c.L + 0.3963377774 * c.A + 0.2158037573 * c.B, 3);
        var m = Math.Pow(c.L - 0.1055613458 * c.A - 0.0638541728 * c.B, 3);
        var s = Math.Pow(c.L - 0.0894841775 * c.A - 1.2914855480 * c.B, 3);
        static byte Enc(double v)
        {
            v = Math.Clamp(v, 0, 1);
            var e = v <= 0.0031308 ? 12.92 * v : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;
            return (byte)Math.Round(255 * e);
        }
        return new SKColor(Enc(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s), Enc(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s), Enc(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s));
    }

    internal const int MaxBubbles = 24;
    internal const int MaxStrands = 3;

    // ED-233: the liquid is drawn in small passes, not one shader. The one-shader form (content() three times for the
    // refraction, each with the currents, caustic, oil and 24 bubbles) overflowed the native stack of ANGLE's D3D shader
    // compile on Avalonia's render thread: the default GPU backend (AngleEgl) crashed the process the first time a liquid
    // track drew (native run 2026-10-02, exit 0xC00000FD). Now: (A) the liquid is composed once into an offscreen surface
    // at device scale: the gradient, then each layer with Skia's own W3C separable blend modes (screen for the oil, soft
    // light for the currents, overlay for the caustic, each the canvas's mix-blend-mode), then each bubble on its own small
    // rectangle, then the capsule's coverage; (B) the glass pass samples that surface three times through the
    // displacement map for the refraction and adds the gloss and the rims. The values are the same as before; the layers
    // meet through an 8-bit surface instead of float registers.
    private const string Uniforms_ = """
        uniform float2 size;            // the liquid (the filled part of the track), px
        uniform float track;            // the whole track's width, px: currents, light and bubbles keep their scale
        uniform float gradWidth;        // texels in grad
        uniform float scale;            // device pixels per px
        uniform float2 breathe;         // saturate, brightness
        uniform float strands;          // the first current's strands (1 to 3)
        uniform float4 strandShape[3];  // amplitude, cycles, phase, stroke width (viewBox units)
        uniform float4 strandColor[3];  // premultiplied colour
        uniform float4 curA;            // first current: viewBox height, opacity, offset (px), unused
        uniform float4 curB;            // the slow current: viewBox height, opacity, offset (px), on
        uniform float4 waveB;           // its amplitude, cycles, phase, stroke width
        uniform float caustic;          // the caustic tile's offset, px
        uniform float4 oil;             // on, left px, width px, tile offset px
        uniform float oilMask;          // 1 in a blend: the oil fades at its stretch's ends
        uniform float3 refraction;      // the channels' displacement scales

        """;

    private const string Helpers = """
        float erf(float x) { float a = 0.147; float x2 = x * x; float r = sqrt(max(0.0, 1.0 - exp(-x2 * (1.2732395 + a * x2) / (1.0 + a * x2)))); return x < 0.0 ? -r : r; } // max: Skia's CPU exp() can return a hair over 1 near 0
        float capsule(float2 p, float2 s) {
            float r = s.y * 0.5; float2 a = float2(r, r); float2 b = float2(max(r, s.x - r), r);
            float2 pa = p - a; float2 ba = b - a; float h = clamp(dot(pa, ba) / max(dot(ba, ba), 0.0001), 0.0, 1.0);
            return length(pa - ba * h) - r;
        }
        half4 over(half4 dst, half4 src) { return src + dst * (1.0 - src.a); }
        // a blurred SVG stroke along a sine, in the current's own viewBox (2000 wide over twice the track)
        float strand(float2 p, float offset, float vbH, float4 shape) {
            float u = (p.x + offset) / (2.0 * track) * 2000.0;
            float v = p.y / size.y * vbH;
            float k = 3.14159265 * shape.y / 1000.0;
            float yc = vbH * 0.5 + shape.x * sin(u * k + shape.z);
            float slope = shape.x * k * cos(u * k + shape.z);
            float d = abs(v - yc) / sqrt(1.0 + slope * slope);
            float w = shape.w * 0.5; float s = 2.0 * 1.41421356;
            return clamp(0.5 * (erf((d + w) / s) - erf((d - w) / s)), 0.0, 1.0);
        }

        """;

    // (A1) the liquid's colour, breathing (saturate, then brightness), opaque.
    private const string BaseMain = """
        half4 main(float2 p) {
            float3 c = float3(grad.eval(float2(clamp(p.x / size.x, 0.0, 1.0) * gradWidth, 0.5)).rgb);
            float s = breathe.x;
            c = float3(dot(c, float3(0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s)),
                       dot(c, float3(0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s)),
                       dot(c, float3(0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s)));
            c = clamp(clamp(c, 0.0, 1.0) * breathe.y, 0.0, 1.0);
            return half4(half3(c), 1.0);
        }
        """;

    // (A2) Grok's oil sheen, screened over the liquid (under the currents in a blend, over the light for one model).
    private const string OilMain = """
        half4 main(float2 p) {
            float lx = p.x - oil.y;
            if (oil.x < 0.5 || lx < 0.0 || lx > oil.z) return half4(0.0);
            float tw = 2.6 * oil.z; float th = size.y;
            float2 uv = float2(mod(lx - oil.w, tw), p.y);
            float ang = radians(110.0); float2 dir = float2(sin(ang), -cos(ang));
            float L = abs(tw * sin(ang)) + abs(th * cos(ang));
            float t = dot(uv - float2(tw, th) * 0.5, dir) / L + 0.5;
            half4 s0 = half4(0.0);
            half4 s1 = half4(half3(124.0, 92.0, 255.0) / 255.0 * 0.38, 0.38);
            half4 s2 = half4(half3(40.0, 220.0, 210.0) / 255.0 * 0.30, 0.30);
            half4 s3 = half4(half3(255.0, 110.0, 180.0) / 255.0 * 0.28, 0.28);
            half4 s4 = half4(half3(255.0, 220.0, 140.0) / 255.0 * 0.20, 0.20);
            half4 g = s0;
            if (t <= 0.18) g = s0;
            else if (t <= 0.32) g = mix(s0, s1, half((t - 0.18) / 0.14));
            else if (t <= 0.42) g = mix(s1, s2, half((t - 0.32) / 0.10));
            else if (t <= 0.52) g = mix(s2, s3, half((t - 0.42) / 0.10));
            else if (t <= 0.60) g = mix(s3, s4, half((t - 0.52) / 0.08));
            else if (t <= 0.74) g = mix(s4, s0, half((t - 0.60) / 0.14));
            float m = 1.0;
            if (oilMask > 0.5) { float f = lx / oil.z; m = f < 0.3 ? f / 0.3 : f > 0.7 ? (1.0 - f) / 0.3 : 1.0; }
            return g * half(0.9 * m);
        }
        """;

    // (A3) the first current: one strand, or a strand per model, composited; soft-lit over the liquid.
    private const string StrandsMain = """
        half4 main(float2 p) {
            half4 sa = half4(0.0);
            for (int i = 0; i < 3; i++) {
                if (float(i) >= strands) break;
                float a = strand(p, curA.z, curA.x, strandShape[i]);
                sa = over(sa, half4(strandColor[i]) * half(a));
            }
            return sa * half(curA.y);
        }
        """;

    // (A4) the slow current, soft-lit.
    private const string SlowMain = """
        half4 main(float2 p) { return half4(1.0) * half(strand(p, curB.z, curB.x, waveB) * curB.y); }
        """;

    // (A5) the caustic light, overlaid.
    private const string CausticMain = """
        float ellipseAlpha(float2 uv, float2 c, float2 r, float a) { float t = length((uv - c) / r); return a * max(0.0, 1.0 - t / 0.7); }
        half4 main(float2 p) {
            float tw = 2.0 * track; float th = size.y; float2 uv = float2(mod(p.x - caustic, tw), p.y);
            float c1 = ellipseAlpha(uv, float2(0.12 * tw, 0.38 * th), float2(38.0, 10.0), 0.55);
            float c2 = ellipseAlpha(uv, float2(0.37 * tw, 0.66 * th), float2(54.0, 12.0), 0.40);
            float c3 = ellipseAlpha(uv, float2(0.63 * tw, 0.34 * th), float2(44.0, 10.0), 0.50);
            float c4 = ellipseAlpha(uv, float2(0.88 * tw, 0.62 * th), float2(60.0, 13.0), 0.38);
            float ca = 1.0 - (1.0 - c1) * (1.0 - c2) * (1.0 - c3) * (1.0 - c4);
            return half4(1.0) * half(ca * 0.8);
        }
        """;

    // (A7) the capsule's coverage, kept with DstIn.
    private const string CoverMain = """
        half4 main(float2 p) { return half4(clamp(0.5 - capsule(p, size) * scale, 0.0, 1.0)); }
        """;

    // (B) the glass map, as the canvas draws it (red across, blue down, a blurred neutral capsule laid over the middle),
    // sampled per channel; then the gloss and the inset rims.
    private const string GlassMain = """
        float2 displacement(float2 p) {
            float inset = min(size.x, size.y) * 0.12; float sigma = max(3.0, size.y * 0.16);
            float2 innerSize = size - 2.0 * inset;
            float sd = capsule(p - float2(inset), innerSize);
            float neutral = 0.94 * 0.5 * (1.0 - erf(sd / (sigma * 1.41421356)));
            float2 raw = float2(clamp(p.x / size.x, 0.0, 1.0), clamp(p.y / size.y, 0.0, 1.0)) - 0.5;
            return raw * (1.0 - neutral);
        }
        half4 main(float2 p) {
            float2 off = displacement(p);
            half4 r = liquid.eval(p + refraction.x * off);
            half4 g = liquid.eval(p + refraction.y * off);
            half4 b = liquid.eval(p + refraction.z * off);
            half4 o = half4(r.r, g.g, b.b, 1.0 - (1.0 - r.a) * (1.0 - g.a) * (1.0 - b.a));
            //LIGHT
            float d = capsule(p, size);
            float cov = clamp(0.5 - d * scale, 0.0, 1.0);
            // the tube's gloss: light above, shade below
            float ty = p.y / size.y;
            half4 gl;
            if (ty <= 0.35) gl = mix(half4(0.16), half4(0.04), half(ty / 0.35));
            else if (ty <= 0.55) gl = mix(half4(0.04), half4(0.0), half((ty - 0.35) / 0.20));
            else gl = mix(half4(0.0), half4(0.0, 0.0, 0.0, 0.18), half((ty - 0.55) / 0.45));
            o = over(o, gl);
            // the inset rims: a 1 px light line, light falling in from the top edge, shade from the bottom edge
            float line = clamp(0.5 - d * scale, 0.0, 1.0) * clamp(0.5 + (d + 1.0) * scale, 0.0, 1.0);
            float top = 0.5 * (1.0 + erf(capsule(p - float2(0.0, 1.0), size) / (0.5 * 1.41421356)));
            float bottom = 0.5 * (1.0 + erf(capsule(p + float2(0.0, 1.0), size) / (0.5 * 1.41421356)));
            o = over(o, half4(1.0) * half(0.34 * line));
            o = over(o, half4(1.0) * half(0.35 * top));
            o = over(o, half4(0.0, 0.0, 0.0, 0.25) * half(bottom));
            return o * half(cov);
        }
        """;

    // (A6) one CSS bubble: the soft halo outside, the radial highlight, the 1 px rim; then the element's opacity.
    private const string BubbleShader = """
        uniform float scale;   // device pixels per px
        uniform float4 A;      // centre x, centre y, scale, opacity
        uniform float4 B;      // border box side, dark rim, pale-liquid look (OD-0090 ink), unused
        float erf(float x) { float a = 0.147; float x2 = x * x; float r = sqrt(max(0.0, 1.0 - exp(-x2 * (1.2732395 + a * x2) / (1.0 + a * x2)))); return x < 0.0 ? -r : r; }
        half4 over(half4 dst, half4 src) { return src + dst * (1.0 - src.a); }
        half4 main(float2 p) {
            float outer = B.x; float sz = outer - 2.0; float R0 = outer * 0.5; float sc = A.z; float px = 1.0 / (scale * sc);
            float2 q = (p - A.xy) / sc + float2(R0);
            float d = length(q - float2(R0)) - R0;
            float inside = clamp(0.5 - d / px, 0.0, 1.0);
            float halo = 0.5 * (1.0 - erf(d / (1.5 * 1.41421356)));
            half4 col = half4(1.0) * half(0.25 * halo * (1.0 - inside));
            bool dark = B.y > 0.5;
            float2 c0 = float2(1.0 + 0.32 * sz, 1.0 + 0.28 * sz); float rad = length(float2(0.68 * sz, 0.72 * sz));
            float t = length(q - c0) / max(rad, 0.001);
            half4 bg;
            if (!dark) {
                half4 a0 = half4(0.95); half4 a1 = half4(0.14); half4 a2 = half4(0.0);
                if (t <= 0.16) bg = a0; else if (t <= 0.46) bg = mix(a0, a1, half((t - 0.16) / 0.30)); else if (t <= 0.72) bg = mix(a1, a2, half((t - 0.46) / 0.26)); else bg = a2;
            } else {
                half4 a0 = half4(0.98); half4 a1 = half4(half3(20.0, 22.0, 30.0) / 255.0 * 0.06, 0.06); half4 a2 = half4(0.0);
                if (t <= 0.18) bg = a0; else if (t <= 0.50) bg = mix(a0, a1, half((t - 0.18) / 0.32)); else if (t <= 0.72) bg = mix(a1, a2, half((t - 0.50) / 0.22)); else bg = a2;
            }
            col = over(col, bg * half(inside));
            float ring = clamp(0.5 - d / px, 0.0, 1.0) * clamp(0.5 + (d + 1.0) / px, 0.0, 1.0);
            half4 rc = dark ? half4(half3(20.0, 22.0, 30.0) / 255.0 * 0.5, 0.5) : half4(0.72);
            col = over(col, rc * half(ring));
            col = col * half(A.w);
            // OD-0090 (jewel-knob.html drawBub): on a pale liquid the bubble takes the pale-liquid look (FastC .bub.dark: the
            // highlight with a faint dark falloff, a neutral 1 px rim at .5), drawn over it at the liquid's paleness
            if (B.z > 0.0 && !dark) {
                half4 d0 = half4(0.98); half4 d1 = half4(half3(20.0, 22.0, 30.0) / 255.0 * 0.06, 0.06); half4 d2 = half4(0.0);
                half4 dg;
                if (t <= 0.18) dg = d0; else if (t <= 0.50) dg = mix(d0, d1, half((t - 0.18) / 0.32)); else if (t <= 0.72) dg = mix(d1, d2, half((t - 0.50) / 0.22)); else dg = d2;
                half4 pale = over(dg * half(inside), half4(half3(20.0, 22.0, 30.0) / 255.0 * 0.5, 0.5) * half(ring));
                col = over(col, pale * half(B.z * min(1.0, A.w)));
            }
            return col;
        }
        """;

    private static Lazy<SKRuntimeEffect> Compile(string source, string what) => new(() =>
        SKRuntimeEffect.CreateShader(source, out var error) ?? throw new InvalidOperationException("Liquid " + what + " shader did not compile: " + error));

    private static readonly Lazy<SKRuntimeEffect> GradientMixEffect = Compile(
        "uniform shader previous; uniform shader current; uniform float amount; half4 main(float2 p) { return mix(previous.eval(p), current.eval(p), amount); }", "gradient fade");
    private static readonly Lazy<SKRuntimeEffect> BaseEffect = Compile("uniform shader grad;\n" + Uniforms_ + Helpers + BaseMain, "base");
    private static readonly Lazy<SKRuntimeEffect> OilEffect = Compile(Uniforms_ + Helpers + OilMain, "oil");
    private static readonly Lazy<SKRuntimeEffect> StrandsEffect = Compile(Uniforms_ + Helpers + StrandsMain, "current");
    private static readonly Lazy<SKRuntimeEffect> SlowEffect = Compile(Uniforms_ + Helpers + SlowMain, "slow current");
    private static readonly Lazy<SKRuntimeEffect> CausticEffect = Compile(Uniforms_ + Helpers + CausticMain, "caustic");
    private static readonly Lazy<SKRuntimeEffect> CoverEffect = Compile(Uniforms_ + Helpers + CoverMain, "coverage");
    private static readonly Lazy<SKRuntimeEffect> Effect = Compile("uniform shader liquid;\n" + Uniforms_ + Helpers + GlassMain, "glass");

    // OD-0073: the glass for fast mode also screens the Strike layer over the refracted liquid, before the gloss and rims
    // (the canvas's mix-blend-mode: screen; premultiplied, so cs + cb - cs cb for colour and alpha alike)
    private static readonly Lazy<SKRuntimeEffect> LitEffect = Compile("uniform shader liquid;\nuniform shader light;\n" + Uniforms_ + Helpers +
        GlassMain.Replace("//LIGHT", "half4 l = light.eval(p); o = l + o - l * o;", StringComparison.Ordinal), "lit glass");
    private static readonly Lazy<SKRuntimeEffect> BubbleEffect = Compile(BubbleShader, "bubble");
    // OD-0093 / OD-0099: the normal ink group then screen light, unbent, under gloss/rims.
    internal static readonly Lazy<SKRuntimeEffect> MixEffect = Compile("uniform shader liquid;\nuniform shader strikeOver;\nuniform shader light;\n" + Uniforms_ + Helpers +
        GlassMain.Replace("//LIGHT", "half4 k = strikeOver.eval(p); o = over(o, k); half4 l = light.eval(p); o = l + o - l * o;", StringComparison.Ordinal), "Mix glass");

    /// <summary>Compiles every pass (tests use it to prove the sources are valid SkSL).</summary>
    internal static SKRuntimeEffect CompiledEffect
    {
        get
        {
            foreach (var pass in new[] { BaseEffect, OilEffect, StrandsEffect, SlowEffect, CausticEffect, CoverEffect, BubbleEffect, LitEffect, MixEffect }) _ = pass.Value;
            _ = StrikeRaster.ColourEffect.Value; _ = StrikeRaster.WashEffect.Value;
            return Effect.Value;
        }
    }

    /// <summary>One frame's values, resolved on the UI thread.</summary>
    internal readonly record struct Frame(Size Size, double Track, double Time, SKColor[] Gradient, LiquidSpec Spec, BubbleLanes? Bubbles, bool DarkBubbles,
        IReadOnlyList<Color> StrandColors, LiquidFlow Flow, IReadOnlyList<StrikeOp>? Strike, double BubbleInk = 0,
        IReadOnlyList<BubbleBurst>? Rings = null, IReadOnlyCollection<(int Lane, int Rise)>? Popped = null, LiquidLayers? Layers = null, StrikeFrame? ResolvedStrike = null, SKColor[]? PreviousGradient = null, double GradientMix = 1,
        StrikeResolvedFrame? Mix = null, StrikeGeometry MixGeometry = default, StrikeInk MixInk = default, StrikeBubbleLanes? MixLanes = null, StrikeFastClock? FastClock = null, StrikeContacts? MixContacts = null, bool MixStill = false, int MixSeed = 0, Marrow.App.Port.ModelsPalette.PaletteLiveSample? Palette = null, bool Dark = false);


    /// <summary>
    /// Draws the liquid tube and its bubbles into <paramref name="context"/> at <paramref name="time"/>: its currents and
    /// caustic at <paramref name="flow"/> (by default a normal pace since time 0), and the Strike lightning
    /// <paramref name="strike"/> over it when given (OD-0073). <paramref name="bubbleInk"/>: how far the bubbles take the
    /// pale-liquid look (OD-0090, the Strike's ink on a pale liquid).
    /// </summary>
    public static void Draw(DrawingContext context, Size size, double track, double time, LiquidSpec spec, SKColor[] gradient,
        BubbleLanes? bubbles, bool darkBubbles, ThemeVariant theme, LiquidFlow? flow = null, IReadOnlyList<StrikeOp>? strike = null, double bubbleInk = 0,
        IReadOnlyList<BubbleBurst>? rings = null, IReadOnlyCollection<(int Lane, int Rise)>? popped = null) =>
        context.Custom(new Operation(new Frame(size, track, time, gradient, spec, bubbles, darkBubbles,
            spec.Segments.Select(s => s.Strand.Resolve(theme)).ToArray(), flow ?? LiquidFlow.At(time, false), strike, bubbleInk, rings, popped)));
    internal static Color[] StrandColorsOf(LiquidSpec spec, ThemeVariant theme)
    {
        var colors = new Color[spec.Segments.Count];
        for (var i = 0; i < colors.Length; i++) colors[i] = spec.Segments[i].Strand.Resolve(theme);
        return colors;
    }


    /// <summary>The gradient row for a spec at a device width.</summary>
    public static SKColor[] GradientFor(LiquidSpec spec, int pixels, ThemeVariant theme) =>
        GradientRow(spec.Stops().Select(s => (s.Color.Resolve(theme), s.At)).ToArray(), Math.Max(1, pixels));

    /// <summary>
    /// Grok's oil, as the shader takes it: (on, left px, width px, tile offset px) and the end mask. One model: the whole
    /// tube; a blend: its stretch widened by 6 % each side and masked at both ends (the canvas's oilStyle). No oil: zeros.
    /// </summary>
    internal static (float[] Oil, float Mask) Oil(LiquidSpec spec, double width, double time)
    {
        var oil = new float[4];
        return (oil, OilInto(oil, spec, width, time));
    }

    // the same, written into oil (4 values); returns the end mask
    private static float OilInto(float[] oil, LiquidSpec spec, double width, double time)
    {
        Array.Clear(oil);
        if (spec.OilRange() is not { } range) return 0f;
        double left = 0, span = width;
        if (spec.IsBlend)
        {
            left = Math.Max(0, range.From - 0.06) * width;
            var right = Math.Max(0, 1 - range.To - 0.06) * width;
            span = width - left - right;
        }
        var position = 1 - Alternate(time, 6.5); // background-position 100 % to 0 %
        oil[0] = 1f; oil[1] = (float)left; oil[2] = (float)span; oil[3] = (float)(-1.6 * span * position);
        return spec.IsBlend ? 1f : 0f;
    }

    /// <summary>The glass pass's uniforms for a frame (separate so tests can read them).</summary>
    internal static SKRuntimeEffectUniforms Uniforms(in Frame f, double scale)
    {
        var pass = new PassUniforms(Effect.Value);
        pass.Fill(f, scale);
        return pass.Uniforms;
    }

    /// <summary>
    /// One pass's uniforms, kept with the track's layers and refilled each frame in place (ED-248 review: no uniform block or
    /// array made per frame), its children as native handles in the effect's declaration order, and its paint. Every pass
    /// but the bubbles declares the same uniforms (Uniforms_), so one filler serves them all; the bubbles set their own.
    /// </summary>
    internal sealed partial class PassUniforms : IDisposable
    {
        private readonly float[] size = new float[2], breathe = new float[2], refraction = new float[3], curA = new float[4], curB = new float[4],
            waveB = new float[4], shapes = new float[MaxStrands * 4], colors = new float[MaxStrands * 4], oil = new float[4];
        private readonly SKData? data;
        private readonly Dictionary<string, int> offsets = [];
        private readonly string[] childNames;
        private readonly nint[]? children;

        public PassUniforms(SKRuntimeEffect effect)
        {
            Effect = effect;
            Uniforms = new SKRuntimeEffectUniforms(effect);
            data = SkiaShaders.DataOf(Uniforms);
            childNames = effect.Children.ToArray();
            children = childNames.Length == 0 ? null : new nint[childNames.Length];
        }

        public SKRuntimeEffect Effect { get; }
        public SKRuntimeEffectUniforms Uniforms { get; }
        public SKPaint Paint { get; } = new();

        /// <summary>Grok's oil this frame, as <see cref="Fill"/> left it.</summary>
        public float[] OilValues => oil;
        public float OilMask { get; private set; }

        /// <summary>Sets a child by name to a native shader handle (0: none), kept until set again.</summary>
        public void SetChild(string name, nint shader) => children![Array.IndexOf(childNames, name)] = shader;

        /// <summary>This frame's shader (a native reference the caller releases).</summary>
        public nint MakeShader() => SkiaShaders.Make(Effect, Uniforms, data, children);

        public void Fill(in Frame f, double scale)
        {
            var (w, h) = (f.Size.Width, f.Size.Height);
            var trackWidth = Math.Max(f.Track, w);
            var (saturate, brightness) = Breathe(f.Time);
            size[0] = (float)w; size[1] = (float)h;
            breathe[0] = (float)saturate; breathe[1] = (float)brightness;
            refraction[0] = (float)Refraction.R; refraction[1] = (float)Refraction.G; refraction[2] = (float)Refraction.B;
            Set("size", size);
            Set("track", (float)trackWidth);
            Set("gradWidth", f.Gradient.Length);
            Set("scale", (float)scale);
            Set("breathe", breathe);
            Set("refraction", refraction);
            if (f.Palette is not null) { ApplyPalette(f); return; } // ED-625: additive bound uniforms.
            // the currents: one model has the canvas's wave1 (amplitude 5, 4 cycles) and wave2 (7, 2 cycles, phase 2) in a
            // 30-unit viewBox; a blend has a strand per model (the lead 10, a worker 6, 4 cycles, phases spread evenly, 3
            // wide) and wave3 (8, 2 cycles, phase 1) in a 40-unit viewBox. 7 s and 11 s a pass (OD-0073: 4.67 s and 7.33 s in
            // fast mode), as the frame's flow says.
            var (fastPx, slowPx, causticPx) = Offsets(f.Flow, trackWidth);
            var fastOffset = (float)fastPx;
            var slowOffset = (float)slowPx;
            Array.Clear(shapes); Array.Clear(colors);
            int count;
            if (!f.Spec.IsBlend)
            {
                count = 1;
                shapes[0] = 5; shapes[1] = 4; shapes[2] = 0; shapes[3] = 5;
                colors[0] = colors[1] = colors[2] = colors[3] = 1;
                Set(curA, 30f, 0.55f, fastOffset, 0); Set(curB, 30f, 0.35f, slowOffset, 1); Set(waveB, 7f, 2f, 2f, 5f);
            }
            else
            {
                count = Math.Min(MaxStrands, f.Spec.Segments.Count);
                for (var i = 0; i < count; i++)
                {
                    var segment = f.Spec.Segments[i];
                    shapes[i * 4] = segment.Lead ? 10 : 6; shapes[i * 4 + 1] = 4; shapes[i * 4 + 2] = (float)(i * Math.PI * 2 / f.Spec.Segments.Count); shapes[i * 4 + 3] = 3;
                    var c = f.StrandColors[i];
                    var a = c.A / 255f;
                    colors[i * 4] = c.R / 255f * a; colors[i * 4 + 1] = c.G / 255f * a; colors[i * 4 + 2] = c.B / 255f * a; colors[i * 4 + 3] = a;
                }
                Set(curA, 40f, 0.55f, fastOffset, 0); Set(curB, 40f, 0.35f, slowOffset, 1); Set(waveB, 8f, 2f, 1f, 5f);
            }
            Set("curA", curA); Set("curB", curB); Set("waveB", waveB);
            Set("strands", count);
            Set("strandShape", shapes);
            Set("strandColor", colors);
            // the caustic tile pans by up to the track's width, 6 s each way (OD-0073: 4 s in fast mode)
            Set("caustic", (float)causticPx);
            OilMask = OilInto(oil, f.Spec, w, f.Time);
            Set("oil", oil);
            Set("oilMask", OilMask);
        }

        private static void Set(float[] v, float a, float b, float c, float d) { v[0] = a; v[1] = b; v[2] = c; v[3] = d; }

        /// <summary>Sets a uniform of this pass: in place in its block once its offset is known (no allocation), through
        /// SkiaSharp if it has none.</summary>
        public void Set(string name, ReadOnlySpan<float> values)
        {
            if (!offsets.TryGetValue(name, out var offset)) offsets[name] = offset = SkiaShaders.OffsetOf(Uniforms, data, name, values.Length);
            if (offset >= 0) SkiaShaders.Write(data!, offset, values);
            else if (values.Length == 1) Uniforms[name] = values[0];
            else Uniforms[name] = values.ToArray();
        }

        public void Set(string name, float value) => Set(name, new ReadOnlySpan<float>(in value));

        public void Dispose() { Paint.Dispose(); Uniforms.Dispose(); }
    }

    /// <summary>The bubbles risen at this frame: centre x, centre y, scale, opacity; border box side, dark rim.</summary>
    internal static IReadOnlyList<(float[] A, float[] B)> Bubbles(in Frame f)
    {
        var list = new List<(float[], float[])>();
        var a = new float[MaxBubbles * 4]; var b = new float[MaxBubbles * 4];
        var n = BubblesInto(f, a, b);
        for (var i = 0; i < n; i++) list.Add((a[(i * 4)..(i * 4 + 4)], b[(i * 4)..(i * 4 + 4)]));
        return list;
    }

    // the same, written four values a bubble into a and b; returns how many
    private static int BubblesInto(in Frame f, float[] a, float[] b)
    {
        var n = 0;
        if (f.MixLanes is { } lanes && f.FastClock is { } clock)
        {
            var fast = f.MixStill ? f.Mix?.Opacity ?? 0 : clock.FastAt(f.Time);
            var at = f.MixStill ? 1.3 : lanes.Simmer ? clock.ClockB(f.Time) : f.Time;
            for (var i = 0; i < Math.Min(MaxBubbles, lanes.Count); i++)
            {
                var now = lanes.Now(i, at, fast); if (now.Alpha < 0.004 || !f.MixStill && f.MixContacts?.IsDead(i, now.K) == true) continue;
                a[n * 4] = (float)now.X; a[n * 4 + 1] = (float)now.Y; a[n * 4 + 2] = (float)now.Scale; a[n * 4 + 3] = (float)now.Alpha;
                b[n * 4] = (float)(Strike.Round(now.Outer * 10) / 10d); b[n * 4 + 1] = 0; b[n * 4 + 2] = (float)f.MixInk.Amount; b[n * 4 + 3] = 0; n++;
            }
            return n;
        }
        var count = Math.Min(MaxBubbles, f.Bubbles?.Count ?? 0);
        for (var i = 0; i < count; i++)
        {
            if (f.Bubbles!.AtFrame(i, f.Time) is not { } now) continue; // between rises
            if (f.Popped is { } popped && popped.Contains((i, now.K))) continue; // OD-0078: burst, gone until its next rise
            var (centre, radius, opacity) = BubbleAt(now.Rise, f.Size.Height, now.Progress);
            if (opacity <= 0.003) continue;
            a[n * 4] = (float)centre.X; a[n * 4 + 1] = (float)centre.Y; a[n * 4 + 2] = (float)(radius / (now.Rise.Outer / 2)); a[n * 4 + 3] = (float)opacity;
            b[n * 4] = (float)now.Rise.Outer; b[n * 4 + 1] = f.DarkBubbles ? 1f : 0f; b[n * 4 + 2] = f.BubbleInk < 0.01 ? 0f : (float)f.BubbleInk; b[n * 4 + 3] = 0f;
            n++;

        }
        return n;
    }

    /// <summary>
    /// A track's drawing state, kept across frames (ED-248 review: a steady frame makes no surface, no uniform block, no
    /// paint and no managed object at all): the liquid's offscreen surface and the Strike layer's, made at the frame's device
    /// size and made again only when that size or the GPU context changes; the gradient's image, made again only for a new
    /// gradient row; each pass's uniforms and paint; the bubbles' values; the lightning's painter. Each frame's shaders and
    /// snapshots are native references released at once (<see cref="SkiaShaders"/>). Render thread only, under a lock on
    /// this object; the UI thread uses only <see cref="StrandColors"/>. A track that leaves the window retires its layers,
    /// and the next liquid frame drawn (on the render thread, where the GPU context is current) disposes them.
    /// </summary>
    public sealed class LiquidLayers
    {
        private static readonly System.Collections.Concurrent.ConcurrentQueue<LiquidLayers> Retired = new();

        private nint context;                         // the GPU context the surfaces belong to (0: raster)
        private SKImageInfo info;
        private SKSurface? liquid, light;
        private SKCanvas? liquidCanvas, lightCanvas;
        private SKColor[]? gradientRow;
        private SKImage? gradientImage;
        private SKShader? gradientShader;
        private SKColor[]? previousRow;
        private SKImage? previousImage;
        private SKShader? previousShader;
        private readonly Dictionary<SKRuntimeEffect, PassUniforms> passes = [];
        private readonly float[] bubbleA = new float[MaxBubbles * 4], bubbleB = new float[MaxBubbles * 4], oneA = new float[4], oneB = new float[4];
        private readonly Marrow.App.Port.ModelsPalette.PaletteLoadingPaint paletteLoading=new();
        private readonly SKPaint glassPaint = new() { IsAntialias = true };
        private readonly StrikePainter painter = new();
        private StrikeRaster? mixRaster;
        private readonly StrikeBurstPainter mixRings = new();
        public int StrikeSurfacesMade { get; private set; }
        internal int MixPasses => (mixRaster?.InkDraws > 0 ? 1 : 0) | (mixRaster?.LightDraws > 0 ? 2 : 0) | (mixRaster?.WashDraws > 0 ? 4 : 0);

        // UI thread: the strand colours of the last spec and theme
        private (LiquidSpec Spec, ThemeVariant Theme)? strandsFor;
        private Color[] strands = [];

        /// <summary>Whether these layers were disposed (a retired track's).</summary>
        public bool Disposed { get; private set; }

        /// <summary>How many offscreen surfaces these layers have made (tests: two for a fast track, again only on a resize).</summary>
        public int SurfacesMade { get; private set; }

        /// <summary>How many gradient images these layers have made (tests: one per gradient row).</summary>
        public int GradientsMade { get; private set; }

        /// <summary>How many shaders these layers have handed Skia (a pass each, a bubble each, the glass).</summary>
        public long ShadersHanded { get; private set; }

        /// <summary>How many snapshots of its surfaces these layers have taken for the glass to sample.</summary>
        public long Snapshots { get; private set; }

        /// <summary>UI thread: the spec's strand colours in the theme, kept while both hold.</summary>
        internal IReadOnlyList<Color> StrandColors(LiquidSpec spec, ThemeVariant theme)
        {
            if (strandsFor is not { } key || !ReferenceEquals(key.Spec, spec) || key.Theme != theme)
            {
                strandsFor = (spec, theme);
                strands = StrandColorsOf(spec, theme);
            }
            return strands;
        }

        /// <summary>Hands these layers to the render thread for disposal (a track leaving the window).</summary>
        public void Retire() => Retired.Enqueue(this);

        /// <summary>Disposes the retired layers. Render thread, with the GPU context current.</summary>
        internal static void DisposeRetired()
        {
            while (Retired.TryDequeue(out var layers)) lock (layers) layers.Dispose();
        }

        private SKSurface Surface(ref SKSurface? surface, ref SKCanvas? canvas, GRContext? gpu, SKImageInfo wanted)
        {
            var handle = gpu?.Handle ?? 0;
            if (handle != context || wanted != info)
            {
                Release(ref liquid, ref liquidCanvas); Release(ref light, ref lightCanvas);
                mixRaster?.Dispose(); mixRaster = null;
                context = handle; info = wanted;
            }
            if (surface is null)
            {
                surface = (gpu is not null ? SKSurface.Create(gpu, true, wanted) : null) ?? SKSurface.Create(wanted);
                canvas = surface.Canvas;
                SurfacesMade++;
            }
            return surface;
        }

        private static void Release(ref SKSurface? surface, ref SKCanvas? canvas) { surface?.Dispose(); surface = null; canvas = null; }

        private SKShader Gradient(SKColor[] row)
        {
            if (gradientShader is not null && ReferenceEquals(row, gradientRow)) return gradientShader;
            gradientShader?.Dispose(); gradientImage?.Dispose();
            using var bitmap = new SKBitmap(row.Length, 1, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = row };
            gradientImage = SKImage.FromBitmap(bitmap);
            gradientShader = gradientImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
            gradientRow = row;
            GradientsMade++;
            return gradientShader;
        }

        private SKShader Previous(SKColor[] row)
        {
            if (previousShader is not null && ReferenceEquals(row, previousRow)) return previousShader;
            previousShader?.Dispose(); previousImage?.Dispose();
            using var bitmap = new SKBitmap(row.Length, 1, SKColorType.Rgba8888, SKAlphaType.Premul) { Pixels = row };
            previousImage = SKImage.FromBitmap(bitmap);
            previousShader = previousImage.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
            previousRow = row; GradientsMade++;
            return previousShader;
        }

        private PassUniforms Pass(SKRuntimeEffect effect)
        {
            if (!passes.TryGetValue(effect, out var pass)) passes[effect] = pass = new PassUniforms(effect);
            return pass;
        }

        // one shader of the pass's uniforms and children, set on its paint, drawn over rect, released
        private void Paint(SKCanvas target, PassUniforms pass, SKRect rect, SKBlendMode mode)
        {
            var shader = pass.MakeShader();
            if (shader == 0) return;
            ShadersHanded++;
            pass.Paint.BlendMode = mode;
            SkiaShaders.SetShader(pass.Paint, shader);
            target.DrawRect(rect, pass.Paint);
            SkiaShaders.SetShader(pass.Paint, 0);
            SkiaShaders.Release(shader);
        }

        /// <summary>Draws one frame of the tube into <paramref name="canvas"/> (in px at its current matrix).</summary>
        internal void Draw(SKCanvas canvas, GRContext? gpu, double opacity, Frame frame)
        {
            var scale = Math.Max(1, canvas.TotalMatrix.ScaleX);
            var (w, h) = (frame.Size.Width, frame.Size.Height);
            var rect = new SKRect(0, 0, (float)w, (float)h);
            var wanted = new SKImageInfo(Math.Max(1, (int)Math.Ceiling(w * scale)), Math.Max(1, (int)Math.Ceiling(h * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);

            // (A) the liquid, composed once at device scale
            var surface = Surface(ref liquid, ref liquidCanvas, gpu, wanted);
            var target = liquidCanvas!;
            var saved = target.Save();
            target.Clear(SKColors.Transparent);
            target.Scale((float)scale);
            PassUniforms Filled(Lazy<SKRuntimeEffect> effect) { var pass = Pass(PaletteEffect(effect, frame)); pass.Fill(frame, scale); return pass; }
            var basePass = Filled(BaseEffect);
            var gradientHandle = Gradient(frame.Gradient).Handle;
            nint mixedGradient = 0;
            if (frame.PreviousGradient is { } previous && frame.GradientMix < 1)
            {
                var blend = Pass(GradientMixEffect.Value);
                blend.SetChild("previous", Previous(previous).Handle);
                blend.SetChild("current", gradientHandle);
                blend.Set("amount", (float)frame.GradientMix);
                mixedGradient = blend.MakeShader(); gradientHandle = mixedGradient;
            }
            basePass.SetChild("grad", gradientHandle);
            var hasOil = basePass.OilValues[0] > 0.5f;
            var oilMask = basePass.OilMask;
            try { Paint(target, basePass, rect, SKBlendMode.SrcOver); }
            finally { basePass.SetChild("grad", 0); SkiaShaders.Release(mixedGradient); }
            frame.Palette?.Ink?.Draw(target,rect,1-(float)(frame.Palette?.SolidMix??0));
            if(frame.Palette is {} palette)paletteLoading.Draw(target,rect,palette.Loading,palette.LoadingPhase,frame.MixStill,frame.Dark); // nth step 14 (was v22's loading overlay): the tube's light and dots, then their 0.2 s fade, under the currents/glass.
            if (hasOil && oilMask > 0.5f) Paint(target, Filled(OilEffect), rect, SKBlendMode.Screen); // a blend's oil goes under its currents
            Paint(target, Filled(StrandsEffect), rect, SKBlendMode.SoftLight);
            Paint(target, Filled(SlowEffect), rect, SKBlendMode.SoftLight);
            Paint(target, Filled(CausticEffect), rect, SKBlendMode.Overlay);
            if (hasOil && oilMask < 0.5f) Paint(target, Filled(OilEffect), rect, SKBlendMode.Screen); // one model's oil goes over its light
            var bubble = Pass(BubbleEffect.Value);
            var bubbles = BubblesInto(frame, bubbleA, bubbleB);
            SKCanvas bubbleTarget = target;
            if (frame.Mix is not null)
            {
                _ = Surface(ref light, ref lightCanvas, gpu, wanted); bubbleTarget = lightCanvas!; bubbleTarget.Clear(SKColors.Transparent); bubbleTarget.Save(); bubbleTarget.Scale((float)scale);
            }
            for (var i = 0; i < bubbles; i++)
            {
                Array.Copy(bubbleA, i * 4, oneA, 0, 4); Array.Copy(bubbleB, i * 4, oneB, 0, 4);
                bubble.Set("scale", (float)scale); bubble.Set("A", oneA); bubble.Set("B", oneB);
                var reach = (oneB[0] * 0.5f + 6f) * oneA[2];
                Paint(bubbleTarget, bubble, new SKRect(oneA[0] - reach, oneA[1] - reach, oneA[0] + reach, oneA[1] + reach), SKBlendMode.SrcOver);
            }
            if (frame.Mix is not null)
            {
                if (!frame.MixStill && frame.MixContacts is { } contacts) mixRings.Draw(bubbleTarget, contacts, frame.MixGeometry, frame.Time, frame.MixInk);
                bubbleTarget.Restore(); var matrix = target.Save(); target.ResetMatrix(); glassPaint.Color = SKColors.White; light!.Draw(target, 0, 0, glassPaint); target.RestoreToCount(matrix);
            }
            if (frame.Rings is { Count: > 0 } rings)
                BubbleBursts.DrawRings(target, rings, Math.Max(0.62, Math.Min(1, h / 40)), frame.Time, frame.BubbleInk);
            Paint(target, Filled(CoverEffect), rect, SKBlendMode.DstIn);
            target.RestoreToCount(saved);

            // OD-0073: the Strike lightning, resolved here (its buffers stay on this thread), drawn source-over into its own
            // layer at device scale; the glass screens it over the refracted liquid (above the bubbles, under the gloss and
            // rims) and clips it with the tube
            SKSurface? lit = null;
            SKSurface? over = null;
            if (frame.Mix is { } resolvedMix)
            {
                if (mixRaster is null) { mixRaster = new StrikeRaster(wanted, gpu); StrikeSurfacesMade += mixRaster.SurfacesMade; }
                mixRaster.Paint(frame.MixGeometry, resolvedMix, frame.MixInk, frame.Time, frame.MixSeed, frame.MixStill ? null : frame.MixContacts);
                lit = mixRaster.Screen; over = mixRaster.Over;
            }
            if (frame.ResolvedStrike is { } strike)
            {
                lock (strike.Channels)
                {
                    var ops = strike.Channels.Scene(strike.Geometry, strike.Time, strike.Tint);
                    if (ops.Count > 0)
                    {
                        lit = Surface(ref light, ref lightCanvas, gpu, wanted);
                        var lc = lightCanvas!;
                        var lightSaved = lc.Save();
                        lc.Clear(SKColors.Transparent);
                        lc.Scale((float)scale);
                        painter.Paint(lc, ops);
                        lc.RestoreToCount(lightSaved);
                    }
                }
            }

            if (frame.Strike is { Count: > 0 } legacy)
            {
                lit = Surface(ref light, ref lightCanvas, gpu, wanted);
                var lc = lightCanvas!;
                var count = lc.Save(); lc.Clear(SKColors.Transparent); lc.Scale((float)scale);
                painter.Paint(lc, legacy); lc.RestoreToCount(count);
            }

            // (B) the glass: the composed liquid refracted per channel, the gloss and the rims
            var toPixels = SKMatrix.CreateScale((float)(1 / scale), (float)(1 / scale));
            nint composed = 0, sampled = 0, lightImage = 0, lightShader = 0, overImage = 0, overShader = 0;
            try
            {
                composed = SkiaShaders.Snapshot(surface);
                sampled = SkiaShaders.ImageShader(composed, toPixels);
                Snapshots++;
                if (lit is not null)
                {
                    lightImage = SkiaShaders.Snapshot(lit);
                    lightShader = SkiaShaders.ImageShader(lightImage, toPixels);
                    Snapshots++;
                }
                if (over is not null) { overImage = SkiaShaders.Snapshot(over); overShader = SkiaShaders.ImageShader(overImage, toPixels); Snapshots++; }
                var glass = Filled(over is not null ? MixEffect : lit is null ? Effect : LitEffect);
                glass.SetChild("liquid", sampled);
                if (lit is not null) glass.SetChild("light", lightShader);
                if (over is not null) glass.SetChild("strikeOver", overShader);
                glassPaint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(opacity * 255, 0, 255));
                var shader = glass.MakeShader();
                if (shader != 0)
                {
                    ShadersHanded++;
                    SkiaShaders.SetShader(glassPaint, shader);
                    canvas.DrawRect(rect, glassPaint);
                    SkiaShaders.SetShader(glassPaint, 0);
                    SkiaShaders.Release(shader);
                }
                glass.SetChild("liquid", 0);
                if (lit is not null) glass.SetChild("light", 0);
                if (over is not null) glass.SetChild("strikeOver", 0);
            }
            finally

            {
                SkiaShaders.Release(lightShader); SkiaShaders.ReleaseImage(lightImage);
                SkiaShaders.Release(overShader); SkiaShaders.ReleaseImage(overImage);
                SkiaShaders.Release(sampled); SkiaShaders.ReleaseImage(composed);
            }
        }

        internal void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            Release(ref liquid, ref liquidCanvas); Release(ref light, ref lightCanvas);
            gradientShader?.Dispose(); gradientImage?.Dispose(); gradientShader = null; gradientImage = null; gradientRow = null;
            previousShader?.Dispose(); previousImage?.Dispose(); previousShader = null; previousImage = null; previousRow = null;
            foreach (var pass in passes.Values) pass.Dispose();
            passes.Clear();
            paletteLoading.Dispose(); glassPaint.Dispose(); painter.Dispose();
            mixRaster?.Dispose(); mixRaster = null; mixRings.Dispose();
        }
    }

    // the drawing of one frame through Avalonia's Skia lease: the retired layers disposed first (the GPU context is current
    // here), then the frame drawn with its layers, or with layers of its own when they were retired
    private static void Render(ImmediateDrawingContext context, in Frame frame)
    {
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
            ?? throw new InvalidOperationException("The liquid track needs the Skia renderer (ISkiaSharpApiLeaseFeature).");
        using var lease = feature.Lease();
        LiquidLayers.DisposeRetired();
        if (frame.Layers is { } layers)
        {
            lock (layers)
            {
                if (!layers.Disposed) { layers.Draw(lease.SkCanvas, lease.GrContext, lease.CurrentOpacity, frame); return; }
            }
        }
        var own = new LiquidLayers();
        try { own.Draw(lease.SkCanvas, lease.GrContext, lease.CurrentOpacity, frame); }
        finally { own.Dispose(); }
    }

    private sealed class Operation(Frame frame) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = new(frame.Size);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }
        public void Render(ImmediateDrawingContext context) => LiquidTrack.Render(context, frame);
    }

    /// <summary>
    /// ED-248 review 2: where a <see cref="Live"/> operation takes its frame when it is drawn (render thread, or main for a
    /// snapshot): the view's scene at its current time, false when there is nothing to draw.
    /// </summary>
    internal interface ILiveSource
    {
        Rect Bounds { get; }
        bool TryFrame(out Frame frame);
    }

    /// <summary>
    /// ED-248 review 2 (OD-0069): a track's one draw operation, recorded again only when what it draws changes (the look, the
    /// size, the theme), never per frame: each time Avalonia draws it (a render-thread frame redraws its area,
    /// <see cref="RenderFrameSource"/>) it resolves the frame then, at the track's current time. So a liquid frame makes no
    /// record, no operation and no render pass on main.
    /// </summary>
    internal sealed class Live(ILiveSource source) : ICustomDrawOperation
    {
        public Rect Bounds => source.Bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { } // Avalonia disposes a replaced recording's operations: this one is kept and recorded again

        public void Render(ImmediateDrawingContext context)
        {
            if (source.TryFrame(out var frame)) LiquidTrack.Render(context, frame);
        }
    }
}
