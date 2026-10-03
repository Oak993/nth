using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Models;
using Marrow.Testing;
using Marrow.UI.Port;
using static Marrow.App.Tests.Fixtures.MCPSettingsPageFixture;

namespace Marrow.App.Tests.Views.Settings;

// The custom server editor (MCPSettingsPage.swift:580-889) through the REAL popover presenter (OD-0095's PopoverCard hung by
// PopoverHost.Prepare, PopoverHost.Follow handing a content set after the popup opened to its host), shown in the window's overlay
// layer because Avalonia 12.1.2 headless cannot open a PopupRoot. The add and edit editors are PlainControls.Popover(buildBeforeOpen:
// false): their content is built once the popup has opened, which Avalonia only shows through Follow. Every other App test of this page
// used RecordingPresenter, which never opens a popup.
// NOT proven here (NATIVE-ONLY): the editor in its own native popup window (Win32 hit testing, the window's own light dismiss, UI
// Automation through the real UIA provider).
[Collection("Headless")]
public sealed class MCPCustomEditorRealPresenterTests(HeadlessSession headless)
{
    // FINDING (not asserted): pressing Escape with the editor open does NOT close the popover through the real presenter in the overlay host
    // (nothing in PopoverHost, Presentation or PlainControls handles Escape; NSPopover's transient behaviour closes on it).

    private sealed class Rig : IDisposable
    {
        public TestRuntime Runtime = new(); public AppModelHarness Harness = new(withRuntime: false); public ManualFrameSource Frames = new();
        public IDisposable FrameScope, Seam; public ReviewOverlayPresenter Presenter; public AppModel Model = null!; public Avalonia.Controls.Window Window = null!; public MCPCustomSection Section = null!;
        public Rig()
        {
            FrameScope = FrameClock.Source.Override(Frames); Presenter = new ReviewOverlayPresenter(Presentation.Presenter.Current); Seam = Presentation.Presenter.Override(Presenter);
            Model = Runtime.OnMain(() => new AppModel());
            Runtime.OnMain(() =>
            {
                Section = new MCPCustomSection(""); AppEnvironment.SetAppModel(Section, Model);
                var host = new Border { Child = Section, Width = 520, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(20) };
                Window = new Avalonia.Controls.Window { Width = 800, Height = 800, Content = host }; SettingsProviderPopupLayers.Install(Window); Window.Show();
            });
            Settle();
        }
        // Everything the page does on the main actor runs as a main-loop step, and its follow-ups (observation callbacks, the popover's frames) are pumped by Settle.
        public void Do(Action action) { Runtime.OnMain(action); Settle(); }
        public void Settle(int milliseconds = 600) { for (var i = 0; i < milliseconds / 20; i++) { Runtime.Advance(TimeSpan.FromMilliseconds(20)); Frames.Advance(TimeSpan.FromMilliseconds(20)); Dispatcher.UIThread.RunJobs(); } Runtime.RunUntilIdle(); Window.UpdateLayout(); }
        public MCPCustomEditor? Editor => Window.GetVisualDescendants().OfType<MCPCustomEditor>().FirstOrDefault();
        public void Press(Point at) => Do(() => { Window.MouseMove(at, RawInputModifiers.None); Window.MouseDown(at, MouseButton.Left, RawInputModifiers.None); Window.MouseUp(at, MouseButton.Left, RawInputModifiers.None); });
        public void Click(Control target, Point? local = null) => Press(target.TranslatePoint(local ?? new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), Window)!.Value);
        public void Key(Key key) => Do(() => { Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.Escape, null); Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.Escape, null); });
        public void OpenAdd() => Click(Window.GetVisualDescendants().OfType<Button>().First(b => b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Add server") && b.Bounds.Width > 100));
        public void Dispose() { try { Runtime.OnMain(() => Window.Close()); Dispatcher.UIThread.RunJobs(); } finally { Seam.Dispose(); FrameScope.Dispose(); Harness.Dispose(); Runtime.Dispose(); } }
    }

    private void Run(Action<Rig> test) => headless.OnUiThread(() => { using var rig = new Rig(); test(rig); });

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:411,435,624-685: real presenter, overlay popup; the native popup window is NATIVE-ONLY
    public void The_add_server_editor_shows_through_the_real_presenter_at_380_by_450_with_the_name_field_focused_and_a_new_draft_each_time()
    {
        Run(rig =>
        {
            Assert.Null(rig.Editor); rig.OpenAdd();
            var popup = Assert.Single(rig.Presenter.Popups); Assert.True(popup.IsOpen, "the add popover did not open");
            var editor = rig.Editor; Assert.NotNull(editor); // not only its one-pixel placeholder: the content set after the popup opened is what the host shows
            Assert.True(editor!.IsAttachedToVisualTree()); Assert.IsType<PopoverCard>(popup.Child); Assert.Same(editor, PopoverHost.Content(popup));
            Assert.Equal(new Size(380, 450), editor.Bounds.Size); Assert.Contains("New server", Nodes<TextBlock>(editor).Select(t => t.Text));
            var name = Field(editor, "My server"); Assert.True(name.IsFocused, "add mode focuses the Name field"); Assert.Contains(Nodes<Button>(editor), b => AutomationProperties.GetName(b) == "Close");
            Assert.True(editor.Bounds.Width > 0 && Nodes<TextBox>(editor).All(t => t.Bounds.Width > 0));
            // A variable row grows the real popup's content with the formula (MCPSettingsPage.swift:683-684).
            rig.Click(Button(editor, "Add environment variable")); Assert.Equal(new Size(380, 484), rig.Editor!.Bounds.Size);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:718-728 through the real presenter; UI Automation itself is NATIVE-ONLY
    public void The_close_button_is_found_by_its_automation_name_and_a_press_near_its_corner_closes_the_real_popover_saving_nothing()
    {
        Run(rig =>
        {
            rig.Do(() => Assert.Null(rig.Model.Mcp.SaveCustom(new MCPCustomServer("custom-existing", "Existing", MCPCustomServer.Kind.Stdio) { Command = "offline-fixture" })));
            var stored = File.ReadAllBytes(MCPPaths.CustomsURL);
            foreach (var corner in new[] { new Point(2, 2), new Point(21, 21) })
            {
                rig.OpenAdd(); var editor = rig.Editor!; rig.Do(() => Field(editor, "My server").Text = "Draft");
                // The element UI Automation lists: Avalonia's automation peer of the button (the source of its UIA provider).
                var close = Nodes<Button>(editor).Single(b => AutomationProperties.GetName(b) == "Close"); var peer = ControlAutomationPeer.CreatePeerForElement(close);
                Assert.Equal("Close", peer.GetName()); Assert.Equal(AutomationControlType.Button, peer.GetAutomationControlType()); Assert.True(peer.IsEnabled());
                var rect = peer.GetBoundingRectangle(); Assert.Equal(24, rect.Width, 0.5); Assert.Equal(24, rect.Height, 0.5);
                // A real press near a corner of its 24 by 24 square.
                rig.Click(close, corner);
                Assert.False(rig.Presenter.Popups[0].IsOpen); Assert.Null(rig.Editor); Assert.Equal(stored, File.ReadAllBytes(MCPPaths.CustomsURL)); Assert.Single(rig.Model.Mcp.Customs);
            }
            // The draft is discarded: the editor opens empty again.
            rig.OpenAdd(); Assert.Equal("", Field(rig.Editor!, "My server").Text);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:624-677 interaction: a press outside, through Avalonia's overlay popup; Escape is NOT closed by the port's popovers in this host (see the report), and the native popup window is NATIVE-ONLY
    public void A_press_outside_closes_the_real_popover_and_discards_the_draft()
    {
        Run(rig =>
        {
            rig.OpenAdd(); var popup = Assert.Single(rig.Presenter.Popups); rig.Do(() => Field(rig.Editor!, "My server").Text = "Draft");
            var outside = new Point(rig.Window.Width - 5, rig.Window.Height - 5);
            rig.Press(outside);
            Assert.False(popup.IsOpen, "a press outside did not close the editor popover"); Assert.Null(rig.Editor); Assert.Empty(rig.Model.Mcp.Customs);
            rig.OpenAdd(); Assert.Equal("", Field(rig.Editor!, "My server").Text);
        });
    }
}
