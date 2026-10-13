using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Marrow.Core.Models;
using Marrow.UI.Port;
using Marrow.UI.Tests.Fixtures;

namespace Marrow.UI.Tests.Panes;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-2")]
public sealed class WebW20NarrowContrastTests(HeadlessSession headless)
{
    [Fact]
    public void Narrow_tabs_use_a_solid_system_surface_and_restore_the_canvas_glass() => headless.OnUiThread(() =>
    {
        using var rig = new NarrowTabStripRig(680);
        rig.Strip.SetTabs([PaneKind.Chat, PaneKind.Browser, PaneKind.Artifacts], PaneKind.Browser);
        rig.Layout();
        var before = rig.Strip.Pill.Bounds;
        Assert.IsAssignableFrom<IGradientBrush>(rig.Strip.Pill.Background);
        foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
        {
            EnvironmentValues.SetAccessibilityHighContrast(rig.Window, palette);
            rig.Layout();
            Assert.Equal(palette.Window, Assert.IsAssignableFrom<ISolidColorBrush>(rig.Strip.Pill.Background).Color);
            Assert.Equal(palette.WindowText, Assert.IsAssignableFrom<ISolidColorBrush>(rig.Strip.Pill.BorderBrush).Color);
            Assert.Equal(default, rig.Strip.Pill.BoxShadow);
            Assert.Equal(before, rig.Strip.Pill.Bounds);
            var face = Assert.IsType<Border>(rig.Strip.TabButtons[1].Content);
            Assert.Equal(palette.Highlight, face.BoxShadow[0].Color);
            var text = Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(face.Child).Children[1]);
            Assert.Equal(palette.WindowText, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        }
        EnvironmentValues.SetAccessibilityHighContrast(rig.Window, null);
        rig.Layout();
        Assert.IsAssignableFrom<IGradientBrush>(rig.Strip.Pill.Background);
        Assert.Equal(before, rig.Strip.Pill.Bounds);
    });
}
