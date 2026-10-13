using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port.Web;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Web;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

[Collection("Headless")]
[Trait("Decision", "ED-NEW-web-w20-2")]
public sealed class WebW20ChatLinkContrastTests(HeadlessSession headless)
{
    [Fact]
    public void The_link_sheet_disables_its_glass_effect_on_a_solid_contrast_surface_and_restores_it() => headless.OnUiThread(() =>
    {
        var choice = new ChatLinkChoice(new WebSettings(new InMemorySettingsStore()), SwiftURL.Create("https://example.org/")!, false);
        var sheet = new ChatLinkSheetView(choice, () => { }) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        var window = new Avalonia.Controls.Window { Width = 640, Height = 420, Content = sheet };
        try
        {
            window.Show();
            var surface = Assert.IsAssignableFrom<Border>(sheet.Child);
            var original = SwiftGlass.GetEffect(surface);
            Assert.NotNull(original);
            foreach (var palette in new[] { HighContrastPalette.Black, HighContrastPalette.White })
            {
                EnvironmentValues.SetAccessibilityHighContrast(window, palette);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Null(SwiftGlass.GetEffect(surface));
                Assert.Equal(palette.Window, Assert.IsAssignableFrom<ISolidColorBrush>(surface.Background).Color);
                Assert.Equal(palette.WindowText, Assert.IsAssignableFrom<ISolidColorBrush>(surface.BorderBrush).Color);
                Assert.Equal(new Thickness(1), surface.BorderThickness);
            }
            EnvironmentValues.SetAccessibilityHighContrast(window, null);
            Assert.Equal(original, SwiftGlass.GetEffect(surface));
            Assert.Null(surface.Background);
            Assert.Equal(default, surface.BorderThickness);
        }
        finally { window.Close(); }
    });
}
