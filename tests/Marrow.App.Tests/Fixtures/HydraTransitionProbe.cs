using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Marrow.App.Tests.Fixtures;

/// <summary>What a transition has done to a child so far, read off the controls: the product of every opacity on the way up, and the scale of its frame.</summary>
public static class HydraTransitionProbe
{
    /// <summary>The opacity the child is drawn at, from itself up to (not including) <paramref name="stop"/>.</summary>
    public static double Opacity(Visual child, Visual stop)
    {
        var opacity = 1.0;
        for (Visual? visual = child; visual is not null && !ReferenceEquals(visual, stop); visual = visual.GetVisualParent())
            opacity *= visual.Opacity;
        return opacity;
    }

    /// <summary>The width of the child's frame as drawn, over its laid-out width.</summary>
    public static double Scale(Control child, Visual relativeTo)
    {
        var from = child.TranslatePoint(new Point(0, 0), relativeTo)!.Value;
        var to = child.TranslatePoint(new Point(child.Bounds.Width, child.Bounds.Height), relativeTo)!.Value;
        return (to.X - from.X) / child.Bounds.Width;
    }
}
