using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Common;
using Xunit;

namespace Marrow.App.Tests.Window;

[Collection("Headless")]
public sealed class SettingsZoomRegressionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Theory]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    [Trait("Req", "UI-SETTINGS-001")]
    public void Live_zoom_keeps_both_edges_of_the_real_Settings_page_inside_the_card(int percent)
    {
        WithManager((_, manager) =>
        {
            manager.ShowSettings(SettingsPage.Shortcuts);
            var window = Settings(manager);
            Drain(window);
            var view = View(manager);
            manager.Model.Settings.AppZoom = percent;
            Drain(window);
            var topLeft = view.TranslatePoint(default, window)!.Value;
            var bottomRight = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), window)!.Value;
            Assert.InRange(topLeft.X, 0, .01);
            Assert.InRange(topLeft.Y, 0, .01);
            Assert.InRange(bottomRight.X, 779.99, 780.01);
            Assert.InRange(bottomRight.Y, 579.99, 580.01);
            Assert.Equal(new Size(780, 580), window.ClientSize);
            manager.Model.Settings.AppZoom = 100;
            Drain(window);
            Assert.Equal(new Size(780, 580), view.Bounds.Size);
            Assert.Single(Nodes<ZoomLayout>(window));
        });
    }
}
