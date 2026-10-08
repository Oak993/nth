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
    // Escape (NSPopover's transient behaviour closes the popover on it, Escape from the focused Name field included; ED-332 (4)):
    // A_press_outside_..._ and Escape_in_the_focused_name_field_... below; the real Settings window's Escape is SettingsEscapeTests.

    private void Run(Action<MCPCustomEditorRig> test) => headless.OnUiThread(() => { using var rig = new MCPCustomEditorRig(); test(rig); });

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
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:570-572 popover over :624-677, NSPopover's transient Escape: closes it from the focused Name field and discards the draft
    public void Escape_in_the_focused_name_field_closes_the_real_popover_and_discards_the_draft()
    {
        Run(rig =>
        {
            rig.OpenAdd(); var popup = Assert.Single(rig.Presenter.Popups);
            var name = Field(rig.Editor!, "My server"); Assert.True(name.IsFocused, "add mode focuses the Name field");
            rig.Do(() => name.Text = "Draft");
            rig.Key(Key.Escape);
            Assert.False(popup.IsOpen, "Escape in the Name field did not close the editor popover"); Assert.Null(rig.Editor); Assert.Empty(rig.Model.Mcp.Customs);
            // The draft is discarded: the editor opens empty again.
            rig.OpenAdd(); Assert.Equal("", Field(rig.Editor!, "My server").Text);
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // MCPSettingsPage.swift:624-677 interaction: a press outside, through Avalonia's overlay popup; the native popup window is NATIVE-ONLY
    public void A_press_outside_closes_the_real_popover_and_discards_the_draft()
    {
        Run(rig =>
        {
            rig.OpenAdd(); var popup = Assert.Single(rig.Presenter.Popups); rig.Do(() => Field(rig.Editor!, "My server").Text = "Draft");
            var outside = new Point(rig.Window.Width - 5, rig.Window.Height - 5);
            Assert.NotNull(rig.Window.InputHitTest(outside));
            rig.Press(outside);
            Assert.False(popup.IsOpen, "a press outside did not close the editor popover"); Assert.Null(rig.Editor); Assert.Empty(rig.Model.Mcp.Customs);
            rig.OpenAdd(); Assert.Equal("", Field(rig.Editor!, "My server").Text);
        });
    }
}
