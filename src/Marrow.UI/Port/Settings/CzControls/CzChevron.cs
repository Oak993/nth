// PORT-ONLY CZ-1: shared 7x5 menu/Advanced chevron, turning .3s ez.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzChevron : Control
{
    private readonly CzNumber turn;
    private readonly bool right;
    public double Angle => turn.Value;
    public CzChevron(bool pointingRight = false)
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        right = pointingRight; Width = right ? 5 : 7; Height = right ? 8 : 5; Opacity = .7;
        RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        turn = new CzNumber(new CzMotion(this), 0, v => RenderTransform = new RotateTransform(v));
    }
    public void SetOpen(bool open) => turn.To(open ? 180 : 0, CzMotion.Ez(.3));
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(CzTheme.Text(this), 1.2);
        if (right) { context.DrawLine(pen, new Point(.5, .5), new Point(4, 4)); context.DrawLine(pen, new Point(4, 4), new Point(.5, 7.5)); return; }
        context.DrawLine(pen, new Point(.5, .5), new Point(3.5, 4)); context.DrawLine(pen, new Point(3.5, 4), new Point(6.5, .5));
    }
}
