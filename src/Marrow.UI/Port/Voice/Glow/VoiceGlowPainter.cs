// PORT-ONLY, owner-ruled addition (OD-0097, second amendment; first-release voice typing, VT-9, voice-SPEC V-13.2, V-13.3): draws one frame of the voice glow, layer by layer as
// the stylesheet of voice-glow.js stacks them (Ya, lines 244-576): the inner light (::before, z 1) and its warp mirror, the stroke ring (::after, z 2), the bloom (z 3) and its
// warp mirror, the band canvas (Sn, z 4) and the epicentre (z 4), all inside the composer's rounded shape. The soft layers are rastered at half size and scaled back up (the
// stylesheet's `data-voice-halfres`, --vb-z 0.5); the 1 px stroke and the band stay full size. Pure SkiaSharp (no Avalonia): the handler lends the canvas.
using Marrow.Core.Port.Voice.Glow;
using SkiaSharp;

namespace Marrow.UI.Port.Voice.Glow;

/// <summary>
/// Draws the glow. Holds the reused surface, paints, paths and uniform blocks: a frame makes no surface and no array; what it still allocates is SkiaSharp's own
/// (every uniform set and every shader or image wrapper).
/// </summary>
internal sealed class VoiceGlowPainter : IDisposable
{
    /// <summary>The soft layers' factor (<c>--vb-z</c> of <c>[data-voice-halfres]</c>).</summary>
    public const double HalfSize = 0.5;

    private const double Epsilon = 1e-6;

    private readonly SKRuntimeEffectUniforms lobeUniforms = new(VoiceGlowShaders.Lobes);
    private readonly SKRuntimeEffectUniforms ellipseUniforms = new(VoiceGlowShaders.Ellipse);
    private readonly SKRuntimeEffectUniforms layerUniforms = new(VoiceGlowShaders.Layer);
    private readonly SKRuntimeEffectChildren layerChildren = new(VoiceGlowShaders.Layer);
    private readonly float[] lobeA = new float[28];
    private readonly float[] lobeC = new float[28];
    private readonly float[] v4a = new float[4];
    private readonly float[] v4b = new float[4];
    private readonly float[] v4c = new float[4];
    private readonly float[] v4d = new float[4];
    private readonly float[] v4e = new float[4];
    private readonly float[] v4f = new float[4];
    private readonly SKPaint paint = new() { IsAntialias = true };
    private readonly SKPaint stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
    private readonly SKPath above = new();
    private readonly SKPath below = new();
    private readonly SKPath belowZ = new();
    private readonly SKPath bandLine = new();
    private readonly SKSamplingOptions linear = new(SKFilterMode.Linear, SKMipmapMode.None);
    private readonly SKSamplingOptions nearest = new(SKFilterMode.Nearest, SKMipmapMode.None);
    private SKSurface? surface;
    private readonly Dictionary<float, SKImageFilter> blurs = new();
    private int surfaceW, surfaceH;
    private GRContext? surfaceGpu;
    private SKShader? noise;
    private double noiseX = double.NaN, noiseY, noiseZ;
    private SKPath? ring;
    private double ringW = double.NaN, ringH, ringRadius, ringPadding;
    private bool disposed;

    /// <summary>How many offscreen surfaces have been made (tests: one per size and context).</summary>
    public int SurfaceCreations { get; private set; }

    /// <summary>The size of the soft layers' surface in pixels (0 x 0: none made).</summary>
    public (int Width, int Height) SurfaceSize => surface is null ? (0, 0) : (surfaceW, surfaceH);

    /// <summary>
    /// Draws the glow with its top left at the canvas's origin, <paramref name="width"/> x <paramref name="height"/> canvas units. <paramref name="frame"/> is the driver's
    /// frame for this size; <paramref name="opacity"/> is the element's <c>--vb-opacity</c> (the fade) times the host's own, 0 to 1; <paramref name="dpr"/> is device pixels
    /// per canvas unit; <paramref name="gpu"/> is the window's GPU context (null: the CPU raster).
    /// </summary>
    public void Draw(SKCanvas canvas, GRContext? gpu, VoiceGlowLook look, VoiceGlowFrame frame, double width, double height, double dpr, double opacity, double z = HalfSize)
    {
        if (disposed || !(opacity > 0) || !(width > 0) || !(height > 0)) return;
        var recipe = look.Recipe;
        var strength = look.Strength;
        var layerSet = frame.HasBand;
        using var css = VoiceGlowFilters.Chain(recipe.HueBase + frame.Hue, recipe.Brightness, recipe.Saturation);
        var split = recipe.Distortion && !frame.WarpOff;
        if (layerSet && (split || recipe.Core is not null)) BuildPolygons(frame, width, height, z);

        canvas.Save();
        using (var shape = new SKRoundRect(new SKRect(0, 0, (float)width, (float)height), (float)recipe.Radius))
            canvas.ClipRoundRect(shape, SKClipOperation.Intersect, true);

        var glow = frame.Glow;
        // 1. the inner light (::before) and its mirror
        DrawSoft(canvas, gpu, look, frame, width, height, dpr, z, css, Layer.Inner, Clamp01(opacity * glow * recipe.InnerOpacity * strength), split);
        // 2. the stroke ring (::after)
        DrawStroke(canvas, look, frame, width, height, css, Clamp01(opacity * glow * recipe.StrokeOpacity * strength));
        // 3. the bloom and its mirror
        DrawSoft(canvas, gpu, look, frame, width, height, dpr, z, css, Layer.Bloom, Clamp01(opacity * glow * recipe.BloomOpacity * strength), split);
        // 4. the band canvas
        if (layerSet) DrawBand(canvas, look, frame, dpr, css, Clamp01(opacity * strength));
        // 5. the epicentre
        if (recipe.Core is { } core) DrawCore(canvas, gpu, look, frame, width, height, dpr, z, core, Clamp01(opacity * Math.Min(1, glow * core.OpacityScale)));
        canvas.Restore();
    }

    private enum Layer { Inner, Bloom }

    private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

    // ---- the band line's polygons (yn): `below` is the area under the line, `above` the rest, in canvas units and, for the epicentre, scaled to the soft layers
    private void BuildPolygons(VoiceGlowFrame f, double width, double height, double z)
    {
        above.Rewind();
        below.Rewind();
        belowZ.Rewind();
        var n = VoiceGlowFrame.BandPointCount;
        below.MoveTo(0, (float)height);
        for (var i = 0; i < n; i++) below.LineTo((float)f.BandX[i], (float)f.BandY[i]);
        below.LineTo((float)width, (float)height);
        below.Close();
        above.MoveTo(0, 0);
        above.LineTo((float)width, 0);
        above.LineTo((float)width, (float)height);
        for (var i = n - 1; i >= 0; i--) above.LineTo((float)f.BandX[i], (float)f.BandY[i]);
        above.LineTo(0, (float)height);
        above.Close();
        belowZ.MoveTo(0, (float)(height * z));
        for (var i = 0; i < n; i++) belowZ.LineTo((float)(f.BandX[i] * z), (float)(f.BandY[i] * z));
        belowZ.LineTo((float)(width * z), (float)(height * z));
        belowZ.Close();
    }

    // a Gaussian blur of sigma `sigma` (CSS blur(r): sigma r), decal edges; made once per radius
    private SKImageFilter Blur(double sigma)
    {
        var key = (float)sigma;
        if (!blurs.TryGetValue(key, out var filter)) blurs[key] = filter = SKImageFilter.CreateBlur(key, key, SKShaderTileMode.Decal);
        return filter;
    }

    // ---- surface
    private SKCanvas Surface(GRContext? gpu, int w, int h)
    {
        if (surface is null || surfaceW != w || surfaceH != h || !ReferenceEquals(surfaceGpu, gpu))
        {
            surface?.Dispose();
            var info = new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
            surface = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
            surfaceW = w;
            surfaceH = h;
            surfaceGpu = gpu;
            SurfaceCreations += 1;
        }
        return surface.Canvas;
    }

    // ---- the lobes of a layer: dt()
    private void SetLobes(GlowLobeSet set, VoiceGlowFrame f, double wl, double hl, double z)
    {
        for (var p = 0; p < VoiceGlowFrame.LobeCount; p++)
        {
            var color = set.Colors[p % set.Colors.Count];
            lobeA[p * 4] = (float)(0.5 * wl + (f.Cx + f.LobeX[p]) * f.W * z);
            lobeA[p * 4 + 1] = (float)(hl + (set.Y + f.LobeY[p]) * z);
            lobeA[p * 4 + 2] = (float)(set.Width(p) * f.W * z);
            lobeA[p * 4 + 3] = (float)(set.Height(p) * f.H * f.LobeL[p] * z);
            lobeC[p * 4] = color.R / 255f;
            lobeC[p * 4 + 1] = color.G / 255f;
            lobeC[p * 4 + 2] = color.B / 255f;
            lobeC[p * 4 + 3] = (float)set.Alpha;
        }
        lobeUniforms["lobeA"] = lobeA;
        lobeUniforms["lobeC"] = lobeC;
        lobeUniforms["fade"] = (float)(set.FadePercent / 100);
    }

    // ---- the inner light and the bloom, with their warp mirrors
    private void DrawSoft(SKCanvas canvas, GRContext? gpu, VoiceGlowLook look, VoiceGlowFrame f, double width, double height, double dpr, double z, SKColorFilter css,
        Layer which, double opacity, bool split)
    {
        if (opacity <= 0) return;
        var r = look.Recipe;
        double wl = width * z, hl = height * z;
        var px = (int)Math.Ceiling(wl * dpr - Epsilon);
        var py = (int)Math.Ceiling(hl * dpr - Epsilon);
        var content = Surface(gpu, Math.Max(1, px), Math.Max(1, py));
        content.Clear(SKColors.Transparent);
        content.Save();
        content.SetMatrix(SKMatrix.CreateScale((float)dpr, (float)dpr));
        var box = new SKRect(0, 0, (float)wl, (float)hl);
        if (which == Layer.Inner)
            using (var rounded = new SKRoundRect(box, (float)(r.Radius * z))) content.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
        var set = which == Layer.Inner ? r.Inner : r.Bloom;
        SetLobes(set, f, wl, hl, z);
        using (var lobes = VoiceGlowShaders.Lobes.ToShader(lobeUniforms))
        {
            paint.Shader = lobes;
            paint.IsAntialias = false;
            paint.BlendMode = SKBlendMode.SrcOver;
            if (which == Layer.Bloom)
            {
                // filter: blur(...) on the element, whose background is clipped to its own rounded box (radius P) before the blur
                paint.ImageFilter = Blur(r.BloomBlur * z);
                content.SaveLayer(paint);
                paint.ImageFilter = null;
                paint.Shader = lobes;
                using (var own = new SKRoundRect(box, (float)(r.RingRadius * z))) content.ClipRoundRect(own, SKClipOperation.Intersect, true);
                content.DrawRect(box, paint);
                content.Restore();
            }
            else
            {
                content.DrawRect(box, paint);
            }
            paint.Shader = null;
            paint.IsAntialias = true;
        }
        if (which == Layer.Inner) DrawInsetShadow(content, r, wl, hl, z);
        content.Restore();

        using var image = surface!.Snapshot();
        var mask = which == Layer.Inner ? r.InnerMask : r.BloomMask;
        if (!split) Composite(canvas, image, f, mask, which == Layer.Inner, r, width, height, dpr, z, opacity, css, look, 0);
        else
        {
            canvas.Save();
            canvas.ClipPath(above, SKClipOperation.Intersect, true);
            Composite(canvas, image, f, mask, which == Layer.Inner, r, width, height, dpr, z, opacity, css, look, 0);
            canvas.Restore();
            if (f.DisplaceActive)
            {
                canvas.Save();
                canvas.ClipPath(below, SKClipOperation.Intersect, true);
                Composite(canvas, image, f, mask, which == Layer.Inner, r, width, height, dpr, z, opacity, css, look, f.DisplaceScale);
                canvas.Restore();
            }
        }
    }

    // the inner light's `box-shadow: inset 0 0 y(I) 1px innerShadow` inside the layer's rounded box
    private void DrawInsetShadow(SKCanvas c, VoiceGlowRecipe r, double wl, double hl, double z)
    {
        if (r.InnerShadow.Alpha == 0) return;
        const float reach = 64;
        var sigma = (float)(r.ShadowBlur * z / 2);
        var radius = (float)(r.Radius * z);
        using var outer = new SKRoundRect(new SKRect(-reach, -reach, (float)wl + reach, (float)hl + reach), 0);
        using var inner = new SKRoundRect(new SKRect(1, 1, (float)wl - 1, (float)hl - 1), Math.Max(0, radius - 1));
        using var blur = sigma > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, sigma) : null;
        paint.Color = r.InnerShadow;
        paint.MaskFilter = blur;
        c.DrawRoundRectDifference(outer, inner, paint);
        paint.MaskFilter = null;
        paint.Color = SKColors.White;
    }

    // one layer's picture through its mask, onto the canvas (the shape clip and the band polygon are set by the caller)
    private void Composite(SKCanvas canvas, SKImage image, VoiceGlowFrame f, GlowEllipseMask mask, bool edge, VoiceGlowRecipe r, double width, double height, double dpr,
        double z, double opacity, SKColorFilter css, VoiceGlowLook look, double displaceScale)
    {
        double wl = width * z, hl = height * z;
        var u = layerUniforms;
        // the ellipse mask ie(): centre (pe, 100% + cy), radii (S(rw*H) * w * mw, S(rh*Q) * h + bh), all times the layer factor
        v4a[0] = (float)(0.5 * wl + f.Cx * f.W * z);
        v4a[1] = (float)(hl + f.Cy * z);
        v4a[2] = (float)(mask.Width * f.W * f.Mw * z);
        v4a[3] = (float)((mask.Height * f.H + f.Bh) * z);
        var halfStop = (float)(mask.HalfPercent / 100);
        if (mask.Tail > 0)
        {
            v4b[0] = 0; v4b[1] = halfStop; v4b[2] = 0.85f; v4b[3] = 1;
            v4c[0] = 1; v4c[1] = 0.5f; v4c[2] = (float)mask.Tail; v4c[3] = 0;
        }
        else
        {
            v4b[0] = 0; v4b[1] = halfStop; v4b[2] = 1; v4b[3] = 1;
            v4c[0] = 1; v4c[1] = 0.5f; v4c[2] = 0; v4c[3] = 0;
        }
        v4d[0] = (float)(r.EdgeMargin * z);
        v4d[1] = edge ? 1 : 0;
        v4d[2] = (float)wl;
        v4d[3] = (float)hl;
        u["ell"] = v4a;
        u["pos"] = v4b;
        u["ma"] = v4c;
        u["edge"] = v4d;
        u["zf"] = (float)z;
        u["opacity"] = (float)opacity;
        u["dispScale"] = (float)displaceScale;
        v4e[0] = (float)f.NoiseDx;
        v4e[1] = (float)f.NoiseDy;
        v4e[2] = 0;
        v4e[3] = 0;
        u["dispShift"] = v4e;
        using var source = image.ToShader(SKShaderTileMode.Decal, SKShaderTileMode.Decal, linear, SKMatrix.CreateScale((float)(1 / (z * dpr)), (float)(1 / (z * dpr))));
        layerChildren["content"] = source;
        layerChildren["noise"] = NoiseFor(look.NoiseFrequencyX, look.NoiseFrequencyY, z);
        using var shader = VoiceGlowShaders.Layer.ToShader(u, layerChildren);
        paint.Shader = shader;
        paint.ColorFilter = css;
        paint.BlendMode = SKBlendMode.SrcOver;
        paint.IsAntialias = false;
        canvas.DrawRect(0, 0, (float)width, (float)height, paint);
        paint.Shader = null;
        paint.ColorFilter = null;
        paint.IsAntialias = true;
    }

    // feTurbulence type="fractalNoise" baseFrequency numOctaves="2" seed="7", in the soft layers' own coordinates
    private SKShader NoiseFor(double fx, double fy, double z)
    {
        if (noise is not null && noiseX == fx && noiseY == fy && noiseZ == z) return noise;
        noise?.Dispose();
        using var perlin = SKShader.CreatePerlinNoiseFractalNoise((float)fx, (float)fy, 2, 7);
        noise = perlin.WithLocalMatrix(SKMatrix.CreateScale((float)(1 / z), (float)(1 / z)));
        noiseX = fx;
        noiseY = fy;
        noiseZ = z;
        return noise;
    }

    // ---- the stroke ring (::after)
    private void DrawStroke(SKCanvas canvas, VoiceGlowLook look, VoiceGlowFrame f, double width, double height, SKColorFilter css, double opacity)
    {
        if (opacity <= 0) return;
        var r = look.Recipe;
        var ringPath = Ring(width, height, r.RingRadius, r.BorderWidth);
        canvas.Save();
        canvas.ClipPath(ringPath, SKClipOperation.Intersect, true);
        paint.ColorFilter = css;
        canvas.SaveLayer(paint);
        paint.ColorFilter = null;
        paint.IsAntialias = false;
        paint.BlendMode = SKBlendMode.SrcOver;

        SetLobes(r.Stroke, f, width, height, 1);
        using (var lobes = VoiceGlowShaders.Lobes.ToShader(lobeUniforms))
        {
            paint.Shader = lobes;
            canvas.DrawRect(0, 0, (float)width, (float)height, paint);
        }

        // Ze: the wash at the source (white on dark, black on light) over the lobes
        var wash = r.Wash;
        var grey = wash.Grey / 255f;
        v4a[0] = (float)(0.5 * width + f.Cx * f.W);
        v4a[1] = (float)(height + 2 + f.Cy);
        v4a[2] = (float)(wash.Width * f.W);
        v4a[3] = (float)(wash.Height * f.H);
        SetStops(0, (float)(wash.SecondPercent / 100), (float)(wash.EndPercent / 100), 1,
            grey * (float)wash.First, (float)wash.First, grey * (float)wash.Second, (float)wash.Second, 0, 0, 0, 0);
        DrawEllipse(canvas, width, height, BlendOp.SrcOver);

        // the mask ie(170, 64, 45): the ellipse's alpha, multiplied in
        var mask = r.StrokeMask;
        v4a[0] = (float)(0.5 * width + f.Cx * f.W);
        v4a[1] = (float)(height + f.Cy);
        v4a[2] = (float)(mask.Width * f.W * f.Mw);
        v4a[3] = (float)((mask.Height * f.H + f.Bh));
        var o = (float)opacity;
        SetStops(0, (float)(mask.HalfPercent / 100), 1, 1, o, o, 0.5f * o, 0.5f * o, 0, 0, 0, 0);
        DrawEllipse(canvas, width, height, BlendOp.DstIn);
        canvas.Restore();
        canvas.Restore();
        paint.Shader = null;
        paint.IsAntialias = true;
    }

    private enum BlendOp { SrcOver, DstIn }

    // stop positions (a..d) then four premultiplied colours as (rgb, alpha) pairs where rgb is the grey of the stop: mask and wash stops are grey or white
    private void SetStops(float p0, float p1, float p2, float p3, float g0, float a0, float g1, float a1, float g2, float a2, float g3, float a3)
    {
        v4b[0] = p0; v4b[1] = p1; v4b[2] = p2; v4b[3] = p3;
        v4c[0] = g0; v4c[1] = g0; v4c[2] = g0; v4c[3] = a0;
        v4d[0] = g1; v4d[1] = g1; v4d[2] = g1; v4d[3] = a1;
        v4e[0] = g2; v4e[1] = g2; v4e[2] = g2; v4e[3] = a2;
        v4f[0] = g3; v4f[1] = g3; v4f[2] = g3; v4f[3] = a3;
    }

    private void DrawEllipse(SKCanvas canvas, double width, double height, BlendOp op)
    {
        var u = ellipseUniforms;
        u["ell"] = v4a;
        u["pos"] = v4b;
        u["s0"] = v4c;
        u["s1"] = v4d;
        u["s2"] = v4e;
        u["s3"] = v4f;
        using var shader = VoiceGlowShaders.Ellipse.ToShader(u);
        paint.Shader = shader;
        paint.BlendMode = op == BlendOp.DstIn ? SKBlendMode.DstIn : SKBlendMode.SrcOver;
        canvas.DrawRect(0, 0, (float)width, (float)height, paint);
        paint.Shader = null;
        paint.BlendMode = SKBlendMode.SrcOver;
    }

    // the border ring of ::after: its border box (radius P) minus its content box (padding 1 px, radius P - 1), even-odd
    private SKPath Ring(double width, double height, double radius, double padding)
    {
        if (ring is not null && ringW == width && ringH == height && ringRadius == radius && ringPadding == padding) return ring;
        ring ??= new SKPath();
        ring.Rewind();
        ring.FillType = SKPathFillType.EvenOdd;
        using (var outer = new SKRoundRect(new SKRect(0, 0, (float)width, (float)height), (float)radius)) ring.AddRoundRect(outer);
        using (var inner = new SKRoundRect(new SKRect((float)padding, (float)padding, (float)(width - padding), (float)(height - padding)), (float)Math.Max(0, radius - padding)))
            ring.AddRoundRect(inner);
        ringW = width;
        ringH = height;
        ringRadius = radius;
        ringPadding = padding;
        return ring;
    }

    // ---- the epicentre (light themes)
    private void DrawCore(SKCanvas canvas, GRContext? gpu, VoiceGlowLook look, VoiceGlowFrame f, double width, double height, double dpr, double z, GlowCore core, double opacity)
    {
        if (opacity <= 0 || !f.HasBand) return;
        var r = look.Recipe;
        double wl = width * z, hl = height * z;
        var px = (int)Math.Ceiling(wl * dpr - Epsilon);
        var py = (int)Math.Ceiling(hl * dpr - Epsilon);
        var content = Surface(gpu, Math.Max(1, px), Math.Max(1, py));
        content.Clear(SKColors.Transparent);
        content.Save();
        content.SetMatrix(SKMatrix.CreateScale((float)dpr, (float)dpr));
        paint.ImageFilter = Blur(core.Blur * z);
        content.SaveLayer(paint);
        paint.ImageFilter = null;
        using (var rounded = new SKRoundRect(new SKRect(0, 0, (float)wl, (float)hl), (float)(r.RingRadius * z))) content.ClipRoundRect(rounded, SKClipOperation.Intersect, true);
        content.ClipPath(belowZ, SKClipOperation.Intersect, true);
        v4a[0] = (float)(0.5 * wl + f.Cx * f.W * z);
        v4a[1] = (float)(hl + f.Cy * z);
        v4a[2] = (float)(core.Width * f.W * z);
        v4a[3] = (float)((core.Height * f.H + f.Bh) * z);
        var mid = (float)core.MidAlpha;
        SetStops(0, (float)(core.SolidPercent / 100), (float)(core.MidPercent / 100), (float)(core.EndPercent / 100), 1, 1, 1, 1, mid, mid, 0, 0);
        paint.IsAntialias = false;
        DrawEllipse(content, wl, hl, BlendOp.SrcOver);
        paint.IsAntialias = true;
        content.Restore();
        content.Restore();

        using var image = surface!.Snapshot();
        paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)VoiceGlowCss.Round(opacity * 255), 0, 255));
        canvas.Save();
        canvas.Scale((float)(1 / (z * dpr)));
        canvas.DrawImage(image, 0, 0, linear, paint);
        canvas.Restore();
        paint.Color = SKColors.White;
    }

    // ---- the band canvas (Sn)
    private void DrawBand(SKCanvas canvas, VoiceGlowLook look, VoiceGlowFrame f, double dpr, SKColorFilter css, double opacity)
    {
        var t = look.Config;
        var h = Math.Min(1, 0.6 * t.BandStrength * f.BendA);
        if (h < 5e-3 || t.BandWidth <= 0 || opacity <= 0) return;
        var dark = t.Theme == VoiceGlowTheme.Dark;
        var o = t.BandWidth * (1 + 0.35 * f.Level);
        var l = t.BandAberration * (0.35 + 0.65 * f.Level);
        var lift = (4 + 12 * l) * t.Scale;
        var shift = 4 * l * t.Scale;
        var d = (dark ? 0.42 : 0.4) * h;
        var lineWidth = 14 * o;
        var blur = 3.5 * t.BandWidth / 2;
        var n = VoiceGlowFrame.BandPointCount;
        var x0 = (float)f.BandX[0];
        var x1 = (float)f.BandX[n - 1];

        paint.ColorFilter = css;
        paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp((int)VoiceGlowCss.Round(opacity * 255), 0, 255));
        canvas.SaveLayer(paint);
        paint.ColorFilter = null;
        paint.Color = SKColors.White;

        // the wide soft halo under the line, then the 16 strokes of four colours and four widths, each blurred on its own
        ParseRgb(t.BandColors.Core, out var core);
        StrokeLine(canvas, f, x0, x1, t.BandTail > 0, core, d * 0.3, lineWidth * 2.2, 0, 0, blur * 3);
        ReadOnlySpan<(double K, double W)> ladder = [(1, 0.16), (0.72, 0.2), (0.46, 0.26), (0.22, 0.34)];
        for (var c = 0; c < 4; c++)
        {
            string text;
            double a, ox, oy;
            switch (c)
            {
                case 0: text = t.BandColors.Above; a = 1; ox = shift; oy = -lift; break;
                case 1: text = t.BandColors.Mid; a = 0.55; ox = shift * 0.35; oy = -lift * 0.35; break;
                case 2: text = t.BandColors.Below; a = 1; ox = -shift; oy = lift; break;
                default: text = t.BandColors.Core; a = 0.9; ox = 0; oy = 0; break;
            }
            ParseRgb(text, out var rgb);
            foreach (var (k, w) in ladder) StrokeLine(canvas, f, x0, x1, t.BandTail > 0, rgb, d * a * w, Math.Max(0.6, lineWidth * k), ox, oy, blur);
        }
        canvas.Restore();
    }

    private static void ParseRgb(string text, out GlowRgb rgb)
    {
        var parts = text.Split(',');
        rgb = new GlowRgb(byte.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture), byte.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture),
            byte.Parse(parts[2].Trim(), System.Globalization.CultureInfo.InvariantCulture));
    }

    private readonly SKColorF[] gradientColors = new SKColorF[4];
    private readonly float[] gradientStops = new float[4];

    // one stroke of the polyline, shifted by (ox, oy), with the canvas gradient I(rgb, alpha) and a blur of sigma `blur` (ctx.filter = blur(...))
    private void StrokeLine(SKCanvas canvas, VoiceGlowFrame f, float x0, float x1, bool tail, GlowRgb rgb, double alpha, double lineWidth, double ox, double oy, double blur)
    {
        var vf = tail ? 0.015f : 0.18f;
        var a = (float)(VoiceGlowCss.Fixed(alpha, 3));
        gradientColors[0] = new SKColorF(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, 0);
        gradientColors[1] = new SKColorF(rgb.R / 255f, rgb.G / 255f, rgb.B / 255f, a);
        gradientColors[2] = gradientColors[1];
        gradientColors[3] = gradientColors[0];
        gradientStops[0] = 0;
        gradientStops[1] = vf;
        gradientStops[2] = 1 - vf;
        gradientStops[3] = 1;
        using var gradient = SKShader.CreateLinearGradient(new SKPoint(x0, 0), new SKPoint(x1, 0), gradientColors, null, gradientStops, SKShaderTileMode.Clamp);
        bandLine.Rewind();
        var n = VoiceGlowFrame.BandPointCount;
        for (var i = 0; i < n; i++)
        {
            var px = (float)(f.BandX[i] + ox);
            var py = (float)(f.BandY[i] + oy);
            if (i == 0) bandLine.MoveTo(px, py);
            else bandLine.LineTo(px, py);
        }
        stroke.Shader = gradient;
        stroke.StrokeWidth = (float)lineWidth;
        stroke.ImageFilter = Blur(blur);
        canvas.DrawPath(bandLine, stroke);
        stroke.ImageFilter = null;
        stroke.Shader = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        surface?.Dispose();
        surface = null;
        noise?.Dispose();
        noise = null;
        foreach (var filter in blurs.Values) filter.Dispose();
        blurs.Clear();
        ring?.Dispose();
        ring = null;
        lobeUniforms.Dispose();
        ellipseUniforms.Dispose();
        layerUniforms.Dispose();
        layerChildren.Dispose();
        paint.Dispose();
        stroke.Dispose();
        above.Dispose();
        below.Dispose();
        belowZ.Dispose();
        bandLine.Dispose();
    }
}
