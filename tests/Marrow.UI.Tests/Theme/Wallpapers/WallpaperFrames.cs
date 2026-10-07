using Marrow.UI.Theme.Wallpapers;
using SkiaSharp;

namespace Marrow.UI.Tests.Theme.Wallpapers;

/// <summary>CPU-raster frames of a renderer, RGBA8888 bytes, at the small sizes the wallpaper proofs use.</summary>
internal static class WallpaperFrames
{
    public static byte[] Draw(WallpaperRenderer r, int width = 32, int height = 20)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        r.Draw(surface.Canvas, width, height);
        return Bytes(surface);
    }

    public static byte[] At(WallpaperRenderer r, double time, int width = 32, int height = 20)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        r.DrawFrameAt(surface.Canvas, width, height, time);
        return Bytes(surface);
    }

    public static int Diff(byte[] a, byte[] b) => a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    private static byte[] Bytes(SKSurface surface)
    {
        using var image = surface.Snapshot(); using var pixels = SKBitmap.FromImage(image);
        return pixels.Bytes;
    }
}
