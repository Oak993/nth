using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Marrow.App.Tests.Window;

// Real user32 message-window harness for WindowManagerHighRiskTests. It never starts Marrow or creates an app window.
internal sealed class NativeMessageWindow : IDisposable
{
    private const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    private const uint WM_NCLBUTTONDOWN = 0x00A1;
    private const uint WM_NCLBUTTONUP = 0x00A2;
    private const uint PM_REMOVE = 0x0001;
    private static readonly nint HWND_MESSAGE = -3;
    private readonly string className = "MarrowWindowManagerHighRisk_" + Guid.NewGuid().ToString("N");
    private readonly NativeWindowProc callback;
    private readonly NativeHandler handler;
    private readonly ushort atom;

    internal NativeMessageWindow(NativeHandler handler)
    {
        this.handler = handler;
        callback = WindowProc;
        var klass = new WndClassEx
        {
            Size = (uint)Marshal.SizeOf<WndClassEx>(),
            WindowProc = callback,
            Instance = GetModuleHandle(null),
            ClassName = className,
        };
        atom = RegisterClassEx(ref klass);
        if (atom == 0 && Marshal.GetLastWin32Error() != ERROR_CLASS_ALREADY_EXISTS)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        Handle = CreateWindowEx(0, className, className, 0, 0, 0, 1, 1, HWND_MESSAGE, 0, klass.Instance, 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public nint Handle { get; }

    public void Dispose()
    {
        DestroyWindow(Handle);
        if (atom != 0) UnregisterClass(className, GetModuleHandle(null));
        GC.KeepAlive(callback);
        GC.KeepAlive(handler);
    }

    public static void Send(nint hwnd, uint message, nint wParam = 0, nint lParam = 0) =>
        SendMessage(hwnd, message, wParam, lParam);

    internal static void Pump(nint hwnd)
    {
        if (PeekMessage(out var message, hwnd, 0, 0, PM_REMOVE))
        {
            _ = TranslateMessage(ref message);
            _ = DispatchMessage(ref message);
        }
    }

    private nint WindowProc(nint hwnd, uint message, nint wParam, nint lParam)
    {
        var handled = false;
        var result = handler(hwnd, message, wParam, lParam, ref handled);
        // A deliberately inert nonclient fallback keeps an invalid caption hit-test from entering DefWindowProc's
        // native drag/modal loop. The production hook handles valid caption hits before this guard is reached.
        if (!handled && (message == WM_NCLBUTTONDOWN || message == WM_NCLBUTTONUP)) return 0;
        return handled ? result : DefWindowProc(hwnd, message, wParam, lParam);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint NativeWindowProc(nint hwnd, uint message, nint wParam, nint lParam);

    internal delegate nint NativeHandler(nint hwnd, uint message, nint wParam, nint lParam, ref bool handled);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public NativeWindowProc WindowProc;
        public int ClassExtra;
        public int WindowExtra;
        public nint Instance;
        public nint Icon;
        public nint Cursor;
        public nint Background;
        public string? MenuName;
        public string ClassName;
        public nint SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint extendedStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool UnregisterClass(string className, nint instance);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SendMessage(nint hwnd, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PeekMessage(
        out NativeMessage message, nint hwnd, uint minimum, uint maximum, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref NativeMessage message);
    [DllImport("user32.dll")] private static extern nint DispatchMessage(ref NativeMessage message);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? moduleName);
}
