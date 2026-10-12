// PORT-ONLY, owner-ruled addition (OD-0150 Composer Light v2; ED-NEW-lightv2-1): the shader lights' SkSL checked against a CPU port of the approved page's GLSL (E:/Droppy/work/design/composer-light-v2/index.html) at three points per light.
// The CPU port (ShaderLightPageMaths, below) is the SkSL's maths term for term (ShaderLightShaders: Prelude, the V2Functions and Main
// on its Screen path, uUseBack 0), and the SkSL's maths is the page's PRELUDE, lightFn and MAIN, read side by side: rimU() and rimN()
// are the SkSL's rounded-rectangle forms, the page's own on the capsule drawn here (uB.y == uR). Main's dither
// ((h21(floor(xy / uAA) + .5) - .5) / 255) is left out: the tolerance (3/255 a channel) takes it, the 8-bit store and the shader's float32
// maths. The light is drawn as the app draws it (ShaderLightRenderer.Draw, Screen over transparent pixels: the shader's own
// premultiplied output), at a fixed phase, and read back at three pixels a light: outside the composer, on its rim and inside it.
using Marrow.Core.Models.ComposerLights;
using Marrow.UI.Port.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Tests.Port.ComposerLights;

public sealed class ShaderLightReferenceTests
{
    // the composer: a capsule (its radius half its height), as on the page
    private const int ComposerWidth = 400, ComposerHeight = 46, ComposerRadius = 23;

    // of 255, a channel: the dither (+-.5), the 8-bit store's rounding and the shader's float32 maths
    private const double Tolerance = 3;

    public static TheoryData<string> Lights => new() { "lumen", "fluxa", "lacus", "caustica", "borea", "quies" };

    // Three pixels a light (composer px, each pixel's top left; the shader is sampled at its centre, + .5): outside the composer, on
    // its rim (half a pixel out), and inside it. Above the composer (22.5 px out) the point is in the feather of the chat text's block
    // (uText's bottom is 44 px over the top, tmask about .84 there), so Light behind text's dimming is in the sum; inside, 9.5 px in,
    // the composer's text zone holds the light at half its luminance cap (tz .5). Lacus's outside point is below the composer instead,
    // where its light leaks (the floor reflection and the pool: the page's below > 0 branch), and its rim point is on the underside
    // (down 1). Every point's light is well above 8/255 in some channel (64/255 the least; the test asserts it).
    private static readonly Dictionary<string, (string Where, int X, int Y)[]> Samples = new()
    {
        ["lumen"] = [("above", 140, -40), ("rim", 280, -1), ("inside", 200, 36)],
        ["fluxa"] = [("above", 140, -40), ("rim", 280, -1), ("inside", 200, 36)],
        ["lacus"] = [("below", 140, 66), ("rim", 280, 46), ("inside", 200, 36)],
        ["caustica"] = [("above", 140, -40), ("rim", 280, -1), ("inside", 200, 36)],
        ["borea"] = [("above", 140, -40), ("rim", 280, -1), ("inside", 200, 36)],
        ["quies"] = [("above", 140, -40), ("rim", 280, -1), ("inside", 200, 36)],
    };

    [Theory]
    [Trait("Decision", "OD-0150")] // ED-NEW-lightv2-1
    [MemberData(nameof(Lights))]
    public void The_shader_draws_the_pages_maths(string id)
    {
        var frame = Frame(id);
        var page = Page(frame, scale: 1, opacity: 1);
        // the composer's top left at (reach, reach), the reach rounded up to a whole pixel: a pixel's centre is a composer point + .5
        var at = (int)Math.Ceiling(frame.Reach);
        var info = new SKImageInfo(ComposerWidth + 2 * at, ComposerHeight + 2 * at, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        using var renderer = new ShaderLightRenderer { Guard = false }; // the pages' maths alone (the readability guard is ED-NEW-lightv2-8's, tested in ShaderLightRenderTests)
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(at, at);
        foreach (var (_, x, y) in Samples[id])
        {
            // the band's draw, clipped to the sample's pixel (the same shader at that pixel, without shading the whole band)
            canvas.Save();
            canvas.ClipRect(new SKRect(x, y, x + 1, y + 1));
            Assert.True(renderer.Draw(canvas, frame, scale: 1, opacity: 1), id + ": nothing was drawn");
            canvas.Restore();
        }
        using var pixmap = surface.PeekPixels();
        var bytes = pixmap.GetPixelSpan();
        var failures = new List<string>();
        foreach (var (where, x, y) in Samples[id])
        {
            var i = (y + at) * pixmap.RowBytes + (x + at) * 4;
            double r = bytes[i], g = bytes[i + 1], b = bytes[i + 2], a = bytes[i + 3];
            var e = page.Main(x + .5, y + .5);
            double er = e.R * 255, eg = e.G * 255, eb = e.B * 255, em = Math.Max(er, Math.Max(eg, eb));
            var point = FormattableString.Invariant($"{id}, {where} ({x + .5}, {y + .5})");
            var values = FormattableString.Invariant($"drawn rgba ({r}, {g}, {b}, {a}), the page's maths ({er:F1}, {eg:F1}, {eb:F1}, alpha {e.A * 255:F1})");
            if (em <= 8) failures.Add(point + ": the page's light is under 8/255 there, so the point proves nothing; " + values);
            if (Math.Abs(r - er) > Tolerance || Math.Abs(g - eg) > Tolerance || Math.Abs(b - eb) > Tolerance)
                failures.Add(point + ": the colour differs by more than 3/255; " + values);
            if (Math.Abs(a - em) > Tolerance) failures.Add(point + ": the alpha is not the largest channel (within 3/255); " + values);
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    // A frame at a fixed phase (Typing's energy, some work, a keystroke's lift near the caret), the light's own spread, intensity 1, and
    // its default pack's colours (Accent: the page's own accent pack), as ShaderLightAnimator.Produce fills one.
    private static ShaderLightFrame Frame(string id)
    {
        var def = ShaderLightCatalog.Find(id) ?? throw new ArgumentException("no shader light " + id, nameof(id));
        var f = new ShaderLightFrame(def)
        {
            Alpha = 1, Loop = .3, Energy = .55, Work = .25, Key = .4, Caret = 120,
            Spread = def.Spread, Intensity = 1, W = ComposerWidth, H = ComposerHeight, Radius = ComposerRadius,
            Behind = .35, Halo = 0, Dark = true,
        };
        var hex = def.Pack == ShaderLightCatalog.AccentPack ? ShaderLightCatalog.PageAccent : ShaderLightCatalog.Pack(def.Pack)!.Colours!;
        double ar = 0, ag = 0, ab = 0;
        for (var i = 0; i < 4; i++)
        {
            var (r, g, b) = ShaderLightCatalog.Linear(hex[i]);
            f.Palette[i * 3] = (float)r; f.Palette[i * 3 + 1] = (float)g; f.Palette[i * 3 + 2] = (float)b;
            ar += r; ag += g; ab += b;
        }
        f.Average[0] = (float)(ar / 4); f.Average[1] = (float)(ag / 4); f.Average[2] = (float)(ab / 4);
        f.Modes[0] = 1; f.Modes[1] = 0; f.Modes[2] = 0;
        return f;
    }

    // The CPU port with the uniforms ShaderLightRenderer.Bind gives the shader for a frame (the Screen path, no fadeBottom: uFade 1e6).
    private static ShaderLightPageMaths Page(ShaderLightFrame f, float scale, float opacity)
    {
        var core = ShaderLightCatalog.Linear(ShaderLightCatalog.Core);
        float w = (float)f.W, h = (float)f.H;
        return new ShaderLightPageMaths(
            light: f.Def.Id, w: w, h: h, radius: (float)f.Radius, loop: (float)f.Loop, energy: (float)f.Energy, work: (float)f.Work,
            intensity: (float)f.Intensity, spread: (float)f.Spread, key: (float)f.Key, caret: (float)f.Caret,
            palette: f.Palette, average: f.Average, core: ((float)core.R, (float)core.G, (float)core.B),
            aa: 1 / Math.Max(1, scale), reach: (float)f.Reach, opacity: (float)Math.Clamp(f.Alpha * opacity, 0, 1), behind: (float)f.Behind,
            text: (ShaderLightRenderer.TextInset, -1e5f, w - ShaderLightRenderer.TextInset, -ShaderLightRenderer.TextGap), fade: 1e6f);
    }
}

/// <summary>
/// The shader lights' SkSL maths on the CPU (ShaderLightShaders: Prelude, the v2 lightFns, Main's Screen path), one method a helper,
/// term for term, in double precision but for h21(): the hash runs in float32 as the shader runs it, because it multiplies its first
/// product's rounding some four thousand times (a double hash is another hash). The uniforms keep the SkSL's names.
/// </summary>
internal readonly struct ShaderLightPageMaths
{
    private const double TAU = 6.28318530718;
    private const double PI = 3.14159265359;

    private readonly string light;
    private readonly V2 uC, uB;
    private readonly double uR, uLoop, uEnergy, uWork, uInt, uSpread, uKey, uCaret, uAA, uReach, uOpacity, uBehind, uFade;
    private readonly V3[] uPal;
    private readonly V3 uAvg, uCore;
    private readonly V2 uTextXY, uTextZW; // uText: the chat text block's left top and right bottom

    public ShaderLightPageMaths(string light, double w, double h, double radius, double loop, double energy, double work, double intensity,
        double spread, double key, double caret, IReadOnlyList<float> palette, IReadOnlyList<float> average, (double R, double G, double B) core,
        double aa, double reach, double opacity, double behind, (double Left, double Top, double Right, double Bottom) text, double fade)
    {
        this.light = light;
        uC = new V2(w / 2, h / 2);
        uB = new V2(w / 2, h / 2);
        uR = radius;
        uLoop = loop; uEnergy = energy; uWork = work; uInt = intensity; uSpread = spread; uKey = key; uCaret = caret;
        uPal = [new V3(palette[0], palette[1], palette[2]), new V3(palette[3], palette[4], palette[5]),
            new V3(palette[6], palette[7], palette[8]), new V3(palette[9], palette[10], palette[11])];
        uAvg = new V3(average[0], average[1], average[2]);
        uCore = new V3(core.R, core.G, core.B);
        uAA = aa; uReach = reach; uOpacity = opacity; uBehind = behind; uFade = fade;
        uTextXY = new V2(text.Left, text.Top);
        uTextZW = new V2(text.Right, text.Bottom);
    }

    // ---- Main, the Screen path (uUseBack 0): returns P = float4(col, m) * uOpacity, without the dither ----

    public (double R, double G, double B, double A) Main(double x, double y)
    {
        var xy = new V2(x, y);
        var q = xy - uC;
        var d = SdRR(q, uB, uR);
        if (d > uReach) return (0, 0, 0, 0);
        var aa = uAA;
        LightFn(xy, q, RimN(q), d, RimU(q), aa, out var outer, out var inner, out var rim);
        var tmask = 1 - Smoothstep(0, 18, SdRR(xy - (uTextXY + uTextZW) * .5, (uTextZW - uTextXY) * .5, 8));
        outer *= 1 - uBehind * tmask * .85;
        var frost = .88 + .22 * VNoise(xy * .05) + .05 * (H21(Floor(xy * 1.5)) - .5);
        outer *= .8 * frost * (1 - Smoothstep(uReach * .75, uReach, d)) * Math.Clamp((uFade - xy.Y) / 13, 0, 1);
        var inside = 1 - Smoothstep(-aa * .6, aa * .6, d);
        var col = Mix(outer, inner * (.92 + .16 * VNoise(xy * .09)), inside) + rim;
        var tz = 1 - Smoothstep(-12, -7, d);
        var lum = Dot(col, new V3(.2126, .7152, .0722));
        var lc = lum > .018 ? .018 + .008 * (1 - Math.Exp(-(lum - .018) / .008)) : lum;
        col *= Mix(1, lc / Math.Max(lum, 1e-5), tz);
        col = Tonemap(Max(col, 0));
        col = Pow(col, 1 / 2.2);
        col = Clamp(col, 0, 1);
        var m = Math.Max(col.X, Math.Max(col.Y, col.Z));
        return (col.X * uOpacity, col.Y * uOpacity, col.Z * uOpacity, m * uOpacity);
    }

    private void LightFn(V2 gp, V2 gq, V2 nrm, double gd, double gu, double gaa, out V3 outer, out V3 inner, out V3 rim)
    {
        switch (light)
        {
            case "lumen": Lumen(gp, gd, gu, gaa, out outer, out inner, out rim); break;
            case "fluxa": Fluxa(gp, gd, gu, out outer, out inner, out rim); break;
            case "lacus": Lacus(gp, gq, nrm, gd, gaa, out outer, out inner, out rim); break;
            case "caustica": Caustica(gp, gd, gu, gaa, out outer, out inner, out rim); break;
            case "borea": Borea(gp, gq, gd, gu, out outer, out inner, out rim); break;
            case "quies": Quies(gp, gd, gu, gaa, out outer, out inner, out rim); break;
            default: throw new InvalidOperationException("not a v2 light: " + light);
        }
    }

    // ---- the v2 lights' lightFn (each takes only the parameters it reads) ----

    // A Lumen, interlayer bloom
    private void Lumen(V2 gp, double gd, double gu, double gaa, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var n1 = Ring(gu, 2.2, 1, 0);
        var n2 = Ring(gu - t * 2, 5, -1, 3.7);
        var c = Pal(gu + t + .18 * Math.Sin(TAU * (gu * 2 - t * 2)) + .2 * n1);
        var bri = Math.Max(.55 + .45 * n2 + .25 * uEnergy * Math.Sin(TAU * (gu * 3 - t * 5)), 0) + KeyAt(gp) * 1.2;
        var d = Math.Max(gd, 0);
        var cb = Mix(c, uAvg, FarMix(d));
        var bo = Mix(bri, .8, Smoothstep(0, uSpread, d) * .8);
        var h = HaloP(d, uSpread, .42, 1);
        var plate = .035 * Math.Exp(-d / (uSpread * 1.2));
        outer = cb * (h * .7 * bo + plate) * gn;
        var off = new V3(-.9, 0, .9) * (1 + .6 * uEnergy);
        var rl = Exp(-Abs(new V3(gd) - off) / (.75 + gaa * .5));
        rim = (c * rl * 1.25 * bri + uCore * Math.Exp(-Math.Abs(gd) / .6) * .18 * bri) * gn;
        var di = Math.Min(gd, 0);
        inner = (c * Math.Exp(di / 9) * .45 * bri + uAvg * Math.Exp(di / 22) * .07) * gn;
    }

    // B Fluxa, edge flow
    private void Fluxa(V2 gp, double gd, double gu, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var flow = Ring(gu - t * 3, 3, 2, 1.3);
        var wav = Ring(gu + t * 2, 6, -3, 5.1);
        var c = Pal(gu * 2 - t * 3 + .35 * flow);
        var c2 = Pal(gu * 2 - t * 3 + .5 + .35 * wav);
        var k = KeyAt(gp);
        var th = (4 + 6 * uEnergy) * (.55 + .9 * wav) + 6 * k;
        var x = (gd + th * .25) / th;
        var band = Math.Exp(-x * x * 1.6);
        var bri = .7 + .5 * flow + k;
        var bc = Mix(c, c2, Smoothstep(-.6, .6, x));
        rim = (bc * band * 1.1 * bri + uCore * band * band * band * band * .35 * bri) * gn;
        var d = Math.Max(gd, 0);
        var cb = Mix(c, uAvg, FarMix(d));
        var bo = Mix(bri, .95, Smoothstep(0, uSpread, d) * .8);
        outer = cb * HaloP(d, uSpread, .55, 1) * .75 * bo * gn;
        var di = Math.Min(gd, 0);
        var refl = Math.Exp(-Sq((di + 8 + 2 * wav) / 3)) * .16;
        inner = (c2 * refl + c * Math.Exp(di / 6) * .3) * bri * gn;
    }

    // C Lacus, light leak
    private void Lacus(V2 gp, V2 gq, V2 nrm, double gd, double gaa, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var xs = gq.X / (2 * uB.X);
        var c = Pal(xs * .8 + t + .12 * Math.Sin(TAU * (xs + t * 2)));
        var warm = Mix(c, uCore, .35);
        var down = Smoothstep(-.7, .95, nrm.Y);
        var b = .88 + .12 * Math.Sin(TAU * t * 2) + .15 * uEnergy * Math.Sin(TAU * (xs * 1.5 - t * 4)) + KeyAt(gp) * .9;
        var d = Math.Max(gd, 0);
        var s = uSpread * Mix(.55, 1.35, down);
        var h = HaloP(d, s, .35, 1) * Mix(.3, 1, down);
        outer = Mix(warm, Mix(uAvg, uCore, .3), FarMix(d)) * h * .8 * b * gn;
        var yF = uC.Y + uB.Y + 9;
        var below = gp.Y - yF;
        if (below > 0)
        {
            var pr = new V2(gp.X + Math.Sin(gp.Y * .55 + TAU * t * 3) * .7 * Math.Min(below / 20, 1), 2 * yF - gp.Y);
            var dr = SdRR(pr - uC, uB, uR);
            var bl = 1.5 + below * .35;
            var refl = Math.Exp(-Math.Abs(dr) / bl) * .32 + (1 - Smoothstep(-bl, bl, dr)) * .06;
            outer += warm * refl * Math.Exp(-below / 22) * b * gn;
            var pool = Math.Exp(-Sq(Math.Max(Math.Abs(gq.X) - uB.X * .7, 0) / (uSpread * .9)) - below / (uSpread * .55));
            outer += Mix(warm, uAvg, .3) * pool * .18 * b * gn;
        }
        var line = Math.Exp(-Math.Abs(gp.Y - yF) / .7) * Math.Exp(-Sq(Math.Max(Math.Abs(gq.X) - uB.X * .6, 0) / uSpread));
        outer += uCore * line * .08 * b * gn * Step(0, gd);
        var di = Math.Min(gd, 0);
        inner = (warm * Math.Exp(di / 8) * Mix(.15, .6, down) + uCore * Math.Exp(di / 3) * down * .25) * b * gn;
        rim = (warm * Math.Exp(-Math.Abs(gd) / (.8 + gaa * .5)) * Mix(.35, 1.1, down) + uCore * Math.Exp(-Math.Abs(gd) / .5) * down * .2) * b * gn;
    }

    // D Caustica, caustic glass
    private static double Caustic(V2 uv, double t)
    {
        var p = uv * TAU - 250;
        var i = p;
        double c = 1;
        for (var n = 0; n < 4; n++)
        {
            double k = n == 0 ? 3 : (n == 1 ? -2 : (n == 2 ? 1 : -1));
            var tt = TAU * t * k;
            i = p + new V2(Math.Cos(tt - i.X) + Math.Sin(tt + i.Y), Math.Sin(tt - i.Y) + Math.Cos(tt + i.X));
            c += 1 / new V2(p.X * .005 / (Math.Abs(Math.Sin(i.X + tt)) + 1e-3), p.Y * .005 / (Math.Abs(Math.Cos(i.Y + tt)) + 1e-3)).Length;
        }
        c /= 4;
        c = 1.17 - Math.Pow(c, 1.4);
        return Math.Min(Math.Pow(Math.Abs(c), 8), 2);
    }

    private void Caustica(V2 gp, double gd, double gu, double gaa, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var c = Pal(gu + t);
        var c2 = Pal(gu + t + .33);
        var d = Math.Max(gd, 0);
        var h = HaloP(d, uSpread, .3, 1);
        var m = Math.Max(h.Z, Math.Exp(Math.Min(gd, 0) / 10));
        double k1 = 0, k2 = 0;
        if (m > .015)
        {
            var uv = gp / 400 + Circ(TAU * t) * .12;
            k1 = Caustic(uv, t);
            k2 = Caustic(uv * 1.015 + .004, t);
        }
        var kc = new V3(k1, (k1 + k2) * .5, k2);
        var bri = .8 + .25 * uEnergy + KeyAt(gp);
        var cb = Mix(Mix(c, c2, Smoothstep(0, uSpread, d)), uAvg, FarMix(d) * .6);
        var hk = h * Math.Exp(-d / (uSpread * .55));
        outer = (cb * h * .3 + cb * kc * hk * (1.2 + .6 * uEnergy) + uCore * kc * hk * .3) * bri * gn;
        var di = Math.Min(gd, 0);
        inner = (c * Math.Exp(di / 7) * .35 + cb * kc * Math.Exp(di / 5) * .25) * bri * gn;
        rim = c * Math.Exp(-Math.Abs(gd) / (.8 + gaa * .5)) * (.7 + .5 * kc.Y) * bri * gn;
    }

    // E Borea, aurora rim
    private void Borea(V2 gp, V2 gq, double gd, double gu, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var br = .5 + .5 * Math.Sin(TAU * t * 2 - PI * .5);
        var d = Math.Max(gd, 0);
        var rays = Ring(gu + t, 13, 2, 0) * .65 + Ring(gu - t, 4, -1, 2) * .55;
        rays = rays * rays * (1 + .6 * uEnergy) + KeyAt(gp) * .8;
        var w1 = 3 + 4 * br + 3 * uEnergy;
        var tail = Math.Exp(-d / (uSpread * (.22 + .12 * br + .1 * uEnergy)));
        var ca = Mix(Pal(gu + t), Pal(gu + t + .3), Smoothstep(0, uSpread * .5, d));
        var off = new V2(Math.Sin(TAU * t) * 7, 5 + 2 * Math.Cos(TAU * t));
        var q2 = gq - off;
        var d2 = SdRR(q2, uB + 4, uR + 4);
        var u2 = RimU(q2);
        var rays2 = Ring(u2 - t, 7, 1, 4);
        var e2 = Math.Exp(-Math.Max(d2, 0) / (uSpread * (.45 + .2 * br))) * (rays2 * .8 + .2) * Smoothstep(-14, 4, d2);
        var cb2 = Mix(Pal(u2 - t + .5), uAvg, FarMix(Math.Max(d2, 0)));
        var lift = .75 + .35 * br;
        outer = (ca * tail * rays * .5 + cb2 * e2 * .3) * gn * lift;
        rim = ca * Math.Exp(-Sq(gd / w1)) * rays * .8 * gn * lift;
        var di = Math.Min(gd, 0);
        inner = (ca * Math.Exp(di / 6) * rays * .3 + cb2 * e2 * .12) * gn * lift;
    }

    // F Quies, calm accent (the pack's first colour)
    private void Quies(V2 gp, double gd, double gu, double gaa, out V3 outer, out V3 inner, out V3 rim)
    {
        var t = uLoop;
        var gn = GainF();
        var acc = uPal[0];
        var br = .86 + .14 * Math.Sin(TAU * t * 2);
        var lobe = Math.Exp(-Sq(Wrapd(gu - t) / .07)) + uWork * .8 * Math.Exp(-Sq(Wrapd(gu - t + .5) / .07));
        var k = KeyAt(gp);
        var d = Math.Max(gd, 0);
        outer = acc * HaloP(d, uSpread, .5, 0) * (.45 + .25 * lobe) * br * (1 + k) * gn;
        rim = (uCore * Math.Exp(-Math.Abs(gd) / (.65 + gaa * .5)) * (.35 + .9 * lobe + k) + acc * Math.Exp(-Math.Abs(gd) / 2.5) * .25) * br * gn;
        var di = Math.Min(gd, 0);
        inner = (acc * Math.Exp(di / 8) * .22 + uCore * Math.Exp(di / 3) * lobe * .12) * br * gn;
    }

    // ---- the Prelude ----

    private static double Sq(double x) => x * x;

    private static double SdRR(V2 p, V2 b, double r)
    {
        var q = Abs(p) - b + r;
        return Max(q, 0).Length + Math.Min(Math.Max(q.X, q.Y), 0) - r;
    }

    // float32, operation for operation, the dot unfused (the inputs here are whole numbers, exact in float32); Skia's raster
    // pipeline gives the same bits
    private static double H21(V2 p)
    {
        var x = Fract((float)p.X * 123.34f);
        var y = Fract((float)p.Y * 456.21f);
        var s = x * (x + 45.32f) + y * (y + 45.32f);
        x += s;
        y += s;
        return Fract(x * y);
    }

    private static double VNoise(V2 p)
    {
        var i = Floor(p);
        var f = p - i;
        var w = f * f * (3 - 2 * f);
        double a = H21(i), b = H21(i + new V2(1, 0)), c = H21(i + new V2(0, 1)), d = H21(i + new V2(1, 1));
        return Mix(Mix(a, b, w.X), Mix(c, d, w.X), w.Y);
    }

    private static V2 Circ(double a) => new(Math.Cos(a), Math.Sin(a));

    private double Ring(double u, double R, double k, double seed) =>
        VNoise(Circ(TAU * u) * R + Circ(TAU * uLoop * k + seed) * 1.6 + new V2(seed * 13.1 + 40, seed * 7.3 + 40));

    private double RimU(V2 q)
    {
        var a = uB.X - uR;
        var e = Math.Max(uB.Y - uR, 0);
        var P = 4 * a + 4 * e + TAU * uR;
        double s;
        if (q.X > a)
        {
            if (q.Y < -e) s = 2 * a + uR * (Math.Atan2(q.Y + e, q.X - a) + PI * .5);
            else if (q.Y > e) s = 2 * a + 2 * e + uR * (Math.Atan2(q.Y - e, q.X - a) + PI * .5);
            else s = 2 * a + PI * .5 * uR + (q.Y + e);
        }
        else if (q.X < -a)
        {
            if (q.Y > e) s = 4 * a + 2 * e + PI * uR + uR * (Math.Atan2(q.Y - e, q.X + a) - PI * .5);
            else if (q.Y < -e)
            {
                var th = Math.Atan2(q.Y + e, q.X + a);
                if (th < 0) th += TAU;
                s = 4 * a + 4 * e + PI * uR + uR * (th - PI * .5);
            }
            else s = 4 * a + 2 * e + 1.5 * PI * uR + (e - q.Y);
        }
        else if (Math.Abs(q.Y) <= e && uB.X - Math.Abs(q.X) < uB.Y - Math.Abs(q.Y))
            s = q.X > 0 ? 2 * a + PI * .5 * uR + (q.Y + e) : 4 * a + 2 * e + 1.5 * PI * uR + (e - q.Y);
        else if (q.Y < 0) s = q.X + a;
        else s = 2 * a + 2 * e + PI * uR + (a - q.X);
        return s / P;
    }

    private V2 RimN(V2 q)
    {
        var a = uB.X - uR;
        var e = Math.Max(uB.Y - uR, 0);
        var v = q - new V2(Math.Clamp(q.X, -a, a), Math.Clamp(q.Y, -e, e));
        var l = v.Length;
        if (l > 1e-3) return v / l;
        if (e > 0 && uB.X - Math.Abs(q.X) < uB.Y - Math.Abs(q.Y)) return new V2(q.X < 0 ? -1 : 1, 0);
        if (e > 0) return new V2(0, q.Y > 0 ? 1 : -1);
        return new V2(0, -1);
    }

    private V3 PalAt(double i) => i < .5 ? uPal[0] : (i < 1.5 ? uPal[1] : (i < 2.5 ? uPal[2] : uPal[3]));

    private V3 Pal(double t)
    {
        t = Fract(t) * 4;
        var fi = Math.Floor(t);
        var f = t - fi;
        f = f * f * (3 - 2 * f);
        return Mix(PalAt(fi), PalAt(Mod(fi + 1, 4)), f);
    }

    private double GainF() => uInt * (.38 + .82 * uEnergy);

    private double FarMix(double d) => Smoothstep(0, uSpread * .9, d) * .8;

    private double KeyAt(V2 p) => uKey * Math.Exp(-Math.Abs(p.X - uCaret) / 80);

    private static double Wrapd(double x) => Math.Abs(Fract(x + .5) - .5);

    private static V3 HaloP(double d, double s, double core, double disp)
    {
        var x = d / (s * (1 + disp * new V3(0, .08, .18)));
        var f = core * Exp(-x * 4.5) + (1 - core) / (1 + x * x * 4.5);
        return f * (1 - Smoothstep(1.4, 2.9, x));
    }

    private static V3 Tonemap(V3 c)
    {
        var m = Math.Max(c.X, Math.Max(c.Y, c.Z));
        c += Math.Max(m - .9, 0) * .22;
        var x = Max(c - .62, 0);
        return Min(c, .62) + .38 * (1 - Exp(-x / .38));
    }

    // ---- GLSL's built-ins (fract(x) = x - floor(x), mod(x, y) = x - y floor(x / y), smoothstep clamped) ----

    private static float Fract(float x) => x - MathF.Floor(x);

    private static double Fract(double x) => x - Math.Floor(x);

    private static double Mod(double x, double y) => x - y * Math.Floor(x / y);

    private static double Smoothstep(double e0, double e1, double x)
    {
        var t = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static V3 Smoothstep(double e0, double e1, V3 x) => new(Smoothstep(e0, e1, x.X), Smoothstep(e0, e1, x.Y), Smoothstep(e0, e1, x.Z));

    private static double Step(double edge, double x) => x < edge ? 0 : 1;

    private static double Mix(double a, double b, double t) => a * (1 - t) + b * t;

    private static V3 Mix(V3 a, V3 b, double t) => a * (1 - t) + b * t;

    private static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static V2 Abs(V2 v) => new(Math.Abs(v.X), Math.Abs(v.Y));

    private static V2 Max(V2 v, double s) => new(Math.Max(v.X, s), Math.Max(v.Y, s));

    private static V2 Floor(V2 v) => new(Math.Floor(v.X), Math.Floor(v.Y));

    private static V3 Abs(V3 v) => new(Math.Abs(v.X), Math.Abs(v.Y), Math.Abs(v.Z));

    private static V3 Exp(V3 v) => new(Math.Exp(v.X), Math.Exp(v.Y), Math.Exp(v.Z));

    private static V3 Max(V3 v, double s) => new(Math.Max(v.X, s), Math.Max(v.Y, s), Math.Max(v.Z, s));

    private static V3 Min(V3 v, double s) => new(Math.Min(v.X, s), Math.Min(v.Y, s), Math.Min(v.Z, s));

    private static V3 Clamp(V3 v, double lo, double hi) => new(Math.Clamp(v.X, lo, hi), Math.Clamp(v.Y, lo, hi), Math.Clamp(v.Z, lo, hi));

    private static V3 Pow(V3 v, double e) => new(Math.Pow(v.X, e), Math.Pow(v.Y, e), Math.Pow(v.Z, e));

    private readonly struct V2(double x, double y)
    {
        public readonly double X = x, Y = y;

        public double Length => Math.Sqrt(X * X + Y * Y);

        public static V2 operator +(V2 a, V2 b) => new(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new(a.X - b.X, a.Y - b.Y);
        public static V2 operator *(V2 a, V2 b) => new(a.X * b.X, a.Y * b.Y);
        public static V2 operator +(V2 a, double s) => new(a.X + s, a.Y + s);
        public static V2 operator -(V2 a, double s) => new(a.X - s, a.Y - s);
        public static V2 operator -(double s, V2 a) => new(s - a.X, s - a.Y);
        public static V2 operator *(V2 a, double s) => new(a.X * s, a.Y * s);
        public static V2 operator *(double s, V2 a) => new(s * a.X, s * a.Y);
        public static V2 operator /(V2 a, double s) => new(a.X / s, a.Y / s);
    }

    private readonly struct V3(double x, double y, double z)
    {
        public readonly double X = x, Y = y, Z = z;

        public V3(double s) : this(s, s, s) { }

        public static V3 operator -(V3 a) => new(-a.X, -a.Y, -a.Z);
        public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, V3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
        public static V3 operator +(V3 a, double s) => new(a.X + s, a.Y + s, a.Z + s);
        public static V3 operator +(double s, V3 a) => new(s + a.X, s + a.Y, s + a.Z);
        public static V3 operator -(V3 a, double s) => new(a.X - s, a.Y - s, a.Z - s);
        public static V3 operator -(double s, V3 a) => new(s - a.X, s - a.Y, s - a.Z);
        public static V3 operator *(V3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
        public static V3 operator *(double s, V3 a) => new(s * a.X, s * a.Y, s * a.Z);
        public static V3 operator /(V3 a, double s) => new(a.X / s, a.Y / s, a.Z / s);
        public static V3 operator /(double s, V3 a) => new(s / a.X, s / a.Y, s / a.Z);
    }
}
