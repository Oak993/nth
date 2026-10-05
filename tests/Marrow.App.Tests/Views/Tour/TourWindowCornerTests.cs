using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Views.Tour;

// Source: DroppyCode/App/Views/Tour/TourView.swift:62-96 (`let shape = RoundedRectangle(cornerRadius: 20, style: .continuous)`: the glass, the tint, the clip, the hairline
// and the scrim under them all take the one shape) over TourWindowController.swift:60-90 (a borderless clear window the size of that shape, so on the Mac the
// card IS the window and the corner outside the shape is clear). OD-0075 (owner, 2026-10-02: "Window corner: the wedge is filled with the window's scrim"); the owner's
// intent of 2026-10-05: Windows rounds window corners less than the app's glass shape, so the glass is extended to the window's edge and reads as one glass surface
// out to the OS edge (ED-NEW-corner-seam); ED-236 (the window is the system's Acrylic, ED-236 UNRESOLVED (a)).
//
// The tour window has window-shaped glass in the source, so it is the same kind of window as the main one: the card's 20 pt corner leaves a wedge of the square Acrylic
// window showing unscrimmed blur (measured over a (240,200,40) backdrop: (234,195,39) there beside the card's own colour). Headless, the window's background is a
// known colour (or stripes) standing in for the blur; the wedge pixel must be the card pixel in its column just inside the 20 pt shape.
[Collection("Headless")]
public sealed class TourWindowCornerTests(HeadlessSession headless)
{
    private const double CardRadius = 20; // TourView.swift:63
    private static readonly Color Backdrop = Color.FromRgb(240, 200, 40);

    private static void Settle(AvaloniaWindow window)
    {
        for (var pass = 0; pass < 6; pass++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-UPDATE-094")] // TourView.swift:62-96
    public void The_tour_windows_four_corners_outside_the_20_point_card_are_the_card_just_inside_it(bool dark, bool striped) => headless.OnUiThread(() =>
    {
        var controller = TourWindowController.Shared;
        var window = controller.Present([new TourPage("page1", "Welcome", "Start here"), new TourPage("page2", "Next", "Then here")], width: 660);
        try
        {
            window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Settle(window);
            // The window is clear over the system's Acrylic: a known backdrop stands in for that blur.
            window.Background = striped ? Marrow.App.Tests.Views.RootViewCornerWedgeTests.Stripes() : new SolidColorBrush(Backdrop);
            Settle(window);
            var width = (int)window.ClientSize.Width;
            var height = (int)window.ClientSize.Height;
            // The card's material shades a strip along each edge (about 10 px wide) and lights a rim along the bottom (about 3 px), so a wedge pixel is compared with the pixel in its
            // column deep in the card, and only where it is clear of the bottom rim and of the 45 degree line where the two meet: more rows up than columns in. The top corners are
            // the page's artwork, a picture that differs from row to row.
            var pairs = CornerSeam.Pairs(width, height, CardRadius, [2, 3], edgeInset: 6, clearance: 1)
                .Concat(CornerSeam.Pairs(width, height, CardRadius, [2, 3], edgeInset: 8, clearance: 1))
                .Where(pair => pair.Corner.StartsWith("bottom", StringComparison.Ordinal)).ToArray();
            CornerSeam.AssertSeamless(window, pairs, $"the tour window, {(dark ? "dark" : "light")}, {(striped ? "striped" : "solid")} backdrop", striped ? null : (Backdrop.R, Backdrop.G, Backdrop.B));
        }
        finally
        {
            controller.Close();
        }
    });
}
