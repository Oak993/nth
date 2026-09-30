// Managed visual-layer assertions, not DWM/Acrylic or continuous-curve pixel proof.
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;

namespace Marrow.App.Tests.Window;

public static class SettingsBackdropAssertions
{
    public static void GlassOpacity(WindowBackdrop backdrop, double expected)
    {
        var root = Assert.IsType<Panel>(backdrop.Child);
        var group = Assert.IsType<Panel>(root.Children[0]);
        var material = Assert.IsType<Panel>(Assert.Single(group.Children));
        Assert.Equal(2, material.Children.Count);
        var glass = Assert.IsType<Border>(material.Children[0]);
        Assert.Equal(GlassEffect.Regular(GlassShape.RoundedRectangle(26, RoundedCornerStyle.Continuous)), SwiftGlass.GetEffect(glass));
        var scrim = Assert.IsType<Border>(material.Children[1]);
        Assert.Equal(expected, scrim.Opacity, 8);
    }

    public static void Solid(WindowBackdrop backdrop, Color expected)
    {
        var root = Assert.IsType<Panel>(backdrop.Child);
        var group = Assert.IsType<Panel>(root.Children[0]);
        var solid = Assert.IsType<Border>(Assert.Single(group.Children));
        Assert.Null(SwiftGlass.GetEffect(solid));
        Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(solid.Background).Color);
    }
}
