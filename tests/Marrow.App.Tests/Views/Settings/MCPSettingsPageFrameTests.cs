using Avalonia.Controls;
using Avalonia.Threading;
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

// PR-MCP-199's capture comparison (ED-290 (1), SETTLED 2026-10-04): the MCP page measured on 1-settings-tour.mov, 1 px = 1 pt (the
// Settings window is 780 x 578 at S 183.5; these S frames carry no row pairing, so a reading is exact to the pixel, compared here to
// within a point). The page column is 520 wide (the Connected card and two 255-wide grid cards with 10 between them, STOP
// opencode-proofs-mcp-a #1). Readings (porting E_lineboxes_verify, rows 1, 2, 4, 5 and 7):
// - S 183.5: the lead card rows 461..554 = 94 (16 + the 22-point info button + 6 + two callout lines of 17 + 16, MCPSettingsPage.swift:89-122,
//   :931); the Connected card 603..794 = 192 with dividers at 661 and 736, so rows 58 / 74 / 58 (Filesystem "14 tools · 0.2.0", GitHub
//   "45 tools · github-mcp-server/remote-7835a232463c42f2f33cb50b3f4fa043cf66697c" wrapped to two lines, Memory "9 tools · 0.6.3"; 11 + body 18
//   + 2 + subheadline 16 + 11, :153-202); the Custom card 843..939 = 97 (the callout note 22 + 34, a divider, the Add server row 11 + body 18 + 11,
//   :387-440).
// - S 186.0: the Search grid's "Brave Search" card 674..799 = 126 (14 + body 18 + 1 + subheadline 16 + 8 + callout 34 + 8 + the 10.5 mono
//   line 13 + 14, :250-316) and the grid's row pitch 136 (126 + 10, :51-52).
// - S 187.5: "Sequential Thinking" wraps its body semibold title to two lines (baselines 656, 674): its card 629..772 = 144 (126 + 18) and the
//   row pitch to the next row 154.
[Collection("Headless")]
public sealed class MCPSettingsPageFrameTests(HeadlessSession headless)
{
    private const double Column = 520;
    private const double Reading = 1; // the frames' pixel, at 1 px = 1 pt

    private void Page(Action<AppModel, TestRuntime, MCPSettingsPage> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        using var frames = FrameClock.Source.Override(new ManualFrameSource());
        using var presentation = Presentation.Presenter.Override(new RecordingPresenter());
        using var audio = SoundOutput.Current.Override(new MCPSettingsSilentSound());
        var model = runtime.OnMain(() => new AppModel());
        MCPSettingsPage page = null!; IDisposable window = null!;
        runtime.OnMain(() => { page = new MCPSettingsPage(""); window = Attach(new Border { Width = Column, Child = page }, model); });
        try { test(model, runtime, page); }
        finally { runtime.OnMain(window.Dispose); }
        runtime.OnMain(() => Dispatcher.UIThread.RunJobs());
    });

    private static void Settle(Control page)
    {
        for (var pass = 0; pass < 4; pass++) { ((Avalonia.Controls.Window)TopLevel.GetTopLevel(page)!).UpdateLayout(); Dispatcher.UIThread.RunJobs(); }
        Layout(page);
    }

    private static void Connect(AppModel model, string id, int tools, string version)
    {
        Connection(model, new MCPConnection(id) { ServerVersion = version });
        Publish(model, id, new MCPConnectionState.Connected(tools));
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Card(MCPSettingsPage page, string name) => Nodes<MCPServerCard>(page).Single(card => Texts(card).Contains(name));

    [Fact]
    [Trait("Req", "PR-MCP-199")] // MCPSettingsPage.swift:89-122, :153-202, :387-440; 1-settings-tour.mov S 183.5
    public void At_S_183_5_the_lead_card_is_94_the_Connected_rows_58_74_58_in_a_192_card_and_the_Custom_card_97() => Page((model, runtime, page) =>
    {
        runtime.OnMain(() =>
        {
            Connect(model, "filesystem", 14, "0.2.0");
            Connect(model, "github", 45, "github-mcp-server/remote-7835a232463c42f2f33cb50b3f4fa043cf66697c");
            Connect(model, "memory", 9, "0.6.3");
        });
        runtime.AdvanceMilliseconds(900); // the celebration ends and the Connected section opens (MCPSettingsPage.swift:38-47)
        runtime.OnMain(() => Measure(page));
    });

    private static void Measure(MCPSettingsPage page)
    {
        Settle(page);
        Assert.Equal(Column, page.Bounds.Width, 0.5);
        Assert.Equal(94, Assert.Single(Nodes<MCPLeadCard>(page)).Bounds.Height, Reading);

        var root = Assert.IsType<StackPanel>(page.Child);
        var connected = Assert.IsType<ChromeSection>(root.Children[1]);
        var rows = Nodes<MCPConnectedRow>(connected);
        Assert.Equal(new[] { "Filesystem", "GitHub", "Memory" }, rows.Select(row => Texts(row)[0]));
        Assert.Equal(new[] { "14 tools \u00B7 0.2.0", "45 tools \u00B7 github-mcp-server/remote-7835a232463c42f2f33cb50b3f4fa043cf66697c", "9 tools \u00B7 0.6.3" }, rows.Select(row => Texts(row)[1]));
        Assert.Equal(58, rows[0].Bounds.Height, Reading);
        Assert.Equal(74, rows[1].Bounds.Height, Reading);
        Assert.Equal(58, rows[2].Bounds.Height, Reading);
        Assert.Equal(192, Assert.Single(Nodes<ChromeCard>(connected)).Bounds.Height, Reading);

        var custom = Assert.Single(Nodes<MCPCustomSection>(page));
        Assert.Equal(97, Assert.Single(Nodes<ChromeCard>(custom)).Bounds.Height, Reading);
    }

    [Fact]
    [Trait("Req", "PR-MCP-199")] // MCPSettingsPage.swift:250-316, :51-52; 1-settings-tour.mov S 186.0 and S 187.5
    public void At_S_186_0_a_catalog_card_is_126_at_a_136_row_pitch_and_at_S_187_5_a_two_line_title_makes_it_144_at_154() => Page((model, runtime, page) => runtime.OnMain(() =>
    {
        Settle(page);
        var brave = Card(page, "Brave Search");
        Assert.Equal(126, brave.Bounds.Height, Reading);
        Assert.Equal(136, Below(page, brave).Bounds.Top - brave.Bounds.Top, Reading);

        var thinking = Card(page, "Sequential Thinking");
        var title = Nodes<TextBlock>(thinking).First(text => text.Text == "Sequential Thinking");
        Assert.Equal(2, title.TextLayout.TextLines.Count); // the title wraps, as at S 187.5
        Assert.Equal(144, thinking.Bounds.Height, Reading);
        Assert.Equal(154, Below(page, thinking).Bounds.Top - thinking.Bounds.Top, Reading);
    }));

    // The card in the same grid column one row down (MCPSettingsPage.swift:51 LazyVGrid, two flexible columns).
    private static Control Below(MCPSettingsPage page, Control card) =>
        Nodes<MCPServerCard>(page).Single(other => other.Parent == card.Parent && Grid.GetColumn(other) == Grid.GetColumn(card) && Grid.GetRow(other) == Grid.GetRow(card) + 1);
}
