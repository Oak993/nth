using Avalonia;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Common;
using Xunit;
using HostWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Window;

// OD-0140 (ED-NEW-settingsinapp-1): the card is the Settings panel in the main window (SettingsOverlay.Card); the zoom scales the panel's
// tree once, at its own root, and leaves the main window's size alone.
[Collection("Headless")]
public sealed class SettingsZoomRegressionTests(HeadlessSession headless) : SettingsWindowFixture(headless)
{
    [Theory]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    [Trait("Req", "UI-SETTINGS-001")]
    [Trait("Decision", "OD-0140")]
    public void Live_zoom_keeps_both_edges_of_the_real_Settings_page_inside_the_card(int percent)
    {
        WithManager((_, manager) =>
        {
            using var rig = new SettingsRiseRig();
            manager.ShowSettings(SettingsPage.Shortcuts);
            var window = Settings(manager);
            Settle(rig, window);
            var overlay = Overlay(manager);
            var card = overlay.Card;
            var view = View(manager);
            var client = window.ClientSize;
            manager.Model.Settings.AppZoom = percent;
            Drain(window);
            var topLeft = view.TranslatePoint(default, window)!.Value;
            var bottomRight = view.TranslatePoint(new Point(view.Bounds.Width, view.Bounds.Height), window)!.Value;
            var cardTopLeft = card.TranslatePoint(default, window)!.Value;
            var cardBottomRight = card.TranslatePoint(new Point(card.Bounds.Width, card.Bounds.Height), window)!.Value;
            Assert.InRange(topLeft.X - cardTopLeft.X, -.01, .01);
            Assert.InRange(topLeft.Y - cardTopLeft.Y, -.01, .01);
            Assert.InRange(bottomRight.X - cardBottomRight.X, -.01, .01);
            Assert.InRange(bottomRight.Y - cardBottomRight.Y, -.01, .01);
            // The card stays inside the window, clear of its edges.
            Assert.True(cardTopLeft.X > 0 && cardTopLeft.Y > 0, $"the card starts at {cardTopLeft}");
            Assert.True(cardBottomRight.X < client.Width && cardBottomRight.Y < client.Height, $"the card ends at {cardBottomRight}");
            Assert.Equal(client, window.ClientSize);
            manager.Model.Settings.AppZoom = 100;
            Drain(window);
            Assert.Equal(card.Bounds.Size, view.Bounds.Size);
            Assert.Single(Nodes<ZoomLayout>(overlay));
        });
    }

    private static void Settle(SettingsRiseRig rig, HostWindow window)
    {
        for (var i = 0; i < 30; i++) { rig.Frame(1.0 / 60); Drain(window); }
    }
}
