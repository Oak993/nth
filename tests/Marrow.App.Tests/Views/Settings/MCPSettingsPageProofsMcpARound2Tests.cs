using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.MCP;
using Marrow.Services.Models;
using Marrow.Testing;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;
using static Marrow.App.Tests.Fixtures.MCPProofsMcpARound2Input;

namespace Marrow.App.Tests.Views.Settings;

// Source: DroppyCode/App/Views/Settings/MCPSettingsPage.swift (B3 52bc792), :89-840.
[Collection("Headless")]
public sealed class MCPSettingsPageProofsMcpARound2Tests(HeadlessSession headless)
{
    private void Run(Action<AppModel,TestRuntime,ManualFrameSource,RecordingPresenter> test) => headless.OnUiThread(() =>
    {
        using var runtime = new TestRuntime(); using var harness = new AppModelHarness(withRuntime:false);
        var frames = new ManualFrameSource(); using var clock = FrameClock.Source.Override(frames);
        var presenter = new RecordingPresenter(); using var presentation = Presentation.Presenter.Override(presenter);
        var model = runtime.OnMain(() => new AppModel()); test(model,runtime,frames,presenter);
        runtime.OnMain(() => Dispatcher.UIThread.RunJobs());
    });
    private static byte[] Pixels(Control control)
    {
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame); using var stream = new MemoryStream(); frame.Save(stream,new Avalonia.Media.Imaging.PngBitmapEncoderOptions()); return stream.ToArray();
    }
    private static void Static(Control control, TestRuntime runtime, ManualFrameSource frames)
    {
        var before = Pixels(control);
        var target = control is TextBlock t ? new Point(t.TextLayout.Width/2,t.TextLayout.Height/2) : new Point(control.Bounds.Width/2,control.Bounds.Height/2);
        var window = (Avalonia.Controls.Window)TopLevel.GetTopLevel(control)!;
        var at = control.TranslatePoint(target,window)!.Value;
        var hit = Assert.IsAssignableFrom<Avalonia.Visual>(window.InputHitTest(at));
        Assert.DoesNotContain(hit.GetVisualAncestors().Prepend(hit),v=>v is Button || v is SmallSwitch);
        window.MouseMove(at,RawInputModifiers.None); runtime.Time.Advance(TimeSpan.FromMilliseconds(100)); frames.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal(before,Pixels(control));
        window.MouseDown(at,MouseButton.Left,RawInputModifiers.None); Assert.Equal(before,Pixels(control));
        window.MouseUp(at,MouseButton.Left,RawInputModifiers.None); Assert.Equal(before,Pixels(control)); MoveAway(control);
    }

    [Fact]
    [Trait("Req","UI-MCP-002")] // MCPSettingsPage.swift:89-120
    [Trait("Req","UI-MCP-003")] // MCPSettingsPage.swift:91-93
    public void Lead_and_shield_remain_visually_static_under_real_hover_and_press()
    {
        Run((model,runtime,frames,presenter) => runtime.OnMain(() =>
        {
            var lead = new MCPLeadCard(); using var host = Attach(lead,model);
            Static(Assert.Single(Nodes<SwiftImage>(lead),i=>i.SymbolName=="checkmark.shield.fill"),runtime,frames);
            Static(Nodes<TextBlock>(lead).Single(t=>t.Text!.StartsWith("Connect a server here",StringComparison.Ordinal)),runtime,frames);
            Assert.DoesNotContain(presenter.Calls,c=>c.Presented); Assert.Empty(model.Mcp.States);
        }));
    }

    [Fact]
    [Trait("Req","UI-MCP-010")] // MCPSettingsPage.swift:175-180: 22 square .contentShape(.rect)
    public void Options_whole_rectangle_accepts_pointer_and_keyboard_and_toggles_bottom_popover()
    {
        Run((model,runtime,_,presenter) =>
        {
            MCPConnectedRow row=null!; IDisposable host=null!; Button options=null!; Popup popup=null!;
            runtime.OnMain(()=> { State(model,"offline",new MCPConnectionState.Connected(1)); row=new MCPConnectedRow(Entry()); host=Attach(row,model); options=Nodes<Button>(row).Single(b=>ToolTip.GetTip(b) as string=="Server options"); popup=Nodes<Popup>(row)[0]; });
            try
            {
                runtime.OnMain(()=>PointerAt(options,new Point(1,1))); Assert.True(presenter.Calls.Last(c=>c.Popup==popup).Presented);
                runtime.OnMain(()=>PointerAt(options,new Point(21,21))); Assert.False(presenter.Calls.Last(c=>c.Popup==popup).Presented);
                runtime.OnMain(()=>KeyboardSpace(options)); Assert.True(presenter.Calls.Last(c=>c.Popup==popup).Presented);
                Assert.Same(options,popup.PlacementTarget); Assert.Equal(PlacementMode.Bottom,popup.Placement);
                Assert.Equal(13,SwiftImage.GetFont(Assert.Single(Nodes<SwiftImage>(options)))!.Resolve().FontSize);
                Assert.Equal(FontWeight.Normal,SwiftImage.GetFont(Assert.Single(Nodes<SwiftImage>(options)))!.Resolve().FontWeight);
            } finally { runtime.OnMain(()=> { MoveAway(options); host.Dispose(); }); }
        });
    }

    [Fact]
    [Trait("Req","UI-MCP-035")] // MCPSettingsPage.swift:417-440: contentShape rect and add-mode popover
    public void Add_row_trailing_empty_area_and_keyboard_open_add_editor()
    {
        Run((model,runtime,_,presenter) =>
        {
            var section=new MCPCustomSection(""); var host=runtime.OnMain(()=>Attach(section,model)); var add=Button(section,"Add server");
            try
            {
                runtime.OnMain(()=>PointerAt(add,new Point(add.Bounds.Width-2,add.Bounds.Height/2)));
                var popup=Assert.Single(Nodes<Popup>(section)); Assert.True(presenter.Calls.Last(c=>c.Popup==popup).Presented);
                Assert.Same(add,popup.PlacementTarget); Assert.Equal(PlacementMode.Bottom,popup.Placement); Assert.True(popup.IsLightDismissEnabled);
                runtime.OnMain(()=> { Read<object>(section,"state").GetType().GetProperty("Adding")!.SetValue(Read<object>(section,"state"),false); });
                Assert.False(presenter.Calls.Last(c=>c.Popup==popup).Presented);
                runtime.OnMain(()=>KeyboardSpace(add)); Assert.True(presenter.Calls.Last(c=>c.Popup==popup).Presented);
            } finally { runtime.OnMain(()=> { MoveAway(add); host.Dispose(); }); }
        });
    }

    [Fact]
    [Trait("Req","PR-MCP-208")] // MCPSettingsPage.swift:869-881; MCPCustom.swift:130-137
    public void Editor_keeps_every_validation_failure_open_and_displays_the_exact_message()
    {
        Run((model,runtime,_,_) => runtime.OnMain(() =>
        {
            var inputs = new[] { ("", "offline-fixture", "", 0, "The server needs a name."),
                ("Example", "", "", 0, "A stdio server needs a command."),
                ("Example", "offline-fixture", "BAD KEY", 1, "“BAD KEY” is not a valid variable name: letters, digits, `_`, `-` and `.`, starting with a letter or `_`."),
                ("Example", "offline-fixture", "TOKEN", 2, "“TOKEN” is listed twice.") };
            foreach(var (name,command,key,count,message) in inputs)
            {
                var editor=new MCPCustomEditor(null); using var host=Attach(editor,model); var closed=0; Popovers.SetClosePopover(editor,()=>closed++);
                Field(editor,"My server").Text=name; Field(editor,"npx").Text=command;
                for(var i=0;i<count;i++) PointerClick(Button(editor,"Add environment variable"));
                foreach(var box in Nodes<TextBox>(editor).Where(t=>t.PlaceholderText=="KEY")) box.Text=key;
                PointerClick(Button(editor,"Add server")); Assert.Contains(message,Texts(editor)); Assert.Equal(0,closed); Assert.Empty(model.Mcp.Customs);
            }
            var http=new MCPCustomEditor(null); using(var host=Attach(http,model))
            {
                Field(http,"My server").Text="Example"; Call(http,"SetKind",MCPCustomServer.Kind.Http); Field(http,"https://…").Text="ftp://example.invalid";
                var closed=0; Popovers.SetClosePopover(http,()=>closed++); PointerClick(Button(http,"Add server"));
                Assert.Contains("The URL must start with https:// (or http:// for a local server).",Texts(http)); Assert.Equal(0,closed); Assert.Empty(model.Mcp.Customs);
            }
            Assert.Null(model.Mcp.SaveCustom(Custom()));
            var taken=new MCPCustomEditor(null); using var takenHost=Attach(taken,model); Field(taken,"My server").Text="Example"; Field(taken,"npx").Text="offline-fixture";
            var takenCloses=0; Popovers.SetClosePopover(taken,()=>takenCloses++); PointerClick(Button(taken,"Add server"));
            Assert.Contains("A server named “Example” already exists.",Texts(taken)); Assert.Equal(0,takenCloses); Assert.Single(model.Mcp.Customs);
        }));
    }

    [Fact]
    [Trait("Req","PR-MCP-210")] // MCPSettingsPage.swift:794-840: draft-only toggle, row identity; Save :877-881
    public void Closing_an_unsaved_lock_toggle_discards_it_and_keeps_both_stores()
    {
        Run((model,runtime,_,_)=>runtime.OnMain(()=>
        {
            var server=Custom(1); Assert.Null(model.Mcp.SaveCustom(server)); Assert.True(model.Mcp.SaveCustomValues(new[]{("KEY0","fixture-value",false)},server.Id,null));
            var before=File.ReadAllBytes(MCPPaths.ConnectionsURL);
            var editor=new MCPCustomEditor(server); using(var host=Attach(editor,model))
            {
                PointerClick(Nodes<Button>(editor).Single(b=>ToolTip.GetTip(b) as string=="Stored in mcp.json; click to keep it in the Credential Manager instead"));
                Assert.NotEqual(default(char),Field(editor,"value").PasswordChar);
            }
            Assert.Equal(before,File.ReadAllBytes(MCPPaths.ConnectionsURL)); Assert.Null(MCPKeychain.Value(server.Id,"KEY0"));
            var reopened=new MCPCustomEditor(server); using var reopenedHost=Attach(reopened,model); Assert.Equal(default(char),Field(reopened,"value").PasswordChar); Assert.Equal("fixture-value",Field(reopened,"value").Text);
        }));
    }
}