using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class MCPSettingsPageSecondTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, TestRuntime> test) => headless.OnUiThread(() => {
        using var runtime = new TestRuntime(); using var frames = FrameClock.Source.Override(new ManualFrameSource()); using var harness = new AppModelHarness(withRuntime: false);
        using var scope = Presentation.Presenter.Override(new RecordingPresenter()); var model = new AppModel();
        runtime.OnMain(() => test(model, runtime)); Dispatcher.UIThread.RunJobs();
    });

    [Fact]
    [Trait("Req", "UI-MCP-004")] // App/Views/Settings/MCPSettingsPage.swift:101,923-949
    [Trait("Req", "UI-MCP-005")] // App/Views/Settings/MCPSettingsPage.swift:102-107,124-133
    public void Lead_information_has_22_target_and_exact_three_fact_rows()
    {
        Ui((model, _) => { var lead = new MCPLeadCard(); using var window = Attach(lead, model);
            var button = Nodes<Button>(lead).Single(x => AutomationProperties.GetName(x) == "About connected servers"); Assert.Equal(22, button.Width); Assert.Equal(22, button.Height); Assert.False(button.Focusable);
            var glyph = Nodes<SwiftImage>(button).Single(); Assert.Equal(FontWeight.Medium, SwiftImage.GetFont(glyph)!.Resolve().FontWeight);
            var facts = (Control)typeof(MCPLeadCard).GetMethod("Facts", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
            Assert.Equal(new[] { "Keys stay in your Credential Manager", "Disconnect to go back to your own setup", "Every provider, whatever the model" }, Texts(facts)); Assert.Equal(new Thickness(14), Assert.IsType<Border>(facts).Padding);
            Assert.All(Nodes<SwiftImage>(facts), x => Assert.Equal(14, x.Width));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-004")] // App/Views/Settings/MCPSettingsPage.swift:95 HStack(spacing: 4), centre alignment by default
    public void Lead_title_sits_on_the_centre_line_of_its_22_point_information_button()
    {
        Ui((model, _) => { var lead = new MCPLeadCard(); using var window = Attach(lead, model);
            var title = Nodes<TextBlock>(lead).Single(x => x.Text == "Connected servers lead"); var button = Nodes<Button>(lead).Single(x => AutomationProperties.GetName(x) == "About connected servers");
            // The 16-point title is shorter than the 22-point button: the HStack puts it at its own height on the button's centre line.
            Assert.Equal(title.DesiredSize.Height, title.Bounds.Height, 0.5);
            Assert.Equal(button.TranslatePoint(new Point(0, button.Bounds.Height / 2), lead)!.Value.Y, title.TranslatePoint(new Point(0, title.Bounds.Height / 2), lead)!.Value.Y, 0.5);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-006")] // App/Views/Settings/MCPSettingsPage.swift:38-47
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:40-45
    public void Connected_card_has_source_radius_and_dividers_and_header_style()
    {
        Ui((model, _) => { State(model, "github", new MCPConnectionState.Connected(1)); State(model, "gitlab", new MCPConnectionState.Connected(2)); var page = new MCPSettingsPage("absent"); using var window = Attach(page, model);
            var header = Nodes<TextBlock>(page).Single(x => x.Text == "Connected"); Assert.Equal(13, header.FontSize); Assert.Equal(FontWeight.SemiBold, header.FontWeight); Assert.Single(Nodes<ChromeCard>(page)); Assert.Single(Nodes<ChromeRowDivider>(page));
            Assert.Contains(Nodes<Border>(Nodes<ChromeCard>(page).Single()), b => b.CornerRadius == new CornerRadius(16));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-008")] // App/Views/Settings/MCPSettingsPage.swift:152-203
    [Trait("Req", "UI-MCP-009")] // App/Views/Settings/MCPSettingsPage.swift:165-168
    [Trait("Req", "UI-MCP-010")] // App/Views/Settings/MCPSettingsPage.swift:169-198
    public void Connected_row_has_22_icon_in_26_slot_and_enabled_switch_and_button_anchor()
    {
        Ui((model, _) => { var entry = Entry(); State(model, entry.Id, new MCPConnectionState.Connected(9)); Connection(model, new MCPConnection(entry.Id) { ServerVersion = "1.7", IsEnabled = true });
            var row = new MCPConnectedRow(entry); using var window = Attach(row, model); var border = Assert.IsType<Border>(row.Child); Assert.Equal(new Thickness(16,11,10,11), border.Padding);
            Assert.Equal("9 tools \U000000B7 1.7", Texts(row)[1]); // U+00B7 MIDDLE DOT
            var icon = Assert.Single(Nodes<MCPIcon>(row)); Assert.Equal(22, icon.Child!.Width); Assert.Equal(26, Assert.IsType<Border>(icon.Parent).Width);
            var toggle = Assert.Single(Nodes<SmallSwitch>(row)); toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Space }); Assert.False(model.Mcp.Connection(entry.Id)!.IsEnabled);
            var options = Nodes<Button>(row).Single(x => ToolTip.GetTip(x) as string == "Server options"); Assert.Equal(22, options.Width); Assert.Equal(22, options.Height);
            Assert.All(Nodes<Popup>(row), p => { Assert.Same(options, p.PlacementTarget); Assert.Equal(PlacementMode.Bottom, p.Placement); });
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-011")] // App/Views/Settings/MCPSettingsPage.swift:182-184
    [Trait("Req", "UI-MCP-012")] // App/Views/Settings/MCPSettingsPage.swift:185-188
    [Trait("Req", "UI-MCP-013")] // App/Views/Settings/MCPSettingsPage.swift:189
    [Trait("Req", "UI-MCP-014")] // App/Views/Settings/MCPSettingsPage.swift:190-192
    public void Connected_menu_has_refresh_show_tools_divider_and_destructive_disconnect()
    {
        Ui((model, _) => { var row = new MCPConnectedRow(Entry()); using var window = Attach(row, model); var menu = Invoke<Control>(row, "BuildMenu");
            Assert.Equal(new[] { "Refresh", "Show tools", "Disconnect" }, Nodes<Button>(menu).Select(AutomationProperties.GetName)); Assert.Single(Nodes<PopoverDivider>(menu));
            Assert.Equal(new[] { "arrow.clockwise", "wrench", "xmark.circle" }, Nodes<SwiftImage>(menu).Select(x => x.SymbolName));
            Assert.All(Nodes<Button>(menu), x => Assert.True(x.IsEnabled));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-015")] // App/Views/Settings/MCPSettingsPage.swift:205-228
    [Trait("Req", "UI-MCP-016")] // App/Views/Settings/MCPSettingsPage.swift:207-211
    [Trait("Req", "UI-MCP-017")] // App/Views/Settings/MCPSettingsPage.swift:212-227
    public void Tools_popover_shows_empty_note_or_exact_names_optional_summaries_and_padding()
    {
        Ui((model, _) => { var row = new MCPConnectedRow(Entry()); using var window = Attach(row, model); var empty = Invoke<Control>(row, "BuildTools"); Assert.Contains("Example tools", Texts(empty)); Assert.Contains("No tools listed yet.", Texts(empty));
            Connection(model, new MCPConnection("offline") { Tools = new[] { new MCPToolSummary("read", "Reads a document"), new MCPToolSummary("list", ""), new MCPToolSummary("write") } });
            var tools = Invoke<Control>(row, "BuildTools"); Assert.Equal(new[] { "Example tools", "read", "Reads a document", "list", "write" }, Texts(tools));
            var summary = Nodes<TextBlock>(tools).Single(x => x.Text == "Reads a document"); Assert.Equal(2, summary.MaxLines); Assert.Equal(11, summary.FontSize);
            Assert.All(Nodes<Border>(tools).Where(b => b.Child is Grid), b => Assert.Equal(new Thickness(8,3), b.Padding));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-018")] // App/Views/Settings/MCPSettingsPage.swift:249-303
    [Trait("Req", "UI-MCP-019")] // App/Views/Settings/MCPSettingsPage.swift:305-319
    public void Card_has_source_surface_padding_and_header_icon_name_vendor_geometry()
    {
        Ui((model, _) => { var card = new MCPServerCard(Entry()); using var window = Attach(card, model); var surface = Assert.IsType<Border>(Assert.IsType<Panel>(card.Child).Children[0]); Assert.Equal(new Thickness(14), surface.Padding);
            var header = Nodes<Grid>(card).Single(x => x.Children.OfType<MCPIcon>().Any()); Assert.Equal(10, header.ColumnSpacing); Assert.Equal(26, Assert.Single(Nodes<MCPIcon>(card)).Child!.Width);
            Assert.Equal(FontWeight.SemiBold, Nodes<TextBlock>(card).Single(x => x.Text == "Example").FontWeight);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-020")] // App/Views/Settings/MCPSettingsPage.swift:255-261
    [Trait("Req", "UI-MCP-021")] // App/Views/Settings/MCPSettingsPage.swift:262-268
    public void Summary_reserves_two_lines_and_tools_reserve_one_space_and_truncate_at_tail()
    {
        Ui((model, _) => { var card = new MCPServerCard(Entry()); using var window = Attach(card, model); var summary = Nodes<TextBlock>(card).Single(x => x.Text == "Summary"); Assert.Equal(2, summary.MaxLines); Assert.True(summary.MinHeight > summary.FontSize * 2);
            var tools = Nodes<TextBlock>(card).Single(x => x.Text == " "); Assert.Equal(10.5, tools.FontSize); Assert.Equal(1, tools.MaxLines); Assert.Equal(TextTrimming.CharacterEllipsis, tools.TextTrimming); Assert.Equal(TextWrapping.NoWrap, tools.TextWrapping); Assert.Equal(new Thickness(0,0,24,0), tools.Margin);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-022")] // App/Views/Settings/MCPSettingsPage.swift:269-274
    [Trait("Req", "UI-MCP-023")] // App/Views/Settings/MCPSettingsPage.swift:275-280
    public void Failure_and_setup_have_exact_message_line_limits_and_sizes()
    {
        Ui((model, _) => { State(model,"offline",new MCPConnectionState.Failed("Rejected")); var failure = new MCPServerCard(Entry()); using var window = Attach(failure,model); var message = Nodes<TextBlock>(failure).Single(x=>x.Text=="Rejected"); Assert.Equal(3,message.MaxLines); Assert.Equal(11,message.FontSize);
            State(model,"offline",new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Enable MCP", "Explanation", new[] { "Step" }, "Open", "https://example.invalid", null))); var setup = new MCPServerCard(Entry()); using var second = Attach(setup,model); Assert.Equal(2,Nodes<TextBlock>(setup).Single(x=>x.Text=="Enable MCP").MaxLines);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:324-330
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:324-330
    public void Not_connected_actions_use_correct_connect_or_sign_in_label_and_regular_glass()
    {
        Ui((model, _) => { foreach(var oauth in new[]{false,true}) { var card=new MCPServerCard(Entry(transport:oauth?new MCPTransport.Oauth("https://example.invalid"):null)); using var window=Attach(card,model); var button=Button(card,oauth?"Sign in":"Connect"); Assert.Contains(Nodes<Border>(button),b=>SwiftGlass.GetEffect(b) is { TintColor:null, IsInteractive:true }); } });
    }
    [Fact]
    [Trait("Req", "UI-MCP-026")] // App/Views/Settings/MCPSettingsPage.swift:331-338
    [Trait("Req", "UI-MCP-027")] // App/Views/Settings/MCPSettingsPage.swift:331-338
    public void Connecting_and_oauth_waiting_use_three_cell_spinner_and_fixed_label()
    {
        Ui((model, _) => { State(model,"offline",new MCPConnectionState.Connecting()); foreach(var oauth in new[]{false,true}) { var card=new MCPServerCard(Entry(transport:oauth?new MCPTransport.Oauth("https://example.invalid"):null)); using var window=Attach(card,model); Assert.Single(Nodes<NthStatusMark>(card)); var text=Nodes<TextBlock>(card).Single(x=>x.Text==(oauth?"Waiting":"Connecting")); Assert.Equal(11,text.FontSize); Assert.Equal(TextWrapping.NoWrap,text.TextWrapping); Assert.DoesNotContain(Nodes<Button>(card), b=>AutomationProperties.GetName(b)=="Cancel"); } });
    }
}

