// PORT-ONLY: first-release feature unit SK-15 (DL-17). The determinate ring of a row that downloads, drawn in the page's own tokens (a track at the label colour's 0.16 and the accent over it).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Port;
using ChromeTokens = Marrow.UI.Chrome.Chrome;

namespace Marrow.App.Port.Skills.Pages;

/// <summary>A ring at <see cref="Value"/> (0 to 1), 14 points across with a 2 point stroke; it starts at 12 o'clock and runs clockwise.</summary>
internal sealed class ProgressRing : Control
{
    private readonly IBrush track;
    private readonly IBrush fill;
    private double value;

    public ProgressRing()
    {
        Width = Height = 14;
        track = ChromeTokens.Overlay(0.16).Brush(this);
        fill = ChromeTokens.Accent.Brush(this);
    }

    public double Value
    {
        get => value;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (this.value == clamped) return;
            this.value = clamped;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const double stroke = 2;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = (Math.Min(Bounds.Width, Bounds.Height) - stroke) / 2;
        context.DrawEllipse(null, new Pen(track, stroke), centre, radius, radius);
        if (value <= 0) return;
        var pen = new Pen(fill, stroke) { LineCap = PenLineCap.Round };
        if (value >= 1) { context.DrawEllipse(null, pen, centre, radius, radius); return; }
        var end = -Math.PI / 2 + 2 * Math.PI * value;
        var start = new Point(centre.X, centre.Y - radius);
        var stop = new Point(centre.X + radius * Math.Cos(end), centre.Y + radius * Math.Sin(end));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments!.Add(new ArcSegment { Point = stop, Size = new Size(radius, radius), SweepDirection = SweepDirection.Clockwise, IsLargeArc = value > 0.5 });
        context.DrawGeometry(null, pen, new PathGeometry { Figures = [figure] });
    }
}
