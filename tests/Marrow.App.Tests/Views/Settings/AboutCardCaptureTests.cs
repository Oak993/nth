using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.UI.Chrome;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Views.Settings;

// PR-SETTINGS-238 (verification: capture comparison of the card), PR-UPDATE-084.
// The reference still is E:\Droppy\work\reference-video\stills\scene-1-settings-tour-213.00.png, the About page at S 213.0 (the Settings window
// at 1 px = 1 pt, its origin at screen (891, 404); measured with PIL, single sample): card x 235 to 755 (520 wide), top 80; header 88 high
// (a divider 88 below the card top: 16 + the 56 icon + 16); the icon frame at the card's (16, 16); the version block 14 after the icon;
// the dividers 88, 163, 222 and 281 below the card top; the Show pill 48 x 20 with its right edge 10 inside the card, centred in its row.
// The words differ by owner ruling (OD-0012 F1, H; OD-0072) and the faces differ (Inter for SF, ED-059), so the row heights that follow the
// text are not compared; the boxes the source fixes are.
[Collection("Headless")]
public sealed class AboutCardCaptureTests(HeadlessSession headless) : SettingsFinishFixture(headless)
{
    [Fact]
    [Trait("Req", "PR-UPDATE-084")] // App/Views/Settings/SettingsView.swift:991-1002; OD-0012 H, OD-0072 (the sentences)
    public void App_card_paragraphs_state_what_the_app_is_and_how_it_is_licensed_in_body_primary_and_callout_secondary()
    {
        With(() => new SettingsView(), (window, _, _, w) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(w); var view = Assert.Single(Nodes<AboutSettingsPage>(window)); // inside the real window, where the text colour is set
            var card = Nodes<ChromeCard>(view)[0]; var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]);
            Assert.Equal(9, rows.Children.Count); // header, divider, Welcome tour, divider, Software update, divider, paragraph, divider, licence
            var what = Assert.IsType<TextBlock>(rows.Children[6]); var licence = Assert.IsType<TextBlock>(rows.Children[8]);
            Assert.IsType<ChromeRowDivider>(rows.Children[5]); Assert.IsType<ChromeRowDivider>(rows.Children[7]); // :989, :996
            // :991-995 `.font(.system(.body))` (13) in the default foreground, :997-1002 `.font(.system(.callout))` (12) in Chrome.secondaryText; each with
            // leading 16, trailing Chrome.rowControlTrailingPadding and vertical 11; both are plain text.
            Assert.Equal("A native home for your coding agents, built in C# with Avalonia.", what.Text); // OD-0012 H: the owner's sentence
            Assert.Equal("Marrow is licensed under the GNU AGPL v3.0 with attribution terms; the terminal is Marrow's own, built on Windows ConPTY.", licence.Text); // OD-0072
            Assert.Equal(13, what.FontSize); Assert.Equal(12, licence.FontSize); Assert.Equal(FontWeight.Normal, what.FontWeight); Assert.Equal(FontWeight.Normal, licence.FontWeight);
            Assert.False(what.IsSet(TextBlock.ForegroundProperty)); // no colour of its own: the default, primary, text colour
            Assert.Equal(Chrome.PrimaryText.Resolve(what.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(what.Foreground).Color);
            Assert.Equal(Chrome.SecondaryText.Resolve(licence.ActualThemeVariant), Assert.IsAssignableFrom<ISolidColorBrush>(licence.Foreground).Color);
            foreach (var paragraph in new[] { what, licence }) { Assert.Equal(new Thickness(16, 11, Chrome.RowControlTrailingPadding, 11), paragraph.Margin); Assert.Equal(TextWrapping.Wrap, paragraph.TextWrapping); Assert.Empty(Nodes<Button>(paragraph)); }        });
    }

    private static int n; private static void Near(double expected, double actual, int places) { n++; Assert.True(Math.Abs(expected - actual) < Math.Pow(10, -places) / 2, $"check {n}: expected {expected}, was {actual}"); }
    private static Rect BoxIn(Control c, Visual to) => new(c.TranslatePoint(default, to)!.Value, c.Bounds.Size);

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-SETTINGS-238's capture comparison; its strings, sizes and order are proven in SettingsRestAboutTests and SettingsViewTests
    public void The_app_card_boxes_match_the_reference_still()
    {
        With(() => new SettingsView(), (view, _, h, w) =>
        {
            SettingsNavigation.Shared.RequestedPage = SettingsPage.About; Drain(w); Tick(h, 400);
            var page = Assert.Single(Nodes<AboutSettingsPage>(view)); var card = Nodes<ChromeCard>(page)[0];
            var box = BoxIn(card, view);
            // the still's edges are read to the pixel (the card is a 3 % wash): within a point
            Assert.InRange(box.X, 234, 236); Assert.InRange(box.Y, 79, 81); Assert.InRange(box.Width, 519, 521); // the card spans the pane's content width, 23 clear of the sidebar and 25 of the window's right edge
            var rows = Assert.IsType<StackPanel>(Assert.IsType<StackPanel>(Assert.IsType<Border>(card.Child).Child).Children[0]);
            var header = Assert.IsType<StackPanel>(rows.Children[0]); var icon = Assert.Single(Nodes<Image>(header)); var labels = Assert.Single(Nodes<StackPanel>(header), s => s.Spacing == 3);
            var iconBox = BoxIn(icon, card); Near(16, iconBox.X, 1); Near(16, iconBox.Y, 1); Near(56, iconBox.Width, 1); Near(56, iconBox.Height, 1);
            Near(14, BoxIn(labels, card).X - iconBox.Right, 1); // HStack(spacing: 14)
            Near(88, BoxIn(Assert.IsType<ChromeRowDivider>(rows.Children[1]), card).Y, 1); // the header is 16 + 56 + 16 high
            var tour = Assert.IsType<ChromeRow>(rows.Children[2]); var show = Assert.Single(Nodes<Button>(tour)); var showBox = BoxIn(show, card); var tourBox = BoxIn(tour, card);
            Assert.InRange(showBox.Width, 45, 52); Near(20, showBox.Height, 1); // the small accent capsule; the reference's 48 is its label's width plus 21, so the Inter label moves it by a point or two
            Near(520 - 10, showBox.Right, 1); // Chrome.rowControlTrailingPadding
            Assert.InRange(showBox.Y + showBox.Height / 2, tourBox.Y + tourBox.Height / 2 - 0.5, tourBox.Y + tourBox.Height / 2 + 0.5); // centred in its row (to the layout's half point)
        });
    }
}
