using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Port.Voice;
using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Fixtures;

// V-T133: only rendered glyph pixels are ink, never their bounding rectangles or the cross's glass disc.
internal static class VoicePillInk
{
    public static bool[] Render(VoicePillScene scene, PixelSize size, string tag)
    {
        var pill = VoicePillProbe.Pill(scene.View);
        var scale = scene.Window.RenderScaling;
        using var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(pill.TransformToVisual(scene.Window)!.Value))
        using (context.PushGeometryClip(pill.ContentClip))
        using (context.PushOpacity(pill.Opacity))
        {
            foreach (var label in pill.Labels)
            {
                using (context.PushTransform(label.TransformToVisual(pill)!.Value))
                using (context.PushOpacity(label.Opacity)) label.Render(context);
            }
            // The design's 12-unit SVG, at 11 px with round 1.5 px strokes. Render it at the actual pressed/opening transform.
            var cross = pill.Cross;
            using (context.PushTransform(cross.TransformToVisual(pill)!.Value))
            using (context.PushOpacity(cross.Opacity))
            using (context.PushTransform(Matrix.CreateScale(11.0 / 12, 11.0 / 12) * Matrix.CreateTranslation(6.5, 6.5)))
                context.DrawGeometry(null, new Pen(Brushes.White, 1.5 * 12 / 11, lineCap: PenLineCap.Round),
                    StreamGeometry.Parse("M3.2,3.2 L8.8,8.8 M8.8,3.2 L3.2,8.8"));
        }
        if (Environment.GetEnvironmentVariable("VT12_DUMP") is { } dump) bitmap.Save(Path.Combine(dump, "ink-" + tag + ".png"), new PngBitmapEncoderOptions());
        var pixels = new byte[size.Width * size.Height * 4];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, size.Width * 4); }
        finally { handle.Free(); }
        var ink = new bool[size.Width * size.Height];
        for (var i = 0; i < ink.Length; i++) ink[i] = pixels[i * 4 + 3] > 0;
        return ink;
    }
}
