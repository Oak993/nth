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
        Assert.NotNull(frame); var origin = control.TranslatePoint(default,window)!.Value; var width = control is TextBlock text ? text.TextLayout.Width : control.Bounds.Width; var height = control is TextBlock text2 ? text2.TextLayout.Height : control.Bounds.Height; if (height > 100) { origin += new Vector(width/2-2,height/2-2); width=height=4; } var rect = new PixelRect((int)Math.Floor(origin.X),(int)Math.Floor(origin.Y),Math.Max(1,(int)Math.Ceiling(width)),Math.Max(1,(int)Math.Ceiling(height))); var bytes=new byte[rect.Width*rect.Height*4]; var pinned=System.Runtime.InteropServices.GCHandle.Alloc(bytes,System.Runtime.InteropServices.GCHandleType.Pinned); try { frame.CopyPixels(rect,pinned.AddrOfPinnedObject(),bytes.Length,rect.Width*4); } finally { pinned.Free(); } return bytes;
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
    [Fact]
    [Trait("Req","UI-MCP-006")] // MCPSettingsPage.swift:38-47
    [Trait("Req","UI-MCP-013")] // MCPSettingsPage.swift:189
    [Trait("Req","UI-MCP-015")] // MCPSettingsPage.swift:207
    [Trait("Req","UI-MCP-016")] // MCPSettingsPage.swift:209-211
    [Trait("Req","UI-MCP-017")] // MCPSettingsPage.swift:212-227
    [Trait("Req","UI-MCP-018")] // MCPSettingsPage.swift:249-303
    [Trait("Req","UI-MCP-019")] // MCPSettingsPage.swift:305-319
    [Trait("Req","UI-MCP-020")] // MCPSettingsPage.swift:257-261
    [Trait("Req","UI-MCP-021")] // MCPSettingsPage.swift:262-268
    [Trait("Req","UI-MCP-022")] // MCPSettingsPage.swift:269-274
    [Trait("Req","UI-MCP-023")] // MCPSettingsPage.swift:275-280
    [Trait("Req","UI-MCP-026")] // MCPSettingsPage.swift:331-338
    [Trait("Req","UI-MCP-027")] // MCPSettingsPage.swift:334-335
    [Trait("Req","UI-MCP-032")] // MCPSettingsPage.swift:360-375
    [Trait("Req","UI-MCP-033")] // MCPSettingsPage.swift:394
    [Trait("Req","UI-MCP-034")] // MCPSettingsPage.swift:396-403
    [Trait("Req","UI-MCP-036")] // MCPSettingsPage.swift:463-499
    [Trait("Req","UI-MCP-038")] // MCPSettingsPage.swift:508-515
    public void Noninteractive_production_labels_rows_and_glyphs_ignore_real_hover_and_press()
    {
        Run((model,runtime,frames,presenter)=>runtime.OnMain(()=>
        {
            State(model,"github",new MCPConnectionState.Connected(3));
            var page=new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page,true);
            using(var host=Attach(page,model)) Static(Nodes<TextBlock>(page).Single(t=>t.Text=="Connected"),runtime,frames);
            var section=new MCPCustomSection(""); using(var host=Attach(section,model))
            {
                Static(Nodes<TextBlock>(section).Single(t=>t.Text=="Custom"),runtime,frames);
                Static(Nodes<TextBlock>(section).Single(t=>t.Text!.StartsWith("Servers of your own",StringComparison.Ordinal)),runtime,frames);
            }
            var row=new MCPConnectedRow(Entry()); using(var host=Attach(row,model))
            {
                var tools=Invoke<Control>(row,"BuildTools"); using(var toolsHost=Attach(tools,model)) foreach(var label in Nodes<TextBlock>(tools)) Static(label,runtime,frames);
                Connection(model,new MCPConnection("offline") { Tools=new[]{new MCPToolSummary("read","A description"),new MCPToolSummary("list")} });
                tools=Invoke<Control>(row,"BuildTools"); using(var toolsHost=Attach(tools,model)) foreach(var label in Nodes<TextBlock>(tools)) { Assert.False(label is SelectableTextBlock); Static(label,runtime,frames); }
                var menu=Invoke<Control>(row,"BuildMenu"); using(var menuHost=Attach(menu,model)) Static(Assert.Single(Nodes<PopoverDivider>(menu)).Child!,runtime,frames);
            }
            var guide=new MCPSetupGuide("Setup title","Reason",new[]{"Step"},"Open","",null);
            foreach(var state in new MCPConnectionState[]{new MCPConnectionState.NotConnected(),new MCPConnectionState.Connecting(),new MCPConnectionState.Failed("Probe failure"),new MCPConnectionState.NeedsSetup(guide),new MCPConnectionState.Connected(3)})
            {
                State(model,"offline",state);
                var card=new MCPServerCard(Entry()); EnvironmentValues.SetAccessibilityReduceMotion(card,true); using(var host=Attach(card,model)) // nth step 11: the "Connecting" label has the light through it by the clock, so this proof of "hover and press change nothing" runs calm (no light)
                {
                    foreach(var label in Nodes<TextBlock>(card).Where(t=>t.Text is "Example" or "Vendor" or "Summary" or " " or "Probe failure" or "Setup title" or "Connecting")) Static(label,runtime,frames);
                    var surface=Assert.IsType<Border>(Assert.IsType<Panel>(card.Child).Children[0]); Static(surface,runtime,frames);
                }
                State(model,"custom-example",state); var custom=new MCPCustomRow(Custom()); EnvironmentValues.SetAccessibilityReduceMotion(custom,true); using(var host=Attach(custom,model))
                {
                    var grid=Assert.IsType<Grid>(Assert.IsType<Border>(custom.Child).Child); var copy=Assert.IsType<StackPanel>(grid.Children[1]);
                    foreach(var label in copy.Children.Cast<TextBlock>()) Static(label,runtime,frames);
                    if(state is MCPConnectionState.Connecting) Static(Nodes<TextBlock>(custom).Single(t=>t.Text=="Connecting"),runtime,frames);
                    Static(Assert.IsType<Border>(custom.Child),runtime,frames);
                }
            }
            State(model,"offline",new MCPConnectionState.Connecting()); var oauth=new MCPServerCard(Entry(transport:new MCPTransport.Oauth("https://example.invalid"))); EnvironmentValues.SetAccessibilityReduceMotion(oauth,true);
            using(var host=Attach(oauth,model)) Static(Nodes<TextBlock>(oauth).Single(t=>t.Text=="Waiting"),runtime,frames);
            State(model,"offline",new MCPConnectionState.Connected(3)); var celebrating=new MCPServerCard(Entry(),true); EnvironmentValues.SetAccessibilityReduceMotion(celebrating,true);
            using(var host=Attach(celebrating,model)) { Static(Assert.Single(Nodes<SwiftImage>(celebrating)),runtime,frames); Static(Assert.Single(Nodes<TextBlock>(celebrating)),runtime,frames); }
            Assert.DoesNotContain(presenter.Calls,c=>c.Presented);
        }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req","UI-MCP-032")] // MCPSettingsPage.swift:64-74,360-375; ED-218 allows the visual exit after the 900ms logical move.
    public void Page_celebration_lasts_exactly_900ms_and_reduce_motion_suppresses_it(bool reduced)
    {
        Run((model,runtime,_,_)=>
        {
            using var sound=SoundOutput.Current.Override(new MCPSettingsSilentSound());
            var page=new MCPSettingsPage(""); EnvironmentValues.SetAccessibilityReduceMotion(page,reduced); var host=runtime.OnMain(()=>Attach(page,model));
            try
            {
                runtime.OnMain(()=>typeof(MCPStore).GetProperty(nameof(MCPStore.States))!.SetValue(model.Mcp,new Dictionary<string,MCPConnectionState>(model.Mcp.States){["github"]=new MCPConnectionState.Connected(3)}));
                var state=Read<object>(page,"view");
                Assert.Equal(!reduced,Read<ValueSet<string>>(state,"Celebrating").Contains("github"));
                if(!reduced)
                {
                    runtime.AdvanceMilliseconds(899); Assert.True(Read<ValueSet<string>>(state,"Celebrating").Contains("github")); Assert.DoesNotContain(Nodes<MCPConnectedRow>(page),r=>Texts(r).Contains("GitHub"));
                    runtime.AdvanceMilliseconds(1); Assert.False(Read<ValueSet<string>>(state,"Celebrating").Contains("github"));
                }
                Assert.Contains(Nodes<MCPConnectedRow>(page),r=>Texts(r).Contains("GitHub"));
            } finally { runtime.OnMain(host.Dispose); }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req","PR-MCP-073")] // MCPStore.swift:115-128,186-193; OD-0008 A12, OD-0012 E6
    public void Delete_uses_latest_definition_cancels_probe_clears_drafts_and_all_configs_and_reports_failed_deletion(bool refuse)
    {
        Run((model,runtime,_,_)=>
        {
            var backend=new MCPSettingsRefusingCredentialBackend { Refuse=false,RefuseDeletes=refuse }; using var secrets=Marrow.Services.Port.SecretStore.Shared.Override(new Marrow.Services.Port.SecretStore(backend));
            var stale=Custom(1) with { Env=new[]{new MCPCustomServer.Variable("TOKEN",IsSecret:true)} };
            var current=stale with { Env=new[]{new MCPCustomServer.Variable("TOKEN",IsSecret:true),new MCPCustomServer.Variable("LATER",IsSecret:true),new MCPCustomServer.Variable("MODE")} };
            var entry=current.CatalogEntry();
            runtime.OnMain(()=> { Assert.Null(model.Mcp.SaveCustom(current)); Connection(model,new MCPConnection(current.Id){ConnectedAt=DateTimeOffset.UtcNow}); Assert.True(model.Mcp.SaveCustomValues(new[]{("TOKEN","first",true),("LATER","second",true),("MODE","plain",false)},current.Id,null)); State(model,current.Id,new MCPConnectionState.Connected(2)); model.Mcp.SetEnabled(true,entry); model.Mcp.SetDraft("unsaved",entry.Fields.Last(),entry); model.Settings.ConfirmBeforeDeleting=false; });
            var paths=new[]{MCPPaths.ClaudeConfigURL,MCPPaths.CodexConfigURL,MCPPaths.CopilotConfigURL,Path.Combine(MCPPaths.Directory,"gemini-settings.json")}; Assert.All(paths,path=>Assert.True(File.Exists(path)));
            var row=new MCPCustomRow(stale); var host=runtime.OnMain(()=>Attach(row,model)); TaskHandle pending=null!;
            try
            {
                // Delete queues its real store action first; Refresh creates the genuine probe handle behind it.
                // Deletion cancels that handle before its offline fixture process could start.
                runtime.OnMain(()=> { PointerClick(Button(Invoke<Control>(row,"BuildMenu"),"Delete")); model.Mcp.Refresh(entry); pending=((Dictionary<string,TaskHandle>)Read<object>(model.Mcp,"probes"))[current.Id]; });
                Assert.True(pending.IsCancelled); Assert.Empty((System.Collections.IDictionary)Read<object>(model.Mcp,"probes"));
                Assert.Empty(model.Mcp.Customs); Assert.Null(model.Mcp.Connection(current.Id)); Assert.False(model.Mcp.States.ContainsKey(current.Id)); Assert.False(model.Mcp.Drafts.ContainsKey(current.Id));
                Assert.DoesNotContain(current.Id,File.ReadAllText(MCPPaths.CustomsURL),StringComparison.Ordinal); Assert.DoesNotContain(current.Id,File.ReadAllText(MCPPaths.ConnectionsURL),StringComparison.Ordinal); Assert.All(paths,path=>Assert.False(File.Exists(path)));
                Assert.Equal(refuse?"first":null,MCPKeychain.Value(current.Id,"TOKEN")); Assert.Equal(refuse?"second":null,MCPKeychain.Value(current.Id,"LATER"));
                Assert.Equal(refuse?"Disconnected, but Credential Manager did not remove this server’s stored values.":null,model.Mcp.CredentialDeletionFailure);
            } finally { runtime.OnMain(host.Dispose); }
        });
    }

}