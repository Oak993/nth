// PORT-ONLY, owner-ruled addition (OD-0139; ED-NEW-lightv2-6): the wallpaper's veil near the composer, drawn by the light's
// handler before the light (over the wallpaper and what the window drew on it, under the light and the composer's glass): the
// composer's rounded rectangle grown by a quarter of the light's spread, blurred by a third of it (Skia blurs a rounded
// rectangle analytically: one draw, no offscreen pass), so it fades out over about the spread with no edge. Its measure reads
// the window's pixels under the composer once, scaled down to a 48 x 16 grid on the GPU, before anything of the light is drawn.
using Marrow.Core.Models.ComposerLights;
using SkiaSharp;

namespace Marrow.UI.Port.ComposerLights;

/// <summary>Draws the veil and measures the wallpaper under the composer.</summary>
public sealed class WallpaperDimVeil : IDisposable
{
    private static readonly SKColor Ink = new(8, 8, 10); // the v4 page's halo ink
    private readonly SKPaint paint = new() { IsAntialias = true, Color = Ink };
    private SKBitmap? grid;
    private bool disposed;

    /// <summary>How far the veil reaches past the composer (px) for a light of <paramref name="spread"/>.</summary>
    public static double Reach(double spread) => spread * (.25 + 3 * Sigma(1)) + 2;

    private static double Sigma(double spread) => spread / 3;

    /// <summary>The veil over a composer of <paramref name="w"/> x <paramref name="h"/> (its top left at the origin) at <paramref name="alpha"/>.</summary>
    public void Draw(SKCanvas canvas, double w, double h, double radius, double spread, double alpha)
    {
        if (disposed || alpha <= .002 || !(w > 0) || !(h > 0)) return;
        var grow = (float)(spread * .25);
        var r = (float)Math.Min(radius, Math.Min(w, h) / 2) + grow;
        using var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Max(.5, Sigma(spread)));
        paint.MaskFilter = blur;
        paint.Color = Ink.WithAlpha((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255));
        canvas.DrawRoundRect(new SKRect(-grow, -grow, (float)w + grow, (float)h + grow), r, r, paint);
        paint.MaskFilter = null;
    }

    /// <summary>
    /// The mean luminance and busyness of the window's pixels under the composer (<paramref name="w"/> x <paramref name="h"/>
    /// at the canvas's origin), or null when they are not all this frame's (outside the canvas's clip) or there is no surface.
    /// </summary>
    public (double Luminance, double Busyness)? Measure(SKCanvas canvas, SKSurface? surface, GRContext? gpu, double w, double h)
    {
        if (disposed || surface is null || !(w > 0) || !(h > 0)) return null;
        var at = SKRectI.Ceiling(canvas.TotalMatrix.MapRect(new SKRect(0, 0, (float)w, (float)h)), true);
        var clip = canvas.DeviceClipBounds;
        if (at.IsEmpty || !clip.Contains(at)) return null;
        using var image = surface.Snapshot(at);
        if (image is null) return null;
        return Measure(image, gpu);
    }

    /// <summary>The measure of an image (scaled down to the grid; on <paramref name="gpu"/> when given, so only the grid is read back).</summary>
    public (double Luminance, double Busyness) Measure(SKImage image, GRContext? gpu)
    {
        var info = new SKImageInfo(WallpaperDim.GridWidth, WallpaperDim.GridHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var small = (gpu is not null ? SKSurface.Create(gpu, false, info) : null) ?? SKSurface.Create(info);
        using var copy = new SKPaint { BlendMode = SKBlendMode.Src };
        small.Canvas.DrawImage(image, new SKRect(0, 0, info.Width, info.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None), copy);
        grid ??= new SKBitmap(info);
        if (!small.ReadPixels(info, grid.GetPixels(), grid.RowBytes, 0, 0)) return (0, 0);
        return WallpaperDim.Measure(grid.GetPixelSpan(), info.Width, info.Height);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        paint.Dispose();
        grid?.Dispose();
    }
}
