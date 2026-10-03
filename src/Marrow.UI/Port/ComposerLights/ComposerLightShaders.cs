// PORT-ONLY, owner-ruled addition (OD-0083, OD-0089; ED-251): the composer light's shaders. The board's "in the port" notes
// (E:/Droppy/work/design/variants/project/ComposerLights.dc.html) are the plan: what blurs is baked once per size (the
// Uiverse halo and rim, Border beam's inset shadow and bloom edge: blurred rounded rectangles), and one SkSL shader per
// light and frame draws everything else from uniforms. Border beam's blobs are radial gradients, evaluated directly
// (ellipse primitives from BorderBeamScene); its windows are the package's profiles (one 2048-texel row each) at the
// share of the outline a pixel maps to: fitted, the ray from the centre (the stroke, the highlight, the bloom) or from an
// end's centre (the inner wash's ends), exactly as beam.js's conics place their stops; as the package, the angle.
// border-beam is Copyright (c) 2026 Jakub Antalik (MIT); the Uiverse light is SelfMadeSystem's (Uiverse.io, MIT).
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

internal static class ComposerLightShaders
{
    /// <summary>The window profiles' rows in <see cref="Profiles"/>: w1, w2, the dark and light highlight, the dark and light bloom.</summary>
    public const int RowW1 = 0, RowW2 = 1, RowHighlightDark = 2, RowHighlightLight = 3, RowBloomDark = 4, RowBloomLight = 5;
    public const int ProfileWidth = 2048;

    private const string Common = """
        const float PI = 3.14159265;
        float rrSdf(float2 p, float2 size, float r) {
            float2 c = size * 0.5;
            float2 q = abs(p - c) - c + r;
            return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
        }
        float erf(float x) { float a = 0.147; float x2 = x * x; float r = sqrt(max(0.0, 1.0 - exp(-x2 * (1.2732395 + a * x2) / (1.0 + a * x2)))); return x < 0.0 ? -r : r; }
        """;

    // Border beam. geo: clip radius, ring radius, device scale, mode (0 rotate, 1 line, 2 pulse inner, 3 pulse outside).
    // win: phase, window radius, fitted, unused. rows: stroke window, inner window, highlight, bloom. bands: on, dv, dh,
    // bloom sigma. opac: inner, stroke, bloom, slot alpha. outer: core opacity, bloom opacity, core sigma, bloom sigma.
    // flags: filter stroke and inner, filter bloom, hue on the bloom, has a shadow.
    internal const string BeamSource = """
        uniform float2 box;
        uniform float4 geo;
        uniform float4 win;
        uniform float4 rows;
        uniform float4 bands;
        uniform float4 opac;
        uniform float4 outer;
        uniform float4 flags;
        uniform float4 shadowC;
        uniform float4 hlc;
        uniform float3x3 hueS;
        uniform float3x3 hueB;
        uniform float3x3 satM;
        uniform float bright;
        uniform float4 lmask;
        uniform float lmaskB;
        uniform float count;
        uniform float4 eg[40];
        uniform float4 es[40];
        uniform float4 e0[40];
        uniform float4 e1[40];
        uniform float4 e2[40];
        uniform shader shadowTex;
        uniform shader bloomTex;
        uniform shader profiles;
        """ + Common + """

        float cov(float d) { return clamp(0.5 - d * geo.z, 0.0, 1.0); }
        float prof(float row, float f) { return float(profiles.eval(float2(fract(f) * 2048.0, row + 0.5)).a); }
        float perimLen(float W, float H, float R) { return 2.0 * (W + H) - (8.0 - 2.0 * PI) * R; }

        // the arc length (from the top centre, clockwise) of the outline point a ray from c along d leaves the box at
        float rayS(float2 c, float2 d, float W, float H, float R) {
            float a = 0.5 * PI * R;
            float s0 = W * 0.5 - R; float s1 = s0 + a; float s2 = s1 + (H - 2.0 * R); float s3 = s2 + a;
            float s4 = s3 + (W - 2.0 * R); float s5 = s4 + a; float s6 = s5 + (H - 2.0 * R); float s7 = s6 + a;
            float best = 1e9; float s = 0.0; float e = 1e-3;
            if (d.y < -1e-6) { float t = -c.y / d.y; float x = c.x + t * d.x; if (t < best && x >= R - e && x <= W - R + e) { best = t; s = x >= W * 0.5 ? x - W * 0.5 : s7 + (x - R); } }
            if (d.y > 1e-6) { float t = (H - c.y) / d.y; float x = c.x + t * d.x; if (t < best && x >= R - e && x <= W - R + e) { best = t; s = s3 + (W - R - x); } }
            if (d.x > 1e-6) { float t = (W - c.x) / d.x; float y = c.y + t * d.y; if (t < best && y >= R - e && y <= H - R + e) { best = t; s = s1 + (y - R); } }
            if (d.x < -1e-6) { float t = -c.x / d.x; float y = c.y + t * d.y; if (t < best && y >= R - e && y <= H - R + e) { best = t; s = s5 + (H - R - y); } }
            if (R > 1e-3) {
                for (int k = 0; k < 4; k++) {
                    float2 ac = k == 0 ? float2(W - R, R) : k == 1 ? float2(W - R, H - R) : k == 2 ? float2(R, H - R) : float2(R, R);
                    float2 oc = c - ac; float b = dot(oc, d); float cc = dot(oc, oc) - R * R; float disc = b * b - cc;
                    if (disc < 0.0) continue;
                    float t = -b + sqrt(disc);
                    if (t >= best || t < 0.0) continue;
                    float2 h = c + t * d;
                    bool ok = k == 0 ? (h.x >= ac.x - e && h.y <= ac.y + e) : k == 1 ? (h.x >= ac.x - e && h.y >= ac.y - e)
                            : k == 2 ? (h.x <= ac.x + e && h.y >= ac.y - e) : (h.x <= ac.x + e && h.y <= ac.y + e);
                    if (!ok) continue;
                    float g = atan(h.x - ac.x, ac.y - h.y);
                    // each arc's angle range, unwrapped: TR 0..pi/2, BR pi/2..pi, BL pi..3pi/2, TL 3pi/2..2pi
                    if ((k == 1 || k == 2) && g < 0.0) g += 2.0 * PI;
                    if (k == 3 && g < PI) g += 2.0 * PI;
                    best = t;
                    s = k == 0 ? s0 + R * g : k == 1 ? s2 + R * (g - 0.5 * PI) : k == 2 ? s4 + R * (g - PI) : s6 + R * (g - 1.5 * PI);
                }
            }
            return s;
        }

        // the share of the turn a ring pixel shows: fitted, the outline point on the ray from the centre; else the angle
        float uCentre(float2 p) {
            float2 c = box * 0.5; float2 d = p - c;
            if (win.z < 0.5) { float a = atan(d.x, -d.y); return fract(a / (2.0 * PI) + 1.0); }
            float R = clamp(win.y, 0.0, min(box.x, box.y) * 0.5);
            if (dot(d, d) < 1e-8) d = float2(0.0, -1.0);
            return rayS(c, normalize(d), box.x, box.y, R) / perimLen(box.x, box.y, R);
        }

        // the inner wash's share: fitted, the straight top and bottom by x, the ends by the ray from the end's centre
        float uInner(float2 p) {
            if (win.z < 0.5) return uCentre(p);
            float W = box.x; float H = box.y; float R = clamp(win.y, 0.0, min(W, H) * 0.5);
            float E = H * 0.5; float L = perimLen(W, H, R);
            if (p.x >= E && p.x <= W - E) {
                if (p.y < H * 0.5) return fract((p.x - W * 0.5) / L + 1.0);
                return fract(((W * 0.5 - R) + PI * R + (H - 2.0 * R) + (W - R - p.x)) / L);
            }
            float2 c = float2(p.x < E ? E : W - E, H * 0.5); float2 d = p - c;
            if (dot(d, d) < 1e-8) d = float2(p.x < E ? -1.0 : 1.0, 0.0);
            return rayS(c, normalize(d), W, H, R) / L;
        }

        // the 1 px stroke: the border box (radius rr) less the content box (1 px in, its corners rr - 1), the mask
        // `linear-gradient(#fff 0 0) content-box, linear-gradient(#fff 0 0)` composited xor
        float ringMask(float2 p, float rr) {
            float outerC = cov(rrSdf(p, box, rr));
            float inner = cov(rrSdf(p - 1.0, box - 2.0, max(0.0, rr - 1.0)));
            return outerC * (1.0 - inner);
        }

        // linear-gradient(white, transparent d, transparent calc(100% - d), white) over a length
        float band(float y, float H, float d) {
            float s1 = max(d, 1e-4); float s2 = max(s1, H - d);
            y = clamp(y, 0.0, H);
            if (y < s1) return 1.0 - y / s1;
            if (y < s2) return 0.0;
            return (y - s2) / max(H - s2, 1e-4);
        }
        float bandsMask(float2 p) { float a = band(p.y, box.y, bands.y); float b = band(p.x, box.x, bands.z); return a + b - a * b; }

        // Line's masks: radial-gradient(ellipse rx ry at x 100%, white 0, rgba(255,255,255,.5) s, transparent)
        float maskEll(float2 p, float rx, float ry, float s) {
            float t = length((p - float2(lmask.x, box.y)) / float2(max(rx, 1e-3), max(ry, 1e-3)));
            if (t < s) return 1.0 - 0.5 * t / s;
            if (t < 1.0) return 0.5 * (1.0 - (t - s) / (1.0 - s));
            return 0.0;
        }

        // the CSS filter chain on a premultiplied colour: hue-rotate, brightness, saturate, each clamped
        half4 filt(half4 c, float3x3 hue, bool useHue) {
            if (c.a <= 0.0) return c;
            float3 rgb = float3(c.rgb) / float(c.a);
            if (useHue) rgb = clamp(hue * rgb, 0.0, 1.0);
            rgb = clamp(rgb * bright, 0.0, 1.0);
            rgb = clamp(satM * rgb, 0.0, 1.0);
            return half4(half3(rgb * float(c.a)), c.a);
        }

        // a halo of Pulse Outside: its box inset -e, radius r + e, scaled .95 x .9 about the centre, blurred
        float softBox(float2 p, float e, float sigma) {
            float2 c = box * 0.5;
            float2 q = c + (p - c) / float2(0.95, 0.9) + e;
            float d = rrSdf(q, box + 2.0 * e, geo.x + e) * 0.92;
            if (sigma < 0.3) return cov(d);
            return 0.5 * (1.0 - erf(d / (sigma * 1.41421356)));
        }

        half4 main(float2 p) {
            half4 a0 = half4(0.0); half4 a1 = half4(0.0); half4 a2 = half4(0.0); half4 a3 = half4(0.0); half4 a4 = half4(0.0);
            for (int i = 0; i < 40; i++) {
                if (float(i) >= count) break;
                float4 g = eg[i]; float4 s = es[i];
                float t = length((p - g.xy) / g.zw);
                if (t >= s.z) continue;
                half4 c;
                if (t < s.x) c = half4(mix(e0[i], e1[i], t / s.x));
                else if (t < s.y) c = half4(mix(e1[i], e2[i], (t - s.x) / (s.y - s.x)));
                else c = half4(e2[i] * (1.0 - (t - s.y) / (s.z - s.y)));
                if (s.w < 0.5) a0 = c + a0 * (1.0 - c.a);
                else if (s.w < 1.5) a1 = c + a1 * (1.0 - c.a);
                else if (s.w < 2.5) a2 = c + a2 * (1.0 - c.a);
                else if (s.w < 3.5) a3 = c + a3 * (1.0 - c.a);
                else a4 = c + a4 * (1.0 - c.a);
            }
            float mode = geo.w;
            float clip = cov(rrSdf(p, box, geo.x));
            half4 col = half4(0.0);
            half4 sh = half4(0.0);
            if (flags.w > 0.5 && clip > 0.0) sh = half4(shadowC) * half(1.0 - float(shadowTex.eval(p).a));
            if (mode < 0.5) {
                float ph = win.x;
                half4 inner = half4(0.0); half4 stroke = half4(0.0); half4 bloom = half4(0.0);
                if (clip > 0.0) {
                    inner = sh + a0 * (1.0 - sh.a);
                    float m = bands.x > 0.5 ? bandsMask(p) : 1.0;
                    inner = inner * half(m * prof(rows.y, uInner(p) - ph) * opac.x);
                    if (flags.x > 0.5) inner = filt(inner, hueS, true);
                }
                float ring = ringMask(p, geo.y);
                if (ring > 0.0) {
                    float u = uCentre(p);
                    float hl = prof(rows.z, u - ph);
                    half4 bg = half4(hlc) * half(hl) + a1 * half(1.0 - hl);
                    stroke = bg * half(prof(rows.x, u - ph) * ring * opac.y);
                    if (flags.x > 0.5) stroke = filt(stroke, hueS, true);
                    // the bloom: the highlight's conic blurred (a Gaussian-weighted mean over the box's inside) times the
                    // blurred edge, on the ring
                    float sg = bands.w; float ws = 0.0; float vs = 0.0;
                    for (int a = 0; a < 5; a++) {
                        for (int b = 0; b < 5; b++) {
                            float2 o = float2(float(a) - 2.0, float(b) - 2.0);
                            float2 q = p + sg * o;
                            float w = exp(-0.5 * dot(o, o)) * cov(rrSdf(q, box, geo.y));
                            if (w > 0.0) { ws += w; vs += w * prof(rows.w, uCentre(q) - ph); }
                        }
                    }
                    float bl = ws > 0.0 ? vs / ws : 0.0;
                    bloom = half4(hlc) * half(bl * float(bloomTex.eval(p).a) * ring * opac.z);
                    bloom = filt(bloom, hueB, false);
                }
                col = stroke + inner * (1.0 - stroke.a);
                col = bloom + col * (1.0 - bloom.a);
                col *= half(clip);
            } else if (mode < 1.5) {
                float ms = maskEll(p, lmask.y, lmask.z, 0.45);
                half4 inner = half4(0.0); half4 stroke = half4(0.0); half4 bloom = half4(0.0);
                if (clip > 0.0) {
                    inner = (sh + a0 * (1.0 - sh.a)) * half(ms * bandsMask(p) * opac.x);
                    if (flags.x > 0.5) inner = filt(inner, hueS, true);
                    // the spikes are painted inside the layer's box, then blurred: the box's blurred edge dims them at the bottom
                    bloom = a2 * half(maskEll(p, lmask.w, lmaskB, 0.35) * float(bloomTex.eval(p).a) * opac.z);
                    if (flags.y > 0.5) bloom = filt(bloom, hueB, flags.z > 0.5);
                }
                float ring = ringMask(p, geo.y);
                if (ring > 0.0) {
                    stroke = a1 * half(ms * ring * opac.y);
                    if (flags.x > 0.5) stroke = filt(stroke, hueS, true);
                }
                col = stroke + inner * (1.0 - stroke.a);
                col = bloom + col * (1.0 - bloom.a);
                col *= half(clip);
            } else if (mode < 2.5) {
                float ring = ringMask(p, geo.y);
                half4 inner = filt(a0 * half(bandsMask(p) * opac.x), hueS, flags.x > 0.5);
                half4 stroke = filt(a1 * half(ring * opac.y), hueS, flags.x > 0.5);
                half4 bloom = half4(0.0);
                if (ring > 0.0) bloom = filt(a2 * half(ring * float(bloomTex.eval(p).a) * opac.z), hueB, flags.z > 0.5);
                col = stroke + inner * (1.0 - stroke.a);
                col = bloom + col * (1.0 - bloom.a);
                col *= half(clip);
            } else {
                half4 stroke = filt(a1 * half(ringMask(p, geo.y) * opac.y * clip), hueS, flags.x > 0.5);
                half4 core = filt(a3 * half(softBox(p, 10.0, outer.z) * outer.x), hueS, flags.x > 0.5);
                half4 halo = filt(a4 * half(softBox(p, 30.0, outer.w) * outer.y), hueS, flags.x > 0.5);
                half4 o = halo + core * (1.0 - halo.a);
                o *= half(1.0 - clip);
                col = stroke + o * (1.0 - stroke.a);
            }
            return col * half(opac.w);
        }
        """;

    // The Uiverse light. g: radius, device scale, angle (radians), breathing scale. halo: hs, hb, sx, sy. rim: rw, rsx, rsy,
    // unused. alphas: halo, rim, inside, group. dots: strength, cell, origin, unused. fade: y where it is gone, ramp, on.
    internal const string AuraSource = """
        uniform float2 box;
        uniform float4 g;
        uniform float4 halo;
        uniform float4 rim;
        uniform float4 alphas;
        uniform float4 dots;
        uniform float4 fade;
        uniform float4 c1;
        uniform float4 c2;
        uniform float4 k1;
        uniform float4 k2;
        uniform float4 n1;
        uniform float4 n2;
        uniform shader haloTex;
        uniform shader rimTex;
        """ + Common + """

        float cov(float d) { return clamp(0.5 - d * g.y, 0.0, 1.0); }
        // E[max(0, x + Z)], Z ~ N(0, s): a ramp smoothed by a Gaussian
        float sramp(float x, float s) {
            if (s < 1e-4) return max(0.0, x);
            float z = x / s;
            return x * 0.5 * (1.0 + erf(z * 0.70710678)) + s * 0.39894228 * exp(-0.5 * z * z);
        }
        // the reference's gradient: cA up to t0, to transparent at t1, transparent to t2, cB from t3, smoothed by s
        half4 grad(float t, float s, half4 cA, half4 cB, float t0, float t1, float t2, float t3) {
            float ka = 1.0 / (t1 - t0); float kb = 1.0 / (t3 - t2);
            float wa = 1.0 - ka * sramp(t - t0, s) + ka * sramp(t - t1, s);
            float wb = kb * sramp(t - t2, s) - kb * sramp(t - t3, s);
            return cA * half(clamp(wa, 0.0, 1.0)) + cB * half(clamp(wb, 0.0, 1.0));
        }
        // the share along the turning square's gradient: transform scale(sx, sy) rotate(a) scale(s) about the centre
        float tOf(float2 p, float sx, float sy) {
            float2 v = (p - box * 0.5) / float2(sx, sy);
            float ca = cos(g.z); float sa = sin(g.z);
            float qx = (ca * v.x + sa * v.y) / g.w;
            return (qx + 200.0) / 400.0;
        }
        // the gradient of t over the plane (t changes along this, at this rate)
        float2 gradT(float sx, float sy) {
            float ca = cos(g.z); float sa = sin(g.z);
            return float2(ca / sx, sa / sy) / (g.w * 400.0);
        }
        float cover(float2 p, bool isHalo) { return isHalo ? float(haloTex.eval(p).a) : float(rimTex.eval(p).a); }
        // a blurred layer, blur(clip x gradient): the clip's blurred coverage A times the gradient's mean under the kernel
        // restricted to the clip. A Gaussian kernel's restricted moments come from A's derivatives: the mean is
        // p + s^2 grad A / A, the variance along n is s^2 + s^4 (n.H.n) / A - (s^2 n.grad A / A)^2; the gradient's mean
        // over that spread along its own axis is the piecewise-linear stops smoothed by a Gaussian.
        half4 blurred(float2 p, float sigma, float sx, float sy, half4 cA, half4 cB, float t0, float t1, float t2, float t3, bool isHalo) {
            float A = cover(p, isHalo);
            if (A <= 0.0005) return half4(0.0);
            float2 gt = gradT(sx, sy); float k = length(gt); float2 n = gt / max(k, 1e-9);
            float h = max(1.0, sigma * 0.25);
            float2 dA = float2(cover(p + float2(h, 0.0), isHalo) - cover(p - float2(h, 0.0), isHalo),
                               cover(p + float2(0.0, h), isHalo) - cover(p - float2(0.0, h), isHalo)) / (2.0 * h);
            float h2 = max(1.0, sigma * 0.5);
            float dnn = (cover(p + n * h2, isHalo) - 2.0 * A + cover(p - n * h2, isHalo)) / (h2 * h2);
            float s2 = sigma * sigma;
            float2 shift = s2 * dA / A;
            float m = length(shift); if (m > 3.0 * sigma) shift *= 3.0 * sigma / m;
            float along = dot(n, shift);
            float v = max(0.0, s2 + s2 * s2 * dnn / A - along * along);
            float t = tOf(p + shift, sx, sy);
            return grad(t, sqrt(v) * k, cA, cB, t0, t1, t2, t3) * half(A);
        }

        half4 main(float2 p) {
            float pill = cov(rrSdf(p, box, g.x));
            // the outer light: the halo, the rim over it, the halftone over both; only outside the composer
            half4 o = half4(0.0);
            if (pill < 1.0) {
                half4 hl = blurred(p, halo.y, halo.z, halo.w, half4(c1), half4(c2), 0.3, 0.5, 0.5, 0.7, true) * half(alphas.x);
                half4 rm = half4(0.0);
                if (alphas.y > 0.0) rm = blurred(p, max(0.3, 2.0 * rim.x), rim.y, rim.z, half4(k1), half4(k2), 0.2, 0.45, 0.55, 0.8, false) * half(alphas.y);
                o = rm + hl * (1.0 - rm.a);
                if (dots.x > 0.0 && o.a > 0.0) {
                    float cell = max(dots.y, 0.5);
                    float2 f = mod(p + dots.z, cell) - cell * 0.5;
                    float r0 = cell * 0.70710678;
                    float w = max(0.3 * r0, 0.7 / g.y);
                    float hole = clamp((length(f) - 0.2 * r0) / w, 0.0, 1.0);
                    float2 e = box * 0.5 + dots.z;
                    float de = length((p - box * 0.5) / e);
                    float fadeE = de <= 0.62 ? 1.0 : max(0.0, 1.0 - (de - 0.62) / 0.38);
                    o *= half(1.0 - dots.x * hole * fadeE);
                }
                if (fade.z > 0.5) o *= half(clamp((fade.x - p.y) / fade.y, 0.0, 1.0));
                o *= half(1.0 - pill);
            }
            // the inside rim: a 1 px frame inside the composer, its clip blurred .6 px
            half4 inside = half4(0.0);
            if (alphas.z > 0.0 && pill > 0.0) {
                float frame = 1.0 - cov(rrSdf(p - 1.0, box - 2.0, max(0.0, g.x - 1.0)));
                if (frame > 0.0) {
                    float clipSoft = 0.5 * (1.0 - erf(rrSdf(p, box, g.x) / (0.6 * 1.41421356)));
                    float t = tOf(p, 4.0 * box.x / 400.0, 4.0 * box.y / 400.0);
                    inside = grad(t, 0.0, half4(n1), half4(n2), 0.3, 0.45, 0.55, 0.7) * half(frame * clipSoft * alphas.z);
                }
            }
            return (inside + o * (1.0 - inside.a)) * half(alphas.w);
        }
        """;

    private static Lazy<SKRuntimeEffect> Compile(string source, string what) => new(() =>
        SKRuntimeEffect.CreateShader(source, out var error) ?? throw new InvalidOperationException("The composer light's " + what + " shader did not compile: " + error));

    private static readonly Lazy<SKRuntimeEffect> beam = Compile(BeamSource, "Border beam");
    private static readonly Lazy<SKRuntimeEffect> aura = Compile(AuraSource, "Uiverse");

    public static SKRuntimeEffect Beam => beam.Value;
    public static SKRuntimeEffect Aura => aura.Value;

    private static readonly Lazy<SKImage> profiles = new(BuildProfiles);

    /// <summary>The six window profiles, one row each, alpha = the profile at the texel's centre (bbProf).</summary>
    public static SKImage Profiles => profiles.Value;

    private static SKImage BuildProfiles()
    {
        var rowsData = new[] { Marrow.Core.Models.ComposerLights.BorderBeamMath.WindowW1, Marrow.Core.Models.ComposerLights.BorderBeamMath.WindowW2,
            Marrow.Core.Models.ComposerLights.BorderBeamMath.HighlightDark, Marrow.Core.Models.ComposerLights.BorderBeamMath.HighlightLight,
            Marrow.Core.Models.ComposerLights.BorderBeamMath.BloomDark, Marrow.Core.Models.ComposerLights.BorderBeamMath.BloomLight };
        var info = new SKImageInfo(ProfileWidth, 8, SKColorType.Rgba8888, SKAlphaType.Premul);
        var pixels = new byte[info.BytesSize];
        for (var row = 0; row < rowsData.Length; row++)
            for (var x = 0; x < ProfileWidth; x++)
            {
                var v = Marrow.Core.Models.ComposerLights.BorderBeamMath.Profile(rowsData[row], (x + 0.5) / ProfileWidth);
                var b = (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
                var o = (row * ProfileWidth + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = pixels[o + 3] = b;
            }
        return SKImage.FromPixelCopy(info, pixels);
    }
}
