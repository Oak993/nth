// PORT-ONLY CZ-1: natural-height fold with clipping, DESIGN gen.py:189-193 (CZ-12.8).
using Avalonia;
using Avalonia.Controls;
namespace Marrow.UI.Port.Settings;

public sealed class CzFoldRow : Decorator
{
    private bool open;
    private readonly CzNumber fraction, fade;
    public double Fraction => fraction.Value;
    public bool Open
    {
        get => open;
        set
        {
            if (open == value) return; open = value; IsEnabled = value;
            fraction.To(value ? 1 : 0, CzMotion.Ease(.34)); fade.To(value ? 1 : 0, CzMotion.Ease(value ? .24 : .14), value ? .08 : 0);
        }
    }
    public CzFoldRow(Control content, bool initiallyOpen = true)
    {
        Child = content; ClipToBounds = true; open = initiallyOpen; IsEnabled = initiallyOpen; Opacity = initiallyOpen ? 1 : 0;
        var motion = new CzMotion(this);
        fraction = new CzNumber(motion, initiallyOpen ? 1 : 0, _ => InvalidateMeasure());
        fade = new CzNumber(motion, initiallyOpen ? 1 : 0, v => Opacity = v);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Child!.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * fraction.Value);
    }
    protected override Size ArrangeOverride(Size finalSize) { Child!.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height)); return finalSize; }
    // DESIGN cz.js:599-610: no divider before the first visible row, around a caption, or after a visible caption.
    public static bool HasDivider(bool firstShown, bool isCaption, bool followsCaption) => !firstShown && !isCaption && !followsCaption;
}
