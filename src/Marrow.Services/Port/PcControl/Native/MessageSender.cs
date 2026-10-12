// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.6, 2.8, critique C7, C31). Rung R2:
// messages to one child window of the target app, never to the foreground and never through the real input queue. Sent
// messages use SendMessageTimeout with SMTO_ABORTIFHUNG (a hung app only fails this call); clicks, keys, characters and
// commands are posted, so nothing waits on a dialog they open. Menu items are read with their state before a command is
// sent (a disabled or greyed item is reported, never run). Never used by automated tests.
using System.Text;
using Marrow.Core.Port.PcControl;

namespace Marrow.Services.Port.PcControl.Native;

public sealed class MessageSender : IPcMessageSink
{
    /// <summary>The most a sent message waits for the app.</summary>
    public const uint SendTimeoutMs = 1500;

    // MapVirtualKey MAPVK_VK_TO_VSC.
    private const uint VkToScan = 0;

    public nint FocusControl(nint window)
    {
        var thread = Win32.GetWindowThreadProcessId(window, out _);
        if (thread == 0) return 0;
        var info = new Win32.GUITHREADINFO { Size = System.Runtime.InteropServices.Marshal.SizeOf<Win32.GUITHREADINFO>() };
        if (!Win32.GetGUIThreadInfo(thread, ref info) || info.Focus == 0) return 0;
        return Win32.GetAncestor(info.Focus, Win32.GA_ROOT) == Win32.GetAncestor(window, Win32.GA_ROOT) ? info.Focus : 0;
    }

    public nint ChildAt(nint window, PcPoint screen)
    {
        using var dpi = Dpi.PerMonitorV2();
        var current = window;
        // The deepest visible, enabled, hit-testable child under the point.
        for (var depth = 0; depth < 32; depth++)
        {
            var point = Client(current, screen);
            var child = Win32.ChildWindowFromPointEx(current, point, Win32.CWP_SKIPINVISIBLE | Win32.CWP_SKIPDISABLED | Win32.CWP_SKIPTRANSPARENT);
            if (child == 0 || child == current) break;
            current = child;
        }
        return current;
    }

    public bool IsUnicode(nint control) => Win32.IsWindowUnicode(control);

    // Posted: a button that opens a modal dialog would otherwise hold this call until the dialog closes.
    public bool ButtonClick(nint control) => Win32.PostMessage(control, Win32.BM_CLICK, 0, 0);

    public bool ReplaceSelection(nint control, string text, bool all)
    {
        if (all && Win32.SendMessageTimeout(control, Win32.EM_SETSEL, 0, -1, Win32.SMTO_ABORTIFHUNG, SendTimeoutMs, out _) == 0) return false;
        // wParam 1: the replacement can be undone in the control.
        return Win32.SendMessageTimeoutText(control, Win32.EM_REPLACESEL, 1, text, Win32.SMTO_ABORTIFHUNG, SendTimeoutMs, out _) != 0;
    }

    public bool SetText(nint control, string text) =>
        Win32.SendMessageTimeoutText(control, Win32.WM_SETTEXT, 0, text, Win32.SMTO_ABORTIFHUNG, SendTimeoutMs, out var result) != 0 && result != 0;

    public bool Chars(nint control, string text)
    {
        foreach (var unit in text)
        {
            if (!Win32.PostMessage(control, Win32.WM_CHAR, unit, 1)) return false;
        }
        return true;
    }

    public bool Key(nint control, ushort virtualKey, bool extended, int repeat)
    {
        var scan = (nint)(MapVirtualKey(virtualKey, VkToScan) & 0xFF);
        var down = 1 | (scan << 16) | (extended ? 1 << 24 : 0);
        var up = down | (1 << 30) | unchecked((nint)(1u << 31));
        for (var i = 0; i < Math.Max(1, repeat); i++)
        {
            if (!Win32.PostMessage(control, Win32.WM_KEYDOWN, virtualKey, down) || !Win32.PostMessage(control, Win32.WM_KEYUP, virtualKey, up)) return false;
        }
        return true;
    }

    public bool Mouse(nint control, PcPoint screen, PcMouseButton button, int count)
    {
        using var dpi = Dpi.PerMonitorV2();
        var point = Client(control, screen);
        var at = Win32.PointParam(point.X, point.Y);
        var (down, up, flag) = button switch
        {
            PcMouseButton.Right => (Win32.WM_RBUTTONDOWN, Win32.WM_RBUTTONUP, Win32.MK_RBUTTON),
            PcMouseButton.Middle => (Win32.WM_MBUTTONDOWN, Win32.WM_MBUTTONUP, Win32.MK_MBUTTON),
            _ => (Win32.WM_LBUTTONDOWN, Win32.WM_LBUTTONUP, Win32.MK_LBUTTON),
        };
        if (!Win32.PostMessage(control, Win32.WM_MOUSEMOVE, 0, at)) return false;
        for (var i = 0; i < Math.Max(1, count); i++)
        {
            var press = i == 1 && button == PcMouseButton.Left ? Win32.WM_LBUTTONDBLCLK : down;
            if (!Win32.PostMessage(control, press, (nint)flag, at) || !Win32.PostMessage(control, up, 0, at)) return false;
        }
        return true;
    }

    public bool Drag(nint control, IReadOnlyList<PcPoint> path)
    {
        using var dpi = Dpi.PerMonitorV2();
        if (path.Count < 2) return false;
        nint At(PcPoint screen)
        {
            var point = Client(control, screen);
            return Win32.PointParam(point.X, point.Y);
        }
        if (!Win32.PostMessage(control, Win32.WM_MOUSEMOVE, 0, At(path[0])) || !Win32.PostMessage(control, Win32.WM_LBUTTONDOWN, (nint)Win32.MK_LBUTTON, At(path[0]))) return false;
        foreach (var point in path.Skip(1))
        {
            if (!Win32.PostMessage(control, Win32.WM_MOUSEMOVE, (nint)Win32.MK_LBUTTON, At(point))) return false;
        }
        return Win32.PostMessage(control, Win32.WM_LBUTTONUP, 0, At(path[^1]));
    }

    public PcMenuItem? MenuItem(nint window, KeyChord chord)
    {
        var menu = Win32.GetMenu(window);
        return menu == 0 ? null : Search(menu, chord, 0);
    }

    // The menu bar and its popups (three levels), item by item, by the shortcut text after the tab.
    private static PcMenuItem? Search(nint menu, KeyChord chord, int depth)
    {
        var count = Win32.GetMenuItemCount(menu);
        for (var position = 0; position < count; position++)
        {
            var state = Win32.GetMenuState(menu, (uint)position, Win32.MF_BYPOSITION);
            if ((state & Win32.MF_POPUP) != 0)
            {
                if (depth < 3 && Win32.GetSubMenu(menu, position) is var sub && sub != 0 && Search(sub, chord, depth + 1) is { } found) return found;
                continue;
            }
            var text = new StringBuilder(256);
            if (Win32.GetMenuString(menu, (uint)position, text, text.Capacity, Win32.MF_BYPOSITION) <= 0) continue;
            var label = text.ToString();
            var tab = label.IndexOf('\t', StringComparison.Ordinal);
            if (tab < 0) continue;
            var parsed = KeyChordParser.Parse(label[(tab + 1)..].Trim());
            if (parsed.Chord is not { } shown || shown.Key != chord.Key || !shown.Modifiers.Order().SequenceEqual(chord.Modifiers.Order())) continue;
            var id = Win32.GetMenuItemID(menu, position);
            if (id == uint.MaxValue) continue;
            return new PcMenuItem((int)id, label, (state & (Win32.MF_GRAYED | Win32.MF_DISABLED)) != 0);
        }
        return null;
    }

    public bool Command(nint window, int id) => Win32.PostMessage(window, Win32.WM_COMMAND, id, 0);

    public bool ShowNoActivate(nint window, bool minimize) => Win32.ShowWindow(window, minimize ? Win32.SW_SHOWMINNOACTIVE : Win32.SW_SHOWNOACTIVATE) || true;

    private static Win32.POINT Client(nint window, PcPoint screen)
    {
        var point = new Win32.POINT { X = screen.X, Y = screen.Y };
        Win32.ScreenToClient(window, ref point);
        return point;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    [System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
