// PORT-ONLY (ED-219): the Win32 ITaskbarList3::SetOverlayIcon behind TaskbarBadge's ITaskbarOverlay seam. The badge is drawn
// by TaskbarBadge; this turns its pixels into an HICON, hands it to the taskbar button of the main window with the count as
// its accessible description, and destroys the icon (the taskbar keeps its own copy). Nothing here is reached by a test: the
// process default of TaskbarBadge.Overlay draws nothing, and only Install (the app's start-up) binds this class.
// The taskbar button does not exist until Explorer has made it, and Explorer makes it again after it restarts: the shell's
// registered "TaskbarButtonCreated" message (and a DPI change, which changes the small-icon size) calls Reapply from the main
// window's procedure hook (AppShell.WndProc), which sets the last overlay again.
using System.Runtime.InteropServices;
using Marrow.App.Window;

namespace Marrow.App.Port;

/// <summary>PORT-ONLY. The main window's taskbar overlay icon.</summary>
public sealed class WindowsTaskbarOverlay : ITaskbarOverlay
{
    // winuser.h
    private const int SM_CXSMICON = 49;
    private const uint DefaultDpi = 96;

    private static readonly Lazy<uint> ButtonCreatedMessage = new(() => RegisterWindowMessage("TaskbarButtonCreated"));

    private TaskbarOverlayIcon? current;
    private ITaskbarList3? list;

    /// <summary>Binds this overlay as the process default: the app's start-up, never a test.</summary>
    public static void Install() => TaskbarBadge.Overlay.SetProcessDefault(new WindowsTaskbarOverlay());

    /// <summary>The window message Explorer posts when the main window's taskbar button has been created.</summary>
    public static uint TaskbarButtonCreated => ButtonCreatedMessage.Value;

    /// <summary>SM_CXSMICON at the main window's DPI (the system's, before the window exists).</summary>
    public int SmallIconSize => GetSystemMetricsForDpi(SM_CXSMICON, WindowDpi(MainHandle()));

    public void SetOverlay(TaskbarOverlayIcon? icon)
    {
        current = icon;
        Apply();
    }

    /// <summary>Sets the last overlay again, at the current small-icon size: the button now exists, or the DPI changed.</summary>
    public void Reapply()
    {
        if (current is { } icon && icon.Size != SmallIconSize) current = TaskbarBadge.Render(icon.Count, SmallIconSize);
        Apply();
    }

    private void Apply()
    {
        var window = MainHandle();
        if (window == IntPtr.Zero) return; // no main window yet: Reapply runs once it is up
        var icon = IntPtr.Zero;
        try
        {
            list ??= CreateTaskbarList();
            if (current is { } badge) icon = CreateIcon(badge);
            list.SetOverlayIcon(window, icon, current?.Description ?? "");
        }
        catch (Exception)
        {
            list = null; // Explorer is not running or the button is not there: the next call or Reapply tries again
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
        }
    }

    private static IntPtr MainHandle() => WindowManager.Shared.MainWindow?.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;

    private static uint WindowDpi(IntPtr window)
    {
        var dpi = window == IntPtr.Zero ? 0 : GetDpiForWindow(window);
        if (dpi == 0) dpi = GetDpiForSystem();
        return dpi == 0 ? DefaultDpi : dpi;
    }

    private static ITaskbarList3 CreateTaskbarList()
    {
        var type = Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))
            ?? throw new InvalidOperationException("The taskbar list is not registered.");
        var created = (ITaskbarList3)Activator.CreateInstance(type)!;
        created.HrInit();
        return created;
    }

    // A 32-bit colour bitmap with alpha, and an all-zero mask (the alpha decides). Icon bitmaps are premultiplied.
    private static IntPtr CreateIcon(TaskbarOverlayIcon badge)
    {
        var size = badge.Size;
        var header = new BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = size, biHeight = -size, biPlanes = 1, biBitCount = 32 };
        var screen = GetDC(IntPtr.Zero);
        var color = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        ReleaseDC(IntPtr.Zero, screen);
        var mask = CreateBitmap(size, size, 1, 1, IntPtr.Zero);
        try
        {
            if (color == IntPtr.Zero || mask == IntPtr.Zero || bits == IntPtr.Zero) throw new InvalidOperationException("No bitmap for the badge.");
            var premultiplied = new byte[badge.Bgra.Length];
            for (var offset = 0; offset < premultiplied.Length; offset += 4)
            {
                var alpha = badge.Bgra[offset + 3];
                premultiplied[offset] = (byte)(badge.Bgra[offset] * alpha / 255);
                premultiplied[offset + 1] = (byte)(badge.Bgra[offset + 1] * alpha / 255);
                premultiplied[offset + 2] = (byte)(badge.Bgra[offset + 2] * alpha / 255);
                premultiplied[offset + 3] = alpha;
            }
            Marshal.Copy(premultiplied, 0, bits, premultiplied.Length);
            var info = new ICONINFO { fIcon = true, hbmMask = mask, hbmColor = color };
            var icon = CreateIconIndirect(ref info);
            return icon == IntPtr.Zero ? throw new InvalidOperationException("No icon for the badge.") : icon;
        }
        finally
        {
            if (color != IntPtr.Zero) DeleteObject(color);
            if (mask != IntPtr.Zero) DeleteObject(mask);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public uint xHotspot;
        public uint yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitsPerPixel, IntPtr bits);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr handle);
}

/// <summary>ITaskbarList3 (shobjidl.h) with the vtable of ITaskbarList and ITaskbarList2 above it, in order; only HrInit and
/// SetOverlayIcon are called.</summary>
[ComImport]
[Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITaskbarList3
{
    // ITaskbarList
    void HrInit();
    void AddTab(IntPtr hwnd);
    void DeleteTab(IntPtr hwnd);
    void ActivateTab(IntPtr hwnd);
    void SetActiveAlt(IntPtr hwnd);

    // ITaskbarList2
    void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);

    // ITaskbarList3
    void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
    void SetProgressState(IntPtr hwnd, int flags);
    void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
    void UnregisterTab(IntPtr hwndTab);
    void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
    void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
    void ThumbBarAddButtons(IntPtr hwnd, uint buttons, IntPtr button);
    void ThumbBarUpdateButtons(IntPtr hwnd, uint buttons, IntPtr button);
    void ThumbBarSetImageList(IntPtr hwnd, IntPtr imageList);
    void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string tip);
    void SetThumbnailClip(IntPtr hwnd, IntPtr clip);
}
