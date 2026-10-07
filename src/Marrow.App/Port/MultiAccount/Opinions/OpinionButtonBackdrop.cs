// PORT-ONLY: CSS backdrop-filter blur(14px) saturate(160%), .btn :1308. CSS blur radius = Skia sigma (filter gaussian stdDeviation).
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;
namespace Marrow.App.Port.MultiAccount.Opinions;
internal sealed class OpinionButtonBackdrop(Rect bounds) : ICustomDrawOperation
{
    public Rect Bounds => bounds;
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }
    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease(); if (lease.SkSurface is not { } surface) return;
        var canvas = lease.SkCanvas; var matrix = canvas.TotalMatrix;
        var scale = MathF.Sqrt(matrix.ScaleX * matrix.ScaleX + matrix.SkewY * matrix.SkewY);
        var local = new SKRect(0, 0, (float)bounds.Width, (float)bounds.Height);
        var crop = matrix.MapRect(local); crop.Inflate(42 * scale, 42 * scale);
        using var whole = surface.Snapshot();
        var source = SKRectI.Ceiling(crop); source.Intersect(new SKRectI(0, 0, whole.Width, whole.Height));
        if (source.IsEmpty) return;
        using var image = surface.Snapshot(source); if (image is null) return;
        using var blur = SKImageFilter.CreateBlur(14 * scale, 14 * scale, SKShaderTileMode.Clamp);
        const float saturation = 1.6f; const float r = .213f, g = .715f, b = .072f; const float inverse = 1 - saturation;
        using var color = SKColorFilter.CreateColorMatrix([r * inverse + saturation, g * inverse, b * inverse, 0, 0,
            r * inverse, g * inverse + saturation, b * inverse, 0, 0, r * inverse, g * inverse, b * inverse + saturation, 0, 0, 0, 0, 0, 1, 0]);
        using var paint = new SKPaint { ImageFilter = blur, ColorFilter = color, Color = SKColors.White.WithAlpha((byte)Math.Round(255 * lease.CurrentOpacity)) };
        canvas.Save(); canvas.ClipRoundRect(new SKRoundRect(local, 13), SKClipOperation.Intersect, true);
        canvas.ResetMatrix(); canvas.DrawImage(image, source.Left, source.Top, paint); canvas.Restore();
    }
}
