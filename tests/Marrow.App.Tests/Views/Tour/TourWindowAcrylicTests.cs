using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Xunit;

namespace Marrow.App.Tests.Views.Tour;

// ED-236 (OD-0005, OD-0014). TourWindowController.swift:82-83 makes the tour window clear (`isOpaque = false`,
// `backgroundColor = .clear`) and TourView.swift:79-86 draws the card as `.glassEffect(.regular, in: shape)` with the
// tint and the scrim over it, so over the desktop (and over the main window) the card blurs what is behind it. A
// Transparent hint left that behind sharp: the tour's text collided with the main window's text under it (native
// launch capture, 2026-10-02). The window composites Acrylic; the card's tint and scrim stay the app's.
[Collection("Headless")]
public sealed class TourWindowAcrylicTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-UPDATE-094")] // DroppyCode/App/Views/Tour/TourWindowController.swift:68-90
    public void The_tour_window_asks_for_the_Acrylic_backdrop_and_keeps_no_background_of_its_own() => headless.OnUiThread(() =>
    {
        var controller = TourWindowController.Shared;
        var window = controller.Present([new TourPage("page1", "Welcome", "Start here")], width: 660);
        try
        {
            Assert.Equal([WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Transparent], window.TransparencyLevelHint);
            Assert.Null(window.Background); // :83 backgroundColor = .clear: the card's own surfaces are the only paint
            Assert.Equal(WindowDecorations.None, window.WindowDecorations); // :69 borderless
        }
        finally
        {
            controller.Close();
        }
    });
}
