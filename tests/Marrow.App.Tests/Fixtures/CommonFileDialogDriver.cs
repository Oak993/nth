using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Drives a real Windows common Open dialog (IFileOpenDialog's window, class #32770) of this process from a test's driver thread
/// with window messages, as a user's typing and clicks reach it: the file name box (control 1148, cmb13, an Edit inside a ComboBoxEx32)
/// and the OK (1) and Cancel (2) buttons. Native runs only (OpenPanelNativeTests).
/// </summary>
public static class CommonFileDialogDriver
{
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_COMMAND = 0x0111;
    private const uint BM_CLICK = 0x00F5;
    private const int IDOK = 1;
    private const int IDCANCEL = 2;
    private const int FileNameBox = 0x047C;

    /// <summary>The visible common dialog of this process, or 0 when none is shown.</summary>
    public static nint Find()
    {
        var process = (uint)Environment.ProcessId;
        nint found = 0;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || ClassName(window) != "#32770") return true;
            GetWindowThreadProcessId(window, out var owner);
            if (owner != process || GetDlgItem(window, IDOK) == 0) return true;
            found = window;
            return false;
        }, 0);
        return found;
    }

    /// <summary>The window's text (a dialog's caption, a button's label), read with WM_GETTEXT.</summary>
    public static string Text(nint window)
    {
        var length = (int)SendMessage(window, WM_GETTEXTLENGTH, 0, 0);
        var buffer = new StringBuilder(length + 1);
        SendMessage(window, WM_GETTEXT, buffer.Capacity, buffer);
        return buffer.ToString();
    }

    /// <summary>The OK button's label as drawn (an ampersand marks its mnemonic).</summary>
    public static string OkLabel(nint dialog) => Text(GetDlgItem(dialog, IDOK));

    /// <summary>Puts <paramref name="text"/> in the file name box, as typing it there does.</summary>
    public static void TypeFileName(nint dialog, string text)
    {
        var box = GetDlgItem(dialog, FileNameBox);
        if (box == 0) throw new InvalidOperationException("the dialog has no file name box");
        var edit = ClassName(box) == "Edit" ? box : Descendant(box, "Edit");
        if (edit == 0) throw new InvalidOperationException("the file name box has no edit field");
        SendMessage(edit, WM_SETTEXT, 0, new StringBuilder(text));
    }

    /// <summary>Clicks OK (posted, as the click lands while the dialog runs its own loop).</summary>
    public static void Ok(nint dialog) => PostMessage(GetDlgItem(dialog, IDOK), BM_CLICK, 0, 0);

    /// <summary>Cancel, as the Cancel button sends it.</summary>
    public static void Cancel(nint dialog) => PostMessage(dialog, WM_COMMAND, IDCANCEL, GetDlgItem(dialog, IDCANCEL));

    public static bool IsOpen(nint dialog) => dialog != 0 && IsWindow(dialog) && IsWindowVisible(dialog);

    private static nint Descendant(nint parent, string className)
    {
        for (var child = FindWindowEx(parent, 0, null, null); child != 0; child = FindWindowEx(parent, child, null, null))
        {
            if (ClassName(child) == className) return child;
            var deeper = Descendant(child, className);
            if (deeper != 0) return deeper;
        }
        return 0;
    }

    private static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    [DllImport("user32.dll")] private static extern bool EnumWindows(Marrow.App.Tests.Window.Native.NativeWindowEnumerator callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetDlgItem(nint dialog, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindowEx(nint parent, nint after, string? className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessage(nint window, uint message, nint wParam, StringBuilder lParam);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
