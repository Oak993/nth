// PORT-ONLY, owner-ruled addition (OD-0139 Composer Light v2; ED-NEW-lightv2-1): the shader lights' SkSL, translated from the
// approved page's GLSL (E:/Droppy/work/design/composer-light-v2/index.html: PRELUDE, each variant's lightFn, MAIN) with the same
// maths and constants. What SkSL (and the app) needed, and nothing else:
//   - the uniforms are SkSL uniforms; the composer's own coordinates (px, top left at the origin) replace gl_FragCoord's flip;
//   - no struct G: its fields are the light function's parameters (p, q, the rim normal, d, u, aa);
//   - pal() picks its colours with float comparisons and mod() (ES2 rules: no integer bitwise operators);
//   - rimU() and rimN() take the page's capsule to any rounded rectangle (straight sides of 2 (b.y - r)); on a capsule
//     (b.y == r, the page's composer) they are the page's own, term for term;
//   - MAIN keeps only the light: the page's backdrop, UI field, glass refraction and shadow are the app's real window and
//     glass. Outside the composer the outer light passes the window's frost (0.8, the page's light on bare backdrop, x the
//     frost), inside it the inner light under the composer's glass, and the rim on top; then the page's tonemap, its 2.2
//     encode and its dither. The result is drawn with Screen over what is behind (the light adds, never clips);
//   - the band: nothing is shaded past the light's reach (ShaderLightDef.Reach x the spread, where haloP() is zero), and
//     the outer light fades out over the band's last quarter (only the exponential tails are still above zero there).
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

internal static partial class ShaderLightShaders
{
    internal const string Prelude = """
        uniform float2 uC;
        uniform float2 uB;
        uniform float uR;
        uniform float uLoop;
        uniform float uEnergy;
        uniform float uWork;
        uniform float uInt;
        uniform float uSpread;
        uniform float uKey;
        uniform float uCaret;
        uniform float3 uPal[4];
        uniform float3 uAvg;
        uniform float3 uCore;
        uniform float uAA;
        uniform float uReach;
        uniform float uOpacity;
        uniform float4 uPulse;
        uniform float4 uPulseA;
        const float TAU = 6.28318530718;
        const float PI = 3.14159265359;
        float sq(float x) { return x * x; }
        float sdRR(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0.)) + min(max(q.x, q.y), 0.) - r; }
        float h21(float2 p) { p = fract(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return fract(p.x * p.y); }
        float vnoise(float2 p) {
          float2 i = floor(p); float2 f = fract(p); float2 w = f * f * (3. - 2. * f);
          float a = h21(i); float b = h21(i + float2(1., 0.)); float c = h21(i + float2(0., 1.)); float d = h21(i + float2(1., 1.));
          return mix(mix(a, b, w.x), mix(c, d, w.x), w.y);
        }
        float2 circ(float a) { return float2(cos(a), sin(a)); }
        float ring(float u, float R, float k, float seed) {
          return vnoise(circ(TAU * u) * R + circ(TAU * uLoop * k + seed) * 1.6 + float2(seed * 13.1 + 40., seed * 7.3 + 40.));
        }
        float rimU(float2 q) {
          float a = uB.x - uR; float e = max(uB.y - uR, 0.);
          float P = 4. * a + 4. * e + TAU * uR;
          float s;
          if (q.x > a) {
            if (q.y < -e) s = 2. * a + uR * (atan(q.y + e, q.x - a) + PI * .5);
            else if (q.y > e) s = 2. * a + 2. * e + uR * (atan(q.y - e, q.x - a) + PI * .5);
            else s = 2. * a + PI * .5 * uR + (q.y + e);
          } else if (q.x < -a) {
            if (q.y > e) s = 4. * a + 2. * e + PI * uR + uR * (atan(q.y - e, q.x + a) - PI * .5);
            else if (q.y < -e) { float th = atan(q.y + e, q.x + a); if (th < 0.) th += TAU; s = 4. * a + 4. * e + PI * uR + uR * (th - PI * .5); }
            else s = 4. * a + 2. * e + 1.5 * PI * uR + (e - q.y);
          } else if (abs(q.y) <= e && uB.x - abs(q.x) < uB.y - abs(q.y)) {
            s = q.x > 0. ? 2. * a + PI * .5 * uR + (q.y + e) : 4. * a + 2. * e + 1.5 * PI * uR + (e - q.y);
          } else if (q.y < 0.) s = q.x + a;
          else s = 2. * a + 2. * e + PI * uR + (a - q.x);
          return s / P;
        }
        float2 rimN(float2 q) {
          float a = uB.x - uR; float e = max(uB.y - uR, 0.);
          float2 v = q - float2(clamp(q.x, -a, a), clamp(q.y, -e, e));
          float l = length(v);
          if (l > 1e-3) return v / l;
          if (e > 0. && uB.x - abs(q.x) < uB.y - abs(q.y)) return float2(q.x < 0. ? -1. : 1., 0.);
          if (e > 0.) return float2(0., q.y > 0. ? 1. : -1.);
          return float2(0., -1.);
        }
        float3 palAt(float i) { return i < .5 ? uPal[0] : (i < 1.5 ? uPal[1] : (i < 2.5 ? uPal[2] : uPal[3])); }
        float3 pal(float t) {
          t = fract(t) * 4.;
          float fi = floor(t); float f = t - fi; f = f * f * (3. - 2. * f);
          return mix(palAt(fi), palAt(mod(fi + 1., 4.)), f);
        }
        float gainF() { return uInt * (.38 + .82 * uEnergy); }
        float farMix(float d) { return smoothstep(0., uSpread * .9, d) * .8; }
        float keyAt(float2 p) { return uKey * exp(-abs(p.x - uCaret) / 80.); }
        float wrapd(float x) { return abs(fract(x + .5) - .5); }
        float3 haloP(float d, float s, float core, float disp) {
          float3 x = d / (s * (1. + disp * float3(0., .08, .18)));
          float3 f = core * exp(-x * 4.5) + (1. - core) / (1. + x * x * 4.5);
          return f * (1. - smoothstep(1.4, 2.9, x));
        }
        float3 tonemap(float3 c) {
          float m = max(c.r, max(c.g, c.b));
          c += max(m - .9, 0.) * .22;
          float3 x = max(c - .62, 0.);
          return min(c, .62) + .38 * (1. - exp(-x / .38));
        }

        """;

    internal const string Main = """

        uniform shader uBack;   // the window's pixels under the band, as drawn before the light (Text and light)
        uniform float uUseBack; // 1: uBack is bound and the light composes with it itself (drawn with Src); 0: drawn with Screen
        uniform float3 uModes;  // the Text and light modes' weights: text above light, light above text, difference
        uniform float uBehind;  // Light behind text (0..1)
        uniform float uHalo;    // Text halo (0..1)
        uniform float4 uText;   // the chat text block (composer px: left, top, right, bottom)
        uniform float uTextLight; // 1: the text is lighter than what is behind it (dark appearance)
        uniform float uFade;    // where the window ends below (composer px): the outer light fades out over the 13 px above it
        uniform float uGuard;     // 1: the readability guard holds the chat text at 4.6:1 (ED-NEW-lightv2-8); 0: the pages' composition alone
        uniform float uGuardLift; // >= 1: how much brighter the light was in the last 150 ms (the guard lets go over that)
        uniform float3 uGlyph;    // the chat text's colour the guard keeps readable (labelColor over the window) ...
        uniform float3 uGround;   // ... and, with no window to read, the window's

        float lumE(half4 c) { return dot(float3(c.rgb) / max(float(c.a), 1e-4), float3(.2126, .7152, .0722)); }
        float3 unpre(half4 c) { return float3(c.rgb) / max(float(c.a), 1e-4); }
        float3 scr(float3 a, float3 b) { return a + b - a * b; }
        float linC(float v) { return v <= .04045 ? v / 12.92 : pow((v + .055) / 1.055, 2.4); }
        float relL(float3 c) { c = clamp(c, 0., 1.); return .2126 * linC(c.r) + .7152 * linC(c.g) + .0722 * linC(c.b); }

        // a chat glyph (window colour G) and the window beside it (B) under the light p, composed as the modes md do it: text
        // above light (the glyph keeps .9 of the light out; the halo sh over the window), light above text (both screened),
        // difference (the glyph one minus the lit window, lo its luminance); their WCAG luminances, the bright side's first (the
        // glyph's behind light text, lit 1; the window's behind dark text)
        float2 sides(float3 p, float3 G, float3 B, float sh, float lo, float3 md, float3 ink, float lit) {
          p = min(p, float3(1.));
          float3 bo = scr(p, B);
          float3 g = md.x * scr(p * .1, G) + md.y * scr(p, G) + md.z * (1. - (float3(lo) + p - float3(lo) * p));
          float3 b = md.x * mix(bo, ink, sh) + (md.y + md.z) * bo;
          float lg = relL(g); float lb = relL(b);
          return lit > .5 ? float2(lg, lb) : float2(lb, lg);
        }

        // the guard's limits: the bright side held at L.x or above and the dark side at L.y or below, (L.x + .05) = 4.6 (L.y +
        // .05), so any glyph and any window pixel each within them read at 4.6:1 together (4.5 and a margin for the dither),
        // whatever light each of them has; L.z the text's own contrast with no light. Where light only brightens the bright side
        // its floor is its unlit luminance; in Difference behind light text (light darkens the glyphs) the two sides share the
        // contrast to spare evenly
        float3 limits(float3 G, float3 B, float lo, float3 md, float3 ink, float lit) {
          float2 z = sides(float3(0.), G, B, 0., lo, md, ink, lit);
          float c0 = (z.x + .05) / (z.y + .05);
          float f = pow(max(c0 / 4.6, 1.), .5 * md.z * lit);
          return float3((z.x + .05) / f - .05 - 1e-4, (z.x + .05) / (4.6 * f) - .05 + 1e-4, c0);
        }

        bool holds(float3 p, float3 G, float3 B, float sh, float lo, float3 md, float3 ink, float lit, float3 L) {
          float2 v = sides(p, G, B, sh, lo, md, ink, lit);
          return v.x >= L.x && v.y <= L.y;
        }

        // the share of the light (0..1) that keeps this pixel within the limits: the most light before it first leaves them,
        // found by a scan and bisection, so the guard takes only as much as it needs (and, where a blend of the modes is not
        // monotonic, always the first, dim side: neighbouring pixels never take opposite sides). Text the window itself shows
        // below 4.6:1 (L.z), or texture, is not guarded (1).
        float guardK(float3 p, float3 G, float3 B, float sh, float lo, float3 md, float3 ink, float lit, float3 L) {
          if (L.z < 4.6) return 1.;
          if (md.z < .001 && holds(p, G, B, sh, lo, md, ink, lit, L)) return 1.;
          float a = 0.; float z = 1.; float hit = 0.;
          for (int i = 1; i <= 8; i++) {
            float h = float(i) / 8.;
            if (hit < .5) {
              if (!holds(p * h, G, B, sh, lo, md, ink, lit, L)) { z = h; hit = 1.; } else a = h;
            }
          }
          if (hit < .5) return 1.;
          for (int i = 0; i < 8; i++) {
            float h = .5 * (a + z);
            if (holds(p * h, G, B, sh, lo, md, ink, lit, L)) a = h; else z = h;
          }
          return a;
        }

        half4 main(float2 xy) {
          float2 q = xy - uC;
          float d = sdRR(q, uB, uR);
          half4 S = uUseBack > .5 ? uBack.eval(xy) : half4(0.);
          if (d > uReach) return S;
          float aa = uAA;
          float3 outer; float3 inner; float3 rim;
          lightFn(xy, q, rimN(q), d, rimU(q), aa, outer, inner, rim);
          // Light behind text (v4 MAIN): the outer light dims under the chat text block, feathered over 18 px
          float tmask = 1. - smoothstep(0., 18., sdRR(xy - (uText.xy + uText.zw) * .5, (uText.zw - uText.xy) * .5, 8.));
          outer *= 1. - uBehind * tmask * .85;
          // the window's glass: static frost the light passes through (the page's light on bare backdrop: x .8)
          float frost = .88 + .22 * vnoise(xy * .05) + .05 * (h21(floor(xy * 1.5)) - .5);
          outer *= .8 * frost * (1. - smoothstep(uReach * .75, uReach, d)) * clamp((uFade - xy.y) / 13., 0., 1.);
          // the composer's glass: the inner light under it, the rim on top
          float inside = 1. - smoothstep(-aa * .6, aa * .6, d);
          float3 col = mix(outer, inner * (.92 + .16 * vnoise(xy * .09)), inside) + rim;
          // the composer's own text (v4 MAIN): everything more than 7-12 px inside the rim is held under a soft luminance cap
          float tz = 1. - smoothstep(-12., -7., d);
          float lum = dot(col, float3(.2126, .7152, .0722));
          float lc = lum > .018 ? .018 + .008 * (1. - exp(-(lum - .018) / .008)) : lum;
          col *= mix(1., lc / max(lum, 1e-5), tz);
          col = tonemap(max(col, 0.));
          col = pow(col, float3(1. / 2.2));
          float m = max(col.r, max(col.g, col.b));
          if (m > .002) col += (h21(floor(xy / uAA) + .5) - .5) / 255.; // the page dithers per device pixel (sk_FragCoord is not open to runtime effects)
          col = clamp(col, 0., 1.);
          m = max(col.r, max(col.g, col.b));
          float4 P = float4(col, m) * uOpacity;
          // the halo's ink: the v4 page's near-black behind light text; behind dark text (the light appearance) the window's white
          float3 ink = uTextLight > .5 ? float3(8., 8., 10.) / 255. : float3(1.);
          float lit = uTextLight > .5 ? 1. : 0.;
          if (uUseBack < .5) {
            // no window to read (drawn with Screen: the light over the text): the guard holds the app's label colour and the
            // window's within the limits across the chat text block
            if (uGuard > .5 && P.a > .003 && d > 0. && tmask > .001) {
              float3 L = limits(uGlyph, uGround, 0., float3(0., 1., 0.), ink, lit);
              float k = guardK(P.rgb * uGuardLift, uGlyph, uGround, 0., 0., float3(0., 1., 0.), ink, lit, L);
              P *= mix(1., k, tmask);
            }
            return half4(P);
          }
          // Text and light (v4's modes): the chat text's glyphs (c) and the soft dark halo behind them, read from the window's
          // pixels around this one: the brightest and the darkest of 16 taps, 2.5 and 8 px out (and their colours, for the
          // readability guard), and the near and far taps' means
          float4 s = float4(S);
          float c = 0.; float near = 0.; float far = 0.; float lo = 0.; float present = 0.; float mn = 0.; float mx = 0.;
          float3 cmn = float3(0.); float3 cmx = float3(0.);
          if (P.a > .003 && d > 0.) {
            float yc = lumE(S);
            mn = yc; mx = yc; float sn = 0.; float sf = 0.;
            cmn = unpre(S); cmx = cmn;
            for (int i = 0; i < 8; i++) {
              float2 o = circ(float(i) * .7853982 + .3927);
              half4 ta = uBack.eval(xy + o * 2.5);
              half4 tb = uBack.eval(xy + o * 8.);
              float a = lumE(ta);
              float b = lumE(tb);
              if (a < mn) { mn = a; cmn = unpre(ta); }
              if (a > mx) { mx = a; cmx = unpre(ta); }
              if (b < mn) { mn = b; cmn = unpre(tb); }
              if (b > mx) { mx = b; cmx = unpre(tb); }
              sn += a; sf += b;
            }
            float range = mx - mn;
            present = smoothstep(.08, .22, range);
            float r = max(range, 1e-3);
            c = present * smoothstep(.3, .7, mix((mx - yc) / r, (yc - mn) / r, lit));
            near = present * clamp(mix((mx - sn / 8.) / r, (sn / 8. - mn) / r, lit), 0., 1.);
            far = present * clamp(mix((mx - sf / 8.) / r, (sf / 8. - mn) / r, lit), 0., 1.);
            lo = mix(mx, mn, lit);
          }
          // text above light: the text on top of the light (its glyphs at the page's .9), the halo's three shadows (3, 10 and 22 px:
          // .95, .7 and .5 of the halo) only where the light is bright (updateHalo's estimate at this line's height)
          float g = uInt * (.38 + .82 * uEnergy) * (1. + uKey * .3);
          float est = min(1., g * 1.25 * exp(min(xy.y, 0.) / (uSpread * .6)) * (1. - uBehind * .85) + .15 * g);
          float ha = uHalo * est;
          float sh = 1. - (1. - .95 * ha * near) * (1. - .7 * ha * far) * (1. - .5 * ha * far * .8);
          float shadow = sh * (1. - c);
          // the readability guard (ED-NEW-lightv2-8): near the chat text the light is dimmed by as much as keeps the app's chat
          // text colour and the window within the limits (4.6:1 between any glyph and window pixel), in the mode shown (nothing
          // where it already is). Every pixel within 2 device pixels of a glyph (the 5 x 5 pixels around it) is held in full, and
          // softly out to about 4 px (the near taps and 8 more 3.5 px out); text is told from texture by its own contrast (4.6 to
          // 6:1 on the window). Where this pixel is the window and the light already leaves it within the limits, the guard has
          // nothing to do and reads nothing (the window beside it, darker behind light text and brighter behind dark, and the
          // halo only add contrast)
          if (uGuard > .5 && P.a > .003 && d > 0.) {
            float3 pg = P.rgb * uGuardLift;
            float3 own = unpre(S);
            if (uModes.z > .001 || !holds(pg, uGlyph, own, 0., lo, uModes, ink, lit, limits(uGlyph, own, lo, uModes, ink, lit))) {
              float gmn = mn; float gmx = mx; float hi = 0.; float lw = 1.;
              float3 wmn = cmn; float3 wmx = cmx;
              for (int j = 0; j < 5; j++) {
                for (int i = 0; i < 5; i++) {
                  half4 t = uBack.eval(xy + float2(float(i) - 2., float(j) - 2.) * uAA);
                  float a = lumE(t);
                  hi = max(hi, a); lw = min(lw, a);
                  if (a < gmn) { gmn = a; wmn = unpre(t); }
                  if (a > gmx) { gmx = a; wmx = unpre(t); }
                }
              }
              float sz = 0.;
              for (int i = 0; i < 8; i++) sz += lumE(uBack.eval(xy + circ(float(i) * .7853982 + .3927) * 3.5));
              float r = max(gmx - gmn, 1e-3);
              float zone = clamp(mix((gmx - sz / 8.) / r, (sz / 8. - gmn) / r, lit), 0., 1.);
              float held = smoothstep(.3, .7, clamp(mix((gmx - lw) / r, (hi - gmn) / r, lit), 0., 1.));
              float w = smoothstep(.08, .22, gmx - gmn) * max(held, smoothstep(.02, .12, max(zone, near)));
              if (w > 0.) {
                float3 B = mix(wmx, wmn, lit);
                float3 L = limits(uGlyph, B, lo, uModes, ink, lit);
                float k = guardK(pg, uGlyph, B, sh, lo, uModes, ink, lit, L);
                P *= mix(1., k, w * smoothstep(4.6, 6., L.z));
              }
            }
          }
          float4 Pa = P * (1. - .9 * c);
          float4 above = Pa + s - Pa * s;
          above.rgb = mix(above.rgb, ink * above.a, shadow);
          // light above text: the light washes over the text (the page's screen-blended text)
          float4 over = P + s - P * s;
          // difference: the glyphs invert against the light (the page's white text, difference-blended): 1 - the lit window
          float3 bgLit = float3(lo) + P.rgb - float3(lo) * P.rgb;
          float4 diff = float4(mix(over.rgb, (1. - bgLit) * over.a, c), over.a);
          return half4(uModes.x * above + uModes.y * over + uModes.z * diff);
        }
        """;

    /// <summary>Each light's light function, by the catalogue's id (the page's lightFn, translated).</summary>
    internal static IReadOnlyDictionary<string, string> Functions => functions ??= Merged();

    private static IReadOnlyDictionary<string, string>? functions;

    // v2's, v3's and v4's (the partial files ShaderLightShaders.V3.cs and .V4.cs)
    private static Dictionary<string, string> Merged()
    {
        var all = new Dictionary<string, string>(V2Functions);
        foreach (var (id, source) in V3Functions) all.Add(id, source);
        foreach (var (id, source) in V4Functions) all.Add(id, source);
        return all;
    }

    /// <summary>The v2 lights' light functions (the page's lightFn, translated).</summary>
    private static IReadOnlyDictionary<string, string> V2Functions { get; } = new Dictionary<string, string>
    {
        // A Lumen, interlayer bloom
        ["lumen"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float n1 = ring(gu, 2.2, 1., 0.);
              float n2 = ring(gu - t * 2., 5., -1., 3.7);
              float3 c = pal(gu + t + .18 * sin(TAU * (gu * 2. - t * 2.)) + .2 * n1);
              float bri = max(.55 + .45 * n2 + .25 * uEnergy * sin(TAU * (gu * 3. - t * 5.)), 0.) + keyAt(gp) * 1.2;
              float d = max(gd, 0.);
              float3 cb = mix(c, uAvg, farMix(d));
              float bo = mix(bri, .8, smoothstep(0., uSpread, d) * .8);
              float3 h = haloP(d, uSpread, .42, 1.);
              float plate = .035 * exp(-d / (uSpread * 1.2));
              outer = cb * (h * .7 * bo + plate) * gn;
              float3 off = float3(-.9, 0., .9) * (1. + .6 * uEnergy);
              float3 rl = exp(-abs(float3(gd) - off) / (.75 + gaa * .5));
              rim = (c * rl * 1.25 * bri + uCore * exp(-abs(gd) / .6) * .18 * bri) * gn;
              float di = min(gd, 0.);
              inner = (c * exp(di / 9.) * .45 * bri + uAvg * exp(di / 22.) * .07) * gn;
            }
            """,
        // B Fluxa, edge flow
        ["fluxa"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float flow = ring(gu - t * 3., 3., 2., 1.3);
              float wav = ring(gu + t * 2., 6., -3., 5.1);
              float3 c = pal(gu * 2. - t * 3. + .35 * flow);
              float3 c2 = pal(gu * 2. - t * 3. + .5 + .35 * wav);
              float k = keyAt(gp);
              float th = (4. + 6. * uEnergy) * (.55 + .9 * wav) + 6. * k;
              float x = (gd + th * .25) / th;
              float band = exp(-x * x * 1.6);
              float bri = .7 + .5 * flow + k;
              float3 bc = mix(c, c2, smoothstep(-.6, .6, x));
              rim = (bc * band * 1.1 * bri + uCore * band * band * band * band * .35 * bri) * gn;
              float d = max(gd, 0.);
              float3 cb = mix(c, uAvg, farMix(d));
              float bo = mix(bri, .95, smoothstep(0., uSpread, d) * .8);
              outer = cb * haloP(d, uSpread, .55, 1.) * .75 * bo * gn;
              float di = min(gd, 0.);
              float refl = exp(-sq((di + 8. + 2. * wav) / 3.)) * .16;
              inner = (c2 * refl + c * exp(di / 6.) * .3) * bri * gn;
            }
            """,
        // C Lacus, light leak
        ["lacus"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float xs = gq.x / (2. * uB.x);
              float3 c = pal(xs * .8 + t + .12 * sin(TAU * (xs + t * 2.)));
              float3 warm = mix(c, uCore, .35);
              float down = smoothstep(-.7, .95, nrm.y);
              float b = .88 + .12 * sin(TAU * t * 2.) + .15 * uEnergy * sin(TAU * (xs * 1.5 - t * 4.)) + keyAt(gp) * .9;
              float d = max(gd, 0.);
              float s = uSpread * mix(.55, 1.35, down);
              float3 h = haloP(d, s, .35, 1.) * mix(.3, 1., down);
              outer = mix(warm, mix(uAvg, uCore, .3), farMix(d)) * h * .8 * b * gn;
              float yF = uC.y + uB.y + 9.;
              float below = gp.y - yF;
              if (below > 0.) {
                float2 pr = float2(gp.x + sin(gp.y * .55 + TAU * t * 3.) * .7 * min(below / 20., 1.), 2. * yF - gp.y);
                float dr = sdRR(pr - uC, uB, uR);
                float bl = 1.5 + below * .35;
                float refl = exp(-abs(dr) / bl) * .32 + (1. - smoothstep(-bl, bl, dr)) * .06;
                outer += warm * refl * exp(-below / 22.) * b * gn;
                float pool = exp(-sq(max(abs(gq.x) - uB.x * .7, 0.) / (uSpread * .9)) - below / (uSpread * .55));
                outer += mix(warm, uAvg, .3) * pool * .18 * b * gn;
              }
              float line = exp(-abs(gp.y - yF) / .7) * exp(-sq(max(abs(gq.x) - uB.x * .6, 0.) / uSpread));
              outer += uCore * line * .08 * b * gn * step(0., gd);
              float di = min(gd, 0.);
              inner = (warm * exp(di / 8.) * mix(.15, .6, down) + uCore * exp(di / 3.) * down * .25) * b * gn;
              rim = (warm * exp(-abs(gd) / (.8 + gaa * .5)) * mix(.35, 1.1, down) + uCore * exp(-abs(gd) / .5) * down * .2) * b * gn;
            }
            """,
        // D Caustica, caustic glass
        ["caustica"] = """
            float caustic(float2 uv, float t) {
              float2 p = uv * TAU - 250.;
              float2 i = p;
              float c = 1.;
              for (int n = 0; n < 4; n++) {
                float k = n == 0 ? 3. : (n == 1 ? -2. : (n == 2 ? 1. : -1.));
                float tt = TAU * t * k;
                i = p + float2(cos(tt - i.x) + sin(tt + i.y), sin(tt - i.y) + cos(tt + i.x));
                c += 1. / length(float2(p.x * .005 / (abs(sin(i.x + tt)) + 1e-3), p.y * .005 / (abs(cos(i.y + tt)) + 1e-3)));
              }
              c /= 4.;
              c = 1.17 - pow(c, 1.4);
              return min(pow(abs(c), 8.), 2.);
            }
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float3 c = pal(gu + t);
              float3 c2 = pal(gu + t + .33);
              float d = max(gd, 0.);
              float3 h = haloP(d, uSpread, .3, 1.);
              float m = max(h.b, exp(min(gd, 0.) / 10.));
              float k1 = 0.; float k2 = 0.;
              if (m > .015) {
                float2 uv = gp / 400. + circ(TAU * t) * .12;
                k1 = caustic(uv, t);
                k2 = caustic(uv * 1.015 + .004, t);
              }
              float3 kc = float3(k1, (k1 + k2) * .5, k2);
              float bri = .8 + .25 * uEnergy + keyAt(gp);
              float3 cb = mix(mix(c, c2, smoothstep(0., uSpread, d)), uAvg, farMix(d) * .6);
              float3 hk = h * exp(-d / (uSpread * .55));
              outer = (cb * h * .3 + cb * kc * hk * (1.2 + .6 * uEnergy) + uCore * kc * hk * .3) * bri * gn;
              float di = min(gd, 0.);
              inner = (c * exp(di / 7.) * .35 + cb * kc * exp(di / 5.) * .25) * bri * gn;
              rim = c * exp(-abs(gd) / (.8 + gaa * .5)) * (.7 + .5 * kc.g) * bri * gn;
            }
            """,
        // E Borea, aurora rim
        ["borea"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float br = .5 + .5 * sin(TAU * t * 2. - PI * .5);
              float d = max(gd, 0.);
              float rays = ring(gu + t, 13., 2., 0.) * .65 + ring(gu - t, 4., -1., 2.) * .55;
              rays = rays * rays * (1. + .6 * uEnergy) + keyAt(gp) * .8;
              float w1 = 3. + 4. * br + 3. * uEnergy;
              float tail = exp(-d / (uSpread * (.22 + .12 * br + .1 * uEnergy)));
              float3 ca = mix(pal(gu + t), pal(gu + t + .3), smoothstep(0., uSpread * .5, d));
              float2 off = float2(sin(TAU * t) * 7., 5. + 2. * cos(TAU * t));
              float2 q2 = gq - off;
              float d2 = sdRR(q2, uB + 4., uR + 4.);
              float u2 = rimU(q2);
              float rays2 = ring(u2 - t, 7., 1., 4.);
              float e2 = exp(-max(d2, 0.) / (uSpread * (.45 + .2 * br))) * (rays2 * .8 + .2) * smoothstep(-14., 4., d2);
              float3 cb2 = mix(pal(u2 - t + .5), uAvg, farMix(max(d2, 0.)));
              float lift = .75 + .35 * br;
              outer = (ca * tail * rays * .5 + cb2 * e2 * .3) * gn * lift;
              rim = ca * exp(-sq(gd / w1)) * rays * .8 * gn * lift;
              float di = min(gd, 0.);
              inner = (ca * exp(di / 6.) * rays * .3 + cb2 * e2 * .12) * gn * lift;
            }
            """,
        // F Quies, calm accent (the pack's first colour)
        ["quies"] = """
            void lightFn(float2 gp, float2 gq, float2 nrm, float gd, float gu, float gaa, out float3 outer, out float3 inner, out float3 rim) {
              float t = uLoop;
              float gn = gainF();
              float3 acc = uPal[0];
              float br = .86 + .14 * sin(TAU * t * 2.);
              float lobe = exp(-sq(wrapd(gu - t) / .07)) + uWork * .8 * exp(-sq(wrapd(gu - t + .5) / .07));
              float k = keyAt(gp);
              float d = max(gd, 0.);
              outer = acc * haloP(d, uSpread, .5, 0.) * (.45 + .25 * lobe) * br * (1. + k) * gn;
              rim = (uCore * exp(-abs(gd) / (.65 + gaa * .5)) * (.35 + .9 * lobe + k) + acc * exp(-abs(gd) / 2.5) * .25) * br * gn;
              float di = min(gd, 0.);
              inner = (acc * exp(di / 8.) * .22 + uCore * exp(di / 3.) * lobe * .12) * br * gn;
            }
            """,
    };

    /// <summary>A light's whole program: the prelude, its light function, MAIN.</summary>
    internal static string Source(string id) => Prelude + Functions[id] + Main;

    private static readonly Dictionary<string, SKRuntimeEffect> effects = [];

    /// <summary>The compiled effect of light <paramref name="id"/> (made once a process; the CPU half of a warm-up).</summary>
    internal static SKRuntimeEffect Effect(string id)
    {
        lock (effects)
        {
            if (effects.TryGetValue(id, out var effect)) return effect;
            effect = SKRuntimeEffect.CreateShader(Source(id), out var errors)
                ?? throw new InvalidOperationException("Composer light " + id + ": its SkSL did not compile: " + errors);
            effects[id] = effect;
            return effect;
        }
    }

    /// <summary>Whether light <paramref name="id"/> has a shader.</summary>
    internal static bool Has(string id) => Functions.ContainsKey(id);
}
