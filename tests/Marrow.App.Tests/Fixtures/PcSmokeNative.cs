using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: Tactus's native probes' helpers (unit 15, P19; ED-NEW-pcctl-43), moved out of the probe classes (a test class
// holds no nested type, R-TEST-2). NATIVE: used only by the *Native* probe classes, which every regular run excludes.

/// <summary>One app of NP-1's rung matrix: its window's exe, how the probe opens it (null: the owner opens it first), the
/// name its text field contains, a safe button to invoke, and how to know its new window.</summary>
public sealed record PcMatrixApp(string Name, string Exe, string? Opens, string? EditName, string? InvokeName, Func<string, string, bool>? IsWindow = null);

/// <summary>NP-3's synthetic pen: one pen device, frames by screen point (InjectSyntheticPointerInput).</summary>
public static class PcSmokePen
{
    public const uint Down = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_DOWN;
    public const uint Move = POINTER_FLAG_INRANGE | POINTER_FLAG_INCONTACT | POINTER_FLAG_UPDATE;
    public const uint Up = POINTER_FLAG_INRANGE | POINTER_FLAG_UP;

    private const uint PT_PEN = 3;
    private const uint POINTER_FEEDBACK_DEFAULT = 1;
    private const uint POINTER_FLAG_INRANGE = 0x0002;
    private const uint POINTER_FLAG_INCONTACT = 0x0004;
    private const uint POINTER_FLAG_DOWN = 0x00010000;
    private const uint POINTER_FLAG_UPDATE = 0x00020000;
    private const uint POINTER_FLAG_UP = 0x00040000;
    private const uint PEN_MASK_PRESSURE = 0x0001;

    /// <summary>A pen device, or 0 when Windows gives none.</summary>
    public static nint Create() => CreateSyntheticPointerDevice(PT_PEN, 1, POINTER_FEEDBACK_DEFAULT);

    public static void Destroy(nint device) => DestroySyntheticPointerDevice(device);

    /// <summary>One pen frame; on failure the Win32 error, else 0.</summary>
    public static int Inject(nint device, PcPoint point, uint flags, uint pressure)
    {
        var info = new POINTER_TYPE_INFO
        {
            Type = PT_PEN,
            Pen = new POINTER_PEN_INFO
            {
                PointerInfo = new POINTER_INFO { PointerType = PT_PEN, PointerFlags = flags, Location = new POINT { X = point.X, Y = point.Y } },
                PenMask = PEN_MASK_PRESSURE,
                Pressure = pressure,
            },
        };
        return InjectSyntheticPointerInput(device, [info], 1) ? 0 : Marshal.GetLastPInvokeError();
    }

    /// <summary>The window under a screen point (to tell whether something covers the canvas).</summary>
    public static nint WindowAt(PcPoint point) => WindowFromPoint(new POINT { X = point.X, Y = point.Y });

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_INFO
    {
        public uint PointerType;
        public uint PointerId;
        public uint FrameId;
        public uint PointerFlags;
        public nint SourceDevice;
        public nint WindowTarget;
        public POINT Location;
        public POINT HimetricLocation;
        public POINT LocationRaw;
        public POINT HimetricLocationRaw;
        public uint Time;
        public uint HistoryCount;
        public int InputData;
        public uint KeyStates;
        public ulong PerformanceCount;
        public int ButtonChangeType;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTER_PEN_INFO
    {
        public POINTER_INFO PointerInfo;
        public uint PenFlags;
        public uint PenMask;
        public uint Pressure;
        public uint Rotation;
        public int TiltX;
        public int TiltY;
    }

    // POINTER_TYPE_INFO: the type, then the union of the touch info (the larger, 144 bytes) and the pen info.
    [StructLayout(LayoutKind.Explicit, Size = 152)]
    private struct POINTER_TYPE_INFO
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public POINTER_PEN_INFO Pen;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreateSyntheticPointerDevice(uint pointerType, uint maxCount, uint mode);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InjectSyntheticPointerInput(nint device, [In] POINTER_TYPE_INFO[] info, uint count);

    [DllImport("user32.dll")]
    private static extern void DestroySyntheticPointerDevice(nint device);

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);
}

/// <summary>NP-12's quiet peek by hand (DESIGN.md 2.10): a saved placement and the no-activate show, bottom and minimize.</summary>
public sealed class PcSmokePlacement
{
    private const int SW_SHOWNOACTIVATE = 4;
    private const int SW_SHOWMINNOACTIVE = 7;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly nint window;
    private WINDOWPLACEMENT placement;

    private PcSmokePlacement(nint window, WINDOWPLACEMENT placement)
    {
        this.window = window;
        this.placement = placement;
    }

    /// <summary>Step 1: the window's placement as it is now.</summary>
    public static PcSmokePlacement Save(nint window)
    {
        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        GetWindowPlacement(window, ref placement);
        return new PcSmokePlacement(window, placement);
    }

    /// <summary>Step 2: restored without activation, at once at the bottom of the Z-order.</summary>
    public void ShowAtBottom()
    {
        ShowWindow(window, SW_SHOWNOACTIVATE);
        SetWindowPos(window, 1, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Step 3: minimized again without activation, and the exact placement put back.</summary>
    public void Restore()
    {
        ShowWindow(window, SW_SHOWMINNOACTIVE);
        SetWindowPlacement(window, ref placement);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public POINT MinPosition;
        public POINT MaxPosition;
        public RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
