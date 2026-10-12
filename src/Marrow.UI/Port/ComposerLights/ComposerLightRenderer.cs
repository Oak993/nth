// PORT-ONLY, owner-ruled addition (OD-0083, OD-0089; ED-251): draws one composer's light from its animator: the Uiverse light
// (one shader over the composer and its halo) and up to three Border beam slots (one shader each, over the composer, or
// over its haloes for Pulse Outside). Per frame only uniforms change. What the board's port notes bake (the blurred
// rounded rectangles; Large and Small's blob fields) is baked once its size, scale or blur has been still for 150 ms (on
// the GPU when there is one); while it moves (the composer growing by a line, a blur's .6 s transition) the shaders draw
// the blurred rectangles analytically and stretch the last blob fields over the new size, so no blur ever runs per frame.
using Marrow.Core.Models.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>When a bake may run: at once when there is none, else once what it is baked from has been still for a while.</summary>
internal sealed class BakeSettle<TKey> where TKey : struct, IEquatable<TKey>
{
    /// <summary>How long a size, scale or blur stays still before it is baked (ED-251).</summary>
    public const double Seconds = 0.15;

    private TKey? baked, pending;
    private double since;

    public TKey? Baked => baked;

    /// <summary>True when the bake for <paramref name="want"/> should run now; <paramref name="stale"/> when the bake in hand is not for it.</summary>
    public bool Due(TKey want, double now, out bool stale)
    {
        stale = false;
        if (baked is { } b && b.Equals(want)) return false;
        if (baked is null) return true;
        stale = true;
        if (pending is not { } p || !p.Equals(want)) { pending = want; since = now; return false; }
        return now - since >= Seconds;
    }

    public void Done(TKey key) { baked = key; pending = null; }

    public void Reset() { baked = null; pending = null; }
}

/// <summary>A blurred rounded rectangle's coverage, baked at device scale, sampled in the light's own coordinates.</summary>
internal sealed class BakedRoundRect : IDisposable
{
    private readonly BakeSettle<(float X, float Y, float W, float H, float R, float Sigma, float Scale)> settle = new();
    private SKImage? image;
    private SKShader? shader;

    /// <summary>
    /// The bake for (rect, radius, sigma) at <paramref name="scale"/> when it is settled (<paramref name="baked"/> true), else
    /// nothing to sample (the shader draws it analytically this frame); <paramref name="stale"/> while it waits to settle.
    /// </summary>
    public SKShader? Get(GRContext? gpu, float x, float y, float w, float h, float r, float sigma, float scale, double now, out bool baked, out bool stale)
    {
        var want = (x, y, w, h, r, sigma, scale);
        if (settle.Due(want, now, out stale)) { Bake(gpu, x, y, w, h, r, sigma, scale); settle.Done(want); stale = false; }
        baked = !stale && shader is not null;
        return baked ? shader : null;
    }

    /// <summary>How many times this rectangle has been baked (tests).</summary>
    public int Bakes { get; private set; }

    private void Bake(GRContext? gpu, float x, float y, float w, float h, float r, float sigma, float scale)
    {
        Bakes++;
        shader?.Dispose(); image?.Dispose();
        var pad = 3 * sigma + 2;
        var left = x - pad; var top = y - pad;
        var pw = Math.Max(1, (int)Math.Ceiling((w + 2 * pad) * scale));
        var phh = Math.Max(1, (int)Math.Ceiling((h + 2 * pad) * scale));
        var info = new SKImageInfo(pw, phh, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scale);
        canvas.Translate(-left, -top);
        using var blur = sigma > 0.05f ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null;
        using var paint = new SKPaint { IsAntialias = true, Color = SKColors.White, MaskFilter = blur };
        if (GpuWarmSites.Draw()) canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), Math.Max(0, r)), paint); // a warm-up site (ED-NEW-warmupsplit-1)
        image = surface.Snapshot();
        var local = SKMatrix.CreateScaleTranslation(1 / scale, 1 / scale, left, top);
        shader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Linear), local);
    }

    public void Dispose() { shader?.Dispose(); image?.Dispose(); shader = null; image = null; settle.Reset(); }
}

/// <summary>Draws a <see cref="ComposerLightAnimator"/>'s current frame.</summary>
public sealed class ComposerLightRenderer : IDisposable
{
    /// <summary>CPU SkSL and profile preparation before GPU linking (no graphics context required).</summary>
    public static void Prepare() { _ = ComposerLightShaders.RotateBake; _ = ComposerLightShaders.Rotate; _ = ComposerLightShaders.Moving; _ = ComposerLightShaders.Aura; _ = ComposerLightShaders.Profiles; }

    private const int Max = EllipseBuffer.Capacity;

    // Large and Small's blob fields, baked at device scale (1 px of margin): the inner blobs and the stroke's.
    private sealed class BlobBake : IDisposable
    {
        public readonly BakeSettle<(BeamSlotConfig Key, float W, float H, float Scale)> Settle = new();
        private SKImage? inner, stroke;
        public SKShader? Inner { get; private set; }
        public SKShader? Stroke { get; private set; }
        public float W { get; private set; }
        public float H { get; private set; }
        public int Bakes { get; private set; }

        public void Set(float w, float h, float scale, SKImage i, SKImage s)
        {
            Release();
            Bakes++;
            W = w; H = h; inner = i; stroke = s;
            var local = SKMatrix.CreateScaleTranslation(1 / scale, 1 / scale, -1, -1);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear);
            Inner = i.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, local);
            Stroke = s.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, local);
        }

        private void Release()
        {
            Inner?.Dispose(); Stroke?.Dispose(); inner?.Dispose(); stroke?.Dispose();
            Inner = Stroke = null; inner = stroke = null;
        }

        public void Dispose() { Release(); Settle.Reset(); }
    }

    private sealed class BeamGpu : IDisposable
    {
        public readonly SKRuntimeEffectUniforms Rotate = new(ComposerLightShaders.Rotate);
        public readonly SKRuntimeEffectChildren RotateChildren = new(ComposerLightShaders.Rotate);
        public readonly SKRuntimeEffectUniforms Moving = new(ComposerLightShaders.Moving);
        public readonly SKRuntimeEffectChildren MovingChildren = new(ComposerLightShaders.Moving);
        public readonly BakedRoundRect Shadow = new(), Bloom = new();
        public readonly BlobBake Blobs = new();
        public readonly float[] Eg = new float[Max * 4], Es = new float[Max * 4], E0 = new float[Max * 4], E1 = new float[Max * 4], E2 = new float[Max * 4];
        public readonly float[] HueS = new float[9], HueB = new float[9], Sat = new float[9];
        public void Dispose() { Rotate.Dispose(); RotateChildren.Dispose(); Moving.Dispose(); MovingChildren.Dispose(); Shadow.Dispose(); Bloom.Dispose(); Blobs.Dispose(); }
    }

    private readonly BeamGpu[] beams = [new(), new(), new()];
    private readonly SKRuntimeEffectUniforms auraUniforms = new(ComposerLightShaders.Aura);
    private readonly SKRuntimeEffectChildren auraChildren = new(ComposerLightShaders.Aura);
    private readonly BakedRoundRect halo = new(), rim = new();
    private readonly SKShader nothing = SKShader.CreateColor(SKColors.Transparent);
    private readonly SKShader profiles = ComposerLightShaders.Profiles.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
    private readonly SKPaint paint = new() { IsAntialias = false, BlendMode = SKBlendMode.SrcOver };
    private readonly ShaderLightRenderer shaderLights = new(); // OD-0150: the shader lights (Composer Light v2)
    private bool disposed;

    /// <summary>How many bakes (blurred rectangles and blob fields) have run so far (tests).</summary>
    internal int BakeCount
    {
        get
        {
            var n = halo.Bakes + rim.Bakes;
            foreach (var b in beams) n += b.Shadow.Bakes + b.Bloom.Bakes + b.Blobs.Bakes;
            return n;
        }
    }

    /// <summary>True when the last frame drew something from a bake still waiting to settle (another frame finishes it).</summary>
    public bool Settling { get; private set; }

    /// <summary>
    /// Draws the light with the composer's top left at the canvas's origin, at a device <paramref name="scale"/>, at
    /// <paramref name="now"/> (seconds, for the bakes' settling), with an overall <paramref name="opacity"/>.
    /// <paramref name="fadeBottom"/> (composer px), when given, is where the outer light has faded out (the playground's
    /// window edge fade, 13 px).
    /// </summary>
    /// <param name="surface">The surface <paramref name="canvas"/> draws into, for the shader lights' Text and light (null: they draw with Screen).</param>
    public void Draw(SKCanvas canvas, GRContext? gpu, ComposerLightAnimator light, float scale, double now = 0, float opacity = 1, float? fadeBottom = null, SKSurface? surface = null)
    {
        if (disposed) return;
        Settling = false;
        if (light.AuraVisible) DrawAura(canvas, gpu, light.Aura, scale, now, opacity, fadeBottom);
        for (var i = 0; i < light.DrawnCount; i++)
        {
            var slot = light.Drawn[i];
            if (light.SlotKey(slot) is { } key) DrawBeam(canvas, gpu, beams[slot], light.Slots[slot], key, scale, now, opacity);
        }
        var shaders = light.Shaders;
        for (var i = 0; i < shaders.DrawnCount; i++)
        {
            // with no GPU the light is a still (ED-251): the software raster shades its band once and keeps the pixels
            if (gpu is null) shaderLights.DrawStill(canvas, shaders.Drawn[i], scale, opacity, fadeBottom);
            else shaderLights.Draw(canvas, shaders.Drawn[i], scale, opacity, surface, fadeBottom);
        }
    }

    private void Cover(SKRuntimeEffectUniforms u, SKRuntimeEffectChildren c, string child, string rect, string parms, BakedRoundRect bake,
        GRContext? gpu, float x, float y, float w, float h, float r, float sigma, float scale, double now)
    {
        var shader = bake.Get(gpu, x, y, w, h, r, sigma, scale, now, out var baked, out var stale);
        Settling |= stale;
        c[child] = shader ?? nothing;
        Set(u, rect, x, y, w, h);
        Set(u, parms, r, sigma, baked ? 1 : 0, 0);
    }

    private void DrawBeam(SKCanvas canvas, GRContext? gpu, BeamGpu g, BeamFrame f, BeamSlotConfig key, float scale, double now, float opacity)
    {
        float w = (float)f.W, h = (float)f.H;
        if (!(w > 0) || !(h > 0)) return;
        var rotate = f.Mode == 0;
        var u = rotate ? g.Rotate : g.Moving;
        var c = rotate ? g.RotateChildren : g.MovingChildren;
        Common(u, g, f, scale, opacity);
        var clip = (float)f.ClipRadius;
        if (f.ShadowSigma > 0 && f.Shadow.A > 0)
            Cover(u, c, "shadowTex", "shadowR", "shadowP", g.Shadow, gpu, 1, 1, w - 2, h - 2, Math.Max(0, clip - 1), (float)f.ShadowSigma, scale, now);
        else { c["shadowTex"] = nothing; Set(u, "shadowR", 0, 0, 0, 0); Set(u, "shadowP", 0, 0, 0, 0); }
        if (f.Mode is 0 or 1 or 2)
            Cover(u, c, "bloomTex", "bloomR", "bloomP", g.Bloom, gpu, 0, 0, w, h, (float)f.RingRadius, (float)Math.Max(0.05, f.BloomSigma), scale, now);
        else { c["bloomTex"] = nothing; Set(u, "bloomR", 0, 0, 0, 0); Set(u, "bloomP", 0, 0, 0, 0); }
        c["profiles"] = profiles;
        SKShader shader;
        if (rotate)
        {
            // Only a size or scale change waits for stillness: a slot reused for another structure bakes at once (ED-251 review N1).
            if (g.Blobs.Settle.Due((key, w, h, scale), now, out var stale) || (stale && g.Blobs.Settle.Baked is { } held && !held.Item1.Equals(key))) { BakeBlobs(gpu, g, f, scale); g.Blobs.Settle.Done((key, w, h, scale)); stale = false; }
            Settling |= stale;
            c["innerTex"] = g.Blobs.Inner; c["strokeTex"] = g.Blobs.Stroke;
            Set(u, "bakeMap", g.Blobs.W / w, g.Blobs.H / h, 0, 0);       // a stale bake stretched over the moving size
            shader = ComposerLightShaders.Rotate.ToShader(u, c);
        }
        else
        {
            Set(u, "bakeMap", 1, 1, 0, 0);
            Ellipses(u, g, f);
            shader = ComposerLightShaders.Moving.ToShader(u, c);
        }
        using (shader)
        {
            var reach = f.Mode == 3 ? 30 + 3 * (float)Math.Max(f.OuterBloomSigma, f.CoreSigma) + 4 : 1;
            paint.Shader = shader;
            if (GpuWarmSites.Draw()) canvas.DrawRect(new SKRect(-reach, -reach, w + reach, h + reach), paint);
            paint.Shader = null;
        }
    }

    // the uniforms every beam shader shares
    private static void Common(SKRuntimeEffectUniforms u, BeamGpu g, BeamFrame f, float scale, float opacity)
    {
        u["box"] = new SKSize((float)f.W, (float)f.H);
        Set(u, "geo", f.ClipRadius, f.RingRadius, scale, f.Mode);
        Set(u, "win", f.Phase, f.WindowRadius, f.Fitted ? 1 : 0, 0);
        Set(u, "rows", ComposerLightShaders.RowW1, f.InnerWindowW2 ? ComposerLightShaders.RowW2 : ComposerLightShaders.RowW1,
            f.Dark ? ComposerLightShaders.RowHighlightDark : ComposerLightShaders.RowHighlightLight, f.Dark ? ComposerLightShaders.RowBloomDark : ComposerLightShaders.RowBloomLight);
        Set(u, "bands", f.Bands ? 1 : 0, f.BandV, f.BandH, f.BloomSigma);
        Set(u, "opac", f.InnerOpacity, f.StrokeOpacity, f.BloomOpacity, f.Alpha * opacity);
        Set(u, "outer", f.OuterCoreOpacity, f.OuterBloomOpacity, f.CoreSigma, f.OuterBloomSigma);
        Set(u, "flags", f.FilterStroke ? 1 : 0, f.FilterBloom ? 1 : 0, f.HueOnBloom ? 1 : 0, f.Shadow.A > 0 && f.ShadowSigma > 0 ? 1 : 0);
        Premul(u, "shadowC", f.Shadow);
        Premul(u, "hlc", f.HighlightColor);
        HueRotate(f.HueStroke, g.HueS); u["hueS"] = g.HueS.AsSpan();
        HueRotate(f.HueBloom, g.HueB); u["hueB"] = g.HueB.AsSpan();
        Saturate(f.Saturation, g.Sat); u["satM"] = g.Sat.AsSpan();
        u["bright"] = (float)f.Brightness;
        Set(u, "lmask", f.MaskX, f.MaskSrx, f.MaskSry, f.MaskBrx);
        u["lmaskB"] = (float)f.MaskBry;
    }

    // the ellipse primitives as the shaders' five arrays
    private static void Ellipses(SKRuntimeEffectUniforms u, BeamGpu g, BeamFrame f)
    {
        var e = f.Ellipses; var n = e.Count; var d = e.Data;
        for (var i = 0; i < Max; i++)
        {
            var o = i * EllipseBuffer.Stride; var k = i * 4;
            for (var j = 0; j < 4; j++)
            {
                if (i < n)
                {
                    g.Eg[k + j] = d[o + j]; g.Es[k + j] = d[o + 4 + j]; g.E0[k + j] = d[o + 8 + j]; g.E1[k + j] = d[o + 12 + j]; g.E2[k + j] = d[o + 16 + j];
                }
                else { g.Eg[k + j] = j < 2 ? 0 : 1; g.Es[k + j] = 0; g.E0[k + j] = g.E1[k + j] = g.E2[k + j] = 0; }
            }
        }
        u["count"] = (float)n;
        u["eg"] = g.Eg.AsSpan(); u["es"] = g.Es.AsSpan(); u["e0"] = g.E0.AsSpan(); u["e1"] = g.E1.AsSpan(); u["e2"] = g.E2.AsSpan();
    }

    // Large and Small's blob fields into two images at device scale (1 px of margin)
    private void BakeBlobs(GRContext? gpu, BeamGpu g, BeamFrame f, float scale)
    {
        float w = (float)f.W, h = (float)f.H;
        using var u = new SKRuntimeEffectUniforms(ComposerLightShaders.RotateBake);
        Common(u, g, f, scale, 1);
        Ellipses(u, g, f);
        using var c = new SKRuntimeEffectChildren(ComposerLightShaders.RotateBake) { ["shadowTex"] = nothing, ["bloomTex"] = nothing, ["profiles"] = profiles };
        var info = new SKImageInfo(Math.Max(1, (int)Math.Ceiling((w + 2) * scale)), Math.Max(1, (int)Math.Ceiling((h + 2) * scale)), SKColorType.Rgba8888, SKAlphaType.Premul);
        SKImage Pass(float bake)
        {
            u["bake"] = bake;
            using var surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale);
            canvas.Translate(1, 1);
            using var shader = ComposerLightShaders.RotateBake.ToShader(u, c);
            using var p = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            if (GpuWarmSites.Draw()) canvas.DrawRect(new SKRect(-1, -1, w + 1, h + 1), p);
            return surface.Snapshot();
        }
        g.Blobs.Set(w, h, scale, Pass(1), Pass(2));
    }

    private void DrawAura(SKCanvas canvas, GRContext? gpu, AuraFrame a, float scale, double now, float opacity, float? fadeBottom)
    {
        float w = (float)a.W, h = (float)a.H;
        if (!(w > 0) || !(h > 0)) return;
        var u = auraUniforms;
        float hs = (float)a.HaloSize, hb = (float)a.HaloBlur, rw = (float)a.RimWidth;
        u["box"] = new SKSize(w, h);
        Set(u, "g", a.Radius, scale, a.Angle * Math.PI / 180, a.Scale);
        Set(u, "halo", hs, hb, a.Sx, a.Sy);
        Set(u, "rim", rw, a.Rsx, a.Rsy, 0);
        Set(u, "alphas", a.HaloAlpha, a.RimAlpha, a.InnerAlpha, a.GroupAlpha * opacity);
        // the halftone's box sits at -(hs + 2.2 hb), snapped to whole px as the browser lays a box out
        Set(u, "dots", a.DotStrength, a.DotCell, Math.Floor(hs + 2.2 * hb + 0.5), 0);
        Set(u, "fade", fadeBottom ?? 0, 13, fadeBottom is null ? 0 : 1, 0);
        Premul(u, "c1", a.C1); Premul(u, "c2", a.C2); Premul(u, "k1", a.K1); Premul(u, "k2", a.K2); Premul(u, "n1", a.N1); Premul(u, "n2", a.N2);
        var r = (float)a.Radius;
        Cover(u, auraChildren, "haloTex", "haloR", "haloP", halo, gpu, -hs, -hs, w + 2 * hs, h + 2 * hs, r + hs, Math.Max(0.05f, hb), scale, now);
        Cover(u, auraChildren, "rimTex", "rimR", "rimP", rim, gpu, -rw, -rw, w + 2 * rw, h + 2 * rw, r + rw, Math.Max(0.3f, 2 * rw), scale, now);
        var reach = hs + 3 * hb + 4;
        using var shader = ComposerLightShaders.Aura.ToShader(u, auraChildren);
        paint.Shader = shader;
        if (GpuWarmSites.Draw()) canvas.DrawRect(new SKRect(-reach, -reach, w + reach, h + reach), paint);
        paint.Shader = null;
    }

    [ThreadStatic] private static float[]? scratch;

    private static void Set(SKRuntimeEffectUniforms u, string name, double x, double y, double z, double w)
    {
        // a reused array, not a stackalloc'd Span: SDK 10.0.112's span-safety rules reject the Span's implicit conversion to SKRuntimeEffectUniform
        var v = scratch ??= new float[4];
        v[0] = (float)x; v[1] = (float)y; v[2] = (float)z; v[3] = (float)w;
        u[name] = v;
    }

    private static void Premul(SKRuntimeEffectUniforms u, string name, CssColor c)
    {
        var a = Math.Clamp(c.A, 0, 1);
        Set(u, name, c.R / 255 * a, c.G / 255 * a, c.B / 255 * a, a);
    }

    /// <summary>The CSS hue-rotate() matrix (Filter Effects 1), column-major for SkSL's float3x3.</summary>
    internal static void HueRotate(double degrees, float[] m)
    {
        var a = degrees * Math.PI / 180; double c = Math.Cos(a), s = Math.Sin(a);
        Put(m, 0.213 + c * 0.787 - s * 0.213, 0.715 - c * 0.715 - s * 0.715, 0.072 - c * 0.072 + s * 0.928,
               0.213 - c * 0.213 + s * 0.143, 0.715 + c * 0.285 + s * 0.140, 0.072 - c * 0.072 - s * 0.283,
               0.213 - c * 0.213 - s * 0.787, 0.715 - c * 0.715 + s * 0.715, 0.072 + c * 0.928 + s * 0.072);
    }

    /// <summary>The CSS saturate() matrix, column-major.</summary>
    internal static void Saturate(double s, float[] m) =>
        Put(m, 0.213 + 0.787 * s, 0.715 - 0.715 * s, 0.072 - 0.072 * s,
               0.213 - 0.213 * s, 0.715 + 0.285 * s, 0.072 - 0.072 * s,
               0.213 - 0.213 * s, 0.715 - 0.715 * s, 0.072 + 0.928 * s);

    // rows in, columns out
    private static void Put(float[] m, double a00, double a01, double a02, double a10, double a11, double a12, double a20, double a21, double a22)
    {
        m[0] = (float)a00; m[1] = (float)a10; m[2] = (float)a20;
        m[3] = (float)a01; m[4] = (float)a11; m[5] = (float)a21;
        m[6] = (float)a02; m[7] = (float)a12; m[8] = (float)a22;
    }

    /// <summary>Releases the bakes and shaders. Call it on the thread that draws (the render thread for a composer's light).</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var b in beams) b.Dispose();
        auraUniforms.Dispose(); auraChildren.Dispose(); halo.Dispose(); rim.Dispose(); nothing.Dispose(); profiles.Dispose(); paint.Dispose();
        shaderLights.Dispose();
    }
}
