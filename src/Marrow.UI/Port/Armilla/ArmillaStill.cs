// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 2.5 size ladder "Notifications: the needs-you toast", "Reduce motion"; the canvas's 48 px notification;
// ED-NEW-dextra-as9-7). No Swift twin.
// One Armilla pose as a picture, for the places that cannot host a live mark (a Windows toast's picture): the same engine and the same
// Skia drawing the live mark uses, snapped to the state's own still pose, on a transparent square. Nothing is drawn outside the square.
using SkiaSharp;

namespace Marrow.UI.Port.Armilla;

public static class ArmillaStill
{
    /// <summary>The mark's still pose for <paramref name="look"/> as a PNG of <paramref name="pixels"/> by <paramref name="pixels"/> (transparent around the mark).</summary>
    public static byte[] Png(ArmillaLook look, int pixels)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentOutOfRangeException.ThrowIfLessThan(pixels, 8);
        var engine = new ArmillaEngine(look);
        using var drawing = new ArmillaSkiaCanvas();
        using var bitmap = new SKBitmap(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var surface = new SKCanvas(bitmap))
        {
            surface.Clear(SKColors.Transparent);
            // the engine draws in the look's own units, round a centre at the origin; scale them to fill the picture
            surface.Scale((float)(pixels / look.Size));
            surface.Translate((float)(look.Size / 2), (float)(look.Size / 2));
            drawing.Begin(surface);
            engine.DrawStill(drawing);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>The needs-you mark of the notification: 48 px in the size ladder, drawn for the theme the toast is shown in.</summary>
    public static ArmillaLook NeedsYouLook(bool dark) => new() { Size = 48, State = ArmillaState.NeedsYou, Dark = dark, Rest = true, Reduce = true, AutoVoice = false };

    /// <summary>The resting mark of a notification that is news and asks nothing (a "Notify me" duty's finding, 13.1): the same size, no attention arc.</summary>
    public static ArmillaLook NewsLook(bool dark) => new() { Size = 48, State = ArmillaState.Idle, Dark = dark, Rest = true, Reduce = true, AutoVoice = false };
}
