using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.App.Tests.Window.Native;

// Test-owned Win32 boundary. No calls here invoke Marrow's executable or a provider.
internal static class WindowManagerNativeMethods
{
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_NCLBUTTONDOWN = 0x00A1;
    internal const uint WM_NCLBUTTONUP = 0x00A2;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_CANCELMODE = 0x001F;
    internal const uint WM_CAPTURECHANGED = 0x0215;
    internal const uint WM_CONTEXTMENU = 0x007B;
    internal const uint WM_SYSCOMMAND = 0x0112;
    internal const uint WM_INITMENUPOPUP = 0x0117;
    internal const uint WM_ENTERMENULOOP = 0x0211;
    internal const uint WM_EXITMENULOOP = 0x0212;
    internal const uint WM_ENTERSIZEMOVE = 0x0231;
    internal const uint WM_SIZING = 0x0214;
    internal const uint WM_EXITSIZEMOVE = 0x0232;
    // Test-owned marker below the registered-message range, sent only to this fixture's HWNDs.
    internal const uint WM_TEST_FENCE = 0xB750;
    internal const uint SC_SIZE = 0xF000;
    internal const uint SC_MINIMIZE = 0xF020;
    internal const uint SC_MAXIMIZE = 0xF030;
    internal const uint SC_CLOSE = 0xF060;
    internal const uint SC_KEYMENU = 0xF100;
    internal const uint SC_RESTORE = 0xF120;
    internal const int HTCLIENT = 1;
    internal const int HTCAPTION = 2;
    internal const int HTMINBUTTON = 8;
    internal const int HTMAXBUTTON = 9;
    internal const int HTRIGHT = 11;
    internal const int HTCLOSE = 20;
    internal const uint MOUSE_MOVE = 0x0001;
    internal const uint MOUSE_LEFTDOWN = 0x0002;
    internal const uint MOUSE_LEFTUP = 0x0004;
    internal const uint MOUSE_RIGHTDOWN = 0x0008;
    internal const uint MOUSE_RIGHTUP = 0x0010;

    internal static void Check(bool succeeded, string operation)
    {
        if (!succeeded) throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }

    internal static void RequireOwner(nint window)
    {
        _ = GetWindowThreadProcessId(window, out var process);
        if (!IsWindow(window) || process != (uint)Environment.ProcessId)
            throw new InvalidOperationException("Native input target is not a live window of this testhost.");
    }

    internal static NativeWindowRect Rect(nint window)
    {
        RequireOwner(window);
        Check(GetWindowRect(window, out var rectangle), "GetWindowRect");
        return rectangle;
    }

    internal static nint Pack(NativeWindowPoint point) =>
        unchecked((nint)(uint)((ushort)(short)point.X | ((uint)(ushort)(short)point.Y << 16)));

    internal static int Hit(nint window, NativeWindowPoint point)
    {
        RequireOwner(window);
        Check(SendMessageTimeout(window, WM_NCHITTEST, 0, Pack(point), 0x0002, 1000, out var result) != 0,
            "WM_NCHITTEST did not return within one second");
        return unchecked((int)result);
    }

    internal static nint RootAt(NativeWindowPoint point) => GetAncestor(WindowFromPoint(point), 2);

    internal static NativeGuiThreadInfo Gui(uint thread)
    {
        var info = new NativeGuiThreadInfo { Size = (uint)Marshal.SizeOf<NativeGuiThreadInfo>() };
        Check(GetGUIThreadInfo(thread, ref info), "GetGUIThreadInfo");
        return info;
    }

    internal static string ClassName(nint window)
    {
        var name = new StringBuilder(256);
        _ = GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    // Only enumerates this fixture's native UI thread. No foreign window titles or contents are read.
    internal static nint MenuOn(uint thread)
    {
        nint found = 0;
        NativeWindowEnumerator callback = (window, _) =>
        {
            if (IsWindowVisible(window) && string.Equals(ClassName(window), "#32768", StringComparison.Ordinal))
            {
                found = window;
                return false;
            }
            return true;
        };
        _ = EnumThreadWindows(thread, callback, 0);
        GC.KeepAlive(callback);
        return found;
    }

    internal static void Post(nint window, uint message, nint wParam = 0, nint lParam = 0)
    {
        if (message is WM_NCLBUTTONDOWN or WM_NCLBUTTONUP or WM_LBUTTONUP or 0x0201)
            throw new InvalidOperationException("Pointer messages must originate from hit-tested SendInput.");
        RequireOwner(window);
        Check(PostMessage(window, message, wParam, lParam), "PostMessage to test window");
    }

    internal static void Mouse(params uint[] flags)
    {
        var inputs = flags.Select(flag => new NativeWindowInput
        {
            Type = 0,
            Mouse = new NativeWindowMouseInput { Flags = flag },
        }).ToArray();
        var count = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeWindowInput>());
        Check(count == inputs.Length, "SendInput did not insert the complete guarded mouse sequence");
    }

    internal static bool Wait(Func<bool> condition, int milliseconds = 3000)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            if (condition()) return true;
            Thread.Sleep(10); // Driver thread only; the native STA continues pumping.
        }
        while (clock.ElapsedMilliseconds < milliseconds);
        return condition();
    }

    internal static void Until(Func<bool> condition, string reason, int milliseconds = 3000)
    {
        if (!Wait(condition, milliseconds)) throw new TimeoutException(reason);
    }

    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint window, uint attribute, out int value, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(nint window, out NativeWindowRect rectangle);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetGUIThreadInfo(uint thread, ref NativeGuiThreadInfo info);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(NativeWindowPoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int maximum);
    [DllImport("user32.dll")] internal static extern bool EnumThreadWindows(uint thread, NativeWindowEnumerator callback, nint parameter);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, NativeWindowInput[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetCursorPos(out NativeWindowPoint point);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern nint SetCapture(nint window);
    [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")] internal static extern nint GetSystemMenu(nint window, bool reset);
    [DllImport("user32.dll")] internal static extern uint GetMenuState(nint menu, uint item, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowPlacement(nint window, ref NativeWindowPlacement placement);
    [DllImport("user32.dll")] internal static extern bool EndMenu();
    [DllImport("user32.dll", SetLastError = true)] internal static extern nuint SetTimer(nint window, nuint id, uint period, NativeWindowTimer callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint window, nuint id);
    [DllImport("comctl32.dll", SetLastError = true)] internal static extern bool SetWindowSubclass(nint window, NativeWindowSubclass callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] internal static extern bool RemoveWindowSubclass(nint window, NativeWindowSubclass callback, nuint id);
    [DllImport("comctl32.dll")] internal static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate bool NativeWindowEnumerator(nint window, nint parameter);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate void NativeWindowTimer(nint window, uint message, nuint id, uint time);

[UnmanagedFunctionPointer(CallingConvention.Winapi)]
internal delegate nint NativeWindowSubclass(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowPoint
{
    internal int X;
    internal int Y;
    internal NativeWindowPoint(int x, int y) { X = x; Y = y; }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
    internal readonly int Width => Right - Left;
    internal readonly int Height => Bottom - Top;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeGuiThreadInfo
{
    internal uint Size;
    internal uint Flags;
    internal nint Active;
    internal nint Focus;
    internal nint Capture;
    internal nint MenuOwner;
    internal nint MoveSize;
    internal nint Caret;
    internal NativeWindowRect CaretRectangle;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowPlacement
{
    internal uint Length;
    internal uint Flags;
    internal uint ShowCommand;
    internal NativeWindowPoint Minimum;
    internal NativeWindowPoint Maximum;
    internal NativeWindowRect Normal;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeWindowMouseInput
{
    internal int X;
    internal int Y;
    internal uint MouseData;
    internal uint Flags;
    internal uint Time;
    internal nuint ExtraInfo;
}

// Explicitly x64, checked before platform initialization and before any input is sent.
[StructLayout(LayoutKind.Explicit, Size = 40)]
internal struct NativeWindowInput
{
    [FieldOffset(0)] internal uint Type;
    [FieldOffset(8)] internal NativeWindowMouseInput Mouse;
}
