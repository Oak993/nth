using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Marrow.App.Views.Settings;
using Marrow.App.Window;
using Marrow.Core.Models;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for UI-MCP-047 (DroppyCode/App/Views/Settings/MCPSettingsPage.swift:570-572 the popover, :624-677 the editor): the custom server
// editor in its own native popup window (a PopupRoot) over the real Settings window (WindowManager.ShowSettings, the real MCP page), driven by
// the system's pointer and keyboard (SetCursorPos, mouse_event, keybd_event). Escape closes an NSPopover (a transient popover is a window that takes the key and
// answers cancelOperation), so it closes the editor and the draft goes with it, with no field focused (edit mode, :663-665 moves no focus) and from the
// focused Name field (add mode, :663-665). The Settings window stays. Needs a free interactive desktop.
[Collection("WindowManagerNative")]
public sealed class MCPCustomEditorNativeTests(WindowManagerNativeSession native)
{
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    private static void Tap(byte key)
    {
        keybd_event(key, 0, 0, 0);
        keybd_event(key, 0, 2, 0); // KEYEVENTF_KEYUP
        Thread.Sleep(60);
    }

    private static void ClickAt(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(120);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
    }

    [Fact]
    [Trait("Req", "UI-MCP-047")] // MCPSettingsPage.swift:570-572, :624-677; NSPopover's transient Escape, on a real popup window over the real Settings window
    public void On_the_real_Settings_window_Escape_closes_the_custom_server_editor_in_edit_mode_and_from_the_focused_Name_field_and_the_draft_goes() => native.Run(subject =>
    {
        N.Check(N.GetCursorPos(out var original), "GetCursorPos");
        try
        {
            WindowManager manager = null!;
            AvaloniaWindow settings = null!;
            native.OnUi(() =>
            {
                subject.Main.Hide();
                subject.Settings.Hide();
                FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
                manager = new WindowManager { MainContent = () => new Border() };
                manager.Model.Mcp.SaveCustom(new MCPCustomServer("custom-example", "Example", MCPCustomServer.Kind.Stdio) { Command = "offline-fixture" });
                manager.ShowSettings(SettingsPage.Mcp);
                settings = manager.SettingsOverlay!.Window; // OD-0140: Settings is a panel in the main window
                native.Register(settings);
            });
            N.Until(() => native.OnUi(() => manager.SettingsOverlay is { Shown: >= 1 }), "the Settings open never finished", 8000);
            Thread.Sleep(1500);
            void Front()
            {
                native.OnUi(() => WindowManagerNativeForeground.Acquire(settings.TryGetPlatformHandle()!.Handle, native.ThreadId));
                N.Until(() => native.OnUi(() => settings.IsActive) && N.GetForegroundWindow() == native.OnUi(() => settings.TryGetPlatformHandle()!.Handle), "the Settings window is not the foreground window");
                Thread.Sleep(300);
            }
            // (these two run on the window's thread: a caller wraps them in native.OnUi)
            Popup? NewestUi() => WindowTransients.Newest(settings) as Popup;
            MCPCustomEditor? EditorUi() => NewestUi()?.Child?.GetVisualDescendants().OfType<MCPCustomEditor>().FirstOrDefault();
            bool EditorOpen() => native.OnUi(() => EditorUi() is not null);
            TextBox NameFieldUi() => EditorUi()!.GetVisualDescendants().OfType<TextBox>().First(field => field.PlaceholderText == "My server");
            PixelPoint CenterOf(Control control) => native.OnUi(() => control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2)));
            Button ButtonWith(string text) => native.OnUi(() => settings.GetVisualDescendants().OfType<Button>().First(button => button.Bounds.Width > 40 && button.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == text)));

            // Edit mode: the Options menu, its Edit row, the editor; no field is focused (:663-665 moves no focus in edit mode).
            Front();
            ClickAt(CenterOf(native.OnUi(() => settings.GetVisualDescendants().OfType<Button>().First(button => ToolTip.GetTip(button) as string == "Server options" && button.IsEffectivelyVisible))));
            N.Until(() => native.OnUi(() => NewestUi() is { IsOpen: true }), "the server options menu never opened");
            Thread.Sleep(1200);
            PopoverItem? edit = null;
            N.Until(() => (edit = native.OnUi(() => NewestUi()?.Child?.GetVisualDescendants().OfType<PopoverItem>().FirstOrDefault(item => item.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Edit")))) is not null, "the server options menu has no Edit row");
            ClickAt(CenterOf(edit!));
            N.Until(EditorOpen, "the editor never opened in edit mode");
            Thread.Sleep(1200);
            Assert.Equal("Example", native.OnUi(() => NameFieldUi().Text));
            Assert.False(native.OnUi(() => EditorUi()!.GetVisualDescendants().OfType<TextBox>().Any(field => field.IsFocused)), "edit mode focused a field");
            Front();
            Tap(0x1B);
            N.Until(() => !EditorOpen(), "Escape did not close the editor in edit mode");
            Assert.True(native.OnUi(() => settings.IsVisible), "the Settings window went with the editor");

            // Add mode: the Name field has the focus; text typed there; Escape closes the editor, and the next one opens empty.
            ClickAt(CenterOf(ButtonWith("Add server")));
            N.Until(EditorOpen, "the add editor never opened");
            N.Until(() => native.OnUi(() => NameFieldUi().IsFocused), "add mode did not focus the Name field");
            Thread.Sleep(600);
            foreach (var key in new byte[] { 0x44, 0x52, 0x41, 0x46, 0x54 }) Tap(key); // d r a f t
            N.Until(() => native.OnUi(() => NameFieldUi().Text) == "draft", "the typed name did not reach the Name field");

            // The editor is 380 x 450 points, 34 more for a variable row (:624, :679-685), in its own popup window.
            Assert.Equal(new Size(380, 450), native.OnUi(() => EditorUi()!.Bounds.Size));
            ClickAt(CenterOf(native.OnUi(() => EditorUi()!.GetVisualDescendants().OfType<Button>().First(button => button.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Add environment variable")))));
            N.Until(() => native.OnUi(() => EditorUi()!.Bounds.Height) == 484, "a variable row did not grow the editor by 34 points");

            // The Kind picker is a popover over the editor: Escape closes it alone, and the next Escape the editor.
            var picker = native.OnUi(() => EditorUi()!.GetVisualDescendants().OfType<GlassPickerButton<MCPCustomServer.Kind>>().Single());
            ClickAt(CenterOf(picker));
            N.Until(() => native.OnUi(() => NewestUi() is { IsOpen: true } popup && popup.Child?.GetVisualDescendants().OfType<MCPCustomEditor>().Any() != true), "the Kind picker never opened");
            Thread.Sleep(900);
            Front();
            Tap(0x1B);
            N.Until(() => native.OnUi(() => NewestUi()?.Child?.GetVisualDescendants().OfType<MCPCustomEditor>().Any() == true), "Escape did not close the Kind picker alone");
            Assert.True(EditorOpen(), "Escape closed the editor with the Kind picker");
            Front();
            Tap(0x1B);
            N.Until(() => !EditorOpen(), "Escape in the focused Name field did not close the editor");
            Assert.True(native.OnUi(() => settings.IsVisible), "the Settings window went with the editor");
            Thread.Sleep(600);
            ClickAt(CenterOf(ButtonWith("Add server")));
            N.Until(EditorOpen, "the add editor did not open again");
            Assert.Equal("", native.OnUi(() => NameFieldUi().Text)); // the draft is discarded
            Assert.Equal(1, native.OnUi(() => manager.Model.Mcp.Customs.Count)); // nothing saved
        }
        finally
        {
            N.SetCursorPos(original.X, original.Y);
        }
    }, factType: typeof(MCPCustomEditorNativeTests));
}
