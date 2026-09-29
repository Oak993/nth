using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.App.Tests.Window.Native;

// Reads one known owned HWND on its own UI thread; no window enumeration or external UIA.
// The enclosing OnUi call and child job provide liveness bounds. GetWindowTextW itself is
// synchronous; a hung callback is a failed child, never a successful empty title.
internal static class WindowManagerNativeTitle
{
    internal static string ReadOnOwnerThread(nint window)
    {
        WindowManagerNativeMethods.RequireOwner(window);
        var owner = WindowManagerNativeMethods.GetWindowThreadProcessId(window, out _);
        if (owner != WindowManagerNativeMethods.GetCurrentThreadId())
            throw new InvalidOperationException("HWND title reads must run on the fixture's owning UI thread.");
        if (!IsWindowUnicode(window))
            throw new InvalidOperationException("The factory did not create a Unicode HWND.");
        const int capacity = 1024;
        var text = new StringBuilder(capacity);
        var copied = GetWindowTextW(window, text, capacity);
        if (copied <= 0 || copied >= capacity - 1 || copied != text.Length)
            throw new InvalidOperationException("Owned HWND title was empty, truncated or inconsistent with its native result: " + copied);
        return text.ToString();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowTextW(nint window, StringBuilder text, int maximum);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowUnicode(nint window);
}
