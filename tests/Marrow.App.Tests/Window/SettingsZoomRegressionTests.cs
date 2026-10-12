using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Testing;
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
            var time = new ManualTimeProvider(); // ED-NEW-textsize-3: the zoom's motion runs on SwiftClock
            using var clock = SwiftClock.Provider.Override(time);
            manager.ShowSettings(SettingsPage.Shortcuts);
            var window = Settings(manager);
            Drain(window);
            var view = View(manager);
            manager.Model.Settings.AppZoom = percent;
            RunFrames(time, window); // ED-NEW-textsize-3: the zoom moves to its new scale over its frames
            var topLeft = view.TranslatePoint(default, window)!.Value;
            var bottomRight = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), window)!.Value;
            Assert.InRange(topLeft.X, 0, .01);
            Assert.InRange(topLeft.Y, 0, .01);
            Assert.InRange(bottomRight.X, 779.99, 780.01);
            Assert.InRange(bottomRight.Y, 579.99, 580.01);
            Assert.Equal(new Size(780, 580), window.ClientSize);
            manager.Model.Settings.AppZoom = 100;
            RunFrames(time, window);
            // 100 % is the base scale (ED-NEW-textsize-2): the page is laid out in the window's points divided by it.
            Assert.Equal(new Size(780 / AppZoom.BaseScale, 580 / AppZoom.BaseScale), view.Bounds.Size);
            Assert.Single(Nodes<ZoomLayout>(window));
        });
    }
}
