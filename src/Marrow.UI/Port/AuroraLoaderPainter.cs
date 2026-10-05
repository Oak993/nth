using SkiaSharp;

namespace Marrow.UI.Port;

// PORT-ONLY: OD-0113 loaders 2/4/5, loading-aurora.html:781-837 and CSS .sk/.sh.
// Paints, paths and gradient resources are owned for the painter lifetime, never allocated per frame.
public sealed class AuroraLoaderPainter : IDisposable
{
    private readonly SKPaint paint = new() { IsAntialias = true, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint layer = new();
    private readonly SKPath path = new();
    private readonly SKRoundRect clip = new();
    private double clipWidth, clipHeight;
    private readonly SKShader[] curtains = [Curtain(true), Curtain(false)];
    private readonly SKShader[] sheens = [Sheen(true), Sheen(false)];
    private readonly SKShader?[] sizedSheens = new SKShader?[2];
    private static readonly double[] BarWidths = [.92, .78, .86, .54, .70];

    private static SKShader Curtain(bool dark) => SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(0, -1),
        [FloatAlpha(AuroraLoaderMotion.Stop(dark, 0), .95f), FloatAlpha(AuroraLoaderMotion.Stop(dark, 1), .4f),
            FloatAlpha(AuroraLoaderMotion.Stop(dark, 2), 0)], null, [0, .45f, 1], SKShaderTileMode.Clamp);

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
        canvas.SaveLayer(layer);
        paint.Shader = null;
        paint.BlendMode = dark && kind != AuroraLoaderKind.AuroraSheen ? SKBlendMode.Plus : SKBlendMode.SrcOver;
        var time = reduceMotion ? .84 : seconds; // page: still pose at .35 * 2.4 s
        switch (kind)
        {
            case AuroraLoaderKind.CurtainFold: DrawCurtain(canvas, width, height, time, dark); break;
            case AuroraLoaderKind.FlowArc: DrawArc(canvas, width, height, time, dark); break;
            case AuroraLoaderKind.AuroraSheen: DrawSheen(canvas, width, height, seconds, dark, reduceMotion); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind));
        }
        canvas.Restore();
    }

    private void DrawCurtain(SKCanvas canvas, double width, double height, double time, bool dark)
    {
        var size = Math.Min(width, height);
        canvas.Translate((float)((width - size) / 2), (float)((height - size) / 2));
        paint.Style = SKPaintStyle.Fill;
        for (var i = 0; i < 24; i++)
        {
            var ray = AuroraLoaderMotion.CurtainRay(size, time, i);
            canvas.Save();
            canvas.Translate((float)ray.X, (float)ray.Foot);
            canvas.Scale(1, (float)ray.Length);
            paint.Shader = curtains[dark ? 0 : 1];
            Color(SKColors.White, ray.Alpha);
            canvas.DrawRect(new SKRect(-.8f, -1, .8f, 0), paint);
            canvas.Restore();
            paint.Shader = null;
            Color(AuroraLoaderMotion.Stop(dark, 0), Math.Round(.5 * ray.Alpha, 3));
            canvas.DrawRect(new SKRect((float)ray.X - .8f, (float)ray.Foot, (float)ray.X + .8f, (float)(ray.Foot + size * .03)), paint);
        }
    }

    private void DrawArc(SKCanvas canvas, double width, double height, double time, bool dark)
    {
        var size = Math.Min(width, height);
        float cx = (float)(width / 2), cy = (float)(height / 2), radius = (float)(size * .36);
        var oval = new SKRect(cx - radius, cy - radius, cx + radius, cy + radius);
        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = 1;
        paint.BlendMode = SKBlendMode.SrcOver;
        Color(dark ? SKColors.White : new(20, 30, 50), dark ? .08 : .1);
        canvas.DrawOval(oval, paint);
        paint.StrokeWidth = 1.4f;
        var angle = AuroraLoaderMotion.ArcAngle(time);
        for (var j = 0; j < 44; j++)
        {
            var a1 = angle - j / 44.0 * 1.75;
            var a0 = angle - (j + 1) / 44.0 * 1.75;
            Color(AuroraLoaderMotion.Palette(dark, j / 44.0 * .8 + time / 2.8), Math.Round(Math.Pow(1 - j / 44.0, .8), 3));
            path.Rewind();
            path.AddArc(oval, (float)(a0 * 180 / Math.PI), (float)((a1 + .02 - a0) * 180 / Math.PI));
            canvas.DrawPath(path, paint);
        }
    }

    private void DrawSheen(SKCanvas canvas, double width, double height, double seconds, bool dark, bool reduceMotion)
    {
        paint.Style = SKPaintStyle.Fill;
        paint.BlendMode = SKBlendMode.SrcOver;
        canvas.Save();
        if (clipWidth != width || clipHeight != height)
        {
            clipWidth = width; clipHeight = height;
            clip.SetRect(new SKRect(0, 0, (float)width, (float)height), 10, 10);
            var span = width * Math.Cos(Math.PI / 18) + height * Math.Sin(Math.PI / 18);
            var matrix = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(10), SKMatrix.CreateScale((float)span, 1));
            for (var i = 0; i < 2; i++) { sizedSheens[i]?.Dispose(); sizedSheens[i] = sheens[i].WithLocalMatrix(matrix); }
        }
        canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
        Color(dark ? new(35, 43, 58) : new(231, 236, 244)); // CSS --bubble, inside the placeholder's outer radius too
        // Chrome's CSS background paint snaps the layout box before the transform (fixture evidence in README).
        for (var i = 0; i < 5; i++) canvas.DrawRoundRect(new SKRect(0, i * 19, (float)Math.Round(width * BarWidths[i]), i * 19 + 12), 6, 6, paint);
        var shift = reduceMotion ? 0 : AuroraLoaderMotion.SheenShift(seconds) * width;
        // CSS translateX moves the whole gradient box; its empty edges do not paint outside that box.
        canvas.ClipRect(new SKRect((float)shift, 0, (float)(shift + width), (float)height));
        canvas.Translate((float)(width / 2 + shift), (float)(height / 2));
        // CSS linear-gradient(100deg): (sin(100), -cos(100)); the matrix is baked on resize, not per frame.
        paint.Shader = sizedSheens[dark ? 0 : 1]; Color(SKColors.White);
        canvas.DrawPaint(paint);
        paint.Shader = null;
        canvas.Restore();
    }

    public void Dispose()
    {
        paint.Dispose(); layer.Dispose(); path.Dispose(); clip.Dispose();
        foreach (var shader in curtains) shader.Dispose();
        foreach (var shader in sheens) shader.Dispose();
        foreach (var shader in sizedSheens) shader?.Dispose();
    }
}
