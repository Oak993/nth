// PORT-ONLY: the Tactus mark as pixels, for the tray and notifications (OD-0146, ED-NEW-pcctl-11; DESIGN.md 4.5). The
// shapes come only from Core's TactusGlyph (the one swappable file). At 16 px (the tray at 100 %) the hand-hinted drawings
// carry the status: Ready (cut open), Working (pressed shut), Paused (lifted 1 px, the corner cut out for two bars). At
// 24 and 32 px (the tray at 150 and 200 %) the 24 unit master: its rest pose, or its lift pose while paused. One colour,
// chosen by the caller for the taskbar it sits on.
using Marrow.Core.Port.PcControl.Mark;
using SkiaSharp;

namespace Marrow.UI.Port.PcControl;

/// <summary>PORT-ONLY. Renders the Tactus mark to bitmaps, PNG and ICO.</summary>
public static class TactusMarkImage
{
    /// <summary>The sizes an icon carries: the tray at 100, 150 and 200 %.</summary>
    public static IReadOnlyList<int> IconSizes { get; } = [16, 24, 32];

    /// <summary>A Core outline as a Skia path, scaled by <paramref name="scale"/> (radii included).</summary>
    public static SKPath Path(TactusPath path, float scale = 1)
    {
        var sk = new SKPath { FillType = SKPathFillType.Winding };
        foreach (var s in path.Segments)
        {
            var x = (float)s.X * scale;
            var y = (float)s.Y * scale;
            switch (s.Kind)
            {
                case TactusSegmentKind.Move:
                    sk.MoveTo(x, y);
                    break;
                case TactusSegmentKind.Line:
                    sk.LineTo(x, y);
                    break;
                case TactusSegmentKind.Arc:
                    var r = (float)s.Radius * scale;
                    sk.ArcTo(r, r, 0, SKPathArcSize.Small, s.Clockwise ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise, x, y);
                    break;
                default:
                    sk.Close();
                    break;
            }
        }
        return sk;
    }

    /// <summary>The mark at <paramref name="pixels"/> square in <paramref name="state"/>, premultiplied BGRA/RGBA (platform order).</summary>
    public static SKBitmap Render(int pixels, TactusSmallState state, SKColor color)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 1);
        var bitmap = new SKBitmap(new SKImageInfo(pixels, pixels, SKImageInfo.PlatformColorType, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        if (TactusGlyph.UsesSmallDrawing(pixels))
        {
            var scale = pixels / (float)TactusGlyph.SmallUnits;
            var drawing = TactusGlyph.SmallDrawing(state);
            using var mark = Path(drawing.Mark, scale);
            canvas.Save();
            if (drawing.KnockOut is { } knock)
            {
                canvas.ClipRect(SKRect.Create((float)knock.X * scale, (float)knock.Y * scale, (float)knock.Width * scale, (float)knock.Height * scale), SKClipOperation.Difference, antialias: true);
            }
            canvas.DrawPath(mark, paint);
            canvas.Restore();
            if (drawing.Extra is { } extra)
            {
                using var bars = Path(extra, scale);
                canvas.DrawPath(bars, paint);
            }
        }
        else
        {
            var scale = pixels / (float)TactusGlyph.MasterUnits;
            using var mark = Path(TactusGlyph.Draw(state == TactusSmallState.Paused ? TactusPose.Lift : TactusPose.Rest), scale);
            canvas.DrawPath(mark, paint);
        }
        canvas.Flush();
        return bitmap;
    }

    /// <summary>The mark as a PNG.</summary>
    public static byte[] Png(int pixels, TactusSmallState state, SKColor color)
    {
        using var bitmap = Render(pixels, state, color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ?? throw new InvalidOperationException("The mark could not be encoded as PNG.");
        return data.ToArray();
    }

    /// <summary>The mark as an ICO holding a PNG for each of <see cref="IconSizes"/> (Windows Vista and later read PNG entries).</summary>
    public static byte[] Ico(TactusSmallState state, SKColor color)
    {
        var images = IconSizes.Select(size => (Size: size, Png: Png(size, state, color))).ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)images.Length);
        var offset = 6 + (16 * images.Length);
        foreach (var (size, png) in images)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0); // no palette
            writer.Write((byte)0); // reserved
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in images) writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }
}
