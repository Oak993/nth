// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1, 16.2; unit AS-19; ED-NEW-dextra-as19-9). No Swift twin.
// The Win32 calls teach mode's three Windows parts make (the input hooks, UI Automation's window and process names, the frame): declared once,
// internal, with the structures they fill. Never called by automated tests; the Native tests (TeachInputNativeTests, TeachProbeNativeTests,
// TeachFramesNativeTests, TeachStripNativeTests) are the lead's to run with focus on a quiet desktop.
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.App.Assistant.Teach;

internal static class TeachNative
{
    internal delegate nint HookProc(int code, nint wParam, nint lParam);

    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;
    internal const uint WmQuit = 0x0012;
    internal const uint WmKeyDown = 0x0100;
    internal const uint WmSysKeyDown = 0x0104;
    internal const uint WmLButtonDown = 0x0201;
    internal const uint WmRButtonDown = 0x0204;
    internal const uint MouseInjected = 0x01;
    internal const uint KeyInjected = 0x10;
    internal const uint GaRoot = 2;
    internal const uint ProcessQueryLimited = 0x1000;
    internal const int SmCxDoubleClick = 36;
    internal const int SmCyDoubleClick = 37;
    internal const uint MonitorDefaultToNearest = 2;
    internal const uint SrcCopy = 0x00CC0020;
    internal const uint CaptureBlt = 0x40000000;
    internal const uint WdaExcludeFromCapture = 0x11;
    internal const int GwlExStyle = -20;
    internal const long WsExNoActivate = 0x08000000;
    internal const long WsExToolWindow = 0x00000080;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHook
    {
        public Point Pt;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHook
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Message
    {
        public nint Window;
        public uint Id;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public Point Pt;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    internal static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out Message message, nint window, uint min, uint max, uint remove);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    internal static extern int GetMessage(out Message message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    internal static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    internal static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern nint WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    internal static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint window, StringBuilder text, int max);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll")]
    internal static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    internal static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    internal static extern int ReleaseDC(nint window, nint dc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(nint window, uint affinity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern nint SetWindowLongPtr(nint window, int index, nint value);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    internal static extern nint GetModuleHandle(string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint process);

    [DllImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(nint handle);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    internal static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    internal static extern nint SelectObject(nint dc, nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);

    [DllImport("gdi32.dll")]
    internal static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[]? bits, ref BitmapInfoHeader info, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(nint obj);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteDC(nint dc);

    /// <summary>The window's title (cut to 200 characters), or "".</summary>
    internal static string TitleOf(nint window)
    {
        if (window == 0) return "";
        var text = new StringBuilder(256);
        return GetWindowText(window, text, text.Capacity) > 0 ? text.ToString() : "";
    }

    /// <summary>The name of the program a process runs ("outlook"), or "" when it cannot be asked (a protected process).</summary>
    internal static string ProgramOf(uint process)
    {
        if (process == 0) return "";
        var handle = OpenProcess(ProcessQueryLimited, false, process);
        if (handle == 0) return "";
        try
        {
            var name = new StringBuilder(520);
            var size = name.Capacity;
            return QueryFullProcessImageName(handle, 0, name, ref size) ? Path.GetFileNameWithoutExtension(name.ToString()) : "";
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The app and the top window title of a window handle.</summary>
    internal static (string App, string Title) WindowOf(nint window)
    {
        if (window == 0) return ("", "");
        var root = GetAncestor(window, GaRoot);
        if (root == 0) root = window;
        GetWindowThreadProcessId(root, out var process);
        return (ProgramOf(process), TitleOf(root));
    }
}
