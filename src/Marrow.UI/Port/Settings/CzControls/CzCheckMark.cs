// PORT-ONLY CZ-1: selected menu item check, fades .16s and scales from .6 over .24s ez.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzCheckMark : Control
{
    private readonly CzNumber fade, scale;
    public CzCheckMark()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        Opacity = 0; RenderTransformOrigin = new RelativePoint(.5, .5, RelativeUnit.Relative);
        var motion = new CzMotion(this);
        fade = new CzNumber(motion, 0, v => Opacity = v); scale = new CzNumber(motion, .6, v => RenderTransform = new ScaleTransform(v, v));
    }
    public void SetChecked(bool check) { fade.To(check ? 1 : 0, CzMotion.Ease(.16)); scale.To(check ? 1 : .6, CzMotion.Ez(.24)); }
    public override void Render(DrawingContext context)
    {
        var pen = new Pen(CzTheme.Text(this), 1.5);
        context.DrawLine(pen, new Point(2, 6), new Point(5, 9)); context.DrawLine(pen, new Point(5, 9), new Point(10, 3));
    }
}
