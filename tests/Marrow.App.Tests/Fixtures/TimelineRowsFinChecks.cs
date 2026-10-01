using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using Marrow.Core.Port;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// Shared assertions of the TimelineRows "finish" tests: a text's font, a colour read through the control's own theme
// variant, and the centre line of controls that SwiftUI's HStack puts on one line.
internal static class TimelineRowsFinChecks
{
    // The face every chat text is drawn in (OD-0009, ED-107): the bundled Inter, never a per-control choice.
    internal static readonly FontFamily Inter = new("fonts:Inter#Inter");

    internal static void Face(TextBlock text, double size, FontWeight weight, string because = "")
    {
        Assert.True(size == text.FontSize, $"{because} font size {text.FontSize} but the Swift text style is {size}");
        Assert.True(weight == text.FontWeight, $"{because} font weight {text.FontWeight} but the Swift weight is {weight}");
        Assert.Equal(Inter, text.FontFamily);
        Assert.Equal(FontStyle.Normal, text.FontStyle);
    }

    internal static Color Resolve(SwiftColor colour, Control on) => colour.Resolve(on.ActualThemeVariant);

    internal static void Paints(IBrush? brush, SwiftColor expected, Control on, string because = "")
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.True(Resolve(expected, on) == solid.Color, $"{because} colour {solid.Color} but expected {Resolve(expected, on)}");
    }

    internal static double CentreY(Control control, Visual root) =>
        control.TranslatePoint(new Point(0, control.Bounds.Height / 2), root)!.Value.Y;

    internal static double Left(Control control, Visual root) => control.TranslatePoint(default, root)!.Value.X;

    // SwiftUI's HStack (alignment .center) draws every child on one centre line; layout rounds an odd height to a half point.
    internal static void OnOneCentreLine(Visual root, params Control[] items)
    {
        var line = CentreY(items[0], root);
        for (var i = 1; i < items.Length; i++)
            Assert.True(Math.Abs(line - CentreY(items[i], root)) <= 0.5, $"item {i} centre {CentreY(items[i], root)} is off the line at {line}");
        // Each text's own glyph box, not just its slot: a TextBlock stretched to a taller slot draws at the top of it.
        foreach (var text in items.OfType<TextBlock>())
            Assert.True(Math.Abs(text.DesiredSize.Height - text.Bounds.Height) <= 1, $"text '{text.Text}' is stretched to {text.Bounds.Height} but is {text.DesiredSize.Height} tall");
    }

    // A measured distance: the Swift's value, what the layout gave, and which gap it is.
    internal static void Near(string what, double expected, double actual, double tolerance = 0.5) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"{what}: the Swift gives {expected} but the layout has {actual}");

    internal static IEnumerable<T> Mounted<T>(Visual root) where T : class => root.GetVisualDescendants().OfType<T>();
}
