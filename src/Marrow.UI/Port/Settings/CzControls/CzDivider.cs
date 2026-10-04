// PORT-ONLY CZ-1: row divider retains its geometry while opacity fades (DESIGN gen.py:208-209, CZ-12.8).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzDivider : Control
{
    private bool shown;
    private readonly CzNumber fade;
    public bool Shown
    {
        get => shown;
        set { shown = value; fade.To(value ? 1 : 0, CzMotion.Ease(.3)); }
    }
    public CzDivider(bool initiallyShown)
    {
        Height = 1; Margin = new Thickness(16, 0, 0, 0); IsHitTestVisible = false;
        shown = initiallyShown; Opacity = initiallyShown ? 1 : 0;
        fade = new CzNumber(new CzMotion(this), Opacity, v => Opacity = v);
        PropertyChanged += (_, e) => { if (e.Property.Name == "ActualThemeVariant") InvalidateVisual(); };
    }
    public override void Render(DrawingContext context) => context.DrawRectangle(CzTheme.Brush(CzTheme.Token(this, "div")), null, new Rect(Bounds.Size));
}
