// PORT-ONLY, T33 / OD-0121: .vpill::before composites its backgrounds before its .55 layer opacity.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

internal sealed class VoicePillHighlights(Rect box, Color highlight) : ICustomDrawOperation
{
    public Rect Bounds => box;
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        var rect = new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
        using var clip = new SKRoundRect(rect, rect.Height / 2, rect.Height / 2);
        using var opacity = new SKPaint { ColorF = new SKColorF(1f, 1f, 1f, (float)(.55 * Math.Clamp(lease.CurrentOpacity, 0, 1))) };
        var saved = canvas.Save();
        try
        {
            canvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
            canvas.SaveLayer(rect, opacity);
            var white = Color.FromArgb(0x1A, 255, 255, 255);
            Ellipse(canvas, rect, rect.Right, rect.MidY, 14, .70f * rect.Height, white, 1);
            Ellipse(canvas, rect, rect.Left, rect.MidY, 14, .70f * rect.Height, white, 1);
            Ellipse(canvas, rect, rect.MidX, rect.Top, .70f * rect.Width, .55f * rect.Height, highlight, .70f);
        }
        finally { canvas.RestoreToCount(saved); }
    }

    private static void Ellipse(SKCanvas canvas, SKRect rect, float cx, float cy, float rx, float ry, Color color, float stop)
    {
        using var shader = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            [new SKColor(color.R, color.G, color.B, color.A), new SKColor(color.R, color.G, color.B, 0)], [0, stop], SKShaderTileMode.Clamp);
        using var paint = new SKPaint { IsAntialias = false, Shader = shader };
        var saved = canvas.Save();
        try
        {
            canvas.Translate(cx, cy);
            canvas.Scale(rx, ry);
            canvas.DrawRect(new SKRect((rect.Left - cx) / rx, (rect.Top - cy) / ry, (rect.Right - cx) / rx, (rect.Bottom - cy) / ry), paint);
        }
        finally { canvas.RestoreToCount(saved); }
    }
}
