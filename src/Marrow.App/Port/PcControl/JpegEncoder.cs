// PORT-ONLY: Tactus, PC control (OD-0140, OD-0141, ED-NEW-pcctl-5, ED-NEW-pcctl-9; DESIGN.md 2.3, 2.12, 3.1). The Skia
// JPEG step behind IPcJpegEncoder: the frame's crop is resized to the model grid (or kept at native pixels), the element
// numbers are drawn on this copy only, and the result is a JPEG with full-resolution colour (4:4:4), so the coloured edges
// of small text stay sharp. The frame's pixels are read in place (pinned); only the crop is copied.
using System.Globalization;
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using SkiaSharp;

namespace Marrow.App.Port.PcControl;

public sealed class JpegEncoder : IPcJpegEncoder
{
    /// <summary>The label's text size, in output pixels: legible to a model at the grid scale, small enough to cover little.</summary>
    public const float LabelTextSize = 12f;

    /// <summary>The label's fill: a strong magenta that reads on light and dark windows alike, with white digits.</summary>
    public static readonly SKColor LabelFill = new(0xD6, 0x14, 0x7A);

    public static JpegEncoder Shared { get; } = new();

    private static readonly Lazy<SKTypeface> face = new(() =>
        SKFontManager.Default.MatchFamily("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default);

    public byte[] Encode(PcFrame frame, PcRect crop, int width, int height, int quality, IReadOnlyList<PcMark> marks)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(width, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(height, 0);
        var clip = PcFrames.Clip(frame, crop);
        if (clip.Width <= 0 || clip.Height <= 0) throw new ArgumentException("The crop has no pixels inside the frame.", nameof(crop));

        var output = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(output);
        using (var canvas = new SKCanvas(bitmap))
        {
            using (var piece = Piece(frame, clip))
            {
                // Native size keeps every pixel as it is; a smaller grid is box-filtered through mipmaps, so thin lines
                // and text average out instead of dropping out (no aliasing at any scale).
                var exact = width == clip.Width && height == clip.Height;
                var sampling = exact ? new SKSamplingOptions(SKFilterMode.Nearest) : new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                canvas.DrawImage(piece, new SKRect(0, 0, clip.Width, clip.Height), new SKRect(0, 0, width, height), sampling);
            }
            if (marks.Count > 0) DrawMarks(canvas, clip, width, height, marks);
        }
        using var pixmap = bitmap.PeekPixels();
        using var data = pixmap.Encode(new SKJpegEncoderOptions(Math.Clamp(quality, 1, 100), SKJpegEncoderDownsample.Downsample444, SKJpegEncoderAlphaOption.Ignore))
            ?? throw new InvalidOperationException("Skia could not encode the JPEG.");
        return data.ToArray();
    }

    /// <summary>The crop as its own raster image (a copy of only those rows), so mipmaps are built for the crop alone.</summary>
    private static SKImage Piece(PcFrame frame, PcRect clip)
    {
        var handle = GCHandle.Alloc(frame.Bgra, GCHandleType.Pinned);
        try
        {
            var start = handle.AddrOfPinnedObject() + (clip.Y * frame.Stride) + (clip.X * 4);
            using var pixmap = new SKPixmap(new SKImageInfo(clip.Width, clip.Height, SKColorType.Bgra8888, SKAlphaType.Opaque), start, frame.Stride);
            return SKImage.FromPixelCopy(pixmap) ?? throw new InvalidOperationException("Skia could not copy the frame.");
        }
        finally
        {
            handle.Free();
        }
    }

    /// <summary>
    /// Each mark inside the crop: a 1 px outline of the element and its number in a small label at the element's top-left
    /// corner, kept inside the image. Marks wholly outside the crop are skipped.
    /// </summary>
    private static void DrawMarks(SKCanvas canvas, PcRect clip, int width, int height, IReadOnlyList<PcMark> marks)
    {
        var scaleX = (float)width / clip.Width;
        var scaleY = (float)height / clip.Height;
        using var outline = new SKPaint { Color = LabelFill.WithAlpha(0xCC), IsStroke = true, StrokeWidth = 1, IsAntialias = false };
        using var fill = new SKPaint { Color = LabelFill, IsAntialias = true };
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var font = new SKFont(face.Value, LabelTextSize) { Edging = SKFontEdging.Antialias, Hinting = SKFontHinting.Normal };
        var metrics = font.Metrics;
        var textHeight = metrics.Descent - metrics.Ascent;
        foreach (var mark in marks)
        {
            var left = (mark.Rect.X - clip.X) * scaleX;
            var top = (mark.Rect.Y - clip.Y) * scaleY;
            var right = (mark.Rect.X + mark.Rect.Width - clip.X) * scaleX;
            var bottom = (mark.Rect.Y + mark.Rect.Height - clip.Y) * scaleY;
            if (right <= 0 || bottom <= 0 || left >= width || top >= height) continue;
            canvas.DrawRect(new SKRect(MathF.Floor(left) + 0.5f, MathF.Floor(top) + 0.5f, MathF.Ceiling(right) - 0.5f, MathF.Ceiling(bottom) - 0.5f), outline);

            var text = mark.Number.ToString(CultureInfo.InvariantCulture);
            var labelWidth = font.MeasureText(text) + 6;
            var labelHeight = textHeight + 2;
            var x = Math.Clamp(MathF.Floor(left), 0, Math.Max(0, width - labelWidth));
            var y = Math.Clamp(MathF.Floor(top), 0, Math.Max(0, height - labelHeight));
            canvas.DrawRoundRect(new SKRect(x, y, x + labelWidth, y + labelHeight), 3, 3, fill);
            canvas.DrawText(text, x + 3, y + 1 - metrics.Ascent, SKTextAlign.Left, font, ink);
        }
    }
}
