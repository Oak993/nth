// PORT-ONLY CZ-1: two stacked shape sets; timings are CZ-12.1.4, DESIGN gen.py:237-240.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
namespace Marrow.UI.Port.Settings;

public sealed class CzSegmentSets : Panel
{
    private readonly CzNumber firstFade, secondFade, firstY, secondY;
    public bool FirstShown { get; private set; }
    public CzSegmentSets(CzSegmentedControl first, CzSegmentedControl second, bool showFirst = true)
    {
        FirstShown = showFirst; Children.Add(first); Children.Add(second);
        var motion = new CzMotion(this);
        first.Opacity = showFirst ? 1 : 0; second.Opacity = showFirst ? 0 : 1;
        first.IsEnabled = showFirst; second.IsEnabled = !showFirst;
        firstFade = new CzNumber(motion, first.Opacity, v => first.Opacity = v); secondFade = new CzNumber(motion, second.Opacity, v => second.Opacity = v);
        firstY = new CzNumber(motion, showFirst ? 0 : 3, v => first.RenderTransform = new TranslateTransform(0, v));
        secondY = new CzNumber(motion, showFirst ? 3 : 0, v => second.RenderTransform = new TranslateTransform(0, v));
    }
    public void ShowFirst(bool first)
    {
        if (FirstShown == first) return; FirstShown = first;
        Children[0].IsEnabled = first; Children[1].IsEnabled = !first;
        firstFade.To(first ? 1 : 0, CzMotion.Ease(first ? .28 : .16), first ? .08 : 0);
        secondFade.To(first ? 0 : 1, CzMotion.Ease(first ? .16 : .28), first ? 0 : .08);
        firstY.To(first ? 0 : 3, CzMotion.Ez(first ? .4 : .3), first ? .08 : 0);
        secondY.To(first ? 3 : 0, CzMotion.Ez(first ? .3 : .4), first ? 0 : .08);
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return new Size(Children.Max(x => x.DesiredSize.Width), Children.Max(x => x.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size finalSize) { foreach (var child in Children) child.Arrange(new Rect(finalSize)); return finalSize; }
}
