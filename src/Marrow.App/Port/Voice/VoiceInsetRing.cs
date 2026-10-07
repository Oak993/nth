// PORT-ONLY, T33 / OD-0121: the capsule's single-AA ring and Chrome's clipped cross inset.
using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Marrow.App.Port.Voice;

internal sealed class VoiceInsetRing(Rect box, double width, Color color, bool clipOuterDisc = false) : ICustomDrawOperation
{
    public Rect Bounds => box;
    public bool HitTest(Point point) => false;
    public bool Equals(ICustomDrawOperation? other) => false;
    public void Dispose() { }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is not { } feature) return;
        using var lease = feature.Lease();
        var outer = new SKRect((float)box.Left, (float)box.Top, (float)box.Right, (float)box.Bottom);
        var inset = (float)width;
        var inner = new SKRect(outer.Left + inset, outer.Top + inset, outer.Right - inset, outer.Bottom - inset);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(color.R, color.G, color.B, (byte)Math.Round(color.A * Math.Clamp(lease.CurrentOpacity, 0, 1))),
        };
        if (clipOuterDisc && inner.Width > 0 && inner.Height > 0)
        {
            // Chrome154 PaintInsetBoxShadow clips to the disc, then FillRectWithContouredHole
            // draws DRRect with a rectangular outer caster. No rounded outer fill is applied again.
            using var clip = new SKRoundRect(outer, outer.Height / 2, outer.Height / 2);
            using var caster = new SKRoundRect(outer, 0, 0);
            using var hole = new SKRoundRect(inner, inner.Height / 2, inner.Height / 2);
            var saved = lease.SkCanvas.Save();
            try
            {
                lease.SkCanvas.ClipRoundRect(clip, SKClipOperation.Intersect, true);
                lease.SkCanvas.DrawRoundRectDifference(caster, hole, paint);
            }
            finally { lease.SkCanvas.RestoreToCount(saved); }
            return;
        }

        // OD-0121 explicitly keeps one AA evaluation at the capsule edge.
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        path.AddRoundRect(outer, outer.Height / 2, outer.Height / 2);
        if (inner.Width > 0 && inner.Height > 0)
            path.AddRoundRect(inner, inner.Height / 2, inner.Height / 2);
        lease.SkCanvas.DrawPath(path, paint);
    }
}
