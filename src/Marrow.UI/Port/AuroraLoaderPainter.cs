using SkiaSharp;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113 loaders 2/4/5, loading-aurora.html:781-837 and CSS .sk/.sh.
// Reuses managed resources and buffers. Native immutable gradient/coverage snapshots own their frame values.
public sealed class AuroraLoaderPainter : IDisposable
{
    /// <summary>Bind the pinned compositor lease protocol before the first visible loader draw.</summary>
    public static void PrepareRendering() => AuroraSkiaLeaseAccess.Prepare();
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint layer = new();
    private double clipWidth, clipHeight;
    private readonly AuroraLoaderCoverage coverage = new();
    private readonly AuroraLoaderArcCoverage arcCoverage = new();
    private readonly SKShader[] bubbles = [SKShader.CreateColor(new SKColor(35, 43, 58)), SKShader.CreateColor(new SKColor(231, 236, 244))];
    private readonly SKShader white = SKShader.CreateColor(SKColors.White);
    private readonly SKShader[] sheens = [Sheen(true), Sheen(false)];
    private readonly SKShader?[] sizedSheens = new SKShader?[2];
    private readonly SKPoint[] points = new SKPoint[2];
    private readonly SKColorF[] colors = new SKColorF[3];
    private readonly float[] positions = [0, .45f, 1];
    private static readonly double[] BarWidths = [.92, .78, .86, .54, .70];
    private SKSurface? sheenRaster, sheenSurface;
    private SKCanvas? sheenRasterCanvas, sheenSurfaceCanvas;
    private SKSizeI rasterPixels, surfacePixels;
    private readonly SKColorFilter floatSampling = FloatSampling();
    private static SKColorFilter FloatSampling()
    {
        // Ganesh filters and blends in floating point. An identity SkSL stage selects Skia's highp CPU
        // pipeline too, preserving N32 surface quantization while avoiding the lowp approximate blend.
        using var effect = SKRuntimeEffect.CreateColorFilter("half4 main(half4 color) { return color; }", out var error)
            ?? throw new InvalidOperationException(error);
        return effect.ToColorFilter() ?? throw new InvalidOperationException("The float sampling filter was not created.");
    }

    private static SKShader Sheen(bool dark)
    {
        // CSS gradients interpolate premultiplied sRGB. Skia's public gradient API interpolates unpremultiplied
        // colour, so bind a dense, fixed table of the CSS function once; never recreate a shader during a sweep.
        double[] stops = [0, .22, .38, .5, .62, .78, 1];
        SKColor[] rgb = [AuroraLoaderMotion.Stop(dark, 0), AuroraLoaderMotion.Stop(dark, 0), AuroraLoaderMotion.Stop(dark, 0),
            AuroraLoaderMotion.Stop(dark, 1), dark ? new(150, 90, 255) : new(112, 72, 224),
            dark ? new(150, 90, 255) : new(112, 72, 224), dark ? new(150, 90, 255) : new(112, 72, 224)];
        double[] alphas = [0, 0, .2, dark ? .26 : .24, dark ? .22 : .2, 0, 0];
        var colors = new SKColorF[1001]; var positions = new float[1001];
        for (var i = 0; i <= 1000; i++)
        {
            var u = i / 1000.0; var at = 0;
            while (at < 5 && u > stops[at + 1]) at++;
            var p = (u - stops[at]) / (stops[at + 1] - stops[at]);
            var left = (1 - p) * alphas[at]; var right = p * alphas[at + 1]; var alpha = left + right;
            colors[i] = alpha <= 0 ? new SKColorF(0, 0, 0, 0) : new SKColorF(
                (float)((rgb[at].Red * left + rgb[at + 1].Red * right) / alpha / 255),
                (float)((rgb[at].Green * left + rgb[at + 1].Green * right) / alpha / 255),
                (float)((rgb[at].Blue * left + rgb[at + 1].Blue * right) / alpha / 255), (float)alpha);
            positions[i] = (float)u;
        }
        return SKShader.CreateLinearGradient(new SKPoint(-.5f, 0), new SKPoint(.5f, 0), colors, null, positions, SKShaderTileMode.Clamp);
    }
    private static SKColorF FloatAlpha(SKColor color, float alpha) => new(color.Red / 255f, color.Green / 255f, color.Blue / 255f, alpha);
    private void Color(SKColor color, double alpha = 1) => paint.ColorF = new SKColorF(color.Red / 255f, color.Green / 255f, color.Blue / 255f, (float)alpha);

    /// <summary>Draw just the loader; its host supplies the delay/fade. Reduce Motion includes its opacity breath.</summary>
    public void Draw(SKCanvas canvas, AuroraLoaderKind kind, double width, double height, double seconds, bool dark, bool reduceMotion, double opacity = 1)
    {
        if (width <= 0 || height <= 0 || opacity <= 0) return;
        layer.ColorF = new SKColorF(1, 1, 1, (float)(opacity * (reduceMotion ? AuroraLoaderMotion.Breath(seconds) : 1)));
        var saved = canvas.SaveLayer(layer);
        try
        {
            paint.Shader = null;
            paint.BlendMode = dark && kind != AuroraLoaderKind.AuroraSheen ? SKBlendMode.Plus : SKBlendMode.SrcOver;
            var time = reduceMotion ? .84 : seconds; // page: still pose at .35 * 2.4 s
            switch (kind)
            {
                case AuroraLoaderKind.CurtainFold: DrawCurtain(canvas, width, height, time, dark); break;
                case AuroraLoaderKind.FlowArc: DrawArc(canvas, width, height, time, dark); break;
                case AuroraLoaderKind.AuroraSheen: DrawSheenSurface(canvas, width, height, seconds, dark, reduceMotion); break;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        finally { paint.ColorFilter = null; paint.IsDither = false; canvas.RestoreToCount(saved); }
    }

    private void DrawCurtain(SKCanvas canvas, double width, double height, double time, bool dark)
    {
        var size = Math.Min(width, height);
        canvas.Translate((float)((width - size) / 2), (float)((height - size) / 2));
        paint.Style = SKPaintStyle.Fill;
        for (var i = 0; i < 24; i++)
        {
            var ray = AuroraLoaderMotion.CurtainRay(size, time, i);
            points[0] = new SKPoint((float)ray.X, (float)ray.Foot);
            points[1] = new SKPoint((float)ray.X, (float)(ray.Foot - ray.Length));
            colors[0] = FloatAlpha(AuroraLoaderMotion.Stop(dark, 0), (float)Math.Round(.95 * ray.Alpha, 3));
            colors[1] = FloatAlpha(AuroraLoaderMotion.Stop(dark, 1), (float)Math.Round(.4 * ray.Alpha, 3));
            colors[2] = FloatAlpha(AuroraLoaderMotion.Stop(dark, 2), 0);
            var shader = AuroraLoaderSkia.sk_shader_new_linear_gradient_color4f(points, colors, 0, positions, 3, SKShaderTileMode.Clamp, 0);
            if (shader == 0) throw new InvalidOperationException("The curtain gradient was not created.");
            var rect = new SKRect((float)(ray.X - .8), (float)(ray.Foot - ray.Length), (float)(ray.X + .8), (float)ray.Foot);
            coverage.Bind(canvas, paint, rect, 0, shader);
            AuroraLoaderSkia.sk_shader_unref(shader);
            paint.IsDither = true; Color(SKColors.White);
            AuroraLoaderCoverage.Fill(canvas, paint, rect);
            paint.Shader = null;
            paint.IsDither = false;
            var color = FloatAlpha(AuroraLoaderMotion.Stop(dark, 0), (float)Math.Round(.5 * ray.Alpha, 3));
            shader = AuroraLoaderSkia.sk_shader_new_color4f(in color, 0);
            rect = new SKRect((float)(ray.X - .8), (float)ray.Foot, (float)(ray.X + .8), (float)(ray.Foot + size * .03));
            coverage.Bind(canvas, paint, rect, 0, shader);
            AuroraLoaderSkia.sk_shader_unref(shader);
            AuroraLoaderCoverage.Fill(canvas, paint, rect);
            paint.Shader = null;
        }
    }

    private void DrawArc(SKCanvas canvas, double width, double height, double time, bool dark)
    {
        paint.IsAntialias = true;
        var size = Math.Min(width, height);
        float cx = (float)(width / 2), cy = (float)(height / 2), radius = (float)(size * .36);
        paint.BlendMode = SKBlendMode.SrcOver;
        arcCoverage.Draw(canvas, paint, cx, cy, radius, 1, 0, 0, false, dark ? SKColors.White : new(20, 30, 50), dark ? .08 : .1);
        var angle = AuroraLoaderMotion.ArcAngle(time);
        for (var j = 0; j < 44; j++)
        {
            var a1 = angle - j / 44.0 * 1.75;
            var a0 = angle - (j + 1) / 44.0 * 1.75;
            arcCoverage.Draw(canvas, paint, cx, cy, radius, 1.4f, a0, a1 + .02, true,
                AuroraLoaderMotion.Palette(dark, j / 44.0 * .8 + time / 2.8), Math.Round(Math.Pow(1 - j / 44.0, .8), 3));
        }
    }

    private void DrawSheenSurface(SKCanvas canvas, double width, double height, double seconds, bool dark, bool reduceMotion)
    {
        var matrix = canvas.TotalMatrix;
        var scale = Math.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
        var backing = Math.Ceiling(scale);
        if (scale <= 0 || backing - scale < .000001)
        {
            DrawSheen(canvas, width, height, seconds, dark, reduceMotion);
            return;
        }
        // Blink creates a 2D-scale render surface when the bars and will-change shimmer are composited
        // descendants. EffectTree rounds that surface scale upward. Picture layers raster at their ideal
        // scale, then bilinear sampling into/out of the backing surface accounts for fractional DPI edges.
        var rasterSize = new SKSizeI((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale));
        var surfaceSize = new SKSizeI((int)Math.Ceiling(width * backing), (int)Math.Ceiling(height * backing));
        Ensure(ref sheenRaster, ref sheenRasterCanvas, ref rasterPixels, rasterSize);
        Ensure(ref sheenSurface, ref sheenSurfaceCanvas, ref surfacePixels, surfaceSize);
        var sourceCanvas = sheenRasterCanvas!;
        sourceCanvas.Clear(SKColors.Transparent); sourceCanvas.ResetMatrix(); sourceCanvas.Scale((float)scale);
        sourceCanvas.SaveLayer();
        DrawSheen(sourceCanvas, width, height, seconds, dark, reduceMotion);
        sourceCanvas.Restore();
        var targetCanvas = sheenSurfaceCanvas!;
        targetCanvas.Clear(SKColors.Transparent); targetCanvas.ResetMatrix();
        var rasterImage = AuroraLoaderSkia.sk_surface_new_image_snapshot(sheenRaster!.Handle);
        if (rasterImage == 0) throw new InvalidOperationException("Sheen picture-layer snapshot was not created.");
        var sampling = new SKSamplingOptions(SKFilterMode.Linear);
        var sourceRect = SKRect.Create((float)(width * scale), (float)(height * scale));
        var targetRect = SKRect.Create((float)(width * backing), (float)(height * backing));
        Color(SKColors.White); paint.IsAntialias = false; paint.ColorFilter = floatSampling;
        try { AuroraLoaderSkia.sk_canvas_draw_image_rect(targetCanvas.Handle, rasterImage, in sourceRect, in targetRect, in sampling, paint.Handle); }
        finally { AuroraLoaderSkia.sk_image_unref(rasterImage); }
        var surfaceImage = AuroraLoaderSkia.sk_surface_new_image_snapshot(sheenSurface!.Handle);
        if (surfaceImage == 0) throw new InvalidOperationException("Sheen render-surface snapshot was not created.");
        var dest = SKRect.Create((float)width, (float)height);
        try { AuroraLoaderSkia.sk_canvas_draw_image_rect(canvas.Handle, surfaceImage, in targetRect, in dest, in sampling, paint.Handle); }
        finally { AuroraLoaderSkia.sk_image_unref(surfaceImage); paint.ColorFilter = null; }
    }

    private static void Ensure(ref SKSurface? surface, ref SKCanvas? canvas, ref SKSizeI pixels, SKSizeI size)
    {
        if (surface is not null && pixels == size) return;
        surface?.Dispose();
        surface = SKSurface.Create(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("Sheen raster surface was not created.");
        canvas = surface.Canvas;
        pixels = size;
    }

    private void DrawSheen(SKCanvas canvas, double width, double height, double seconds, bool dark, bool reduceMotion)
    {
        paint.IsAntialias = true;
        paint.Style = SKPaintStyle.Fill;
        paint.BlendMode = SKBlendMode.SrcOver;
        canvas.Save();
        if (clipWidth != width || clipHeight != height)
        {
            clipWidth = width; clipHeight = height;
            var span = width * Math.Cos(Math.PI / 18) + height * Math.Sin(Math.PI / 18);
            var matrix = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(10), SKMatrix.CreateScale((float)span, 1));
            for (var i = 0; i < 2; i++) { sizedSheens[i]?.Dispose(); sizedSheens[i] = sheens[i].WithLocalMatrix(matrix); }
        }
        Color(SKColors.White);
        // Chrome's CSS background paint snaps the layout box before the transform (fixture evidence in README).
        for (var i = 0; i < 5; i++)
        {
            var rect = new SKRect(0, i * 19, (float)Math.Round(width * BarWidths[i]), i * 19 + 12);
            coverage.Bind(canvas, paint, rect, 6, bubbles[dark ? 0 : 1].Handle);
            AuroraLoaderCoverage.Fill(canvas, paint, rect);
        }
        paint.Shader = null;
        canvas.Save();
        var shift = reduceMotion ? 0 : AuroraLoaderMotion.SheenShift(seconds) * width;
        // CSS translateX moves the whole gradient box; its empty edges do not paint outside that box.
        canvas.ClipRect(new SKRect((float)shift, 0, (float)(shift + width), (float)height));
        canvas.Translate((float)(width / 2 + shift), (float)(height / 2));
        // CSS linear-gradient(100deg): (sin(100), -cos(100)); the matrix is baked on resize, not per frame.
        paint.Shader = sizedSheens[dark ? 0 : 1]; Color(SKColors.White); paint.IsDither = true;
        canvas.DrawPaint(paint);
        paint.Shader = null; paint.IsDither = false;
        canvas.Restore();
        // CSS overflow clips the composed bars and shimmer as one layer.
        paint.BlendMode = SKBlendMode.DstIn;
        coverage.Bind(canvas, paint, new SKRect(0, 0, (float)width, (float)height), 10, white.Handle);
        canvas.DrawPaint(paint);
        paint.Shader = null;
        paint.BlendMode = SKBlendMode.SrcOver;
        canvas.Restore();
    }

    public void Dispose()
    {
        paint.Dispose(); layer.Dispose();
        coverage.Dispose();
        arcCoverage.Dispose();
        foreach (var shader in bubbles) shader.Dispose();
        white.Dispose();
        foreach (var shader in sheens) shader.Dispose();
        foreach (var shader in sizedSheens) shader?.Dispose();
        sheenRaster?.Dispose(); sheenSurface?.Dispose();
        floatSampling.Dispose();
    }
}
