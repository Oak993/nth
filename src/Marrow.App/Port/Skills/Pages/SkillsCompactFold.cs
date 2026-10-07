// PORT-ONLY: locked sd-page.css:111-113,478-479: state height .34s --ez and opacity .26s CSS ease.
using Avalonia;
using Avalonia.Controls;

namespace Marrow.App.Port.Skills.Pages;

internal sealed class SkillsCompactFold : Decorator
{
    private readonly SkillsMotionNumber height, opacity;
    private bool shown;
    internal SkillsCompactFold(Control content)
    {
        Child = content; ClipToBounds = true; IsEnabled = false;
        height = new(this, 0, _ => InvalidateMeasure());
        opacity = new(this, 0, value => Opacity = value);
    }
    internal double Fraction => height.Value;
    internal void Show(bool next, bool animate)
    {
        if (shown == next) return;
        shown = next; IsEnabled = next;
        height.Set(next ? 1 : 0, .34, movement: true, animate: animate);
        opacity.Set(next ? 1 : 0, .26, animate: animate);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        Child!.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        return new Size(Child.DesiredSize.Width, Child.DesiredSize.Height * height.Value);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        Child!.Arrange(new Rect(0, 0, finalSize.Width, Child.DesiredSize.Height));
        return finalSize;
    }
}
