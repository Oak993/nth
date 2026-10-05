// PORT-ONLY: OD-0112's locked D Aurora, ported from design-src/app-icon/src/icons-a-j/D-aurora.js
// and iconkit.js. No loading-control or boot-stage integration belongs to this renderer.
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Marrow.UI.Port;

/// <summary>
/// Deterministic Canvas 2D drawing in icon units. Own one instance per render thread and dispose it there.
/// Reuses paths, paints, gradient buffers and raster scratch canvases. Skia's C ABI avoids managed wrappers
/// for changing gradients, surfaces, snapshots and filters. Native drawing objects still allocate in Skia and are unreferenced
/// as soon as their consumer takes ownership. The zero-allocation contract measures managed bytes, as OrbRendererTests do.
/// </summary>
public sealed class AuroraMark : IDisposable
{
    public const int LaunchDurationMs = 1200;
    public const int IdlePeriodMs = 7200;

    // These are Skia-generated device program families, not custom SkSL.
    // Blur and cached-night preparation use CPU raster surfaces; their image sampling/blends
    // and the direct gradients/solids run on the supplied destination canvas. The App warm-up registry can invoke Warmup
    // on its own GPU canvas, before showing Aurora, without Marrow.UI depending on Marrow.App.
    public static IReadOnlyList<string> WarmupPrograms { get; } = Array.AsReadOnly(new[]
    {
        "aurora-small-gradient-dither-src-over", "aurora-night-image-linear-src-over",
        "aurora-ray-gradient-dither-plus", "aurora-blur-image-catmull-rom-plus",
        "aurora-blur-image-catmull-rom-src-over", "aurora-ridge-gradient-dither-src-over",
        "aurora-glass-gradient-dither-src-over", "aurora-star-solid-src-over", "aurora-outline-solid-src-over",
    });

    private readonly record struct Ray(double U, double H, double A, double W, double K);
    private readonly record struct Star(double X, double Y, double A, double Phase);
    private readonly record struct Box(double X, double Y, double W, double H, double R);
    private static readonly Ray[] Rays = MakeRays();
    private static readonly Star[] Stars = MakeStars();
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeMiter = 10 };
    private readonly SKPaint layerPaint = new() { IsAntialias = true };
    private readonly SKPath path = new();
    private readonly SKPath strokePath = new();
    private readonly SKPath deviceBoundsPath = new();
    private readonly SKPoint[] points = new SKPoint[2];
    private readonly SKColorF[] colors = new SKColorF[13];
    private readonly float[] stops = new float[13];
    private Raster scratch = new();
    private Raster filteredScratch = new();
    private Raster nightRaster = new();
    private nint nightImage;
    private double nightSize;
    private int nightPixels;
    private string? nightTheme;
    private SKMatrix blurOrigin;
    private SKMatrix blurScratchMatrix;
    private SKRect blurBounds;
    private SKMatrix blurTransform;
    private SKBlendMode blurBlend;
    private double blurAlpha;
    private float blurSigma;
    private SKSizeI deviceSize;
    private bool disposed;

    /// <param name="deviceCanvasSize">The Canvas 2D canvas.width/height equivalent. Supply this when
    /// the caller has already restricted its clip; otherwise the device clip gives the target extent.</param>
    public void Draw(SKCanvas canvas, double size, string theme, AuroraTime t, SKSizeI? deviceCanvasSize = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        if (!double.IsFinite(size) || size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (!double.IsFinite(t.LaunchProgress) || !double.IsFinite(t.IdleSeconds)) throw new ArgumentOutOfRangeException(nameof(t));
        var clip = canvas.DeviceClipBounds;
        deviceSize = deviceCanvasSize ?? new SKSizeI(Math.Max(1, clip.Right), Math.Max(1, clip.Bottom));
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0) throw new ArgumentOutOfRangeException(nameof(deviceCanvasSize));
        var small = size <= 32;
        var margin = small ? 1 : Math.Floor(size * 6 + 0.5) / 100;
        var width = size - 2 * margin;
        var p = new Box(margin, margin, width, width, width * (small ? 0.24 : 0.225));
        var l = t.LaunchProgress;
        var phase = 2 * Math.PI * (((t.IdleSeconds * 1000 % IdlePeriodMs) + IdlePeriodMs) % IdlePeriodMs / IdlePeriodMs);
        var sway = Math.Sin(phase);
        var alpha = Out(Seg(l, 0, 0.35));
        var saved = canvas.Save();
        try
        {
            Night(canvas, size, theme, p, alpha);
            if (!small) DrawStars(canvas, size, p, Out(Seg(l, 0.1, 0.5)), phase / (2 * Math.PI), alpha);
            var reveal = l >= 1 ? 1 : InOut(Seg(l, 0.18, 0.92));
            var bright = l >= 1 ? 1 : 0.6 + 0.4 * Out(Seg(l, 0.5, 1));
            Curtain(canvas, size, p, reveal, phase, sway, bright);
            Glass(canvas, size, theme, p, alpha);
        }
        finally { canvas.RestoreToCount(saved); }
    }

    /// <summary>
    /// Draws the real program families into the supplied offscreen GPU canvas. The caller supplies the
    /// actual design size and sets its device transform: run this for every size/scale used by the loading
    /// and boot controls, since Skia selects blur kernels by device sigma. No custom SkSL is needed.
    /// A still and a frame during the tile fade exercise both opaque and opacity-modulated
    /// versions of the families; multiple named entries are intentionally safe to repeat.
    /// </summary>
    public void Warmup(SKCanvas canvas, string program, double size, SKSizeI? deviceCanvasSize = null)
    {
        if (!WarmupPrograms.Contains(program, StringComparer.Ordinal)) throw new ArgumentException("Unknown Aurora program.", nameof(program));
        var designSize = string.Equals(program, WarmupPrograms[0], StringComparison.Ordinal) ? Math.Min(size, 32) : size;
        Draw(canvas, designSize, "dark", AuroraTime.Still, deviceCanvasSize);
        Draw(canvas, designSize, "dark", AuroraTime.Launch(.3), deviceCanvasSize);
    }

    private void Night(SKCanvas c, double size, string theme, Box p, double alpha)
    {
        if (size <= 32)
        {
            Squircle(p.X, p.Y, p.W, p.H, p.R);
            NightGradient(p, alpha);
            DrawAnalyticPath(c.Handle, c.TotalMatrix);
            return;
        }
        var matrix = c.TotalMatrix;
        var scale = Math.Sqrt((double)matrix.ScaleX * matrix.ScaleX + (double)matrix.SkewY * matrix.SkewY);
        if (scale == 0) scale = 1; // IconKit.scaleOf: Math.hypot(...) || 1.
        var cacheScale = scale <= .5 + 1e-6 ? .5 : scale <= 1 + 1e-6 ? 1 : scale <= 1.5 + 1e-6 ? 1.5 :
            scale <= 2 + 1e-6 ? 2 : scale <= 3 + 1e-6 ? 3 : scale <= 4 + 1e-6 ? 4 : scale <= 6 + 1e-6 ? 6 : scale <= 8 + 1e-6 ? 8 : scale;
        var pixels = (int)Math.Floor(size * cacheScale + .5);
        if (nightImage == 0 || nightSize != size || nightPixels != pixels || !string.Equals(nightTheme, theme, StringComparison.Ordinal))
        {
            nightRaster.Ensure(pixels, pixels, false);
            nightRaster.Clear();
            var nightMatrix = SKMatrix.CreateScale((float)(pixels / size), (float)(pixels / size));
            SetMatrix(nightRaster.Canvas, nightMatrix);
            var originalSize = deviceSize;
            deviceSize = new SKSizeI(pixels, pixels);
            try
            {
                var shadow = BeginBlur(nightMatrix, size * .03, SKBlendMode.SrcOver);
                Solid(string.Equals(theme, "light", StringComparison.Ordinal) ? Color(10, 30, 50, .32) : Color(0, 0, 0, .6));
                Squircle(p.X + p.W * .03, p.Y + p.H * .05, p.W * .94, p.H * .96, p.R);
                DrawAnalyticPath(shadow, blurScratchMatrix);
                EndBlur(nightRaster.Canvas);
                Squircle(p.X, p.Y, p.W, p.H, p.R);
                NightGradient(p, 1);
                DrawAnalyticPath(nightRaster.Canvas, nightMatrix);
            }
            finally { deviceSize = originalSize; }
            var nextImage = Snapshot(nightRaster.Surface);
            if (nightImage != 0) Native.sk_image_unref(nightImage);
            nightImage = nextImage;
            nightSize = size; nightPixels = pixels; nightTheme = theme;
        }
        layerPaint.Color = SKColors.White.WithAlpha(AlphaByte(alpha)); layerPaint.BlendMode = SKBlendMode.SrcOver;
        var source = SKRect.Create(pixels, pixels); var dest = SKRect.Create((float)size, (float)size);
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        Native.sk_canvas_draw_image_rect(c.Handle, nightImage, in source, in dest, in sampling, layerPaint.Handle);
    }

    private void NightGradient(Box p, double alpha)
    {
        colors[0] = Opaque(0xff040818); colors[1] = Opaque(0xff06122a); colors[2] = Opaque(0xff071a2c); colors[3] = Opaque(0xff08222c);
        stops[0] = 0; stops[1] = .5f; stops[2] = .8f; stops[3] = 1;
        Gradient(p.X, p.Y, p.X, p.Y + p.H, 4, alpha);
    }

    private void DrawStars(SKCanvas c, double size, Box p, double k, double tw, double alpha)
    {
        for (var i = 0; i < Stars.Length; i++)
        {
            var s = Stars[i];
            var a = k * s.A * (.75 + .25 * Math.Cos(2 * Math.PI * (tw + s.Phase))) * (i % 3 == 0 ? 1 : .6);
            Solid(Color(220, 235, 255, Fixed3(a * .85)), alpha);
            var x = p.X + p.W * (.06 + .88 * s.X); var y = p.Y + p.H * (.06 + s.Y);
            var r = size * (.0022 + .0022 * s.A);
            path.Rewind(); path.MoveTo((float)(x + r), (float)y);
            var fx = (float)x; var fy = (float)y; var fr = (float)r;
            var oval = new SKRect(fx - fr, fy - fr, fx + fr, fy + fr);
            path.ArcTo(oval, 0, 180, false); path.ArcTo(oval, 180, 180, false); path.Close();
            DrawAnalyticPath(c.Handle, c.TotalMatrix);
        }
    }

    private void Curtain(SKCanvas c, double size, Box p, double reveal, double phase, double sway, double bright)
    {
        c.Save();
        Squircle(p.X, p.Y, p.W, p.H, p.R);
        ClipAnalyticPath(c);
        if (size <= 32)
        {
            var tiny = size <= 20;
            Solid(Color(80, 255, 190, .45), reveal, stroke: tiny ? 4 : 6, round: true);
            FootPath(p, phase, sway, tiny ? -2 : -3, 16, 0, 1, 1);
            DrawAnalyticPath(c.Handle, c.TotalMatrix, p);
            Solid(Opaque(0xff8affd0), reveal, stroke: tiny ? 2 : 2.5, round: true);
            FootPath(p, phase, sway, 0, 16, 0, 1, 1);
            DrawAnalyticPath(c.Handle, c.TotalMatrix, p);
            c.Restore();
            return;
        }
        var glow = BeginBlur(c, size * .02, SKBlendMode.Plus);
        DrawRays(glow, p, reveal, phase, sway, bright, 3, .55);
        EndBlur(c);
        DrawRays(c.Handle, p, reveal, phase, sway, bright, 1, .7);
        var foot = BeginBlur(c, size * .006, SKBlendMode.Plus);
        for (var i = 0; i <= 12; i++)
        {
            colors[i] = Color(170, 255, 215, Fixed3((.02 + .22 * Rays[i * 13].A * Rays[i * 13 + 1].A) * bright));
            stops[i] = i / 12f;
        }
        Gradient(p.X, 0, p.X + p.W, 0, 13, 1, SKBlendMode.Plus, size * .01, true);
        FootPath(p, phase, sway, 0, 40, .02, .96, Math.Clamp(reveal * 1.15, 0, 1));
        DrawAnalyticStroke(foot, blurScratchMatrix);
        EndBlur(c);

        colors[0] = Opaque(0xff04121a); colors[1] = Opaque(0xff02080d); stops[0] = 0; stops[1] = 1;
        Gradient(p.X, p.Y + p.H * .78, p.X, p.Y + p.H, 2);
        path.Rewind(); path.MoveTo((float)p.X, (float)(p.Y + p.H));
        for (var i = 0; i <= 30; i++)
        {
            var u = i / 30.0;
            path.LineTo((float)(p.X + p.W * u), (float)(p.Y + p.H * (.8 + .06 * Math.Sin(u * 6.1 + 1) + .025 * Math.Sin(u * 15.3) + .012 * Math.Sin(u * 37))));
        }
        path.LineTo((float)(p.X + p.W), (float)(p.Y + p.H)); path.Close();
        DrawAnalyticPath(c.Handle, c.TotalMatrix);
        Solid(Color(90, 255, 190, Fixed3(.25 * bright * reveal)), stroke: size * .003);
        c.DrawPath(path, paint);
        c.Restore();
    }

    private void DrawRays(nint c, Box p, double reveal, double phase, double sway, double bright, double wk, double ak)
    {
        foreach (var r in Rays)
        {
            var show = Math.Clamp((reveal * 1.25 - r.U * .25) * 1.6 - r.U * .6, 0, 1);
            if (show <= 0) continue;
            var x = p.X + p.W * (.02 + .96 * r.U);
            var fy = Foot(p, r.U, phase, sway);
            var flick = .8 + .2 * Math.Sin(2 * Math.PI * (r.K + phase / (2 * Math.PI)));
            var len = p.H * r.H * (.5 + .5 * show) * (.9 + .1 * flick);
            var a = r.A * show * bright * flick * ak;
            colors[0] = Color(150, 255, 200, Fixed3(a * .8));
            colors[1] = Color(60, 245, 150, Fixed3(a * .7));
            colors[2] = Color(30, 190, 160, Fixed3(a * .32));
            colors[3] = Color(170, 80, 230, Fixed3(a * .16));
            colors[4] = Color(150, 90, 255, 0);
            stops[0] = 0; stops[1] = .06f; stops[2] = .4f; stops[3] = .78f; stops[4] = 1;
            Gradient(x, fy, x, fy - len, 5, 1, SKBlendMode.Plus);
            var w = p.W * .012 * r.W * wk;
            var rayRect = SKRect.Create((float)(x - w / 2), (float)(fy - len), (float)w, (float)len);
            Native.sk_canvas_draw_rect(c, in rayRect, paint.Handle);
            var dl = p.H * (.07 + .09 * r.K);
            colors[0] = Color(90, 250, 170, Fixed3(a * .42)); colors[1] = Color(60, 200, 160, 0);
            stops[0] = 0; stops[1] = 1;
            Gradient(x, fy, x, fy + dl, 2, 1, SKBlendMode.Plus);
            var tailRect = SKRect.Create((float)(x - w / 2), (float)fy, (float)w, (float)dl);
            Native.sk_canvas_draw_rect(c, in tailRect, paint.Handle);
        }
    }

    private void Glass(SKCanvas c, double size, string theme, Box p, double alpha)
    {
        var light = string.Equals(theme, "light", StringComparison.Ordinal);
        if (size <= 32)
        {
            Solid(light ? Color(0, 20, 40, .5) : Color(255, 255, 255, .22), alpha, stroke: 1);
            Squircle(p.X + .5, p.Y + .5, p.W - 1, p.H - 1, p.R);
            c.DrawPath(path, paint);
            return;
        }
        c.Save(); Squircle(p.X, p.Y, p.W, p.H, p.R); ClipAnalyticPath(c);
        var lw = size * .018;
        var rim = BeginBlur(c, size * .006, SKBlendMode.SrcOver, alpha);
        colors[0] = Color(255, 255, 255, .55); colors[1] = Color(255, 255, 255, .08);
        colors[2] = Color(160, 255, 220, .05); colors[3] = Color(160, 255, 220, .3);
        stops[0] = 0; stops[1] = .4f; stops[2] = .8f; stops[3] = 1;
        Gradient(p.X, p.Y, p.X + p.W * .6, p.Y + p.H, 4, 1, stroke: lw);
        Squircle(p.X + lw * .3, p.Y + lw * .3, p.W - lw * .6, p.H - lw * .6, p.R);
        DrawAnalyticStroke(rim, blurScratchMatrix);
        EndBlur(c);
        colors[0] = Color(255, 255, 255, .16); colors[1] = Color(255, 255, 255, .04); colors[2] = Color(255, 255, 255, 0);
        stops[0] = 0; stops[1] = .6f; stops[2] = 1;
        Gradient(p.X, p.Y, p.X + p.W * .4, p.Y + p.H * .55, 3, alpha);
        path.Rewind(); path.MoveTo((float)p.X, (float)p.Y); path.LineTo((float)(p.X + p.W), (float)p.Y);
        path.LineTo((float)(p.X + p.W), (float)(p.Y + p.H * .12));
        path.CubicTo((float)(p.X + p.W * .6), (float)(p.Y + p.H * .3), (float)(p.X + p.W * .3), (float)(p.Y + p.H * .36), (float)p.X, (float)(p.Y + p.H * .56));
        path.Close(); c.DrawPath(path, paint); c.Restore();
        var line = Math.Max(.75, size * .003);
        Solid(light ? Color(0, 20, 40, .4) : Color(255, 255, 255, .18), alpha, stroke: line);
        Squircle(p.X + line / 2, p.Y + line / 2, p.W - line, p.H - line, p.R);
        c.DrawPath(path, paint);
    }

    private void Solid(SKColorF color, double alpha = 1, SKBlendMode blend = SKBlendMode.SrcOver, double stroke = 0, bool round = false)
    {
        Native.sk_paint_set_shader(paint.Handle, 0);
        paint.IsDither = false;
        paint.ColorF = color.WithAlpha(color.Alpha * (float)alpha);
        Style(blend, stroke, round);
    }

    private void Gradient(double x0, double y0, double x1, double y1, int count, double alpha = 1,
        SKBlendMode blend = SKBlendMode.SrcOver, double stroke = 0, bool round = false)
    {
        points[0] = new SKPoint((float)x0, (float)y0); points[1] = new SKPoint((float)x1, (float)y1);
        var shader = Native.sk_shader_new_linear_gradient_color4f(points, colors, 0, stops, count, SKShaderTileMode.Clamp, 0);
        if (shader == 0) throw new InvalidOperationException("Aurora gradient creation failed.");
        Native.sk_paint_set_shader(paint.Handle, shader);
        Native.sk_shader_unref(shader);
        paint.ColorF = new SKColorF(1, 1, 1, (float)alpha);
        paint.IsDither = true;
        Style(blend, stroke, round);
    }

    private void Style(SKBlendMode blend, double stroke, bool round)
    {
        paint.BlendMode = blend;
        paint.Style = stroke > 0 ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
        paint.StrokeWidth = (float)stroke;
        paint.StrokeCap = round ? SKStrokeCap.Round : SKStrokeCap.Butt;
        paint.StrokeJoin = SKStrokeJoin.Miter;
    }

    // Skia 119's CPU scan converter selects its old 4x4 AA for point-dense small
    // paths. Chromium's newer Skia always uses analytic AA. A move with no edge
    // preserves the filled geometry while bypassing 119's bounds/point heuristic.
    // Its analytic RLE blitter also needs the original round-out device bounds:
    // the small-path mask blitter normally supplies that confinement itself.
    private void DrawAnalyticPath(nint canvas, SKMatrix matrix, Box? smallCurtainBounds = null, SKPath? filledStroke = null)
    {
        var drawingPath = filledStroke ?? path;
        // Transform the controls themselves, as SkDraw does. Mapping only their
        // enclosing rectangle overestimates the heuristic's bounds under rotation/skew.
        drawingPath.Transform(matrix, deviceBoundsPath);
        var bounds = deviceBoundsPath.Bounds;
        if (smallCurtainBounds is null && deviceBoundsPath.PointCount < Math.Max(bounds.Width, bounds.Height) / 2 - 10)
        {
            Native.sk_canvas_draw_path(canvas, drawingPath.Handle, paint.Handle);
            return;
        }
        if (smallCurtainBounds is { } p)
            bounds = matrix.MapRect(SKRect.Create((float)p.X, (float)p.Y, (float)p.W, (float)p.H));
        var saved = Native.sk_canvas_save(canvas);
        try
        {
            ConfineToDeviceBounds(canvas, matrix, bounds);
            AddAnalyticContour(drawingPath, matrix, bounds, paint.Style == SKPaintStyle.Stroke);
            Native.sk_canvas_draw_path(canvas, drawingPath.Handle, paint.Handle);
        }
        finally { Native.sk_canvas_restore_to_count(canvas, saved); }
    }

    private void DrawAnalyticStroke(nint canvas, SKMatrix matrix)
    {
        // Preserve Skia's dedicated hairline rasterizer. Wider strokes become
        // the same filled outline SkDraw builds, with its real transform/precision.
        var width = paint.StrokeWidth;
        var xLength = FastLength(width * matrix.ScaleX, width * matrix.SkewY);
        var yLength = FastLength(width * matrix.SkewX, width * matrix.ScaleY);
        var affine = matrix.Persp0 == 0 && matrix.Persp1 == 0 && matrix.Persp2 == 1;
        var hairline = affine && xLength <= 1 && yLength <= 1;
        if (hairline || !paint.GetFillPath(path, strokePath, matrix))
        {
            Native.sk_canvas_draw_path(canvas, path.Handle, paint.Handle);
            return;
        }
        paint.Style = SKPaintStyle.Fill;
        try { DrawAnalyticPath(canvas, matrix, filledStroke: strokePath); }
        finally { paint.Style = SKPaintStyle.Stroke; }
    }

    // SkDrawTreatAAStrokeAsHairline's conservative length estimate, on both axes.
    private static float FastLength(float x, float y) => Math.Max(Math.Abs(x), Math.Abs(y)) + .5f * Math.Min(Math.Abs(x), Math.Abs(y));

    private void ClipAnalyticPath(SKCanvas canvas)
    {
        var matrix = canvas.TotalMatrix;
        path.Transform(matrix, deviceBoundsPath);
        var bounds = deviceBoundsPath.Bounds;
        if (deviceBoundsPath.PointCount >= Math.Max(bounds.Width, bounds.Height) / 2 - 10)
        {
            ConfineToDeviceBounds(canvas.Handle, matrix, bounds);
            AddAnalyticContour(path, matrix, bounds, false);
        }
        canvas.ClipPath(path, SKClipOperation.Intersect, true);
    }

    private static void ConfineToDeviceBounds(nint canvas, SKMatrix matrix, SKRect bounds)
    {
        var rounded = new SKRect(MathF.Floor(bounds.Left), MathF.Floor(bounds.Top), MathF.Ceiling(bounds.Right), MathF.Ceiling(bounds.Bottom));
        SetMatrix(canvas, SKMatrix.Identity);
        Native.sk_canvas_clip_rect_with_operation(canvas, in rounded, SKClipOperation.Intersect, false);
        SetMatrix(canvas, matrix);
    }

    private static void AddAnalyticContour(SKPath drawingPath, SKMatrix matrix, SKRect bounds, bool stroke)
    {
        if (!matrix.TryInvert(out var inverse)) return;
        var outside = inverse.MapPoint(bounds.Right + 1024, bounds.Bottom + 1024);
        drawingPath.MoveTo(outside);
        // The stroker discards an isolated move. This closed zero-length contour
        // is outside the original device bounds, so its cap cannot draw a pixel.
        if (stroke) { drawingPath.LineTo(outside); drawingPath.Close(); }
    }

    // IconKit.blur rasterizes into two pooled HTML canvases (initially 300 by 150),
    // filters at scratch resolution, then samples the requested padded source rectangle.
    private nint BeginBlur(SKCanvas c, double radius, SKBlendMode blend, double alpha = 1) =>
        BeginBlur(c.TotalMatrix, radius, blend, alpha);

    private nint BeginBlur(SKMatrix matrix, double radius, SKBlendMode blend, double alpha = 1)
    {
        var scale = Math.Sqrt((double)matrix.ScaleX * matrix.ScaleX + (double)matrix.SkewY * matrix.SkewY);
        if (scale == 0) scale = 1; // IconKit.scaleOf: Math.hypot(...) || 1.
        var rd = radius * scale;
        var k = Math.Clamp(rd / 2.5, 1, 8);
        var width = (int)(Math.Max(1, Math.Ceiling(deviceSize.Width / k)) + 4);
        var height = (int)(Math.Max(1, Math.Ceiling(deviceSize.Height / k)) + 4);
        blurBounds = SKRect.Create(width, height);
        blurTransform = new SKMatrix((float)k, 0, (float)(-2 * k), 0, (float)k, (float)(-2 * k), 0, 0, 1);
        blurOrigin = matrix; blurBlend = blend; blurAlpha = alpha;
        // CSS filter blur() specifies sigma directly; shadowBlur alone uses radius / 2.
        blurSigma = (float)Math.Round(rd / k, 2, MidpointRounding.AwayFromZero);
        scratch.Ensure(width, height, true); filteredScratch.Ensure(width, height, true);
        scratch.Clear(); filteredScratch.Clear();
        var scratchMatrix = new SKMatrix((float)(matrix.ScaleX / k), (float)(matrix.SkewX / k), (float)(matrix.TransX / k + 2),
            (float)(matrix.SkewY / k), (float)(matrix.ScaleY / k), (float)(matrix.TransY / k + 2), 0, 0, 1);
        blurScratchMatrix = scratchMatrix;
        SetMatrix(scratch.Canvas, scratchMatrix);
        return scratch.Canvas;
    }

    private void EndBlur(SKCanvas c) => EndBlur(c.Handle);

    private void EndBlur(nint canvas)
    {
        var image = Snapshot(scratch.Surface);
        nint filtered = 0;
        try
        {
            var filter = Native.sk_imagefilter_new_blur(blurSigma, blurSigma, SKShaderTileMode.Decal, 0, 0);
            if (filter == 0) throw new InvalidOperationException("Aurora blur creation failed.");
            Native.sk_paint_set_imagefilter(layerPaint.Handle, filter);
            Native.sk_imagefilter_unref(filter);
            layerPaint.Color = SKColors.White; layerPaint.BlendMode = SKBlendMode.SrcOver;
            var sampling = new SKSamplingOptions(SKFilterMode.Linear);
            Native.sk_canvas_draw_image(filteredScratch.Canvas, image, 0, 0, in sampling, layerPaint.Handle);
            Native.sk_paint_set_imagefilter(layerPaint.Handle, 0);
            filtered = Snapshot(filteredScratch.Surface);
            layerPaint.Color = SKColors.White.WithAlpha(AlphaByte(blurAlpha)); layerPaint.BlendMode = blurBlend;
            sampling = new SKSamplingOptions(SKCubicResampler.CatmullRom);
            var dest = blurTransform.MapRect(blurBounds);
            SetMatrix(canvas, SKMatrix.Identity);
            // The Skia C shim uses Chrome's fast source-rectangle constraint.
            Native.sk_canvas_draw_image_rect(canvas, filtered, in blurBounds, in dest, in sampling, layerPaint.Handle);
        }
        finally
        {
            SetMatrix(canvas, blurOrigin);
            if (filtered != 0) Native.sk_image_unref(filtered);
            Native.sk_image_unref(image);
            Native.sk_paint_set_imagefilter(layerPaint.Handle, 0);
        }
        // The module pushes tmp followed by the filtered canvas, reversing their roles next call.
        (scratch, filteredScratch) = (filteredScratch, scratch);
    }

    private static nint Snapshot(nint surface)
    {
        var image = Native.sk_surface_new_image_snapshot(surface);
        return image != 0 ? image : throw new InvalidOperationException("Aurora scratch snapshot failed.");
    }

    private static void SetMatrix(nint canvas, SKMatrix matrix)
    {
        var matrix44 = new SKMatrix44(matrix);
        Native.sk_canvas_set_matrix(canvas, in matrix44);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct ImageInfo(nint ColorSpace, int Width, int Height, int ColorType, SKAlphaType AlphaType);

    private struct Raster
    {
        public nint Surface;
        public nint Canvas;
        private int width;
        private int height;
        public void Ensure(int w, int h, bool pool)
        {
            if (pool) { w = Math.Max(Math.Max(width, 300), w); h = Math.Max(Math.Max(height, 150), h); }
            if (Surface != 0 && width == w && height == h) return;
            // Chromium's CPU Canvas 2D uses native N32 (BGRA on Windows/Linux).
            // The RGBA raster blitters in Skia 119 round gradients and image alpha differently.
            var info = new ImageInfo(0, w, h, 6, SKAlphaType.Premul);
            var surface = Native.sk_surface_new_raster(in info, 0, 0);
            if (surface == 0) throw new InvalidOperationException("Aurora scratch allocation failed.");
            Dispose(); Surface = surface; Canvas = Native.sk_surface_get_canvas(surface); width = w; height = h;
        }
        public readonly void Clear()
        {
            SetMatrix(Canvas, SKMatrix.Identity);
            Native.sk_canvas_clear(Canvas, 0);
        }
        public void Dispose()
        {
            if (Surface != 0) Native.sk_surface_unref(Surface);
            Surface = 0; Canvas = 0;
        }
    }

    private void Squircle(double x, double y, double w, double h, double r)
    {
        r = Math.Min(r, Math.Min(w / 2, h / 2));
        var ex = Math.Min(r * 1.33, w / 2); var ey = Math.Min(r * 1.33, h / 2);
        const double pull = (1 - .572) * .6;
        path.Rewind(); path.MoveTo((float)(x + ex), (float)y); path.LineTo((float)(x + w - ex), (float)y);
        path.CubicTo((float)(x + w - ex * pull), (float)y, (float)(x + w), (float)(y + ey * pull), (float)(x + w), (float)(y + ey));
        path.LineTo((float)(x + w), (float)(y + h - ey));
        path.CubicTo((float)(x + w), (float)(y + h - ey * pull), (float)(x + w - ex * pull), (float)(y + h), (float)(x + w - ex), (float)(y + h));
        path.LineTo((float)(x + ex), (float)(y + h));
        path.CubicTo((float)(x + ex * pull), (float)(y + h), (float)x, (float)(y + h - ey * pull), (float)x, (float)(y + h - ey));
        path.LineTo((float)x, (float)(y + ey));
        path.CubicTo((float)x, (float)(y + ey * pull), (float)(x + ex * pull), (float)y, (float)(x + ex), (float)y); path.Close();
    }

    private void FootPath(Box p, double phase, double sway, double dy, int n, double xOffset, double xScale, double end)
    {
        path.Rewind();
        for (var i = 0; i <= n * end; i++)
        {
            var u = i / (double)n;
            var x = (float)(p.X + p.W * (xOffset + xScale * u)); var y = (float)(Foot(p, u, phase, sway) + dy);
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
    }

    private static double Foot(Box p, double u, double phase, double sway) =>
        p.Y + p.H * (.6 - .12 * Math.Sin(Math.PI * 1.5 * u + .35) + .045 * Math.Sin(Math.PI * 3.6 * u + 1.1) + sway * .025 * Math.Sin(2 * Math.PI * u * 1.5 + phase));
    private static double Seg(double p, double a, double b) => Math.Clamp((p - a) / (b - a), 0, 1);
    private static double Out(double u) => 1 - Math.Pow(1 - u, 3);
    private static double InOut(double u) => u < .5 ? 4 * u * u * u : 1 - Math.Pow(-2 * u + 2, 3) / 2;
    private static double Fixed3(double value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static byte AlphaByte(double alpha) => (byte)Math.Clamp((int)MathF.Round((float)alpha * 255, MidpointRounding.AwayFromZero), 0, 255);
    private static SKColorF Color(byte r, byte g, byte b, double alpha) => new(r / 255f, g / 255f, b / 255f, (float)alpha);
    private static SKColorF Opaque(uint color) => (SKColorF)(SKColor)color;

    private static double Random(ref uint s)
    {
        // IconKit.rng uses SIGNED >> 17 (not the conventional xorshift logical shift).
        unchecked { s ^= s << 13; s ^= (uint)((int)s >> 17); s ^= s << 5; }
        return s / 4294967296.0;
    }
    private static Ray[] MakeRays()
    {
        uint seed = 42; var result = new Ray[200];
        for (var i = 0; i < result.Length; i++)
        {
            var q = Random(ref seed);
            result[i] = new Ray(Random(ref seed), .14 + .34 * q * q + .06 * Random(ref seed), .2 + .8 * Random(ref seed), .35 + .8 * Random(ref seed), Random(ref seed));
        }
        Array.Sort(result, (a, b) => a.U.CompareTo(b.U)); return result;
    }
    private static Star[] MakeStars()
    {
        uint seed = 8; var result = new Star[46];
        for (var i = 0; i < result.Length; i++) result[i] = new Star(Random(ref seed), Random(ref seed) * .62, .3 + Random(ref seed) * .7, Random(ref seed));
        return result;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        scratch.Dispose(); filteredScratch.Dispose(); nightRaster.Dispose();
        if (nightImage != 0) Native.sk_image_unref(nightImage);
        path.Dispose(); strokePath.Dispose(); deviceBoundsPath.Dispose(); paint.Dispose(); layerPaint.Dispose();
    }

    private static class Native
    {
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_surface_new_raster(in ImageInfo info, nint rowBytes, nint props);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_surface_get_canvas(nint surface);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_surface_new_image_snapshot(nint surface);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_surface_unref(nint surface);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_image_unref(nint image);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_clear(nint canvas, uint color);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_image(nint canvas, nint image, float x, float y, in SKSamplingOptions sampling, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_image_rect(nint canvas, nint image, in SKRect source, in SKRect dest, in SKSamplingOptions sampling, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_set_matrix(nint canvas, in SKMatrix44 matrix);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int sk_canvas_save(nint canvas);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_restore_to_count(nint canvas, int count);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_clip_rect_with_operation(nint canvas, in SKRect rect, SKClipOperation operation, [MarshalAs(UnmanagedType.I1)] bool antialias);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_path(nint canvas, nint path, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_canvas_draw_rect(nint canvas, in SKRect rect, nint paint);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_imagefilter_new_blur(float sigmaX, float sigmaY, SKShaderTileMode mode, nint input, nint crop);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_imagefilter_unref(nint filter);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_paint_set_imagefilter(nint paint, nint filter);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern nint sk_shader_new_linear_gradient_color4f([In] SKPoint[] points, [In] SKColorF[] colors, nint colorSpace, [In] float[] positions, int count, SKShaderTileMode mode, nint matrix);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_paint_set_shader(nint paint, nint shader);
        [DllImport("libSkiaSharp", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void sk_shader_unref(nint shader);
    }
}
