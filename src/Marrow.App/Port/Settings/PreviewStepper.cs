// PORT-ONLY CZ-3 (customization-SPEC CZ-4.5.1; DESIGN gen.py .czst): the preview model switch's stepper, 22 x 22, radius 11, a chevron glyph. Transparent; the hover
// fill and the brighter glyph on hover; pressed scale 0.9.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port.Settings;

namespace Marrow.App.Port.Settings;

internal sealed class PreviewStepper : CzButton
{
    private sealed class Glyph(bool forward) : Control
    {
        public override void Render(DrawingContext context)
        {
            // the design's 10 x 10 paths, "M6.2 2 3.2 5l3 3" and "M3.8 2 6.8 5l-3 3", stroked 1.5 round
            var pen = new Pen(CzTheme.Text(this, secondary: !((PreviewStepper?)Parent!).Hot), 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            var geometry = new StreamGeometry();
            using (var c = geometry.Open())
            {
                if (forward) { c.BeginFigure(new Point(3.8, 2), false); c.LineTo(new Point(6.8, 5)); c.LineTo(new Point(3.8, 8)); }
                else { c.BeginFigure(new Point(6.2, 2), false); c.LineTo(new Point(3.2, 5)); c.LineTo(new Point(6.2, 8)); }
                c.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geometry);
        }
    }

    private readonly Glyph glyph;

    internal PreviewStepper(string name, bool forward) : base(name)
    {
        Width = Height = 22;
        CornerRadius = new CornerRadius(11);
        Padding = default;
        ChromeFill = false;
        PressScale = .9;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        glyph = new Glyph(forward) { Width = 10, Height = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        Child = glyph;
        Refresh();
    }

    /// <summary>Whether the pointer is over it: the glyph then takes the primary text colour.</summary>
    internal bool Hot => IsPointerOver;

    protected override void Refresh()
    {
        base.Refresh();
        Background = IsPointerOver ? CzTheme.Brush(CzTheme.Token(this, "hov")) : Brushes.Transparent;
        glyph?.InvalidateVisual();
    }
}
