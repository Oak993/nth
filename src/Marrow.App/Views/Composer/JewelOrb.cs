using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Views.Composer;

// DEVIATION OD-0090 (owner ruling, 2026-10-03, locked page E:\Droppy\work\design\jewel-knob-locked\): the effort knob is the
// Jewel orb. PORTED CODE: the Violet Ember fluid, the noise bank, the contour functions and the glass shell below are a
// transcription into SkSL of knob-orbs-1.js (GLSL ES 3.00), itself a transcription of "Liquid Orb Editor"
// (https://github.com/lersent001/orb), effect.wgsl, src/presets.ts and src/orb-states.ts: MIT License, Copyright (c) 2026
// LerSent001 (THIRD_PARTY_NOTICES.md carries the notice). knob-orbs-1.js's Marrow additions are carried as written: the
// ball filling the knob, the pixel-aware edge, the 2x render below 128 device px filtered down 2:1, the flare's highlight
// roll-off, the charge and flare light toward the exit angle, the glass hairline with its pink/blue fringe and the light
// pane's contact line. The page's own layers over the orb (jewel-knob.html .kw) are drawn in the same pass: the tube
// refracted at the rim (.thru.r: the tube turned over and minified 0.6x about the knob's centre, blurred 0.5 px, in a ring
// 72-87-96-100 % of the radius, at 26 %), the rim highlight and the inset fringe and shade (.kw::after).
// Engineering decisions: one SkSL program, drawn once a frame into a pooled surface at the render size and filtered down
// into the knob (no WebGL); its uniforms are one float4 block written in place and handed to Skia's C API, so a frame makes
// no managed objects on the render thread. Reduce motion stills the gem's flow (OD-0090: light only; knob-orbs-1.js lets it
// creep at 0.35x, the owner's text says still).

/// <summary>
/// The orb's two states as knob-orbs-1.js buildStates() makes them for Violet Ember: the preset's values (presets.ts) and its
/// Idle profile (orb-states.ts), with the material's explicit colours and numbers laid over each.
/// </summary>
public sealed record JewelOrbStates(JewelOrbState Think, JewelOrbState Idle, SKColor Tint)
{
    // presets.ts violetEmber (Thinking) and its Idle multipliers (orb-states.ts)
    private const double Speed = 1.12, ContourDeform = 0.04, Zoom = 0.58, Warp = 4.7, RidgeAmt = 0.73, Sharp = 3.3, Shade = 0.18,
        Sheen = 0.2, Gloss = 0.34, GlassOpacity = 0.62, ShellMidAlpha = 0.28, ShellEdgeAlpha = 0.24, Exposure = 1.28;

    public static JewelOrbStates Of(JewelMaterial m)
    {
        JewelOrbState Lay(JewelState s, double contour, double zoom, double warp, double ridge, double sharp) =>
            new([s.ColorA, s.ColorB, s.ColorC, s.ColorD, s.Highlight, s.ShellInner, s.ShellMid, s.ShellEdge, s.Sheen, s.Spec, s.Canvas, s.Glow],
                s.Speed, contour, s.Zoom ?? zoom, s.Warp ?? warp, s.RidgeAmt ?? ridge, s.Sharp ?? sharp, Shade, Sheen, Gloss, GlassOpacity,
                ShellMidAlpha, ShellEdgeAlpha, s.Exposure);
        return new(Lay(m.Think, ContourDeform, Zoom, Warp, RidgeAmt, Sharp),
            Lay(m.Idle, ContourDeform * 0.3, Zoom * 0.92, Warp * 0.46, RidgeAmt * 0.42, Sharp * 0.78), m.Surge);
    }
}

/// <summary>One state's values: the colours in the shader's order (colorA..D, highlight, shellInner, shellMid, shellEdge,
/// sheen, spec, canvas, glow) and the numbers the program reads.</summary>
public sealed record JewelOrbState(IReadOnlyList<SKColor> Colors, double Speed, double ContourDeform, double Zoom, double Warp, double RidgeAmt,
    double Sharp, double Shade, double Sheen, double Gloss, double GlassOpacity, double ShellMidAlpha, double ShellEdgeAlpha, double Exposure);

/// <summary>
/// The orb's life (knob-orbs-1.js makeOrb().frame): the activation (it follows the charge up, and settles back to Idle over
/// 0.65 s, smoothstep), the integrated motion phase (speed from Idle's to Thinking's with the activation: 2.6 to 4.2 for the
/// Jewel), and the uniforms of a frame. UI thread.
/// </summary>
public sealed class JewelOrb
{
    /// <summary>The settle back to Idle, s (orb-states.ts defaultOrbTransitionDuration).</summary>
    public const double Settle = 0.65;

    private double? last;
    private double peak, peakT;

    public JewelOrb(JewelMaterial material) { States = JewelOrbStates.Of(material); }

    public JewelOrbStates States { get; private set; }

    /// <summary>recolor(colors): the next frames use this material; the motion goes on where it is.</summary>
    public void Recolor(JewelMaterial material) => States = JewelOrbStates.Of(material);

    /// <summary>The integrated motion phase (the shader's time).</summary>
    public double Phase { get; private set; }

    /// <summary>The activation, 0 (Idle) to 1 (Thinking).</summary>
    public double Activation { get; private set; }

    /// <summary>The gem's speed at the last frame (Idle's 2.6 to Thinking's 4.2; 0 under reduce motion).</summary>
    public double Speed { get; private set; }

    /// <summary>
    /// The render size and the render pixels per CSS pixel of a knob <paramref name="px"/> CSS px wide at
    /// <paramref name="dpr"/> (resize(px)): below 128 device px it renders at 2x, to be filtered down 2:1.
    /// </summary>
    public static (int Size, double PerPx) RenderSize(double px, double dpr)
    {
        dpr = Math.Min(3, Math.Max(1, dpr));
        var dev = Math.Max(2, (int)Math.Floor(px * dpr + 0.5));
        var scale = dev < 128 ? 2 : 1;
        return (dev * scale, dpr * scale);
    }

    /// <summary>True when a knob <paramref name="px"/> CSS px wide at <paramref name="dpr"/> renders at 2x.</summary>
    public static bool Supersampled(double px, double dpr) => Math.Max(2, (int)Math.Floor(px * Math.Min(3, Math.Max(1, dpr)) + 0.5)) < 128;

    private static double Smooth(double x) { var c = Math.Clamp(x, 0, 1); return c * c * (3 - 2 * c); }
    private static double Lin(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    private static double Srgb(double v) => v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1 / 2.4) - 0.055;

    /// <summary>
    /// One frame at <paramref name="t"/> s: the activation and the phase move on, and <paramref name="into"/> receives the
    /// program's values for a <paramref name="size"/> px render at <paramref name="perPx"/> render pixels per CSS pixel.
    /// <paramref name="charge"/> and <paramref name="flare"/> are the material's share of the lightning's signal.
    /// </summary>
    public void Frame(double t, double charge, double flare, double exitAngle, bool reduceMotion, int size, double perPx, bool dark, JewelUniforms into)
    {
        var dt = last is { } before ? Math.Min(0.1, Math.Max(0, t - before)) : 0;
        last = t;
        charge = Math.Clamp(double.IsFinite(charge) ? charge : 0, 0, 1); flare = Math.Clamp(double.IsFinite(flare) ? flare : 0, 0, 1);
        var target = Math.Max(charge, flare);
        if (target >= Activation) { Activation = target; peak = Activation; peakT = t; }
        else Activation = Math.Max(target, peak * (1 - Smooth((t - peakT) / Settle)));
        var k = reduceMotion ? 0 : Activation; // reduced motion: no speed-up, no shape change; light only
        var (T, I) = (States.Think, States.Idle);
        double L(double i, double th) => i + (th - i) * k;
        Speed = reduceMotion ? 0 : L(I.Speed, T.Speed);
        Phase += dt * Speed;
        var u = into.Values;
        u[0] = size; u[1] = size; u[2] = (float)Phase;
        u[4] = (float)L(I.Zoom, T.Zoom); u[5] = (float)L(I.Warp, T.Warp); u[6] = (float)L(I.RidgeAmt, T.RidgeAmt); u[7] = (float)L(I.Sharp, T.Sharp);
        u[8] = (float)L(I.Shade, T.Shade); u[9] = (float)T.Sheen; u[10] = (float)T.Gloss; u[11] = (float)T.ShellMidAlpha;
        u[12] = (float)T.ShellEdgeAlpha; u[13] = (float)L(I.Exposure, T.Exposure); u[14] = (float)T.GlassOpacity;
        var contour = L(I.ContourDeform, T.ContourDeform);
        u[15] = (float)contour;
        // colours: the state colours (colorA..D, highlight, glow) mixed in linear light by the activation; the shell's as Idle has them
        for (var i = 0; i < 12; i++)
        {
            SKColor a = I.Colors[i], b = T.Colors[i];
            var mixes = i is < 5 or 11;
            for (var ch = 0; ch < 3; ch++)
            {
                double va = (ch == 0 ? a.Red : ch == 1 ? a.Green : a.Blue) / 255.0, vb = (ch == 0 ? b.Red : ch == 1 ? b.Green : b.Blue) / 255.0;
                u[28 + i * 4 + ch] = (float)(mixes ? Srgb(Lin(va) + (Lin(vb) - Lin(va)) * Activation) : va);
            }
        }
        // ball radius in uv: fill the canvas, leaving room for the contour wobble and the edge anti-aliasing
        var cd = Math.Max(contour, T.ContourDeform);
        var ballRad = (1 - 1.5 / size) / (1 + cd * 0.11) / (1 + Math.Max(0.01, 1.5 / size));
        u[16] = (float)Activation; u[17] = (float)(reduceMotion ? flare * 0.6 : flare); u[18] = (float)(double.IsFinite(exitAngle) ? exitAngle : Math.PI); u[19] = 2f / size;
        u[20] = (float)ballRad; u[21] = dark ? 1 : 0; u[22] = (float)perPx;
        u[109] = (float)cd;
        u[24] = States.Tint.Red / 255f; u[25] = States.Tint.Green / 255f; u[26] = States.Tint.Blue / 255f;
    }
}

/// <summary>
/// One frame's values for the knob's programs, as one block of float4 slots (the layout <see cref="JewelKnobShader"/>
/// declares): [0] render size, size, phase, knob CSS px; [1] zoom, warp, ridgeAmt, sharp; [2] shade, sheen, gloss,
/// shellMidAlpha; [3] shellEdgeAlpha, exposure, glassOpacity, contourDeform; [4] activation, flare, exit angle, a device px
/// in uv; [5] ball radius, dark, render px per CSS px, ring opacity; [6] the flare's tint; [7..18] the twelve colours;
/// [19] the knob's centre on the track, the track's width and height; [20] the pane; [21] the empty tube; [22..26] the fill
/// along its length. A last slot the program does not read: [27] the lift (the deeper shadow's opacity), the contour's
/// largest deform (for <see cref="Resize"/>), and the supersample the frame was made for (2: a 2x render, 1: none).
/// </summary>
public sealed class JewelUniforms
{
    /// <summary>The program's float4 slots.</summary>
    public const int Slots = 27;

    /// <summary>The program's uniform block, bytes.</summary>
    public const int ShaderBytes = Slots * 16;

    public float[] Values { get; } = new float[(Slots + 1) * 4];

    /// <summary>The deeper shadow's opacity (how far the knob has lifted).</summary>
    public float Lift { get => Values[108]; set => Values[108] = value; }

    /// <summary>
    /// The same frame for another render size (the CPU renderer draws the knob at 1x, without the 2x supersample, so its cost
    /// stays bounded): the size, a device pixel in uv, the ball's radius and the render pixels per CSS pixel follow it.
    /// </summary>
    public static void Resize(float[] u, int size, double perPx)
    {
        var cd = u[109];
        u[0] = size; u[1] = size; u[19] = 2f / size; u[22] = (float)perPx;
        u[20] = (float)((1 - 1.5 / size) / (1 + cd * 0.11) / (1 + Math.Max(0.01, 1.5 / size)));
    }

    public float Phase => Values[2];
    public (float Zoom, float Warp, float RidgeAmt, float Sharp) Fold => (Values[4], Values[5], Values[6], Values[7]);
    public float Exposure => Values[13];
    public float ContourDeform => Values[15];
    public (float Activation, float Flare, float Exit, float Px) Fx => (Values[16], Values[17], Values[18], Values[19]);
    public (float BallRadius, float Dark, float PerPx) Kn => (Values[20], Values[21], Values[22]);
    public (float R, float G, float B) Tint => (Values[24], Values[25], Values[26]);

    /// <summary>Colour <paramref name="i"/> (the shader's order), sRGB 0..1.</summary>
    public (float R, float G, float B) Color(int i) => (Values[28 + i * 4], Values[29 + i * 4], Values[30 + i * 4]);

    /// <summary>The page's layers: the knob's CSS size, its place on the track and what the ring refracts.</summary>
    public void SetPage(double knobPx, double ringOpacity, double knobX, double trackWidth, double trackHeight, SKColor pane, SKColor empty, ReadOnlySpan<SKColor> fill)
    {
        var u = Values;
        u[3] = (float)knobPx; u[23] = (float)ringOpacity;
        u[76] = (float)knobX; u[77] = (float)trackWidth; u[78] = (float)trackHeight;
        Put(80, pane); Put(84, empty);
        for (var i = 0; i < 5; i++) Put(88 + i * 4, fill.Length == 0 ? empty : fill[Math.Min(i * (fill.Length - 1) / 4, fill.Length - 1)]);
        void Put(int at, SKColor c) { u[at] = c.Red / 255f; u[at + 1] = c.Green / 255f; u[at + 2] = c.Blue / 255f; u[at + 3] = c.Alpha / 255f; }
    }

    public void CopyTo(JewelUniforms other) => Array.Copy(Values, other.Values, Values.Length);
}

/// <summary>The knob's SkSL program (see the file note).</summary>
public static class JewelKnobShader
{
    // ED-252 review (OD-0090, ED-233): four programs, not one. On ANGLE (D3D11) the one-program form took 506 ms to compile
    // on its first draw, two programs (the fluid, the glass) 87 to 105 ms each (measured on a real window); the lead's bar is
    // about 50 ms a compile. So: the fluid's first two noise passes (its low and cross currents), then the rest of the
    // fluid reading them, once per dispersion sample; the orb's glass, light and hairline reading the three samples; the
    // page's layers over the orb. Each program writes floats at the very pixels the next one reads (nearest, at the same
    // pixel centres): nothing is resampled. The currents are half floats split in two (a part a half float holds exactly and
    // the rest), about 1e-6 each; the fluid's samples half floats; the orb full floats where the GPU has them (in half floats
    // the orb's 8-bit rounding moved: the pixel test's mean went from 0.09-0.17 to 0.14-0.30 of 255).
    private const string Head = """
        uniform float4 u[27];

        // ---- effect.wgsl's noise bank, the Violet Ember fluid (style 21) and the contour, after knob-orbs-1.js
        float lqHash(float2 pIn) {
            float2 p = fract(pIn * float2(123.34, 456.21));
            p = p + float2(dot(p, p + float2(45.32)));
            return fract(p.x * p.y);
        }
        float lqNoise(float2 p) {
            float2 i = floor(p);
            float2 f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            return mix(mix(lqHash(i), lqHash(i + float2(1.0, 0.0)), f.x), mix(lqHash(i + float2(0.0, 1.0)), lqHash(i + float2(1.0, 1.0)), f.x), f.y);
        }
        float2 lqFbm(float2 pIn, float bs) {
            float2 p = pIn;
            float s = 0.0; float a = 0.5; float m = 0.0; float vr = 0.0; float g = 1.0;
            float e = -6.0 * bs * bs;
            for (int i = 0; i < 5; i++) {
                float b = exp(e * g);
                s += a * (0.5 + b * (lqNoise(p) - 0.5));
                vr += a * a * (1.0 - b * b);
                m += a;
                a *= 0.5;
                g *= 4.1209;
                p = float2(0.8 * p.x - 0.6 * p.y, 0.6 * p.x + 0.8 * p.y) * 2.03;
            }
            return float2(s / m, 0.32 * sqrt(vr) / m);
        }
        float lqRidge(float v, float k) { return pow(clamp(1.0 - abs(v * 2.0 - 1.0), 0.0, 1.0), k); }
        float3 lqRamp(float v) {
            float3 c = mix(u[7].rgb, u[8].rgb, smoothstep(0.0, 0.45, v));
            c = mix(c, u[9].rgb, smoothstep(0.38, 0.72, v));
            c = mix(c, u[10].rgb, smoothstep(0.68, 1.0, v));
            return c;
        }
        float lqRidgeS(float2 vs, float k) {
            float d = 1.73205081 * vs.y;
            return (lqRidge(vs.x - d, k) + 4.0 * lqRidge(vs.x, k) + lqRidge(vs.x + d, k)) / 6.0;
        }
        float3 glsFinishPresetFluid(float3 colorIn, float2 p) {
            float shade = u[2].x;
            float3 color = colorIn;
            color = mix(color, u[11].rgb, shade * 0.22 * smoothstep(0.15, 1.15, dot(p, float2(-0.32, 0.78))));
            color = color * (1.0 - shade * 0.34 * smoothstep(-0.1, 1.2, dot(p, float2(0.45, -0.62))));
            color = color * (1.0 - shade * 0.22 * smoothstep(0.72, 1.08, length(p)));
            return clamp(color, float3(0.0), float3(1.0));
        }
        float2 glsRotate(float2 p, float angle) { float c = cos(angle); float s = sin(angle); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
        float glsHighlightLobe(float2 normal, float2 direction, float cut, float power) {
            float angular = clamp((dot(normal, direction) - cut) / max(1.0 - cut, 0.001), 0.0, 1.0);
            return pow(angular, power);
        }
        float glsRefractionProfile(float t) {
            float depth = clamp(t, 0.0, 1.0);
            return 1.0 - sqrt(max(1.0 - (1.0 - depth) * (1.0 - depth), 0.0));
        }
        float3 glsOver(float3 dst, float3 src, float a) { float k = clamp(a, 0.0, 1.0); return src * k + dst * (1.0 - k); }
        float2 glsContourWave(float angle, float t) {
            float wave = sin(angle * 3.0 + t * 0.62) * 0.52 + sin(angle * 5.0 - t * 0.41 + 1.7) * 0.31 + sin(angle * 2.0 + t * 0.23 + 3.1) * 0.17;
            float slope = cos(angle * 3.0 + t * 0.62) * 1.56 + cos(angle * 5.0 - t * 0.41 + 1.7) * 1.55 + cos(angle * 2.0 + t * 0.23 + 3.1) * 0.34;
            return float2(wave, slope);
        }
        float glsContourScale(float2 uv, float t, float amount) {
            if (amount <= 0.0) return 1.0;
            return 1.0 + clamp(amount, 0.0, 1.0) * 0.11 * glsContourWave(atan(uv.y, uv.x), t).x;
        }
        float2 glsContourNormal(float2 uv, float rad, float t, float amount) {
            float distance = length(uv);
            if (distance <= 0.0001) return float2(0.0);
            float2 radial = uv / distance;
            float slope = clamp(amount, 0.0, 1.0) * 0.11 * glsContourWave(atan(uv.y, uv.x), t).y;
            float2 tangent = float2(-radial.y, radial.x);
            return normalize(radial - tangent * (rad * slope / distance));
        }
        // the ball's geometry at a pixel (knob-orbs-1.js MAIN's head, style 21): false outside the ball
        bool ball(float2 fc, out float2 p, out float2 normal, out float2 refractedP, out float channelSplit, out float clearFa,
                  out float edgeDepth, out float pd, out float ae, out float pxp) {
            float2 size = u[0].xy;
            float2 uv = (2.0 * fc - size) / max(min(size.x, size.y), 1.0);
            float rad = u[5].x;
            float t = u[0].z;
            float contourDeform = u[3].w;
            float contourRad = rad * glsContourScale(uv, t, contourDeform);
            pxp = u[4].w / contourRad;
            ae = max(0.01, 0.75 * pxp);
            p = float2(0.0); normal = float2(0.0); refractedP = float2(0.0); channelSplit = 0.0; clearFa = 0.0; edgeDepth = 0.0; pd = 0.0;
            if (length(uv) > contourRad * (1.0 + ae)) return false;
            float gloss = u[2].z; float shellMidAlpha = u[2].w; float glassOpacity = u[3].z;
            p = uv / contourRad;
            pd = length(p);
            clearFa = 1.0 - smoothstep(0.995, 1.04, pd);
            normal = glsContourNormal(uv, rad, t, contourDeform);
            edgeDepth = max(1.0 - pd, 0.0);
            float refractionWidth = 0.015 + 0.95 * clamp(shellMidAlpha, 0.0, 1.0);
            float refractionProfile = pow(glsRefractionProfile(edgeDepth / max(refractionWidth, 0.001)), 0.68);
            refractedP = p - normal * (1.6 * clamp(glassOpacity, 0.0, 1.0) * refractionProfile);
            channelSplit = 0.14 * clamp(gloss, 0.0, 2.0) * clamp(glassOpacity, 0.0, 1.0) * refractionProfile;
            return true;
        }
        """;

    /// <summary>The noise program: the fluid's low and cross currents (their values) for one dispersion sample.</summary>
    internal const string NoiseSource = Head + """
        half4 main(float2 coord) {
            float2 fc = float2(coord.x, u[0].y - coord.y);
            float2 p; float2 normal; float2 refractedP; float channelSplit; float clearFa; float edgeDepth; float pd; float ae; float pxp;
            if (!ball(fc, p, normal, refractedP, channelSplit, clearFa, edgeDepth, pd, ae, pxp) || clearFa <= 0.0) return half4(0.0);
            float k = u[19].w;
            if (k != 0.0 && channelSplit <= 0.0) return half4(0.0);
            float2 at = refractedP + normal * (channelSplit * k);
            float2 p0 = at; float t = u[0].z;
            float zoom = u[1].x; float warp = u[1].y; float ridgeAmt = u[1].z; float sharp = u[1].w;
            float scale = 1.08 + zoom * 1.18;
            float blur = 0.011 + 0.005 * zoom;
            float radius = length(p0);
            float twist = t * 0.055 + radius * (0.72 + warp * 0.11) + 0.08 * sin(t * 0.31 + radius * 4.0);
            float2 q = glsRotate(p0 * scale, twist);
            float2 low = lqFbm(q * 1.18 + float2(t * 0.068, -t * 0.105), blur * 1.18);
            float2 crs = lqFbm(glsRotate(q, -1.12) * 1.52 + float2(-t * 0.094, t * 0.042) + float2(low.x * 1.35, -low.x * 0.72), blur * 1.52);
            // each current to about 1e-6, not a half float's 2.4e-4: a part exact in a half float (1/1024 steps) and the rest,
            // scaled into a half float's finer range around 0.5 (the fluid program adds them back)
            float lowHi = floor(low.x * 1024.0 + 0.5) / 1024.0; float crsHi = floor(crs.x * 1024.0 + 0.5) / 1024.0;
            return half4(half(lowHi), half(0.5 + (low.x - lowHi) * 512.0), half(crsHi), half(0.5 + (crs.x - crsHi) * 512.0));
        }
        """;

    /// <summary>The fluid program: the rest of the Violet Ember fluid, over the noise program's currents, per sample.</summary>
    internal const string FluidSource = """
        uniform shader noise;
        """ + Head + """
        float3 glsPresetFluid(float2 p, float t, float lowX, float crsX) {
            float zoom = u[1].x; float warp = u[1].y; float ridgeAmt = u[1].z; float sharp = u[1].w;
            float scale = 1.08 + zoom * 1.18;
            float blur = 0.011 + 0.005 * zoom;
            float radius = length(p);
            float twist = t * 0.055 + radius * (0.72 + warp * 0.11) + 0.08 * sin(t * 0.31 + radius * 4.0);
            float2 q = glsRotate(p * scale, twist);
            float2 warped = q + float2(lowX - 0.5, crsX - 0.5) * (0.3 + warp * 0.12);
            float2 melt = lqFbm(warped * 1.34 + float2(crsX * 1.48, lowX * 1.12), blur * 1.34);
            float vs = 2.05 + zoom * 0.72;
            float veins = lqRidgeS(lqFbm(warped * vs + float2(-2.1, t * 0.052), blur * vs), 0.82 + sharp * 0.58);
            float heat = smoothstep(0.18, 0.92, melt.x * (0.72 - ridgeAmt * 0.16) + veins * (0.32 + ridgeAmt * 0.5));
            float3 color = lqRamp(heat);
            color = color * (0.94 + 0.06 * sin(t * 0.44 + melt.x * 5.0));
            color = mix(color, u[11].rgb, pow(veins, 4.0) * 0.045);
            return glsFinishPresetFluid(color, p);
        }
        // one dispersion sample of the fluid at every pixel (u[19].w: -1 red, 0 green, +1 blue; the red and blue ones only
        // where the glass refracts), for the orb program to read at the same pixel
        half4 main(float2 coord) {
            float2 fc = float2(coord.x, u[0].y - coord.y);
            float2 p; float2 normal; float2 refractedP; float channelSplit; float clearFa; float edgeDepth; float pd; float ae; float pxp;
            if (!ball(fc, p, normal, refractedP, channelSplit, clearFa, edgeDepth, pd, ae, pxp) || clearFa <= 0.0) return half4(0.0);
            float k = u[19].w;
            if (k != 0.0 && channelSplit <= 0.0) return half4(0.0);
            float2 at = refractedP + normal * (channelSplit * k);
            half4 currents = noise.eval(coord);
            float lowX = float(currents.r) + (float(currents.g) - 0.5) / 512.0;
            float crsX = float(currents.b) + (float(currents.a) - 0.5) / 512.0;
            return half4(half3(glsPresetFluid(at, u[0].z, lowX, crsX)), 1.0);
        }
        """;

    /// <summary>The orb program: the shell, the lightning and the hairline over the fluid's three samples.</summary>
    internal const string OrbSource = """
        uniform shader fluidR;
        uniform shader fluidG;
        uniform shader fluidB;
        """ + Head + """

        float3 mrFlareRoll(float3 c, float fl) {
            float3 hard = clamp(c, float3(0.0), float3(1.0));
            float3 k = max(c - 0.72, float3(0.0));
            float3 soft = min(c, float3(0.72)) + 0.28 * (float3(1.0) - exp(-k / 0.28));
            return mix(hard, clamp(soft, float3(0.0), float3(1.0)), clamp(fl * 1.6, 0.0, 1.0));
        }
        // orbGlassLiquidAnim for the knob (knob-orbs-1.js MAIN, style 21), premultiplied
        half4 orb(float2 fc, float2 coord) {
            float2 p; float2 normal; float2 refractedP; float channelSplit; float clearFa; float edgeDepth; float pd; float ae; float pxp;
            if (!ball(fc, p, normal, refractedP, channelSplit, clearFa, edgeDepth, pd, ae, pxp)) return half4(0.0);
            float2 contourNormal = normal;
            float sheen = u[2].y; float gloss = u[2].z;
            float shellEdgeAlpha = u[3].x; float exposure = u[3].y; float glassOpacity = u[3].z;
            float3 fcol = float3(0.0);
            if (clearFa > 0.0) {
                fcol = float3(fluidG.eval(coord).rgb);
                // the three dispersion samples differ only where the glass refracts (the same values elsewhere); the CPU
                // fallback takes the middle one only (u[6].w)
                if (channelSplit > 0.0 && u[6].w < 0.5) {
                    fcol.r = float(fluidR.eval(coord).r);
                    fcol.b = float(fluidB.eval(coord).b);
                }
            }
            float lum = dot(fcol, float3(0.213, 0.715, 0.072));
            float3 clearSat = clamp(float3(lum) + (fcol - float3(lum)) * 1.22, float3(0.0), float3(1.0));
            float3 col = glsOver(u[17].rgb, clearSat, 0.99 * clearFa);
            // glass shell
            float surfaceWidth = 0.026 + 0.055 * clamp(shellEdgeAlpha, 0.0, 1.0);
            float surfaceBand = (1.0 - smoothstep(0.0, surfaceWidth, edgeDepth)) * clearFa;
            float opticalRim = pow(surfaceBand, 1.8);
            col = glsOver(col, u[12].rgb, opticalRim * glassOpacity * 0.45);
            float coolSplit = glsHighlightLobe(normal, normalize(float2(0.84, 0.54)), -0.32, 1.8);
            float warmSplit = glsHighlightLobe(normal, normalize(float2(-0.62, -0.78)), -0.28, 2.0);
            float dispersion = opticalRim * clamp(gloss, 0.0, 2.0) * (0.8 + 0.8 * shellEdgeAlpha);
            col = glsOver(col, u[13].rgb, dispersion * coolSplit);
            col = glsOver(col, u[14].rgb, dispersion * warmSplit);
            float edgeShadow = opticalRim * (0.015 + 0.15 * shellEdgeAlpha) * (0.15 + 0.85 * max(dot(normal, float2(0.45, -0.89)), 0.0));
            col = col * (1.0 - edgeShadow);
            float key = opticalRim * glsHighlightLobe(normal, normalize(float2(-0.68, 0.73)), 0.2, 2.8) * clamp(sheen, 0.0, 2.0) * 1.4;
            float fill = opticalRim * glsHighlightLobe(normal, normalize(float2(0.74, -0.67)), 0.4, 3.6) * clamp(sheen, 0.0, 2.0) * 1.0;
            col = glsOver(col, u[15].rgb, key);
            col = glsOver(col, u[16].rgb, fill);
            // Marrow: the lightning (activation, flare, exit angle)
            float act = u[4].x; float fl = u[4].y;
            float2 ex = float2(cos(u[4].z), -sin(u[4].z));
            float2 cc = ex * 0.16 * act;
            float core = exp(-dot(p - cc, p - cc) * mix(7.0, 3.2, max(act, fl)));
            float3 tint = u[6].rgb;
            col += mix(tint, float3(1.0), 0.35) * core * (0.16 * act * act + 0.42 * fl) * clearFa;
            float lobe = glsHighlightLobe(contourNormal, ex, 0.35, 2.2);
            float rimBand = 1.0 - smoothstep(0.0, 0.28, edgeDepth);
            col += mix(tint, float3(1.0), 0.55) * lobe * rimBand * (0.22 * act * act + 1.05 * fl) * clearFa;
            float2 dx = p - ex * 0.94;
            col += mix(tint, float3(1.0), 0.7) * exp(-dot(dx, dx) * 20.0) * fl * 0.75 * clearFa;
            col += float3(1.0) * exp(-dot(dx, dx) * 70.0) * fl * 0.9 * clearFa;
            float expo = max(exposure, 0.0) * (1.0 + 0.42 * fl);
            // Marrow: the glass hairline (1 CSS px) with the knob's pink/blue fringe; a contact line on a light pane
            float hw = u[5].z * pxp;
            float light = 1.0 - u[5].y;
            float hair = (1.0 - smoothstep(hw * 0.55, hw * 1.35, edgeDepth)) * (0.75 + 0.25 * max(contourNormal.y, 0.0)) * clearFa;
            float3 hairC = mix(float3(1.0), float3(1.0, 0.353, 0.549), 0.55 * max(-contourNormal.x, 0.0));
            hairC = mix(hairC, float3(0.353, 0.667, 1.0), 0.55 * max(contourNormal.x, 0.0));
            float hairA = mix(0.42, 0.62, light);
            float contact = light * (1.0 - smoothstep(0.0, hw * 0.8, edgeDepth)) * clearFa * 0.31;
            float ballA = 1.0 - smoothstep(1.0 - ae, 1.0 + ae, pd);
            col = mrFlareRoll(col * expo, fl);
            col = glsOver(col, hairC, hair * hairA);
            col = glsOver(col, float3(0.06, 0.06, 0.09), contact * 0.32);
            return half4(half3(col * ballA), half(ballA));
        }

        half4 main(float2 coord) { return orb(float2(coord.x, u[0].y - coord.y), coord); }
        """;

    /// <summary>The page program: the tube refracted at the rim, the rim highlight and the fringes, over the orb.</summary>
    internal const string PageSource = """
        uniform shader orbs;
        """ + Head + """
        // ---- the page's layers (jewel-knob.html .kw), in the knob's CSS px
        float erf(float x) { float a = 0.147; float x2 = x * x; float r = sqrt(max(0.0, 1.0 - exp(-x2 * (1.2732395 + a * x2) / (1.0 + a * x2)))); return x < 0.0 ? -r : r; }
        float phi(float x) { return 0.5 * (1.0 + erf(x * 0.70710678)); }
        // a capsule's signed distance (box x0..x1, y0..y1)
        float capsule(float2 p, float x0, float x1, float y0, float y1) {
            float r = (y1 - y0) * 0.5; float cy = (y0 + y1) * 0.5;
            float a = x0 + r; float b = max(a, x1 - r);
            return length(float2(p.x - clamp(p.x, a, b), p.y - cy)) - r;
        }
        float3 fillAt(float f) {
            float x = clamp(f, 0.0, 1.0) * 4.0;
            if (x < 1.0) return mix(u[22].rgb, u[23].rgb, x);
            if (x < 2.0) return mix(u[23].rgb, u[24].rgb, x - 1.0);
            if (x < 3.0) return mix(u[24].rgb, u[25].rgb, x - 2.0);
            return mix(u[25].rgb, u[26].rgb, x - 3.0);
        }
        half4 over(half4 dst, half4 src) { return src + dst * (1.0 - src.a); }
        half4 main(float2 coord) {
            float2 size = u[0].xy;
            float perPx = u[5].z;
            half4 o = orbs.eval(coord); // the orb, at this very pixel
            float kn = u[0].w; float R = kn * 0.5;
            if (kn <= 0.0) return o; // the orb alone (tests compare it with knob-orbs-1.js's canvas)
            float2 q = coord / perPx;
            float d = length(q - float2(R));
            float cov = clamp((R - d) * perPx + 0.5, 0.0, 1.0);
            if (cov <= 0.0) return o;
            // .thru.r: the tube behind, turned over and minified 0.6x about the centre, blurred 0.5 px, in the rim ring
            float rho = d / R;
            float mask = rho <= 0.72 ? 0.0 : rho < 0.87 ? (rho - 0.72) / 0.15 : rho <= 0.96 ? 1.0 : clamp((1.0 - rho) / 0.04, 0.0, 1.0);
            float ringA = u[5].w * mask * cov;
            if (ringA > 0.0) {
                float x = u[19].x; float W = u[19].y; float H = u[19].z; float a = 0.6;
                float y0 = R - a * H * 0.5; float y1 = R + a * H * 0.5;
                float tube = 1.0 - phi(capsule(q, R + a * x - a * W, R + a * x, y0, y1) / 0.5);
                float lx0 = R * (1.0 - a); float lx1 = R + a * x;
                float liquid = 1.0 - phi(capsule(q, lx0, lx1, y0, y1) / 0.5);
                float3 rc = mix(mix(u[20].rgb, u[21].rgb, tube), fillAt(1.0 - (q.x - lx0) / max(lx1 - lx0, 0.001)), liquid);
                o = over(o, half4(half3(rc * ringA), half(ringA)));
            }
            // .kw::after: the rim highlight (radial-gradient(64% 40% at 50% 9%)), then the inset shade, blue and pink fringes
            float t = length((q - float2(R, 0.09 * kn)) / float2(0.64 * kn, 0.4 * kn));
            float hl = t <= 0.55 ? mix(0.24, 0.07, t / 0.55) : t < 1.0 ? mix(0.07, 0.0, (t - 0.55) / 0.45) : 0.0;
            o = over(o, half4(half(hl * cov)));
            float shadeA = 0.16 * (1.0 - phi((R - length(q - float2(R, R - 1.5))) / 1.25)) * cov;
            o = over(o, half4(0.0, 0.0, 0.0, half(shadeA)));
            float blueA = 0.42 * (1.0 - phi((R + 0.1 - length(q - float2(R - 0.6, R))) / 0.35)) * cov;
            o = over(o, half4(half3(float3(96.0, 176.0, 255.0) / 255.0 * blueA), half(blueA)));
            float pinkA = 0.4 * (1.0 - phi((R + 0.1 - length(q - float2(R + 0.6, R))) / 0.35)) * cov;
            o = over(o, half4(half3(float3(255.0, 96.0, 150.0) / 255.0 * pinkA), half(pinkA)));
            return o;
        }
        """;

    private static Lazy<SKRuntimeEffect> Compile(string source, string what) => new(() =>
        SKRuntimeEffect.CreateShader(source, out var error) ?? throw new InvalidOperationException("The Jewel knob's " + what + " program did not compile: " + error));

    private static readonly Lazy<SKRuntimeEffect> noise = Compile(NoiseSource, "noise"), fluid = Compile(FluidSource, "fluid"),
        orb = Compile(OrbSource, "orb"), page = Compile(PageSource, "page");

    public static SKRuntimeEffect Noise => noise.Value;
    public static SKRuntimeEffect Fluid => fluid.Value;
    public static SKRuntimeEffect Orb => orb.Value;
    public static SKRuntimeEffect Page => page.Value;

    /// <summary>Makes the four programs (Skia's SkSL compile, on the CPU, any thread): the GPU warm-up does it off the render
    /// thread before its first step, so that thread only links them (or loads them from the cache).</summary>
    internal static void Prepare() { _ = Noise; _ = Fluid; _ = Orb; _ = Page; }
}

/// <summary>
/// The knob's drawing on the render thread: the program into a surface at the render size (kept, one per size and GPU
/// context), then filtered down into the knob's box. Nothing managed is made per frame (native shaders through Skia's C API).
/// </summary>
internal sealed class JewelKnobRenderer
{
    [ThreadStatic] private static Dictionary<(nint Context, int Size), JewelKnobRenderer>? pool;

    /// <summary>Tests and evidence frames: the CPU renderer draws the GPU's path too (2x, every frame, all three samples).</summary>
    internal static bool SupersampleOnCpu { get; set; }

    /// <summary>The 1x size of a frame made for a 2x render (null when it was already at 1x).</summary>
    internal static (int Size, double PerPx)? CpuSize(float[] values) =>
        values[110] == 2 ? ((int)values[0] / 2, values[22] / 2.0) : null;

    private readonly SKSurface surface;
    private readonly SKPaint program = new();
    private readonly SKPaint blit = new() { IsAntialias = false };
    // jewel-knob.html: --knobShadow 0 2px 6px, and the lift's 0 7px 18px and 0 2px 5px (CSS blur radius b: sigma b / 2)
    private readonly SKPaint shadow = new() { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3) };
    // (rgba(0,0,0,.34) and .2, drawn as a group at the lift's opacity: liftGroup)
    private readonly SKPaint liftFar = new() { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 9), Color = new SKColor(0, 0, 0, (byte)Math.Round(0.34 * 255)) };
    private readonly SKPaint liftNear = new() { IsAntialias = true, MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 2.5f), Color = new SKColor(0, 0, 0, (byte)Math.Round(0.2 * 255)) };
    private readonly SKPaint liftGroup = new();
    private readonly SKPath circle = new();
    private SKRect circleFor;
    private readonly nint block = Marshal.AllocHGlobal(JewelUniforms.ShaderBytes); // the uniforms' native copy, kept
    private readonly int size;

    private readonly GRContext? gpu;

    private JewelKnobRenderer(GRContext? gpu, int size)
    {
        this.size = size;
        this.gpu = gpu;
        var info = new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info)
            ?? throw new InvalidOperationException("The Jewel knob's surface could not be made.");
    }

    /// <summary>The renderer for a size on this render thread's context (made once, kept).</summary>
    public static JewelKnobRenderer For(GRContext? gpu, int size)
    {
        pool ??= [];
        var key = (gpu?.Handle ?? 0, size);
        if (!pool.TryGetValue(key, out var renderer))
        {
            // a new GPU context retires the renderers of an old one
            foreach (var old in pool.Where(p => p.Key.Context != key.Item1).Select(p => p.Key).ToArray()) { pool[old].Dispose(); pool.Remove(old); }
            pool[key] = renderer = new JewelKnobRenderer(gpu, size);
        }
        return renderer;
    }

    // Temporary warm-up contexts must release this thread's retained surfaces before the context is destroyed.
    internal static void Retire(GRContext gpu)
    {
        if (pool is null) return;
        foreach (var key in pool.Keys.Where(k => k.Context == gpu.Handle).ToArray()) { pool[key].Dispose(); pool.Remove(key); }
    }

    private void Dispose() { foreach (var made in surfaces) made?.Dispose(); surface.Dispose(); program.Dispose(); blit.Dispose(); shadow.Dispose(); liftFar.Dispose(); liftNear.Dispose(); liftGroup.Dispose(); circle.Dispose(); Marshal.FreeHGlobal(block); }

    /// <summary>
    /// GpuWarmup's steps (render thread): 0 the noise program, 1 the fluid, 2 the orb, 3 the page with the
    /// shadows' blur and the blit (a whole frame of the knob into an offscreen pixel). The surfaces and programs are the
    /// knob's own, so its first visible draw finds them made.
    /// </summary>
    internal void Warm(int step)
    {
        var values = warmValues ??= WarmFrame(size);
        Marshal.Copy(values, 0, block, JewelUniforms.ShaderBytes / sizeof(float));
        Marshal.WriteInt32(block, 79 * sizeof(float), BitConverter.SingleToInt32Bits(0f));
        switch (step)
        {
            case 0: Pass(JewelKnobShader.Noise, Surface(gpu, size, Noise, 1), size); break;
            case 1: Pass(JewelKnobShader.Fluid, Surface(gpu, size, Fluid, 1), size, Surface(gpu, size, Noise, 1)); break;
            case 2:
                var g = Surface(gpu, size, Fluid, 1);
                Pass(JewelKnobShader.Orb, Surface(gpu, size, Orbs, 0), size, g, g, g);
                break;
            default:
                using (var pixel = (gpu is not null ? SKSurface.Create(gpu, false, new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul)) : null)
                    ?? SKSurface.Create(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul)))
                    Draw(pixel.Canvas, values, new SKRect(-60, -60, -12, -12), 1); // the page program, the shadows and the blit: nothing lands
                break;
        }
    }

    private float[]? warmValues;

    private static float[] WarmFrame(int px)
    {
        var u = new JewelUniforms();
        new JewelOrb(JewelMaterial.For(EffortBrand.Purple)).Frame(0, 0, 0, Math.PI, false, px, px / EffortSlider.ThumbSize, true, u);
        u.SetPage(EffortSlider.ThumbSize, 0.26, 100, 298, EffortSlider.TrackHeight, SKColors.Black, SKColors.Black, [SKColors.Black, SKColors.Black, SKColors.Black, SKColors.Black, SKColors.Black]);
        u.Lift = 1; // the lift's blurs too
        return u.Values;
    }

    /// <summary>The GPU's frame: the shadows, the program at the frame's own size, filtered into the knob's box.</summary>
    public void Draw(SKCanvas canvas, float[] uniforms, SKRect box, float opacity)
    {
        Shadows(canvas, uniforms, box, opacity);
        Program(surface, size, uniforms);
        if (Probe) ProbeCentre();
        Blit(canvas, surface, size, box, opacity);
    }

    /// <summary>The native evidence run only (off in the app): read back the program's centre pixel after each frame.</summary>
    internal static volatile bool Probe;

    private static long probed = -1, probeFailures;

    /// <summary>Read-backs that failed.</summary>
    internal static long ProbeFailures => Interlocked.Read(ref probeFailures);

    /// <summary>The last centre pixel read back (0xAARRGGBB as Skia's BGRA bytes), or -1.</summary>
    internal static long ProbedCentre => Interlocked.Read(ref probed);

    private void ProbeCentre()
    {
        var pixel = Marshal.AllocHGlobal(4);
        try
        {
            if (surface.ReadPixels(new SKImageInfo(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul), pixel, 4, size / 2, size / 2))
                Interlocked.Exchange(ref probed, unchecked((uint)Marshal.ReadInt32(pixel)));
            else Interlocked.Increment(ref probeFailures);
        }
        finally { Marshal.FreeHGlobal(pixel); }
    }

    /// <summary>The shadows, outside the knob's circle as CSS box-shadows are: the knob's own, then the lift's.</summary>
    internal void Shadows(SKCanvas canvas, float[] uniforms, SKRect box, float opacity)
    {
        if (circleFor != box) { circle.Rewind(); circle.AddOval(box); circleFor = box; }
        float cx = box.MidX, cy = box.MidY, r = box.Width / 2;
        var dark = uniforms[21] > 0.5f;
        var lift = Math.Clamp(uniforms[108], 0, 1);
        canvas.Save();
        canvas.ClipPath(circle, SKClipOperation.Difference, true);
        shadow.Color = dark ? new SKColor(0, 0, 0, (byte)Math.Round(0.24 * 255 * opacity)) : new SKColor(20, 32, 50, (byte)Math.Round(0.2 * 255 * opacity));
        canvas.DrawCircle(cx, cy + 2, r, shadow);
        if (lift > 0)
        {
            // s.lift's opacity is the element's (jewel-knob.html:118, stepSlider :597): its two shadows composite together,
            // then the group at the lift's opacity, as CSS draws an element with opacity
            liftGroup.Color = new SKColor(0, 0, 0, (byte)Math.Round(255 * lift * opacity));
            canvas.SaveLayer(SKRect.Inflate(box, (float)JewelKnobView.ShadowReach, (float)JewelKnobView.ShadowReach), liftGroup);
            canvas.DrawCircle(cx, cy + 2, r, liftNear);
            canvas.DrawCircle(cx, cy + 7, r, liftFar);
            canvas.Restore();
        }
        canvas.Restore();
    }

    /// <summary>
    /// Runs the programs into <paramref name="target"/> (<paramref name="px"/> square): the fluid's samples (the middle one
    /// only when the frame asks, u[6].w), then the glass over them.
    /// </summary>
    internal void Program(SKSurface target, int px, float[] uniforms)
    {
        var context = target.Context as GRContext;
        Marshal.Copy(uniforms, 0, block, JewelUniforms.ShaderBytes / sizeof(float));
        var middleOnly = uniforms[27] >= 0.5f;
        for (var k = -1; k <= 1; k++)
        {
            if (middleOnly && k != 0) continue;
            Marshal.WriteInt32(block, 79 * sizeof(float), BitConverter.SingleToInt32Bits(k));
            var currents = Surface(context, px, Noise, k + 1);
            Pass(JewelKnobShader.Noise, currents, px);
            Pass(JewelKnobShader.Fluid, Surface(context, px, Fluid, k + 1), px, currents);
        }
        Marshal.WriteInt32(block, 79 * sizeof(float), BitConverter.SingleToInt32Bits(0f));
        SKSurface Sample(int k) => Surface(context, px, Fluid, middleOnly ? 1 : k);
        var orbs = Surface(context, px, Orbs, 0);
        Pass(JewelKnobShader.Orb, orbs, px, Sample(0), Sample(1), Sample(2));
        Pass(JewelKnobShader.Page, target, px, orbs);
    }

    // one program into a surface, its children the snapshots of the given surfaces read at the same pixels (nearest)
    // (up to three inputs, as arguments: no array a frame)
    private void Pass(SKRuntimeEffect effect, SKSurface into, int px, SKSurface? first = null, SKSurface? second = null, SKSurface? third = null)
    {
        var handles = first is null ? null : second is null ? one : children;
        if (handles is not null)
            for (var k = 0; k < handles.Length; k++)
            {
                var image = JewelNative.Snapshot(k == 0 ? first! : k == 1 ? second! : third!);
                handles[k] = JewelNative.ImageShader(image, SKMatrix.Identity, nearest: true);
                JewelNative.ReleaseImage(image);
            }
        var shader = JewelNative.Shader(effect, block, JewelUniforms.ShaderBytes, handles);
        if (handles is not null) for (var k = 0; k < handles.Length; k++) { JewelNative.Release(handles[k]); handles[k] = 0; }
        var c = into.Canvas;
        c.Clear(SKColors.Transparent);
        JewelNative.SetShader(program, shader);
        JewelNative.Release(shader);
        c.DrawRect(new SKRect(0, 0, px, px), program);
        JewelNative.SetShader(program, 0);
    }

    // the passes' surfaces at the render size (the currents and the fluid's samples half floats, the orb full floats), kept
    private const int Noise = 0, Fluid = 3, Orbs = 6;
    private readonly SKSurface?[] surfaces = new SKSurface?[7];
    private readonly int[] surfaceSizes = new int[7];
    private readonly nint[] children = new nint[3], one = new nint[1];

    private SKSurface Surface(GRContext? context, int px, int kind, int k)
    {
        var at = kind + k;
        if (surfaces[at] is { } kept && surfaceSizes[at] == px) return kept;
        surfaces[at]?.Dispose();
        var info = new SKImageInfo(px, px, SKColorType.RgbaF16, SKAlphaType.Premul);
        SKSurface? Make(SKImageInfo of) => context is not null ? SKSurface.Create(context, false, of) : SKSurface.Create(of);
        var made = (kind == Orbs ? Make(info.WithColorType(SKColorType.RgbaF32)) : null) ?? Make(info)
            ?? (context is not null ? SKSurface.Create(context, false, info.WithColorType(SKColorType.Rgba8888)) : SKSurface.Create(info.WithColorType(SKColorType.Rgba8888)))
            ?? throw new InvalidOperationException("The Jewel knob's pass surface could not be made.");
        surfaces[at] = made; surfaceSizes[at] = px;
        return made;
    }

    /// <summary>Draws <paramref name="source"/> (<paramref name="px"/> square) into the knob's box, filtered linearly.</summary>
    internal void Blit(SKCanvas canvas, SKSurface source, int px, SKRect box, float opacity)
    {
        var image = JewelNative.Snapshot(source);
        var matrix = SKMatrix.CreateScale(box.Width / px, box.Height / px).PostConcat(SKMatrix.CreateTranslation(box.Left, box.Top));
        var sampled = JewelNative.ImageShader(image, matrix);
        JewelNative.ReleaseImage(image);
        JewelNative.SetShader(blit, sampled);
        JewelNative.Release(sampled);
        blit.Color = SKColors.White.WithAlpha((byte)Math.Clamp(Math.Round(opacity * 255), 0, 255));
        canvas.DrawRect(box, blit);
        JewelNative.SetShader(blit, 0);
    }
}

/// <summary>
/// One knob on the CPU renderer (the software fallback, no GPU context; render thread only). The page's look costs too much
/// there (measured p50 21 and 31 ms a frame at 1.5x and 2x display scale), so on the CPU only: the orb renders at 48 px at
/// most, whatever the scale, and is filtered up into the knob; the gem's program runs every other frame (30 Hz), the frame
/// between reusing its picture; the two dispersion samples at the rim are skipped (the middle one only); and the knob measures
/// its own program: when the median of its last 16 runs exceeds 7 ms it steps the render size down (48, 40, 32, 24 px), and
/// back up when the median falls under 2.5 ms. The shadows are drawn every frame (they follow the knob and its lift).
/// </summary>
internal sealed class JewelCpuKnob
{
    /// <summary>The render sizes it steps through, px.</summary>
    internal static readonly int[] Sizes = [48, 40, 32, 24];

    /// <summary>The program's median over 16 runs above which it steps down, and below which it steps back up, ms.</summary>
    internal const double StepDown = 7, StepUp = 2.5;

    [ThreadStatic] private static JewelCpuKnob? shared;

    private SKSurface? surface;
    private int size;
    private long frames;
    private readonly float[] scratch = new float[(JewelUniforms.Slots + 1) * 4];
    private readonly double[] times = new double[16], sorted = new double[16];
    private int filled, next;

    /// <summary>A knob without its own state (a drawing made outside a slider).</summary>
    internal static JewelCpuKnob Shared => shared ??= new JewelCpuKnob();

    /// <summary>The step: an index into <see cref="Sizes"/>.</summary>
    public int Level { get; private set; }

    /// <summary>How many frames it has drawn, and how many of them ran the program.</summary>
    public long Frames => frames;
    public long Programs { get; private set; }

    /// <summary>The size it last rendered at, px.</summary>
    public int RenderedSize => size;

    /// <summary>Its last picture of the gem (tests).</summary>
    internal SKSurface? Picture => surface;

    /// <summary>The render size for a frame: its 1x device size, at most this step's.</summary>
    internal int SizeFor(float[] values)
    {
        var device = CpuSize(values)?.Size ?? (int)values[0];
        return Math.Max(2, Math.Min(device, Sizes[Level]));
    }

    private static (int Size, double PerPx)? CpuSize(float[] values) => JewelKnobRenderer.CpuSize(values);

    public void Draw(SKCanvas canvas, float[] values, SKRect box, float opacity)
    {
        var px = SizeFor(values);
        var renderer = JewelKnobRenderer.For(null, px);
        var fresh = surface is null || size != px;
        if (fresh)
        {
            surface?.Dispose();
            surface = SKSurface.Create(new SKImageInfo(px, px, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("The Jewel knob's surface could not be made.");
            size = px;
        }
        renderer.Shadows(canvas, values, box, opacity);
        if (fresh || frames % 2 == 0)
        {
            Array.Copy(values, scratch, scratch.Length);
            JewelUniforms.Resize(scratch, px, px / Math.Max(1, (double)values[3]));
            scratch[27] = 1; // the middle sample only
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            renderer.Program(surface!, px, scratch);
            Record(System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Programs++;
        }
        frames++;
        renderer.Blit(canvas, surface!, px, box, opacity);
    }

    /// <summary>One program run's time, ms; steps the size when the median of 16 calls for it.</summary>
    internal void Record(double ms)
    {
        times[next] = ms; next = (next + 1) % times.Length;
        if (++filled < times.Length) return;
        Array.Copy(times, sorted, times.Length);
        Array.Sort(sorted);
        var median = (sorted[7] + sorted[8]) / 2;
        if (median > StepDown && Level < Sizes.Length - 1) { Level++; filled = 0; }
        else if (median < StepUp && Level > 0) { Level--; filled = 0; }
        else filled = times.Length - 1; // a sliding window
    }
}

/// <summary>
/// libSkiaSharp's C API for the frame path, the functions SkiaSharp's own SkiaApi binds: SkiaSharp's managed ToShader copies
/// the uniforms into an SKData it never disposes and wraps every shader and snapshot in a finalizable object, so a frame
/// path through it leaves garbage behind every frame (measured on the liquid track, ED-248 review). Each native reference
/// here is released at once.
/// </summary>
internal static class JewelNative
{
    private const string Skia = "libSkiaSharp";

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_data_new_with_copy(nint source, nint length);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_data_unref(nint data);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_runtimeeffect_make_shader(nint effect, nint uniforms, nint[]? children, nint childCount, ref SKMatrix localMatrix);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_shader_unref(nint shader);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_paint_set_shader(nint paint, nint shader);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_surface_new_image_snapshot(nint surface);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern nint sk_image_make_shader(nint image, SKShaderTileMode tileX, SKShaderTileMode tileY, ref SKSamplingOptions sampling, ref SKMatrix localMatrix);

    [DllImport(Skia, CallingConvention = CallingConvention.Cdecl)]
    private static extern void sk_image_unref(nint image);

    /// <summary>The native functions bound here (a test compares their parameter types with SkiaSharp's own bindings).</summary>
    internal static IReadOnlyList<System.Reflection.MethodInfo> Bindings => typeof(JewelNative)
        .GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
        .Where(m => m.Name.StartsWith("sk_", StringComparison.Ordinal)).ToArray();

    /// <summary>The effect's shader for a uniform block (copied): a native reference, released with <see cref="Release"/>.</summary>
    public static nint Shader(SKRuntimeEffect effect, nint uniforms, int bytes, nint[]? children = null)
    {
        var identity = SKMatrix.Identity;
        var block = sk_data_new_with_copy(uniforms, bytes);
        try { return sk_runtimeeffect_make_shader(effect.Handle, block, children, children?.Length ?? 0, ref identity); }
        finally { sk_data_unref(block); }
    }

    public static nint Snapshot(SKSurface surface) => sk_surface_new_image_snapshot(surface.Handle);

    /// <summary>The image's shader through <paramref name="matrix"/>, sampled linearly, transparent outside it.</summary>
    public static nint ImageShader(nint image, SKMatrix matrix, bool nearest = false)
    {
        var sampling = new SKSamplingOptions(nearest ? SKFilterMode.Nearest : SKFilterMode.Linear);
        return sk_image_make_shader(image, SKShaderTileMode.Decal, SKShaderTileMode.Decal, ref sampling, ref matrix);
    }

    public static void SetShader(SKPaint paint, nint shader) => sk_paint_set_shader(paint.Handle, shader);

    public static void Release(nint shader) { if (shader != 0) sk_shader_unref(shader); }

    public static void ReleaseImage(nint image) { if (image != 0) sk_image_unref(image); }
}

/// <summary>
/// One frame of the knob, its values fixed on the UI thread, drawn on the render thread. A slider's view keeps a few and
/// takes one back when Avalonia disposes it (Avalonia disposes a drawing operation only once it will not be drawn again:
/// on the UI thread before it is sent, on the render thread when the next frame's drawing replaces it), so a frame makes
/// no new operation or uniform block.
/// </summary>
internal sealed class JewelKnobOperation : ICustomDrawOperation
{
    private readonly Action<JewelKnobOperation>? giveBack;
    private readonly Action<float[]>? update;
    private int given;

    public JewelKnobOperation(Rect bounds, Rect box, float[] values, JewelCpuKnob? cpu = null, Action<JewelKnobOperation>? giveBack = null, Action<float[]>? update = null)
    {
        Bounds = bounds; Box = box; Values = values; Cpu = cpu; this.giveBack = giveBack; this.update = update;
    }

    public Rect Bounds { get; private set; }
    public Rect Box { get; private set; }
    public float[] Values { get; }
    public JewelCpuKnob? Cpu { get; }

    /// <summary>Makes a returned operation the next frame's.</summary>
    internal void Reuse(Rect bounds, Rect box, float[] values)
    {
        Bounds = bounds; Box = box;
        Array.Copy(values, Values, Values.Length);
        Volatile.Write(ref given, 0);
    }

    public bool HitTest(Point p) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { if (Interlocked.Exchange(ref given, 1) == 0) giveBack?.Invoke(this); }

    public void Render(ImmediateDrawingContext context)
    {
        update?.Invoke(Values);
        if (Values[0] <= 0) return;
        var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()
            ?? throw new InvalidOperationException("The Jewel knob needs the Skia renderer (ISkiaSharpApiLeaseFeature).");
        using var lease = feature.Lease();
        var box = new SKRect((float)Box.Left, (float)Box.Top, (float)Box.Right, (float)Box.Bottom);
        var opacity = (float)lease.CurrentOpacity;
        if (lease.GrContext is null && !JewelKnobRenderer.SupersampleOnCpu)
        {
            (Cpu ?? JewelCpuKnob.Shared).Draw(lease.SkCanvas, Values, box, opacity);
            Interlocked.Increment(ref cpuFrames);
            return;
        }
        if (lease.GrContext is not null) GpuProgramCache.ForContext(lease.GrContext); // (once a context; the warm-up has normally done it)
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { JewelKnobRenderer.For(lease.GrContext, (int)Values[0]).Draw(lease.SkCanvas, Values, box, opacity); }
        catch (Exception error) { LastError = error.GetType().Name + ": " + error.Message; throw; } // recorded for the native evidence run, not swallowed
        if (lease.GrContext is null) return;
        LastGpuFrameTicks = Environment.TickCount64;
        // the GPU path's record (the native evidence run): how many frames, and the longest draw call (the first takes the
        // program's compile)
        var ms = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (Interlocked.Increment(ref gpuFrames) == 1) firstGpuRenderMs = ms;
        else if (ms > maxGpuRenderMs) maxGpuRenderMs = ms;
    }

    private static long gpuFrames, cpuFrames;

    /// <summary>Diagnostics for the native evidence run: the last render-thread error, when the last GPU frame was drawn.</summary>
    internal static volatile string? LastError;
    internal static long LastGpuFrameTicks;
    private static double maxGpuRenderMs, firstGpuRenderMs;

    /// <summary>The first knob draw call on a GPU context, ms (it carries the programs' compile).</summary>
    internal static double FirstGpuRenderMs => Volatile.Read(ref firstGpuRenderMs);

    /// <summary>Knob frames drawn on a GPU context and on the CPU renderer, in this process.</summary>
    internal static long GpuFrames => Interlocked.Read(ref gpuFrames);
    internal static long CpuFrames => Interlocked.Read(ref cpuFrames);

    /// <summary>The longest knob draw call on a GPU context after the first, ms.</summary>
    internal static double MaxGpuRenderMs => Volatile.Read(ref maxGpuRenderMs);
}
