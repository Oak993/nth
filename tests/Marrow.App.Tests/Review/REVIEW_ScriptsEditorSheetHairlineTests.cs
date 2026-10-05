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
// curve begins and the sheet's 26 pt corners have no edge line at all (they had the source's arc hairline before). This builds the same composition.
[Collection("Headless")]
public sealed class REVIEW_ScriptsEditorSheetHairlineTests(HeadlessSession headless)
{
    [Fact]
    public void REVIEW_a_backdrop_inside_a_26_point_rounded_clip_still_has_its_hairline_on_the_curve() => headless.OnUiThread(() =>
    {
        var grey = Color.FromRgb(128, 128, 128);
        var sheet = new Border { Width = 400, Height = 300, ClipToBounds = true, Child = new Panel { Children = { new WindowBackdrop(0.5) } } }
            .RoundedRectangle(Chrome.WindowCornerRadius, RoundedCornerStyle.Continuous);
        var window = new AvaloniaWindow { Width = 400, Height = 300, RequestedThemeVariant = ThemeVariant.Dark, Content = sheet };
        window.Show();
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        window.Background = new SolidColorBrush(grey);
        for (var pass = 0; pass < 4; pass++) { window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        try
        {
            var diagonal = Enumerable.Range(0, 20).Select(i => (i, i)).ToArray();
            var colours = CornerSeam.Rgb(window, diagonal);
            static int Luma((int R, int G, int B) c) => c.R + c.G + c.B;
            var first = Enumerable.Range(0, 20).First(i => Math.Abs(Luma(colours[i]) - 3 * grey.R) > 60); // first pixel of the sheet, clear of the antialiased edge
            var body = Luma(colours[first + 6]);
            var onCurve = Math.Max(Math.Abs(Luma(colours[first + 1]) - body), Math.Abs(Luma(colours[first + 2]) - body));
            // the straight edge, for scale: the outermost row of the sheet, against three rows in
            var edge = CornerSeam.Rgb(window, (200, 0), (200, 4));
            var straight = Math.Abs(Luma(edge[0]) - Luma(edge[1]));
            Assert.True(straight > 20, $"the straight edge has a hairline ({straight})");
            Assert.True(onCurve > straight / 2, $"the 26 pt curve of a rounded-clipped backdrop has no hairline: {onCurve} levels against the straight edge's {straight} (diagonal {string.Join(" ", colours.Take(first + 7))})");
        }
        finally { window.Close(); }
    });
}
