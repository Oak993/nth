// PORT-ONLY: Tactus, PC control (OD-0144, ED-NEW-pcctl-7, ED-NEW-pcctl-8; DESIGN.md 2.6, 2.8, 2.11, 3.1). The Win32 calls
// of the cursor-free rungs and the user-state reads: messages to one child window (SendMessageTimeout, PostMessage), the
// thread's focus (GetGUIThreadInfo), the child under a point, menu items and their state, the real cursor, key state and
// the last input time (read only), showing a window without activating it, process start times and out-of-context
// WinEvent hooks. Nothing here runs in automated tests.
using System.Runtime.InteropServices;
using System.Text;

namespace Marrow.Services.Port.PcControl.Native;

internal static partial class Win32
{
    internal delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    internal struct GUITHREADINFO
    {
        public int Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public RECT CaretRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LASTINPUTINFO
    {
        public int Size;
        public uint Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FILETIME
    {
        public uint Low;
        public uint High;

        public readonly long Ticks => ((long)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
        public uint Private;
    }

    internal const uint SMTO_ABORTIFHUNG = 0x0002;
    internal const uint SMTO_BLOCK = 0x0001;
    internal const uint WM_SETTEXT = 0x000C;
    internal const uint WM_KEYDOWN = 0x0100;
    internal const uint WM_KEYUP = 0x0101;
    internal const uint WM_CHAR = 0x0102;
    internal const uint WM_COMMAND = 0x0111;
    internal const uint WM_MOUSEMOVE = 0x0200;
    internal const uint WM_LBUTTONDOWN = 0x0201;
    internal const uint WM_LBUTTONUP = 0x0202;
    internal const uint WM_LBUTTONDBLCLK = 0x0203;
    internal const uint WM_RBUTTONDOWN = 0x0204;
    internal const uint WM_RBUTTONUP = 0x0205;
    internal const uint WM_MBUTTONDOWN = 0x0207;
    internal const uint WM_MBUTTONUP = 0x0208;
    internal const uint WM_QUIT = 0x0012;
    internal const uint BM_CLICK = 0x00F5;
    internal const uint EM_SETSEL = 0x00B1;
    internal const uint EM_REPLACESEL = 0x00C2;
    internal const uint MK_LBUTTON = 0x0001;
    internal const uint MK_RBUTTON = 0x0002;
    internal const uint MK_MBUTTON = 0x0010;
    internal const uint CWP_SKIPINVISIBLE = 0x0001;
    internal const uint CWP_SKIPDISABLED = 0x0002;
    internal const uint CWP_SKIPTRANSPARENT = 0x0004;
    internal const uint MF_BYPOSITION = 0x0400;
    internal const uint MF_GRAYED = 0x0001;
    internal const uint MF_DISABLED = 0x0002;
    internal const uint MF_POPUP = 0x0010;
    internal const int SW_SHOWMINNOACTIVE = 7;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal static readonly nint HWND_TOP = 0;
    internal const int VK_SHIFT = 0x10;
    internal const int VK_CONTROL = 0x11;
    internal const int VK_MENU = 0x12;
    internal const int VK_LWIN = 0x5B;
    internal const int VK_RWIN = 0x5C;
    internal const int VK_LBUTTON = 0x01;
    internal const int VK_RBUTTON = 0x02;
    internal const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    internal const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    internal const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    internal const uint EVENT_OBJECT_CREATE = 0x8000;
    internal const uint EVENT_OBJECT_DESTROY = 0x8001;
    internal const uint EVENT_OBJECT_SHOW = 0x8002;
    internal const uint EVENT_OBJECT_HIDE = 0x8003;
    internal const uint EVENT_OBJECT_REORDER = 0x8004;
    internal const uint EVENT_OBJECT_FOCUS = 0x8005;
    internal const uint EVENT_OBJECT_SELECTION = 0x8006;
    internal const uint EVENT_OBJECT_STATECHANGE = 0x800A;
    internal const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    internal const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    internal const uint EVENT_OBJECT_VALUECHANGE = 0x800E;
    internal const int OBJID_WINDOW = 0;
    internal const int OBJID_CARET = -8;
    internal const int OBJID_CURSOR = -9;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SendMessageTimeout(nint window, uint message, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SendMessageTimeoutText(nint window, uint message, nint wParam, string lParam, uint flags, uint timeoutMs, out nint result);

    [DllImport("user32.dll", EntryPoint = "PostMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint ChildWindowFromPointEx(nint parent, POINT point, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(nint window, ref POINT point);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowUnicode(nint window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint GetMenu(nint window);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMenuItemCount(nint menu);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint GetSubMenu(nint menu, int position);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetMenuItemID(nint menu, int position);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetMenuState(nint menu, uint item, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMenuStringW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMenuString(nint menu, uint item, StringBuilder text, int max, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern uint GetTickCount();

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetProcessTimes(nint process, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWinEvent(nint hook);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern int GetMessage(out MSG message, nint window, uint min, uint max);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    internal static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll", EntryPoint = "PostThreadMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint thread, uint message, nint wParam, nint lParam);

    /// <summary>A window message's lParam for a client point: x in the low word, y in the high word.</summary>
    internal static nint PointParam(int x, int y) => (nint)((y << 16) | (x & 0xFFFF));
}
