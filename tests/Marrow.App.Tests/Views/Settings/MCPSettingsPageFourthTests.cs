using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Port;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
namespace Marrow.App.Tests.Views.Settings;

[Collection("Headless")]
public sealed class MCPSettingsPageFourthTests(HeadlessSession headless)
{
    private void Ui(Action<AppModel,TestRuntime> test)=>headless.OnUiThread(()=>{using var runtime=new TestRuntime();using var harness=new AppModelHarness(withRuntime:false);using var frames=FrameClock.Source.Override(new ManualFrameSource());using var presentation=Presentation.Presenter.Override(new RecordingPresenter());var model=new AppModel();runtime.OnMain(()=>test(model,runtime));Dispatcher.UIThread.RunJobs();});
    [Fact]
    [Trait("Req", "UI-MCP-052")] // App/Views/Settings/MCPSettingsPage.swift:643-645,756-764
    [Trait("Req", "UI-MCP-053")] // App/Views/Settings/MCPSettingsPage.swift:777-791
    [Trait("Req", "UI-MCP-054")] // App/Views/Settings/MCPSettingsPage.swift:649-651,756-764
    [Trait("Req", "UI-MCP-096")] // App/Views/Settings/MCPSettingsPage.swift:892-898
    public void Command_url_and_arguments_use_exact_padding_fonts_and_field_surfaces()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);var command=Field(editor,"npx");Assert.Equal(new Thickness(10,7),command.Padding);Assert.Equal(12,command.FontSize);Assert.Equal(new CornerRadius(9),Assert.IsType<Border>(command.Parent).CornerRadius);
            var args=Nodes<TextBox>(editor).Single(x=>x.AcceptsReturn);Assert.Equal(66,args.Height);Assert.Equal(new Thickness(11,4),args.Padding);Assert.Contains("One argument per line; empty lines are dropped.",Texts(editor));Call(editor,"SetKind",MCPCustomServer.Kind.Http);var url=Field(editor,"https://\U00002026");Assert.Equal(new Thickness(10,7),url.Padding);Assert.Equal(12,url.FontSize);Assert.DoesNotContain(Nodes<TextBox>(editor),x=>x.AcceptsReturn); // OD-0032: the 11 pt argument inset is 6 plus the TextEditor's own 5
        }); // U+2026 HORIZONTAL ELLIPSIS
    }
    [Fact]
    [Trait("Req", "UI-MCP-055")] // App/Views/Settings/MCPSettingsPage.swift:794-840
    [Trait("Req", "UI-MCP-056")] // App/Views/Settings/MCPSettingsPage.swift:798
    [Trait("Req", "UI-MCP-057")] // App/Views/Settings/MCPSettingsPage.swift:799-803
    public void Variable_key_edit_keeps_its_identity_and_kind_switch_keeps_key_and_value()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(Custom(1));using var window=Attach(editor,model);var key=Field(editor,"KEY");key.Text="MODE";Field(editor,"value").Text="sample-mode";Assert.Same(key,Field(editor,"KEY"));Assert.Equal(new Thickness(10,7),key.Padding);Assert.Equal(12,key.FontSize);
            Click(Button(editor,"Add environment variable"));Assert.Same(key,Field(editor,"KEY"));Call(editor,"SetKind",MCPCustomServer.Kind.Http);Assert.Contains("Headers",Texts(editor));Assert.Equal("MODE",Field(editor,"KEY").Text);Assert.Equal("sample-mode",Field(editor,"value").Text);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-057")] // App/Views/Settings/MCPSettingsPage.swift:799-803,767-774
    public void Locked_value_template_masks_the_actual_text_presenter()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(Custom(1));using var window=Attach(editor,model);Field(editor,"value").Text="sample-mode";
            var toggle=Nodes<Button>(editor).Single(x=>ToolTip.GetTip(x) as string=="Stored in mcp.json; click to keep it in the Credential Manager instead");Click(toggle);Dispatcher.UIThread.RunJobs();var box=Field(editor,"value");box.ApplyTemplate();var presenter=box.GetVisualDescendants().OfType<TextPresenter>().Single();Assert.Equal('\u2022',presenter.PasswordChar);Assert.False(presenter.RevealPassword); // U+2022 BULLET
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-058")] // App/Views/Settings/MCPSettingsPage.swift:804-814
    [Trait("Req", "UI-MCP-059")] // App/Views/Settings/MCPSettingsPage.swift:815-825
    [Trait("Req", "UI-MCP-060")] // App/Views/Settings/MCPSettingsPage.swift:828-835
    [Trait("Req", "UI-MCP-061")] // App/Views/Settings/MCPSettingsPage.swift:836-838
    public void Variable_controls_add_blank_unlocked_row_then_toggle_and_remove_with_exact_glyphs_and_help()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);var add=Button(editor,"Add environment variable");Assert.Contains(Nodes<SwiftImage>(add),i=>i.SymbolName=="plus");Click(add);Assert.Equal("",Field(editor,"KEY").Text);Assert.Equal(default(char),Field(editor,"value").PasswordChar);
            var remove=Nodes<Button>(editor).Single(x=>ToolTip.GetTip(x) as string=="Remove this variable");Assert.Equal(24,remove.Width);Assert.Equal(24,remove.Height);Assert.Equal(FontWeight.Medium,SwiftImage.GetFont(Nodes<SwiftImage>(remove).Single())!.Resolve().FontWeight);
            var toggle=Nodes<Button>(editor).Single(x=>ToolTip.GetTip(x) as string=="Stored in mcp.json; click to keep it in the Credential Manager instead");Assert.Equal(FontWeight.Medium,SwiftImage.GetFont(Nodes<SwiftImage>(toggle).Single())!.Resolve().FontWeight);Click(remove);Assert.DoesNotContain(Nodes<TextBox>(editor),x=>x.PlaceholderText=="KEY");Assert.Contains("Locked values are kept in the Credential Manager.",Texts(editor));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-062")] // App/Views/Settings/MCPSettingsPage.swift:691-696
    [Trait("Req", "UI-MCP-063")] // App/Views/Settings/MCPSettingsPage.swift:698-702,864-889
    [Trait("Req", "UI-MCP-095")] // App/Views/Settings/MCPSettingsPage.swift:687-704
    public void Validation_error_and_prominent_save_stay_in_the_ten_spaced_fixed_footer()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);Click(Button(editor,"Add server"));var error=Nodes<TextBlock>(editor).Single(x=>x.Text=="The server needs a name.");Assert.Equal(11,error.FontSize);Assert.Equal(3,error.MaxLines);Assert.Equal(TextWrapping.Wrap,error.TextWrapping);var footer=Assert.IsType<StackPanel>(error.Parent);Assert.Equal(10,footer.Spacing);
            var scroll=Nodes<ScrollViewer>(editor).Single();Assert.DoesNotContain(error,Nodes<TextBlock>(Assert.IsAssignableFrom<Control>(scroll.Content)));var save=Button(editor,"Add server");Assert.Contains(Nodes<Border>(save),b=>SwiftGlass.GetEffect(b)?.TintColor is not null);Assert.Equal(Avalonia.Layout.HorizontalAlignment.Right,save.HorizontalAlignment);
        });
    }
    [Fact]
    [Trait("Rule", "R-TEST-2")] // App/Views/Settings/MCPSettingsPage.swift:864-889; subset only, PR-MCP-208 complemented by MCPSettingsPageProofsMcpATests
    public void Credential_failure_keeps_editor_open_and_retry_replaces_saved_definition_without_duplicating()
    {
        Ui((model,_)=>{var backend=new MCPSettingsRefusingCredentialBackend();using var secrets=SecretStore.Shared.Override(new SecretStore(backend));var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);var closed=false;Popovers.SetClosePopover(editor,()=>closed=true);
            Field(editor,"My server").Text="Example";Field(editor,"npx").Text="offline-fixture";Click(Button(editor,"Add environment variable"));Field(editor,"KEY").Text="TOKEN";Field(editor,"value").Text="sample-mode";Click(Nodes<Button>(editor).Single(x=>ToolTip.GetTip(x) as string=="Stored in mcp.json; click to keep it in the Credential Manager instead"));Click(Button(editor,"Add server"));Assert.False(closed);Assert.Contains("The Credential Manager could not save a value. Keep this editor open and try again.",Texts(editor));Assert.Single(model.Mcp.Customs);
            backend.Refuse=false;Field(editor,"My server").Text="Renamed";Click(Button(editor,"Add server"));Assert.True(closed);var saved=Assert.Single(model.Mcp.Customs);Assert.Equal("custom-example",saved.Id);Assert.Equal("Renamed",saved.Name);Assert.Equal("sample-mode",model.Mcp.StoredValue(saved.Id,"TOKEN",true));Assert.Null(model.Mcp.StoredValue(saved.Id,"TOKEN",false));
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-064")] // App/Views/Settings/MCPSettingsPage.swift:973-1024
    [Trait("Req", "UI-MCP-065")] // App/Views/Settings/MCPSettingsPage.swift:975-985
    [Trait("Req", "UI-MCP-066")] // App/Views/Settings/MCPSettingsPage.swift:990-993,1026-1036
    public void Information_has_340_width_16_padding_header_sizes_and_58_detail_label_column()
    {
        Ui((model,_)=>{var info=new MCPInfoPopover(Entry());var border=Assert.IsType<Border>(info.Child);Assert.Equal(340,border.Width);Assert.Equal(new Thickness(16),border.Padding);Assert.Equal(14,Assert.IsType<StackPanel>(border.Child).Spacing);Assert.Equal(28,Assert.Single(Nodes<MCPIcon>(info)).Child!.Width);Assert.Equal(14,Nodes<TextBlock>(info).Single(x=>x.Text=="Example").FontSize);Assert.Contains("Vendor \U000000B7 Developer",Texts(info));Assert.All(Nodes<TextBlock>(info).Where(x=>x.Text is "Connects" or "Needs"),x=>Assert.Equal(58,x.Width));}); // U+00B7 MIDDLE DOT
    }
    [Fact]
    [Trait("Req", "UI-MCP-067")] // App/Views/Settings/MCPSettingsPage.swift:994-1000,1073-1121
    public void Tool_chips_use_exact_padding_radius_and_flow_wraps_with_six_spacing()
    {
        Ui((model,_)=>{var chips=new FlowChips(new[]{"read","write"});var flow=Assert.IsType<FlowLayout>(chips.Child);Assert.Equal(6,flow.Spacing);Assert.All(flow.Children.OfType<Border>(),b=>{Assert.Equal(new Thickness(7,3),b.Padding);Assert.Equal(new CornerRadius(6),b.CornerRadius);Assert.Equal(10.5,Assert.IsType<TextBlock>(b.Child).FontSize);});
            var layout=new FlowLayout();foreach(var chipIndex in Enumerable.Range(0,3))layout.Children.Add(new Border{Width=20,Height=10});layout.Measure(new Size(46,double.PositiveInfinity));Assert.Equal(new Size(46,26),layout.DesiredSize);layout.Arrange(new Rect(0,0,46,26));Assert.Equal(new Point(0,0),layout.Children[0].Bounds.Position);Assert.Equal(new Point(26,0),layout.Children[1].Bounds.Position);Assert.Equal(new Point(0,16),layout.Children[2].Bounds.Position);
        });
    }
    [Fact]
    [Trait("Req", "UI-MCP-068")] // App/Views/Settings/MCPSettingsPage.swift:1002-1009
    [Trait("Req", "UI-MCP-069")] // App/Views/Settings/MCPSettingsPage.swift:1010-1019
    public void Optional_change_and_docs_link_have_exact_ellipsis_and_bold_ten_point_arrow()
    {
        Ui((model,_)=>{var info=new MCPInfoPopover(Entry() with{DocsURL="https://example.invalid/docs",Fields=new[]{new MCPField("key","API Key","",IsRequired:false)}});Assert.NotNull(Button(info,"Change the api key\U00002026"));var docs=Button(info,"Read the docs");var arrow=Nodes<SwiftImage>(docs).Single();Assert.Equal("arrow.up.right",arrow.SymbolName);Assert.Equal(10,SwiftImage.GetFont(arrow)!.Resolve().FontSize);Assert.Equal(FontWeight.Bold,SwiftImage.GetFont(arrow)!.Resolve().FontWeight);}); // U+2026 HORIZONTAL ELLIPSIS
    }
    [Fact]
    [Trait("Req", "UI-MCP-089")] // App/Views/Settings/MCPSettingsPage.swift:60-62
    [Trait("Req", "UI-MCP-093")] // App/Views/Settings/MCPSettingsPage.swift:49-59
    [Trait("Req", "UI-MCP-094")] // App/Views/Settings/MCPSettingsPage.swift:95-98
    public void Footer_and_category_and_lead_title_keep_exact_text_styles()
    {
        Ui((model,_)=>{var page=new MCPSettingsPage("");using var window=Attach(page,model);var footer=Assert.IsType<TextBlock>(Assert.IsType<StackPanel>(page.Child).Children[^1]);Assert.Equal("Connected servers reach every provider when a thread starts: the CLIs at launch, the API models through nth itself.",footer.Text);Assert.Equal(11,footer.FontSize);var title=Nodes<TextBlock>(page).Single(x=>x.Text=="Connected servers lead");Assert.Equal(13,title.FontSize);Assert.Equal(FontWeight.SemiBold,title.FontWeight);var header=Nodes<TextBlock>(page).Single(x=>x.Text=="Developer");Assert.Equal(13,header.FontSize);Assert.Equal(FontWeight.SemiBold,header.FontWeight);});
    }
    [Fact]
    [Trait("Req", "UI-MCP-091")] // App/Views/Settings/MCPSettingsPage.swift:1124-1141
    public void Server_icon_has_caller_square_size_template_and_custom_rack_fallback()
    {
        Ui((model,_)=>{foreach(var size in new[]{22d,26d,28d}){var icon=new MCPIcon(Entry(),size);var image=Assert.IsType<SwiftImage>(icon.Child);Assert.Equal(size,image.Width);Assert.Equal(size,image.Height);Assert.Equal("server.rack",image.SymbolName);Assert.Equal(ImageRenderingMode.Template,image.EffectiveRenderingMode);}});
    }
    [Fact]
    [Trait("Req", "UI-MCP-095")] // App/Views/Settings/MCPSettingsPage.swift:632-656,659-662,687-704
    [Trait("Req", "UI-MCP-055")] // App/Views/Settings/MCPSettingsPage.swift:794-840
    public void Scroll_form_renders_and_adding_variables_preserves_existing_fields()
    {
        Ui((model,_)=>{var editor=new MCPCustomEditor(null);using var window=Attach(editor,model);var scroll=Nodes<ScrollViewer>(editor).Single();scroll.ApplyTemplate();Assert.Contains(scroll.GetVisualDescendants(),x=>x is ScrollContentPresenter);
            var name=Field(editor,"My server");var command=Field(editor,"npx");Click(Button(editor,"Add environment variable"));Assert.Same(name,Field(editor,"My server"));Assert.Same(command,Field(editor,"npx"));var key=Field(editor,"KEY");Click(Button(editor,"Add environment variable"));Assert.Same(key,Field(editor,"KEY"));
        });
    }
}


