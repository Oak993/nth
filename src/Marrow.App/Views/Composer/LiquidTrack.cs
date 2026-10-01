using System.Globalization;
using System.Text;
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
// (TrackEffect.swift :1045-1335). Reference: the locked canvas, copy at
// E:\Droppy\work\design\effort-liquid-approved-v15.dc.html. One SkSL shader draws the tube in a single pass:
//   the liquid's colour along the track (a provider's three liquid stops, or OD-0048's blend: workers to the left, the
//   leads at the knob end, each lead 1.6 x a worker's stretch, each colour held over the middle third of its stretch
//   and mixed with its neighbours in OKLab, so no grey band or cut line appears);
//   two soft flowing currents and drifting caustic light, functions of continuous time, so they never loop or jump;
//   light from above, shade below, a gentle breathing; Grok's moving oil sheen (iridescent, screen-blended, soft-edged);
//   refraction toward the rounded rim, each colour channel bent a little differently (a faint fringe), after
//   rizzytoday/liquid-glass's displacement-map technique (MIT); and a thin even rim, no sweeping shine.
// Bubbles are drawn over it: each track has its own seeded set, mostly tiny, some small, a few medium, each wandering
// its own way (rising left or right, swaying, or drifting with the current), fading in and out so no restart shows.

/// <summary>One stretch of the liquid: its colours (left to right) and its share of the track.</summary>
public sealed record LiquidSegment(IReadOnlyList<SwiftColor> Colors, double Weight, bool Oil);

/// <summary>What the liquid tube draws, built from a <see cref="TrackLook"/>.</summary>
public sealed record LiquidSpec(IReadOnlyList<LiquidSegment> Segments)
{
    /// <summary>OD-0048: a lead's stretch, in workers' stretches.</summary>
    public const double LeadShare = 1.6;

    /// <summary>The colour stops along the track (position 0..1), as the shader takes them.</summary>
    public IReadOnlyList<(SwiftColor Color, double At)> Stops()
    {
        var list = new List<(SwiftColor, double)>();
        var total = Segments.Sum(s => s.Weight);
        double at = 0;
        if (Segments.Count == 1)
        {
            var only = Segments[0].Colors;
            for (var i = 0; i < only.Count; i++) list.Add((only[i], only.Count == 1 ? 0.5 : i / (double)(only.Count - 1)));
            return list;
        }
        for (var k = 0; k < Segments.Count; k++)
        {
            var s = Segments[k];
            var from = at / total; at += s.Weight; var to = at / total; var w = to - from;
            // each colour held over the middle third of its stretch; neighbours mix across the gaps between
            var heartFrom = from + w / 3; var heartTo = to - w / 3;
            if (s.Colors.Count == 1) { list.Add((s.Colors[0], heartFrom)); list.Add((s.Colors[0], heartTo)); }
            else for (var i = 0; i < s.Colors.Count; i++) list.Add((s.Colors[i], heartFrom + (heartTo - heartFrom) * i / (s.Colors.Count - 1)));
        }
        return list;
    }

    /// <summary>The oil sheen's reach along the track (0..1), or null when no segment carries it.</summary>
    public (double From, double To)? OilRange()
    {
        var total = Segments.Sum(s => s.Weight);
        double at = 0;
        foreach (var s in Segments)
        {
            var from = at / total; at += s.Weight;
            if (s.Oil) return (from, at / total);
        }
        return null;
    }

    /// <summary>
    /// The spec for a look: a provider's own liquid at maximum, or OD-0048's blend for a pair. A pair is one lead
    /// (the thread's provider) and one worker (the heads), so the lead sits against the knob, 1.6 times the heads' stretch.
    /// </summary>
    public static LiquidSpec For(TrackLook look)
    {
        var lead = new LiquidSegment(look.Brand.LiquidStops.ToArray(), LeadShare, look.Brand.HasOilSheen);
        if (look.Pair is not { } pair) return new LiquidSpec([lead with { Weight = 1 }]);
        var heads = pair.HeadsBrand is { } brand
            ? new LiquidSegment(brand.LiquidStops.ToArray(), 1, brand.HasOilSheen)
            : new LiquidSegment(pair.Colors.ToArray(), 1, false); // one lead over many heads: the roster's colours
        return new LiquidSpec([heads, lead]); // workers to the left, the lead at the knob end
    }
}

/// <summary>One bubble's path, in fractions of the track's height and absolute points along it.</summary>
public sealed record LiquidBubble(double X, double Bottom, double Size, double Duration, double Offset, double Opacity,
    double S0, double S1, double S2, Point P1, Point P2, Point P3);

/// <summary>The liquid tube's drawing (see the file note).</summary>
public static class LiquidTrack
{
    /// <summary>A track's own bubbles: seeded, so a track keeps its set; mostly tiny, some small, a few medium.</summary>
    public static IReadOnlyList<LiquidBubble> Bubbles(int seed, double width, double height)
    {
        uint s = (uint)(seed * 7919 + 17) | 1;
        double R() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s % 100000) / 100000.0; }
        var count = Math.Clamp((int)(width / 30), 4, 24);
        var list = new List<LiquidBubble>(count);
        for (var i = 0; i < count; i++)
        {
            var kind = R();
            var size = kind < 0.6 ? 1.4 + R() * 1.6 : kind < 0.9 ? 3 + R() * 2 : 5 + R() * 3;
            var up = height * (0.55 + R() * 0.8);
            var side = (R() - 0.5) * 2;
            var flow = R() < 0.25;
            var p1 = new Point(flow ? 14 + R() * 24 : side * (4 + R() * 10), -(up * (0.2 + R() * 0.2)));
            var p2 = new Point(flow ? 30 + R() * 40 : side * (2 + R() * 14) + (R() - 0.5) * 10, -(up * (0.45 + R() * 0.25)));
            var p3 = new Point(flow ? 50 + R() * 60 : (R() - 0.5) * 30, -up);
            var duration = (flow ? 3.6 : 1.8) + R() * 3.2;
            list.Add(new LiquidBubble(width * (0.02 + R() * 0.94), -6 + R() * height * 0.45, size, duration, R() * duration,
                0.55 + R() * 0.4, 0.35 + R() * 0.3, 0.7 + R() * 0.3, 0.9 + R() * 0.35, p1, p2, p3));
        }
        return list;
    }

    /// <summary>Where a bubble is at a time, and how visible: (centre, radius, opacity).</summary>
    public static (Point Centre, double Radius, double Opacity) BubbleAt(LiquidBubble b, double height, double time)
    {
        var phase = ((time + b.Offset) % b.Duration) / b.Duration;
        static double Ease(double x) => 1 - Math.Pow(1 - x, 2.2);
        Point Lerp(Point a, Point c, double f) => new(a.X + (c.X - a.X) * f, a.Y + (c.Y - a.Y) * f);
        Point at; double scale; double opacity;
        if (phase < 0.38) { var f = Ease(phase / 0.38); at = Lerp(new Point(0, 0), b.P1, f); scale = b.S0 + (b.S1 - b.S0) * f; }
        else if (phase < 0.70) { var f = Ease((phase - 0.38) / 0.32); at = Lerp(b.P1, b.P2, f); scale = b.S1; }
        else { var f = Ease((phase - 0.70) / 0.30); at = Lerp(b.P2, b.P3, f); scale = b.S1 + (b.S2 - b.S1) * f; }
        opacity = phase < 0.12 ? b.Opacity * phase / 0.12 : phase < 0.70 ? b.Opacity : b.Opacity * (1 - (phase - 0.70) / 0.30);
        return (new Point(b.X + at.X, height - b.Bottom + at.Y), b.Size * scale / 2, Math.Max(0, opacity));
    }

    internal const int MaxStops = 12;

    private const string Shader = """
        uniform float2 size;
        uniform float time;
        uniform float count;
        uniform float4 oil;
        uniform float refraction;
        uniform float4 c0; uniform float4 c1; uniform float4 c2; uniform float4 c3; uniform float4 c4; uniform float4 c5;
        uniform float4 c6; uniform float4 c7; uniform float4 c8; uniform float4 c9; uniform float4 c10; uniform float4 c11;

        float3 toLinear(float3 c) { return pow(clamp(c, 0.0, 1.0), float3(2.2)); }
        float3 toSrgb(float3 c) { return pow(clamp(c, 0.0, 1.0), float3(1.0 / 2.2)); }
        float3 toLab(float3 c) {
            float3 l = toLinear(c);
            float3 lms = float3(0.4122214708 * l.r + 0.5363325363 * l.g + 0.0514459929 * l.b,
                                0.2119034982 * l.r + 0.6806995451 * l.g + 0.1073969566 * l.b,
                                0.0883024619 * l.r + 0.2817188376 * l.g + 0.6299787005 * l.b);
            lms = pow(max(lms, float3(0.0)), float3(1.0 / 3.0));
            return float3(0.2104542553 * lms.x + 0.7936177850 * lms.y - 0.0040720468 * lms.z,
                          1.9779984951 * lms.x - 2.4285922050 * lms.y + 0.4505937099 * lms.z,
                          0.0259040371 * lms.x + 0.7827717662 * lms.y - 0.8086757660 * lms.z);
        }
        float3 fromLab(float3 c) {
            float3 lms = float3(c.x + 0.3963377774 * c.y + 0.2158037573 * c.z,
                                c.x - 0.1055613458 * c.y - 0.0638541728 * c.z,
                                c.x - 0.0894841775 * c.y - 1.2914855480 * c.z);
            lms = lms * lms * lms;
            float3 l = float3(4.0767416621 * lms.x - 3.3077115913 * lms.y + 0.2309699292 * lms.z,
                              -1.2684380046 * lms.x + 2.6097574011 * lms.y - 0.3413193965 * lms.z,
                              -0.0041960863 * lms.x - 0.7034186147 * lms.y + 1.7076147010 * lms.z);
            return toSrgb(l);
        }
        float4 stopAt(int i) {
            if (i == 0) return c0; if (i == 1) return c1; if (i == 2) return c2; if (i == 3) return c3;
            if (i == 4) return c4; if (i == 5) return c5; if (i == 6) return c6; if (i == 7) return c7;
            if (i == 8) return c8; if (i == 9) return c9; if (i == 10) return c10; return c11;
        }
        float3 colourAt(float x) {
            float4 a = c0;
            if (x <= a.w) return a.rgb;
            for (int i = 1; i < 12; i++) {
                if (float(i) >= count) break;
                float4 b = stopAt(i);
                if (x <= b.w) {
                    float f = (b.w - a.w) > 0.0001 ? (x - a.w) / (b.w - a.w) : 1.0;
                    f = f * f * (3.0 - 2.0 * f);
                    return fromLab(mix(toLab(a.rgb), toLab(b.rgb), f));
                }
                a = b;
            }
            return a.rgb;
        }
        float hash(float2 p) { return fract(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float noise(float2 p) {
            float2 i = floor(p); float2 f = fract(p); float2 u = f * f * (3.0 - 2.0 * f);
            return mix(mix(hash(i), hash(i + float2(1, 0)), u.x), mix(hash(i + float2(0, 1)), hash(i + float2(1, 1)), u.x), u.y);
        }
        float band(float d, float w) { return exp(-d * d / (w * w)); }
        float3 hue(float h) { return clamp(abs(fract(h + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0, 0.0, 1.0); }
        float3 liquid(float2 p) {
            float h = size.y;
            float3 c = colourAt(clamp(p.x / size.x, 0.0, 1.0));
            c *= 1.0 + 0.10 * (0.5 - p.y / h);
            float y1 = 0.5 * h + 0.22 * h * sin(p.x / h * 0.9 - time * 1.6);
            float y2 = 0.5 * h + 0.30 * h * sin(p.x / h * 0.55 + 1.7 - time * 1.0);
            float flowLight = 0.16 * band(p.y - y1, 0.09 * h + 1.0) + 0.10 * band(p.y - y2, 0.13 * h + 1.0);
            c = mix(c, c * 1.22 + 0.05, flowLight);
            float n = noise(float2(p.x * 0.045 - time * 0.35, p.y * 0.16 + time * 0.12)) * noise(float2(p.x * 0.07 + time * 0.22, p.y * 0.2 - time * 0.1));
            c += smoothstep(0.22, 0.55, n) * 0.09;
            c *= 1.0 + 0.03 * sin(time * 1.3);
            if (oil.z > 0.0) {
                float x = p.x / size.x;
                float w = smoothstep(oil.x - 0.04, oil.x + 0.06, x) * (1.0 - smoothstep(oil.y - 0.06, oil.y + 0.04, x));
                float irid = smoothstep(0.35, 0.75, noise(float2(p.x * 0.012 - time * 0.25, p.y * 0.08)));
                float3 sheen = hue(fract(p.x * 0.004 - p.y * 0.012 + time * 0.07)) * 0.6 + 0.2;
                c = 1.0 - (1.0 - c) * (1.0 - sheen * oil.z * w * irid * 0.55);
            }
            return clamp(c, 0.0, 1.0);
        }
        float capsule(float2 p) {
            float r = size.y * 0.5;
            float2 a = float2(r, r); float2 b = float2(max(r, size.x - r), r);
            float2 pa = p - a; float2 ba = b - a;
            float t = clamp(dot(pa, ba) / max(dot(ba, ba), 0.0001), 0.0, 1.0);
            return length(pa - ba * t) - r;
        }
        half4 main(float2 p) {
            float d = capsule(p);
            float coverage = clamp(0.5 - d, 0.0, 1.0);
            if (coverage <= 0.0) return half4(0);
            float r = size.y * 0.5;
            float2 a = float2(r, r); float2 b = float2(max(r, size.x - r), r);
            float2 axis = a + (b - a) * clamp(dot(p - a, b - a) / max(dot(b - a, b - a), 0.0001), 0.0, 1.0);
            float2 inward = normalize(axis - p + float2(0.0001));
            float edge = 1.0 - smoothstep(0.0, r * 0.9, -d);
            float2 disp = inward * edge * edge * refraction;
            float3 col = float3(liquid(p + disp).r, liquid(p + disp * 0.86).g, liquid(p + disp * 0.72).b);
            float shade = 1.0 + 0.16 * (0.5 - p.y / size.y) * 2.0;
            col *= mix(1.0, shade, 0.5);
            float rim = 1.0 - smoothstep(0.0, 1.4, -d);
            col = mix(col, float3(1.0), rim * 0.32);
            return half4(half3(col * coverage), half(coverage));
        }
        """;

    private static readonly Lazy<SKRuntimeEffect> Effect = new(() =>
        SKRuntimeEffect.CreateShader(Shader, out var error) ?? throw new InvalidOperationException("Liquid shader did not compile: " + error));

    /// <summary>Compiles the shader (tests use it to prove the source is valid SkSL).</summary>
    internal static SKRuntimeEffect CompiledEffect => Effect.Value;

    /// <summary>Draws the liquid tube and its bubbles into <paramref name="context"/> at <paramref name="time"/>.</summary>
    public static void Draw(DrawingContext context, Size size, double time, LiquidSpec spec, IReadOnlyList<LiquidBubble> bubbles, bool darkSpark, ThemeVariant theme) =>
        context.Custom(new Operation(size, time, spec.Stops().Select(s => (s.Color.Resolve(theme), s.At)).ToArray(), spec.OilRange(), bubbles, darkSpark));

    // All frame values are resolved before the render-thread lease (as FusionOperation, ED-213).
    private sealed class Operation(Size size, double time, (Color Color, double At)[] stops, (double From, double To)? oil, IReadOnlyList<LiquidBubble> bubbles, bool darkSpark) : ICustomDrawOperation
    {
        public Rect Bounds { get; } = new(size);
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
                ?? throw new InvalidOperationException("The liquid track needs the Skia renderer (ISkiaSharpApiLeaseFeature).");
            using var lease = feature.Lease();
            var canvas = lease.SkCanvas;
            var uniforms = new SKRuntimeEffectUniforms(Effect.Value)
            {
                ["size"] = new[] { (float)size.Width, (float)size.Height },
                ["time"] = (float)time,
                ["count"] = (float)Math.Min(MaxStops, stops.Length),
                ["refraction"] = (float)Math.Max(2, size.Height * 0.26),
            };
            uniforms["oil"] = oil is { } range ? new[] { (float)range.From, (float)range.To, 1f, 0f } : new[] { 0f, 0f, 0f, 0f };
            for (var i = 0; i < MaxStops; i++)
            {
                var (c, at) = stops[Math.Min(i, stops.Length - 1)];
                uniforms["c" + i.ToString(CultureInfo.InvariantCulture)] = new[] { c.R / 255f, c.G / 255f, c.B / 255f, (float)at };
            }
            using var shader = Effect.Value.ToShader(uniforms);
            using var paint = new SKPaint { Shader = shader, IsAntialias = true, Color = SKColors.White.WithAlpha((byte)Math.Clamp(lease.CurrentOpacity * 255, 0, 255)) };
            canvas.DrawRect(new SKRect(0, 0, (float)size.Width, (float)size.Height), paint);
            // the bubbles, inside the tube
            var save = canvas.Save();
            try
            {
                using var clip = new SKPath();
                clip.AddRoundRect(new SKRect(0, 0, (float)size.Width, (float)size.Height), (float)size.Height / 2, (float)size.Height / 2);
                canvas.ClipPath(clip, SKClipOperation.Intersect, antialias: true);
                var rimColor = darkSpark ? new SKColor(20, 22, 30) : SKColors.White;
                foreach (var bubble in bubbles)
                {
                    var (centre, radius, opacity) = BubbleAt(bubble, size.Height, time);
                    if (opacity <= 0.01 || radius <= 0.2) continue;
                    var alpha = opacity * lease.CurrentOpacity;
                    using var fill = SKShader.CreateRadialGradient(new SKPoint((float)(centre.X - radius * 0.35), (float)(centre.Y - radius * 0.4)), (float)(radius * 1.3),
                        [SKColors.White.WithAlpha((byte)(240 * alpha)), SKColors.White.WithAlpha((byte)(36 * alpha)), SKColors.White.WithAlpha(0)], [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
                    using var body = new SKPaint { Shader = fill, IsAntialias = true };
                    canvas.DrawCircle((float)centre.X, (float)centre.Y, (float)radius, body);
                    using var ring = new SKPaint { Color = rimColor.WithAlpha((byte)(180 * alpha)), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 0.8f };
                    canvas.DrawCircle((float)centre.X, (float)centre.Y, (float)radius, ring);
                }
            }
            finally { canvas.RestoreToCount(save); }
        }
    }
}
