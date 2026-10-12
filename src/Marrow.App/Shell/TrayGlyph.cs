// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.5 "Armilla", size ladder "The tray icon"; unit AS-6; ED-NEW-dextra-as6-10). No Swift twin.
// The tray icon's still frames: one colour, pre-rendered at 16, 20, 24 and 32 physical px (100 % to 200 % scale), swapped only
// when the state changes. They use the pose numbers of Armilla's XS tier (the canvas's DextraMark): Ready is the long arc with
// its attention arc and the presence point; Working is the one still ring with a small opening at the left (so it never reads
// as a spinner); Paused is two equal arcs, dimmed; Needs you is the attention arc stepped out, thicker. AS-8's ArmillaMark
// replaces these with its own stills (one control draws everything); the ICO builder and the sizes stay.
using SkiaSharp;

namespace Marrow.App.Tray;

public static class TrayGlyph
{
    /// <summary>The physical sizes of the frames in the icon.</summary>
    public static readonly IReadOnlyList<int> Sizes = [16, 20, 24, 32];

    private const float Ring = 0.72f; // R0 of the XS tier, as a share of the half-box

    /// <summary>The icon file (ICO with a PNG frame per size) in <paramref name="ink"/>; Needs you is drawn in the warm colour.</summary>
    public static byte[] Ico(TrayState state, SKColor ink)
    {
        var frames = Sizes.Select(size => (Size: size, Png: Png(state, size, ink))).ToList();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)frames.Count);
        var offset = 6 + 16 * frames.Count;
        foreach (var (size, png) in frames)
        {
            writer.Write((byte)size);
            writer.Write((byte)size);
            writer.Write((byte)0); // no palette
            writer.Write((byte)0);
            writer.Write((ushort)1); // planes
            writer.Write((ushort)32); // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }
        foreach (var (_, png) in frames) writer.Write(png);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>One frame as a PNG.</summary>
    public static byte[] Png(TrayState state, int size, SKColor ink)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            Draw(canvas, state, size, ink);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Draws the state's still pose in the square of <paramref name="size"/> px. Nothing is drawn outside the box.</summary>
    public static void Draw(SKCanvas canvas, TrayState state, int size, SKColor ink)
    {
        var half = size / 2f;
        var stroke = Stroke(size);
        var radius = half * Ring;
        var color = state == TrayState.NeedsYou ? Warm : ink;
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round, Color = color };
        void Arc(float center, float length, float alpha, float width = 1f)
        {
            if (length <= 0) return;
            paint.Color = color.WithAlpha((byte)Math.Round(255 * alpha));
            paint.StrokeWidth = stroke * width;
            var rect = new SKRect(half - radius, half - radius, half + radius, half + radius);
            // Angles in Armilla run clockwise from twelve o'clock; Skia's start at three o'clock.
            canvas.DrawArc(rect, center - length / 2f - 90f, length, false, paint);
        }
        switch (state)
        {
            case TrayState.Working:
                // One still ring with a small opening at the left (324 degrees, opening centred at 270).
                Arc(90f, 324f, 1f);
                break;
            case TrayState.Paused:
                // Two equal 136 degree arcs, the figure dimmed to 52 %.
                Arc(270f, 136f, 0.52f);
                Arc(90f, 136f, 0.52f);
                break;
            case TrayState.NeedsYou:
                // Attention at 150 degrees, warm and thicker, with the long arc opposite it.
                Arc(150f + 180f, 240f, 0.68f);
                Arc(150f, 40f, 1f, 1.5f);
                break;
            default:
                // Idle: attention at 45 degrees, the long arc (236) centred opposite it.
                Arc(45f + 180f, 236f, 0.78f);
                Arc(45f, 34f, 1f, 1.3f);
                break;
        }
        // The presence point: about 6 % of the box, still.
        paint.Style = SKPaintStyle.Fill;
        paint.Color = color.WithAlpha((byte)(state == TrayState.Paused ? 133 : 255));
        canvas.DrawCircle(half, half, Math.Max(1f, size * 0.06f), paint);
    }

    // The canvas's stroke ladder (SW): 1.6 px at 16, 1.8 at 20, 2.0 at 24, 2.2 at 32.
    private static float Stroke(int size) => size switch { <= 16 => 1.6f, <= 20 => 1.8f, <= 24 => 2.0f, _ => 2.2f };

    private static readonly SKColor Warm = new(0xF2, 0xB4, 0x5E);
}
