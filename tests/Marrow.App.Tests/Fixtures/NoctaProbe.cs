using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Marrow.App.Views.Settings;

namespace Marrow.App.Tests.Fixtures;

// OD-0141, ED-NEW-nocta-1: reads the Nocta switch as drawn. Record lists every brush and pen one Render issues (through a DrawingGroup, so the
// objects are the very ones the switch passed); Snapshot renders it at a scale on a ground, with room for its shadows and focus ring.
internal static class NoctaProbe
{
    internal const double Margin = 8;

    internal sealed record Drawn(IReadOnlyList<IBrush> Brushes, IReadOnlyList<IPen> Pens);

    internal static Drawn Record(SmallSwitch toggle)
    {
        var group = new DrawingGroup();
        using (var context = group.Open()) toggle.Render(context);
        var brushes = new List<IBrush>(); var pens = new List<IPen>();
        void Walk(Drawing drawing)
        {
            if (drawing is GeometryDrawing geometry) { if (geometry.Brush is { } b) brushes.Add(b); if (geometry.Pen is { } p) pens.Add(p); }
            else if (drawing is DrawingGroup inner) foreach (var child in inner.Children) Walk(child);
        }
        Walk(group);
        return new Drawn(brushes, pens);
    }

    /// <summary>The lit lume's brush (radial, white at its centre, the lume colour at 0.7) when one Render draws it.</summary>
    internal static bool DrawsLitLume(SmallSwitch toggle, Color lume) => Record(toggle).Brushes.OfType<IRadialGradientBrush>()
        .Any(b => b.GradientStops.Count == 2 && b.GradientStops[0].Color == Colors.White && b.GradientStops[1].Offset == .7 && b.GradientStops[1].Color == lume);

    internal static SkiaSharp.SKBitmap Snapshot(SmallSwitch toggle, double scale, Color? ground = null)
    {
        var width = (int)Math.Ceiling((SmallSwitch.TrackWidth + 2 * Margin) * scale); var height = (int)Math.Ceiling((SmallSwitch.TrackHeight + 2 * Margin) * scale);
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        using (var context = bitmap.CreateDrawingContext())
        {
            if (ground is { } fill) context.FillRectangle(new SolidColorBrush(fill), new Rect(0, 0, width, height));
            using (context.PushTransform(Matrix.CreateTranslation(Margin, Margin) * Matrix.CreateScale(scale, scale))) toggle.Render(context);
        }
        using var stream = new MemoryStream(); bitmap.Save(stream, new PngBitmapEncoderOptions());
        return SkiaSharp.SKBitmap.Decode(stream.ToArray());
    }

    /// <summary>The pixel at a point in the switch's own space (points), in a snapshot taken at the scale.</summary>
    internal static SkiaSharp.SKColor At(SkiaSharp.SKBitmap pixels, double scale, double x, double y) =>
        pixels.GetPixel((int)Math.Floor((x + Margin) * scale), (int)Math.Floor((y + Margin) * scale));

    /// <summary>Relative luminance (sRGB weights), premultiplied by coverage: how bright the pixel reads, colour aside.</summary>
    internal static double Luma(SkiaSharp.SKColor c) => (0.2126 * c.Red + 0.7152 * c.Green + 0.0722 * c.Blue) * c.Alpha / 255.0 / 255.0;
}
