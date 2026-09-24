using System.IO;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.UI.Markdown;
using Marrow.UI.Tests.Fixtures;
using Xunit;

namespace Marrow.UI.Tests.Markdown;

// Source under test: DroppyCode/UI/Markdown/MarkdownView.swift (B2 1fbf81e), FaviconCache's image pipeline:
// PR-THEME-138 (cleaned/removeWhitePlate, :955-1048) and PR-THEME-139/resizedIcon (:1084-1098). Reflects into
// the private members the same way RichLinkAutolinkTests.cs does; a headless session is required because the
// pipeline draws through Avalonia's rendering surface (RenderTargetBitmap.CreateDrawingContext).
[Collection("Headless")]
public sealed class FaviconCacheImagePipelineTests(HeadlessSession headless)
{
    private static byte[] EncodePng(Avalonia.PixelSize size, byte[,,] rgba)
    {
        var bitmap = new WriteableBitmap(size, new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var locked = bitmap.Lock())
        {
            var pixels = new byte[locked.RowBytes * size.Height];
            for (var y = 0; y < size.Height; y++)
            for (var x = 0; x < size.Width; x++)
            {
                var i = y * locked.RowBytes + x * 4;
                pixels[i] = rgba[y, x, 2]; // B
                pixels[i + 1] = rgba[y, x, 1]; // G
                pixels[i + 2] = rgba[y, x, 0]; // R
                pixels[i + 3] = rgba[y, x, 3]; // A
            }
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, locked.Address, pixels.Length);
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }

    private static Avalonia.Media.IImage? Cleaned(byte[] data)
    {
        var method = typeof(FaviconCache).GetMethod("Cleaned", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Avalonia.Media.IImage?)method.Invoke(null, [data]);
    }

    private static (byte R, byte G, byte B, byte A) PixelAt(WriteableBitmap bitmap, int x, int y)
    {
        using var locked = bitmap.Lock();
        var pixels = new byte[locked.RowBytes * bitmap.PixelSize.Height];
        System.Runtime.InteropServices.Marshal.Copy(locked.Address, pixels, 0, pixels.Length);
        var i = y * locked.RowBytes + x * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i], pixels[i + 3]);
    }

    [Fact]
    [Trait("Req", "PR-THEME-138")] // MarkdownView.swift:986-990, :1010-1019, :1030-1032: a white plate touching a
    // border floods to full transparency; a red center pixel is untouched.
    public void Cleaned_removes_a_white_border_plate_and_keeps_an_opaque_center_pixel()
    {
        headless.OnUiThread(() =>
        {
            const int n = 6;
            var rgba = new byte[n, n, 4];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                rgba[y, x, 0] = 255; rgba[y, x, 1] = 255; rgba[y, x, 2] = 255; rgba[y, x, 3] = 255; // white plate
            }
            // A solid red 2x2 center, fully opaque, isolated from every border by at least one white pixel.
            for (var y = 2; y <= 3; y++)
            for (var x = 2; x <= 3; x++)
            {
                rgba[y, x, 0] = 255; rgba[y, x, 1] = 0; rgba[y, x, 2] = 0; rgba[y, x, 3] = 255;
            }
            var png = EncodePng(new Avalonia.PixelSize(n, n), rgba);

            var cleanedObj = Cleaned(png);
            var cleaned = Assert.IsType<WriteableBitmap>(cleanedObj);

            var corner = PixelAt(cleaned, 0, 0);
            Assert.Equal(0, corner.A); // :1030-1032 plate pixel -> alpha 0

            var center = PixelAt(cleaned, 2, 2);
            Assert.Equal(255, center.A); // not adjacent to a plate pixel across the whole white border: untouched
            Assert.Equal(255, center.R);
            Assert.Equal(0, center.G);
            Assert.Equal(0, center.B);
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-138")] // MarkdownView.swift:957-960: downscaled so neither side exceeds 64 pixels
    public void Cleaned_downscales_so_the_long_side_is_at_most_64()
    {
        headless.OnUiThread(() =>
        {
            const int n = 100;
            var rgba = new byte[n, n, 4];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                rgba[y, x, 0] = 10; rgba[y, x, 1] = 20; rgba[y, x, 2] = 30; rgba[y, x, 3] = 255;
            }
            var png = EncodePng(new Avalonia.PixelSize(n, n), rgba);

            var cleaned = Assert.IsType<WriteableBitmap>(Cleaned(png));

            Assert.True(cleaned.PixelSize.Width <= 64);
            Assert.True(cleaned.PixelSize.Height <= 64);
            Assert.True(cleaned.PixelSize.Width == 64 || cleaned.PixelSize.Height == 64);
        });
    }

    [Fact]
    [Trait("Req", "PR-THEME-138")] // MarkdownView.swift:1084-1098: rendered at 28x28, declared at 14x14 points
    public void ResizedIcon_is_a_28_pixel_bitmap_declared_at_14_points()
    {
        headless.OnUiThread(() =>
        {
            const int n = 8;
            var rgba = new byte[n, n, 4];
            for (var y = 0; y < n; y++)
            for (var x = 0; x < n; x++)
            {
                rgba[y, x, 0] = 5; rgba[y, x, 1] = 6; rgba[y, x, 2] = 7; rgba[y, x, 3] = 255;
            }
            var png = EncodePng(new Avalonia.PixelSize(n, n), rgba);
            var cleaned = (Avalonia.Media.IImage)Cleaned(png)!;

            var method = typeof(FaviconCache).GetMethod("ResizedIcon", BindingFlags.NonPublic | BindingFlags.Static)!;
            var resized = (Avalonia.Media.IImage)method.Invoke(null, [cleaned])!;
            var bitmap = Assert.IsType<WriteableBitmap>(resized);

            Assert.Equal(28, bitmap.PixelSize.Width);
            Assert.Equal(28, bitmap.PixelSize.Height);
            Assert.Equal(14, bitmap.Size.Width, 3);
            Assert.Equal(14, bitmap.Size.Height, 3);
        });
    }
}
