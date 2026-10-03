// PORT-ONLY, owner-ruled addition (OD-0083, OD-0089; ED-251): draws one composer's light from its animator: the Uiverse light
// (one shader over the composer and its halo) and up to three Border beam slots (one shader each, over the composer, or
// over its haloes for Pulse Outside). Per frame only uniforms change; the blurred rounded rectangles are baked when the
// composer's size, the scale or a blur changes (on the GPU when there is one), never per frame.
using Marrow.Core.Models.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>A blurred rounded rectangle's coverage, baked at device scale, sampled in the light's own coordinates.</summary>
internal sealed class BakedRoundRect : IDisposable
{
    private (float X, float Y, float W, float H, float R, float Sigma, float Scale) key = (float.NaN, 0, 0, 0, 0, 0, 0);
    private SKImage? image;
    public SKShader? Shader { get; private set; }

    /// <summary>Makes sure the bake matches (rect, radius, sigma) at <paramref name="scale"/>; returns its sampling shader.</summary>
    public SKShader Get(GRContext? gpu, float x, float y, float w, float h, float r, float sigma, float scale)
    {
        var want = (x, y, w, h, r, sigma, scale);
        if (Shader is not null && want == key) return Shader;
        key = want;
        Shader?.Dispose(); image?.Dispose();
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
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x, y, x + w, y + h), Math.Max(0, r)), paint);
        image = surface.Snapshot();
        var local = SKMatrix.CreateScaleTranslation(1 / scale, 1 / scale, left, top);
        Shader = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, new SKSamplingOptions(SKFilterMode.Linear), local);
        return Shader;
    }

    public void Dispose() { Shader?.Dispose(); image?.Dispose(); Shader = null; image = null; }
}

/// <summary>Draws a <see cref="ComposerLightAnimator"/>'s current frame.</summary>
public sealed class ComposerLightRenderer : IDisposable
{
    private const int Max = EllipseBuffer.Capacity;

    // Large and Small's static layers, baked at device scale: the inner wash, the stroke's blobs on the ring, the ring.
    private sealed class RotateBake : IDisposable
    {
        private (BeamSlotConfig Key, float W, float H, float Clip, float Ring, float ShadowSigma, float Scale)? key;
        private SKImage? inner, stroke, ring;
        public SKShader? Inner { get; private set; }
        public SKShader? Stroke { get; private set; }
        public SKShader? Ring { get; private set; }

        public bool Matches(BeamSlotConfig k, float w, float h, float clip, float ringR, float sigma, float scale) =>
            Inner is not null && key == (k, w, h, clip, ringR, sigma, scale);

        public void Set(BeamSlotConfig k, float w, float h, float clip, float ringR, float sigma, float scale, SKImage i, SKImage s, SKImage r)
        {
            Dispose();
            key = (k, w, h, clip, ringR, sigma, scale);
            inner = i; stroke = s; ring = r;
            var local = SKMatrix.CreateScaleTranslation(1 / scale, 1 / scale, -1, -1);
            var sampling = new SKSamplingOptions(SKFilterMode.Linear);
            Inner = i.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, sampling, local);
            Stroke = s.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, sampling, local);
            Ring = r.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, sampling, local);
        }

        public void Dispose()
        {
            Inner?.Dispose(); Stroke?.Dispose(); Ring?.Dispose(); inner?.Dispose(); stroke?.Dispose(); ring?.Dispose();
            Inner = Stroke = Ring = null; inner = stroke = ring = null;
        }
    }

    private sealed class BeamGpu : IDisposable
    {
        public readonly SKRuntimeEffectUniforms Rotate = new(ComposerLightShaders.Rotate);
        public readonly SKRuntimeEffectChildren RotateChildren = new(ComposerLightShaders.Rotate);
        public readonly SKRuntimeEffectUniforms Moving = new(ComposerLightShaders.Moving);
        public readonly SKRuntimeEffectChildren MovingChildren = new(ComposerLightShaders.Moving);
        public readonly BakedRoundRect Shadow = new(), Bloom = new();
        public readonly RotateBake Bake = new();
        public readonly float[] Eg = new float[Max * 4], Es = new float[Max * 4], E0 = new float[Max * 4], E1 = new float[Max * 4], E2 = new float[Max * 4];
        public readonly float[] HueS = new float[9], HueB = new float[9], Sat = new float[9];
        public void Dispose() { Rotate.Dispose(); RotateChildren.Dispose(); Moving.Dispose(); MovingChildren.Dispose(); Shadow.Dispose(); Bloom.Dispose(); Bake.Dispose(); }
    }

    private readonly BeamGpu[] beams = [new(), new(), new()];
    private readonly SKRuntimeEffectUniforms auraUniforms = new(ComposerLightShaders.Aura);
    private readonly SKRuntimeEffectChildren auraChildren = new(ComposerLightShaders.Aura);
    private readonly BakedRoundRect halo = new(), rim = new();
    private readonly SKShader nothing = SKShader.CreateColor(SKColors.Transparent);
    private readonly SKShader profiles = ComposerLightShaders.Profiles.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Linear));
    private readonly SKPaint paint = new() { IsAntialias = false, BlendMode = SKBlendMode.SrcOver };

    /// <summary>
    /// Draws the light with the composer's top left at the canvas's origin, at a device <paramref name="scale"/>, with an
    /// overall <paramref name="opacity"/>. <paramref name="fadeBottom"/> (composer px), when given, is where the outer light
    /// has faded out (the playground's window edge fade, 13 px).
    /// </summary>
    public void Draw(SKCanvas canvas, GRContext? gpu, ComposerLightAnimator light, float scale, float opacity = 1, float? fadeBottom = null)
    {
        if (light.AuraVisible) DrawAura(canvas, gpu, light.Aura, scale, opacity, fadeBottom);
        for (var i = 0; i < light.DrawnCount; i++)
        {
            var slot = light.Drawn[i];
            if (light.SlotKey(slot) is { } key) DrawBeam(canvas, gpu, beams[slot], light.Slots[slot], key, scale, opacity);
        }
    }

    private void DrawBeam(SKCanvas canvas, GRContext? gpu, BeamGpu g, BeamFrame f, BeamSlotConfig key, float scale, float opacity)
    {
        float w = (float)f.W, h = (float)f.H;
        if (w <= 0 || h <= 0) return;
        var rotate = f.Mode == 0;
        var u = rotate ? g.Rotate : g.Moving;
        Common(u, g, f, scale, opacity);
        var clip = (float)f.ClipRadius;
        var shadow = f.ShadowSigma > 0 && f.Shadow.A > 0
            ? g.Shadow.Get(gpu, 1, 1, w - 2, h - 2, Math.Max(0, clip - 1), (float)f.ShadowSigma, scale)
            : nothing;
        var bloom = f.Mode is 0 or 1 or 2 ? g.Bloom.Get(gpu, 0, 0, w, h, (float)f.RingRadius, (float)Math.Max(0.05, f.BloomSigma), scale) : nothing;
        SKShader shader;
        if (rotate)
        {
            if (!g.Bake.Matches(key, w, h, clip, (float)f.RingRadius, (float)f.ShadowSigma, scale)) Bake(gpu, g, f, key, shadow, scale);
            var c = g.RotateChildren;
            c["innerTex"] = g.Bake.Inner; c["strokeTex"] = g.Bake.Stroke; c["ringTex"] = g.Bake.Ring;
            c["bloomTex"] = bloom; c["profiles"] = profiles; c["shadowTex"] = nothing;
            shader = ComposerLightShaders.Rotate.ToShader(u, c);
        }
        else
        {
            Ellipses(u, g, f);
            var c = g.MovingChildren;
            c["shadowTex"] = shadow; c["bloomTex"] = bloom; c["profiles"] = profiles;
            shader = ComposerLightShaders.Moving.ToShader(u, c);
        }
        using (shader)
        {
            var reach = f.Mode == 3 ? 30 + 3 * (float)Math.Max(f.OuterBloomSigma, f.CoreSigma) + 4 : 1;
            paint.Shader = shader;
            canvas.DrawRect(new SKRect(-reach, -reach, w + reach, h + reach), paint);
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

    // Large and Small's static layers into three images at device scale (1 px of margin), once per structure, size and scale
    private void Bake(GRContext? gpu, BeamGpu g, BeamFrame f, BeamSlotConfig key, SKShader shadow, float scale)
    {
        float w = (float)f.W, h = (float)f.H;
        using var u = new SKRuntimeEffectUniforms(ComposerLightShaders.RotateBake);
        Common(u, g, f, scale, 1);
        Ellipses(u, g, f);
        using var c = new SKRuntimeEffectChildren(ComposerLightShaders.RotateBake) { ["shadowTex"] = shadow, ["profiles"] = profiles };
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
            canvas.DrawRect(new SKRect(-1, -1, w + 1, h + 1), p);
            return surface.Snapshot();
        }
        g.Bake.Set(key, w, h, (float)f.ClipRadius, (float)f.RingRadius, (float)f.ShadowSigma, scale, Pass(1), Pass(2), Pass(3));
    }

    private void DrawAura(SKCanvas canvas, GRContext? gpu, AuraFrame a, float scale, float opacity, float? fadeBottom)
    {
        float w = (float)a.W, h = (float)a.H;
        if (w <= 0 || h <= 0) return;
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
        auraChildren["haloTex"] = halo.Get(gpu, -hs, -hs, w + 2 * hs, h + 2 * hs, r + hs, Math.Max(0.05f, hb), scale);
        auraChildren["rimTex"] = rim.Get(gpu, -rw, -rw, w + 2 * rw, h + 2 * rw, r + rw, Math.Max(0.3f, 2 * rw), scale);
        var reach = hs + 3 * hb + 4;
        using var shader = ComposerLightShaders.Aura.ToShader(u, auraChildren);
        paint.Shader = shader;
        canvas.DrawRect(new SKRect(-reach, -reach, w + reach, h + reach), paint);
        paint.Shader = null;
    }

    private static void Set(SKRuntimeEffectUniforms u, string name, double x, double y, double z, double w)
    {
        Span<float> v = stackalloc float[4];
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

    public void Dispose()
    {
        foreach (var b in beams) b.Dispose();
        auraUniforms.Dispose(); auraChildren.Dispose(); halo.Dispose(); rim.Dispose(); nothing.Dispose(); profiles.Dispose(); paint.Dispose();
    }
}
