using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for tests/Marrow.App.Tests/Views/Sidebar/RowGlideTests.cs and RowGlideHeadlessTests.cs
// (DroppyCode/App/Views/Sidebar/RowGlide.swift, B2 1fbf81e).

/// <summary>
/// A row face for the model tests. RowGlide.swift:30-31 stores the faces as `NSImage` (ED-095: Avalonia IImage) and
/// the animator never looks inside them (:63-128), so any IImage value serves; the model tests never draw one, and a
/// draw would be a test bug.
/// </summary>
public sealed class FaceSample(double width, double height) : IImage
{
    public Size Size { get; } = new(width, height);

    public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) =>
        throw new InvalidOperationException("A model test drew a face; only RowGlideHeadlessTests draw faces.");
}

/// <summary>What the headless tests draw and read.</summary>
public static class GlidePixels
{
    /// <summary>A face that is one solid colour at 96 DPI, so one bitmap pixel is one DIP and one frame pixel.</summary>
    public static WriteableBitmap Solid(int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = bitmap.Lock();
        // Premultiplied BGRA, little endian: B, G, R, A in memory.
        var r = (byte)(red * alpha / 255);
        var g = (byte)(green * alpha / 255);
        var b = (byte)(blue * alpha / 255);
        var row = new byte[width * 4];
        for (var x = 0; x < width; x++)
        {
            row[x * 4] = b;
            row[x * 4 + 1] = g;
            row[x * 4 + 2] = r;
            row[x * 4 + 3] = alpha;
        }
        for (var y = 0; y < height; y++) Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        return bitmap;
    }

    /// <summary>The colour of one pixel of a captured frame, as R, G, B.</summary>
    public static (int R, int G, int B) At(WriteableBitmap frame, int x, int y)
    {
        using var buffer = frame.Lock();
        var bytes = new byte[4];
        Marshal.Copy(buffer.Address + y * buffer.RowBytes + x * 4, bytes, 0, 4);
        if (buffer.Format == PixelFormat.Rgba8888) return (bytes[0], bytes[1], bytes[2]);
        if (buffer.Format == PixelFormat.Bgra8888) return (bytes[2], bytes[1], bytes[0]);
        throw new InvalidOperationException($"Unexpected frame format {buffer.Format}.");
    }

    /// <summary>Largest per-channel difference between two colours.</summary>
    public static int Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));
}

/// <summary>
/// RowGlide.swift:76 (`NSWorkspace.shared.accessibilityDisplayShouldReduceMotion`) and :135-136 (NSScreen backing
/// scale factor, ImageRenderer) are the identical three AppKit reads UI/Common/Genie.swift makes; the port reuses
/// that file's seam, Marrow.UI.Port.IGeniePlatform, rather than a RowGlide-specific one. This fake lets a test set
/// Reduce Motion per call and never renders (RowGlideAnimator.Render is not exercised by this part's records).
/// </summary>
public sealed class FakeGeniePlatform : IGeniePlatform
{
    public bool AccessibilityDisplayShouldReduceMotion { get; set; }

    public double? MainScreenBackingScaleFactor => 2;

    public IImage? RenderImage(Control content, double scale) =>
        throw new InvalidOperationException("A RowGlideAnimator model test rendered a face; render() is not covered by this part's records.");
}
