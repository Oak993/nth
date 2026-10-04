using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageTestFirst;

namespace Marrow.App.Tests.Views.Settings;

// PR-MCP-199 against the reference frames (1-settings-tour.mov S 180.50, 184.00 and 185.50; stills in E:\Droppy\work\reference-video\stills at
// 2560 x 1440, 1 px = 1 pt: the 780 x 580 Settings window is 780 px wide). Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift:35-63.
//
// The comparison found one real difference and this unit fixed it: the port gave every text line the font's own height (Inter: 13.3 pt at 11 pt, 14.5 at
// 12, 15.7 at 13), the Mac gives 16, 17 and 18 (SOURCE FACT, measured on the stills: wrapped lines of the lead card and the Custom note are 17 apart at the
// callout 12, the two-line GitHub detail 16 apart at the subheadline 11, a Connected row is 58 high = 11 + 18 + 2 + 16 + 11). Every card of the page was
// 3 to 7 points shorter than the Mac's (lead 90 against 94, the Connected card 171 against 192, a catalog card 118 against 126). SwiftFont now sets the
// line height of the default design at those three sizes (ED-NEW-frames-1). The numbers below are the stills' (pixel rows counted on them), the port's
// are the headless layout of the same page: the same three servers connected, the cards 520 wide as in the window's content column.
[Collection("Headless")]
public sealed class MCPSettingsPageFrameTests(HeadlessSession headless)
{
    private static string[] Titles(Control control) => Nodes<TextBlock>(control).Select(text => text.Text ?? "").ToArray();

    private void OnPage(Action<MCPSettingsPage, Avalonia.Controls.Window, Func<Control, Rect>> check) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        using var audio = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        var model = runtime.OnMain(() => new AppModel());
        MCPSettingsPage page = null!; Avalonia.Controls.Window window = null!;
        runtime.OnMain(() =>
        {
            // The Connected list of the video: Filesystem 14 tools 0.2.0, GitHub 45 tools with a version line that wraps, Memory 9 tools 0.6.3.
            Publish(model, "filesystem", new MCPConnectionState.Connected(14));
            Publish(model, "github", new MCPConnectionState.Connected(45));
            Publish(model, "memory", new MCPConnectionState.Connected(9));
            Connection(model, new MCPConnection("filesystem") { ServerVersion = "0.2.0" });
            Connection(model, new MCPConnection("github") { ServerVersion = "github-mcp-server/remote-7835a232463c42f2f33cb50b3f4fa043cf66697c" });
            Connection(model, new MCPConnection("memory") { ServerVersion = "0.6.3" });
            page = new MCPSettingsPage("");
            var host = new Border { Width = 520, Child = page, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            AppEnvironment.SetAppModel(host, model);
            window = new Avalonia.Controls.Window { Content = new ScrollViewer { Content = host }, Width = 520, Height = 3000 };
            window.Show();
        });
        runtime.AdvanceMilliseconds(900); // the celebration of a newly connected card ends: the Connected section opens
        try
        {
            runtime.OnMain(() =>
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Rect Box(Control c) => new(c.TranslatePoint(default, page)!.Value, c.Bounds.Size);
                check(page, window, Box);
            });
        }
        finally { runtime.OnMain(window.Close); }
    });

    [Fact]
    [Trait("Req", "PR-MCP-199")] // App/Views/Settings/MCPSettingsPage.swift:35-63; UI/Chrome/Chrome.swift:59,79; the stills of 1-settings-tour.mov S 180.50-185.50
    public void The_page_stacks_lead_connected_custom_grids_and_footer_and_every_card_is_the_height_the_Mac_draws_it()
    {
        OnPage((page, window, box) =>
        {
            // :36 a leading-aligned vertical stack with 20 between sections.
            var root = Assert.IsType<StackPanel>(page.Child);
            Assert.Equal(20, root.Spacing);
            // :37-62 the order: the lead card, Connected, Custom, one section per non-empty category, the footer.
            var order = root.Children.Where(child => child.Bounds.Height > 0).Select(child => child switch
            {
                MCPLeadCard => "lead",
                ChromeSection section => "section:" + Titles(section)[0],
                MCPCustomSection => "custom",
                TextBlock => "footer",
                _ => child.GetType().Name,
            }).ToArray();
            Assert.Equal(new[] { "lead", "section:Connected", "custom", "section:Developer", "section:Browser", "section:Search", "section:Work", "section:Data", "section:Cloud", "section:Knowledge", "footer" }, order);
            var kids = root.Children.Where(child => child.Bounds.Height > 0).ToArray();
            for (var i = 1; i < kids.Length; i++) Assert.Equal(20, box(kids[i]).Top - box(kids[i - 1]).Bottom, 0.5);
            var footer = Assert.IsType<TextBlock>(root.Children[^1]);
            Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through Marrow itself.", footer.Text);
            Assert.Equal(11, footer.FontSize); // .subheadline
            Assert.Equal(16, box(footer).Height / (footer.TextWrapping == Avalonia.Media.TextWrapping.Wrap && box(footer).Height > 20 ? 2 : 1), 0.5); // two 16 pt lines at 520: the Mac wraps it in two (S 188.14)

            // The lead card: 94 tall on the still (its card spans pixel rows 464 to 557).
            var lead = box(root.Children[0]);
            Assert.Equal(94, lead.Height, 1);
            // The Connected section: its title box 18 + 10, then the card 192 tall (rows 606 to 797): Filesystem 58, a divider, GitHub 74, a divider, Memory 58.
            var connected = Assert.IsType<ChromeSection>(root.Children[1]);
            var card = Nodes<ChromeCard>(connected).Single();
            Assert.Equal(192, box(card).Height, 1);
            Assert.Equal(48, box(card).Top - lead.Bottom, 1); // 20 + the 18 point title + 10 (the stills: 48.5)
            var rows = Nodes<MCPConnectedRow>(card).Select(row => box(row)).ToArray();
            Assert.Equal(new[] { 58d, 74, 58 }, rows.Select(row => row.Height).ToArray());
            Assert.Equal(1, rows[1].Top - rows[0].Bottom, 0.5); // the divider
            // The Custom card: the note's two 17 pt lines, the divider, the Add server row: 97 (the still: 443 to 539 at 2x).
            var custom = Nodes<ChromeCard>(root.Children[2]).Single();
            Assert.Equal(97, box(custom).Height, 1);
            // One section per non-empty category, each a two-column grid of 255 wide cards with 10 between the columns and the rows.
            var developer = Assert.IsType<ChromeSection>(root.Children[3]);
            var cards = Nodes<MCPServerCard>(developer).Select(c => box(c)).ToArray();
            Assert.Equal(3, cards.Length); // GitLab, Git, Sentry (S 184.00)
            Assert.All(cards, c => { Assert.Equal(255, c.Width, 1); Assert.Equal(126, c.Height, 1); }); // a card is 126 tall: its title pixel rows are 136 apart row to row with 10 between
            Assert.Equal(10, cards[1].Left - cards[0].Right, 0.5);
            Assert.Equal(136, cards[2].Top - cards[0].Top, 0.5); // 708 to 844 on S 184.00
            // Across sections: the last card, 20, the 18 + 10 title, the next card: 174 from title to title on S 185.50 (706 to 880).
            var browser = Assert.IsType<ChromeSection>(root.Children[4]);
            var firstBrowser = Nodes<MCPServerCard>(browser).Select(c => box(c)).First();
            Assert.Equal(126 + 20 + 28, firstBrowser.Top - cards[2].Top, 0.5);
            // A card whose name wraps to two lines is 18 taller (Chrome DevTools), so its row is 144 and the row after follows 10 below.
            var browserCards = Nodes<MCPServerCard>(browser).Select(c => box(c)).ToArray();
            Assert.Equal(144, browserCards[0].Height, 1);
            Assert.Equal(144, browserCards[1].Height, 1);
            Assert.Equal(154, browserCards[2].Top - browserCards[0].Top, 0.5);
        });
    }
}
