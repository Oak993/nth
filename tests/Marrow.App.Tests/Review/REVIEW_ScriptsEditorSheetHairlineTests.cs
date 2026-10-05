using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Review;

// REVIEW of 1b8c75d08 (ED-NEW-corner-seam). ChatView.PresentScriptsEditor (ChatView.cs:2053-2056) still clips a WindowBackdrop to a 26 pt continuous
// Border (ED item 8 (d): "unchanged"). But the backdrop's hairline is now the rectangle's inner stroke, so inside that rounded Border it ends where the
// curve begins and the sheet's 26 pt corners have no edge line at all (they had the source's arc hairline before). This builds the same composition, with the option ChatView.PresentScriptsEditor now passes (WindowBackdrop's roundedHairline: the source's curve for a backdrop that is not a window's whole content).
[Collection("Headless")]
public sealed class REVIEW_ScriptsEditorSheetHairlineTests(HeadlessSession headless)
{
    [Fact]
    [Trait("Decision", "OD-0075")]
    [Trait("Decision", "ED-236")] // ED-NEW-corner-seam: the hairline of a backdrop clipped to its own shape follows that shape
    public void REVIEW_a_backdrop_inside_a_26_point_rounded_clip_still_has_its_hairline_on_the_curve() => headless.OnUiThread(() =>
    {
        var grey = Color.FromRgb(128, 128, 128);
        var sheet = new Border { Width = 400, Height = 300, ClipToBounds = true, Child = new Panel { Children = { new WindowBackdrop(0.5, roundedHairline: true) } } }
            .RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);
        var window = new AvaloniaWindow { Width = 400, Height = 300, RequestedThemeVariant = ThemeVariant.Dark, Content = sheet };
        window.Show();
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        window.Background = new SolidColorBrush(grey);
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            static int Luma((int R, int G, int B) c) => c.R + c.G + c.B;
            // The curve, measured across it: along the rows 10, 20 and 30 (the curve runs between 0 and 6 px in from the left edge there) the hairline is the pixel just inside the
            // shape that differs most from the body. (A first version of this test looked for the hairline from the first pixel that differs by 60 from the
            // backdrop along the diagonal, which is past the hairline: the stroke is the pixel before it, where the antialiased edge and the stroke share a pixel.)
            var body = Luma(CornerSeam.Rgb(window, (60, 20))[0]);
            int OnCurve(int row) => CornerSeam.Rgb(window, Enumerable.Range(0, 10).Select(x => (x, row)).ToArray())
                .Where(c => Math.Abs(Luma(c) - 3 * grey.R) > 30) // inside the shape: not the backdrop beside it (the antialiased edge pixel is far from both)
                .Select(c => Math.Abs(Luma(c) - body)).DefaultIfEmpty(0).Max();
            var onCurve = new[] { 10, 20, 30 }.Select(OnCurve).Min();
            // the straight edge, for scale: the outermost row of the sheet, against three rows in
            var edge = CornerSeam.Rgb(window, (200, 0), (200, 4));
            var straight = Math.Abs(Luma(edge[0]) - Luma(edge[1]));
            Assert.True(straight > 20, $"the straight edge has a hairline ({straight})");
            Assert.True(onCurve > straight / 2, $"the 26 pt curve of a rounded-clipped backdrop has no hairline: {onCurve} levels against the straight edge's {straight}");
        }
        finally { window.Close(); }
    });
}
