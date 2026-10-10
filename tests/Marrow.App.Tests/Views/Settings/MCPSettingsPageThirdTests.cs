using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
public sealed class MCPSettingsPageThirdTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel, ManualFrameSource> test) => headless.OnUiThread(() => {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime:false); var frames=new ManualFrameSource(); using var frameScope=FrameClock.Source.Override(frames);
        using var scope=Presentation.Presenter.Override(new RecordingPresenter());var model=new AppModel();runtime.OnMain(()=>test(model,frames));Dispatcher.UIThread.RunJobs();
    });
    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:341-344
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:345-348
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:345-348
    public void Setup_uses_prominent_glass_and_failure_labels_follow_oauth()
    {
        Ui((model,_)=>{var guide=new MCPSetupGuide("Setup","Explanation",new[]{"Step"},"Open","https://example.invalid",null);State(model,"offline",new MCPConnectionState.NeedsSetup(guide));var card=new MCPServerCard(Entry());using var window=Attach(card,model);var fix=Button(card,"Fix it");Assert.Contains(Nodes<Border>(fix),b=>SwiftGlass.GetEffect(b)?.TintColor is not null);
            State(model,"offline",new MCPConnectionState.Failed("Rejected"));foreach(var oauth in new[]{false,true}){var retry=new MCPServerCard(Entry(transport:oauth?new MCPTransport.Oauth("https://example.invalid"):null));using var second=Attach(retry,model);Assert.NotNull(Button(retry,oauth?"Sign in again":"Try again"));}
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-031")] // App/Views/Settings/MCPSettingsPage.swift:297-301,905-909
    public void Corner_information_is_bottom_trailing_with_ten_padding_and_named_for_entry()
    {
        Ui((model,_)=>{var card=new MCPServerCard(Entry());using var window=Attach(card,model);var info=Assert.Single(Nodes<MCPInfoButton>(card));Assert.Equal(new Thickness(10),info.Margin);Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right,info.HorizontalAlignment);Assert.Equal(Avalonia.Layout.VerticalAlignment.Bottom,info.VerticalAlignment);Assert.Equal("About Example",AutomationProperties.GetName(Assert.Single(Nodes<Button>(info))));});
    }
    [Fact]
    [Trait("Req", "UI-MCP-032")] // App/Views/Settings/MCPSettingsPage.swift:360-375
    public void Celebration_centres_the_minimum_120_block_and_starts_spring_when_motion_is_on()
    {
        Ui((model,frames)=>{State(model,"offline",new MCPConnectionState.Connected(3));var card=new MCPServerCard(Entry(),true);using var window=Attach(card,model);var success=Nodes<StackPanel>(card).Single(x=>x.MinHeight==120);Assert.Equal(8,success.Spacing);Assert.Equal(Avalonia.Layout.VerticalAlignment.Center,success.VerticalAlignment);Assert.Contains("Connected \U000000B7 3 tools",Texts(card));Assert.True(frames.Pending>0);}); // U+00B7 MIDDLE DOT
    }
    [Fact]
    [Trait("Req", "UI-MCP-032")] // App/Views/Settings/MCPSettingsPage.swift:360-375
    [Trait("Decision", "ED-011")]
    public void Success_glyph_continues_after_response_time_until_the_shared_spring_settles() => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime: false);
        var frames = new ManualFrameSource(); using var source = FrameClock.Source.Override(frames);
        MCPServerCard card = null!; IDisposable window = null!; SwiftImage glyph = null!;
        runtime.OnMain(() =>
        {
            var model = new AppModel(); State(model, "offline", new MCPConnectionState.Connected(3));
            card = new MCPServerCard(Entry(), true); window = Attach(card, model); glyph = Assert.Single(Nodes<SwiftImage>(card));
            Assert.Equal(0.5, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleX);
        });
        try
        {
            runtime.AdvanceMilliseconds(500); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(500)));
            runtime.OnMain(() =>
            {
                // :373 spring(duration: 0.5, bounce: 0.4). ED-011: damping 0.6; response is not settling time.
                var omega = 2 * Math.PI / 0.5; var damped = omega * 0.8;
                var progress = 1 - Math.Exp(-0.6 * omega * 0.5) * (Math.Cos(damped * 0.5) + 0.75 * Math.Sin(damped * 0.5));
                Assert.Equal(0.5 + 0.5 * progress, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleX, 7);
                Assert.True(frames.Pending > 0);
            });
            // ED-011 settling duration ln(1000)/(0.6 * 2*pi/0.5) is below 0.920 s.
            runtime.AdvanceMilliseconds(420); runtime.OnMain(() => frames.Advance(TimeSpan.FromMilliseconds(420)));
            runtime.OnMain(() => { Assert.Equal(1, Assert.IsType<ScaleTransform>(glyph.RenderTransform).ScaleX); Assert.Equal(0, frames.Pending); });
        }
        finally { runtime.OnMain(window.Dispose); }
    });

    [Fact]
    [Trait("Rule", "R-VIEW-11")] // App/Views/Settings/MCPSettingsPage.swift:508-515; UI-MCP-038 font subset
    public void Custom_connecting_label_applies_the_shared_subheadline_font()
    {
        Ui((model, _) =>
        {
            var server = Custom(); State(model, server.Id, new MCPConnectionState.Connecting());
            var row = new MCPCustomRow(server); using var window = Attach(row, model);
            var label = Nodes<TextBlock>(row).Single(x => x.Text == "Connecting");
            Assert.Equal(11, label.FontSize);
            Assert.Equal(SwiftFont.System(SwiftFont.TextStyle.Subheadline).Resolve().FontFamily, label.FontFamily);
            Assert.Equal(FontWeight.Normal, label.FontWeight); Assert.Equal(TextWrapping.NoWrap, label.TextWrapping);
            Assert.Single(Nodes<NthStatusMark>(row)); // nth step 11
        });
    }

    // STOP App.Views.Settings.MCPSettingsPage #13
    // Kind: partial record verification. Swift: MCPSettingsPage.swift:508-515. Records: UI-MCP-038.
    // This regression proves the Connecting label's font, fixed no-wrap text and spinner presence only.
    // Needed for whole-record acceptance: spacing 6, spinner cellSize 3, source color/fixed sizing and
    // all state-gating checks in one complete verification. UI-MCP-039 instead names the connected enable
    // switch (:516-520); its separate existing coverage is retained unchanged below.

    [Fact]
    [Trait("Req", "UI-MCP-033")] // App/Views/Settings/MCPSettingsPage.swift:394
    [Trait("Req", "UI-MCP-034")] // App/Views/Settings/MCPSettingsPage.swift:397-403
    [Trait("Req", "UI-MCP-035")] // App/Views/Settings/MCPSettingsPage.swift:417-440
    public void Custom_empty_section_has_exact_note_header_and_full_width_add_row_with_plus_slot()
    {
        Ui((model,_)=>{var section=new MCPCustomSection("");using var window=Attach(section,model);Assert.Equal("Custom",Texts(section)[0]);var note=Nodes<TextBlock>(section).Single(x=>x.Text!.StartsWith("Servers of your own:",StringComparison.Ordinal));Assert.Equal(new Thickness(16,11),note.Margin);Assert.Equal(12,note.FontSize);Assert.Single(Nodes<ChromeRowDivider>(section));
            var add=Button(section,"Add server");var icon=Nodes<SwiftImage>(add).Single(x=>x.SymbolName=="plus.circle");Assert.Equal(26,Assert.IsType<Border>(icon.Parent).Width);Assert.Equal(new Thickness(16,11,10,11),Assert.IsType<Border>(add.Content).Padding);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-036")] // App/Views/Settings/MCPSettingsPage.swift:463-480,483-498
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:504-507
    [Trait("Req", "UI-MCP-038")] // App/Views/Settings/MCPSettingsPage.swift:508-515
    [Trait("Req", "UI-MCP-039")] // App/Views/Settings/MCPSettingsPage.swift:516-520
    [Trait("Req", "UI-MCP-040")] // App/Views/Settings/MCPSettingsPage.swift:521-524
    [Trait("Req", "UI-MCP-041")] // App/Views/Settings/MCPSettingsPage.swift:525-528
    [Trait("Req", "UI-MCP-042")] // App/Views/Settings/MCPSettingsPage.swift:542-553
    public void Custom_row_geometry_and_all_five_actions_match_source_without_oauth_labels()
    {
        Ui((model,_)=>{var server=Custom();foreach(var state in new MCPConnectionState[]{new MCPConnectionState.NotConnected(),new MCPConnectionState.Connecting(),new MCPConnectionState.Connected(4),new MCPConnectionState.NeedsSetup(new MCPSetupGuide("Setup","Reason",new[]{"Step"},"Open","https://example.invalid",null)),new MCPConnectionState.Failed("Rejected")}){
            State(model,server.Id,state);var row=new MCPCustomRow(server);using var window=Attach(row,model);Assert.Equal(new Thickness(16,11,10,11),Assert.IsType<Border>(row.Child).Padding);Assert.Equal(26,Assert.IsType<Border>(Assert.Single(Nodes<MCPIcon>(row)).Parent).Width);
            var options=Nodes<Button>(row).Single(x=>ToolTip.GetTip(x) as string=="Server options");Assert.Equal(22,options.Width);Assert.Equal(22,options.Height);
            switch(state){case MCPConnectionState.NotConnected:Assert.NotNull(Button(row,"Connect"));break;case MCPConnectionState.Connecting:Assert.Single(Nodes<NthStatusMark>(row));Assert.Equal(TextWrapping.NoWrap,Nodes<TextBlock>(row).Single(x=>x.Text=="Connecting").TextWrapping);break;case MCPConnectionState.Connected:Assert.Single(Nodes<SettingsSwitch>(row));Assert.Contains("4 tools \U000000B7 Custom",Texts(row));break;case MCPConnectionState.NeedsSetup:Assert.NotNull(Button(row,"Fix it"));break;case MCPConnectionState.Failed:Assert.NotNull(Button(row,"Try again"));Assert.Equal(3,Nodes<TextBlock>(row).Single(x=>x.Text=="Rejected").MaxLines);break;}
        }}); // U+00B7 MIDDLE DOT
    }
    [Fact]
    [Trait("Req", "UI-MCP-043")] // App/Views/Settings/MCPSettingsPage.swift:556-559
    [Trait("Req", "UI-MCP-044")] // App/Views/Settings/MCPSettingsPage.swift:560-562
    [Trait("Req", "UI-MCP-045")] // App/Views/Settings/MCPSettingsPage.swift:563
    [Trait("Req", "UI-MCP-046")] // App/Views/Settings/MCPSettingsPage.swift:564-566
    public void Custom_menu_has_edit_disabled_disconnect_divider_delete_and_enables_disconnect_when_connected()
    {
        Ui((model,_)=>{var row=new MCPCustomRow(Custom());using var window=Attach(row,model);var menu=Invoke<Control>(row,"BuildMenu");Assert.Equal(new[]{"Edit","Disconnect","Delete"},Nodes<Button>(menu).Select(AutomationProperties.GetName));Assert.False(Button(menu,"Disconnect").IsEnabled);Assert.True(Button(menu,"Delete").IsEnabled);Assert.Single(Nodes<PopoverDivider>(menu));State(model,"custom-example",new MCPConnectionState.Connected(1));Assert.True(Button(Invoke<Control>(row,"BuildMenu"),"Disconnect").IsEnabled);});
    }
    [Fact]
    [Trait("Req", "UI-MCP-047")] // App/Views/Settings/MCPSettingsPage.swift:624-685
    [Trait("Req", "UI-MCP-048")] // App/Views/Settings/MCPSettingsPage.swift:706-729
    [Trait("Req", "UI-MCP-049")] // App/Views/Settings/MCPSettingsPage.swift:718-728
    public void Editor_header_shows_add_or_existing_name_kind_and_semibold_24_close()
    {
        Ui((model,_)=>{foreach(var edit in new[]{false,true}){var editor=new MCPCustomEditor(edit?Custom():null);Assert.Equal(new Thickness(16),Assert.IsType<Border>(editor.Child).Padding);Assert.Contains(edit?"Example":"New server",Texts(editor));Assert.Contains("Custom \U000000B7 stdio",Texts(editor));var close=Nodes<Button>(editor).Single(x=>AutomationProperties.GetName(x)=="Close");Assert.Equal(24,close.Width);Assert.Equal(24,close.Height);var glyph=Nodes<SwiftImage>(close).Single();Assert.Equal(FontWeight.SemiBold,SwiftImage.GetFont(glyph)!.Resolve().FontWeight);}}); // U+00B7 MIDDLE DOT
    }
    [Fact]
    [Trait("Req", "UI-MCP-050")] // App/Views/Settings/MCPSettingsPage.swift:732-743,747-753
    [Trait("Req", "UI-MCP-051")] // App/Views/Settings/MCPSettingsPage.swift:635-640
    public void Name_has_source_font_padding_surface_and_kind_picker_has_exact_stdio_pc_wording()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);var name=Field(editor,"My server");Assert.Equal(15,name.FontSize);Assert.Equal(FontWeight.SemiBold,name.FontWeight);Assert.Equal(new Thickness(10,7),name.Padding);Assert.Equal(new CornerRadius(9),Assert.IsType<Border>(name.Parent).CornerRadius);Assert.Contains("stdio \U00002014 a command on this PC",Texts(editor));Assert.Contains("Kind",Texts(editor));}); // U+2014 EM DASH
    }
}

