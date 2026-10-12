// PORT-ONLY: Tactus pill v2, what the auto-dodge looks at (OD-0145, ED-NEW-pcctl-23, ED-NEW-pcctl-13; DESIGN.md 10.3
// "Auto-dodge", "Dodge rules"). Only the top-level, visible, uncloaked window that is topmost over the pill's area counts
// (a Z-order walk that skips Tactus's own overlays). Its caption buttons (minimize, maximize, close) come from
// WM_GETTITLEBARINFOEX; for custom title bars (Chromium, Electron, Office, WinUI) from DWMWA_CAPTION_BUTTON_BOUNDS where it
// answers, else from UI Automation (the window's Minimize, Maximize, Restore and Close buttons near its top). A showing
// auto-hide taskbar at the top of the monitor is the other reason. Everything is turned into the monitor's DIP for
// PillDodgePlanner. Read on window events only (EVENT_SYSTEM_FOREGROUND, EVENT_OBJECT_LOCATIONCHANGE debounced 100 ms,
// display changes), never per frame, and off the UI thread (DodgeWatch), because a title-bar query or a UIA read of a slow
// window may take up to 150 ms. The Win32 and UIA reads sit behind interfaces; tests use fakes.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Port.PcControl.Pill;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

/// <summary>The windows and buttons the dodge reads. Real implementations never run in tests.</summary>
public interface ICaptionButtonSource
{
    /// <summary>The topmost top-level, visible, uncloaked, not minimized window over <paramref name="area"/> (screen pixels), skipping <paramref name="ignore"/>.</summary>
    nint TopWindowOver(PcRect area, IReadOnlySet<nint> ignore);

    /// <summary>The window's caption buttons in screen pixels (empty when it shows none or they cannot be read).</summary>
    IReadOnlyList<PcRect> Buttons(nint window);

    /// <summary>The bottom edge (screen pixels) of an auto-hide taskbar at the top of the monitor that is showing now; null otherwise.</summary>
    int? TopTaskbarBottom(PcRect monitor);
}

/// <summary>The dodge's inputs for one pill on one monitor, in that monitor's DIP.</summary>
public sealed record DodgeInputs(IReadOnlyList<PillRect> CaptionButtons, double? TopTaskbarBottom, nint TopWindow)
{
    public static DodgeInputs None { get; } = new([], null, 0);
}

public sealed class CaptionButtons(ICaptionButtonSource source)
{
    /// <summary>Tactus's own overlay windows: never a reason to dodge.</summary>
    public HashSet<nint> Ignore { get; } = [];

    /// <summary>Reads what lies under the pill resting at <paramref name="pill"/> (its anchor rect, monitor DIP) plus the 8 DIP margin.</summary>
    public DodgeInputs Read(OverlayMonitor monitor, PillRect pill)
    {
        var area = monitor.ToScreen(pill.Inflate(PillDodgePlanner.Margin));
        var screen = new PcRect(area.X, area.Y, area.Width, area.Height);
        var top = source.TopWindowOver(screen, Ignore);
        var buttons = top == 0 ? [] : source.Buttons(top).Where(b => !b.IsEmpty).Select(monitor.ToDip).ToList();
        double? taskbar = source.TopTaskbarBottom(monitor.Bounds) is { } bottom ? (bottom - monitor.Bounds.Y) / monitor.Scale : null;
        return new DodgeInputs(buttons, taskbar, top);
    }

    /// <summary>The dodge the planner gives for these inputs (pure; the guards are the planner's Update).</summary>
    public static PillDodge Plan(PillRect pill, PillAnchor anchor, OverlayMonitor monitor, DodgeInputs inputs) =>
        PillDodgePlanner.Plan(pill, anchor, monitor.WorkDip, inputs.CaptionButtons, inputs.TopTaskbarBottom);
}

/// <summary>
/// The real reads: a Z-order walk (GetWindow from the top), WM_GETTITLEBARINFOEX through SendMessageTimeout (100 ms, abort
/// if hung), DWMWA_CAPTION_BUTTON_BOUNDS, a UIA fallback for custom title bars, and the shell's taskbar state. Never run by tests.
/// </summary>
public sealed class Win32CaptionButtonSource(IUiaReader? uia = null) : ICaptionButtonSource
{
    private const uint GwHwndNext = 2;
    private const int GwlStyle = -16;
    private const uint WsVisible = 0x10000000;
    private const uint WsMinimize = 0x20000000;
    private const uint WmGetTitleBarInfoEx = 0x033F;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint StateInvisible = 0x00008000;
    private const uint StateOffscreen = 0x00010000;
    private const uint StateUnavailable = 0x00000001;
    private const int DwmCloaked = 14;
    private const int DwmCaptionButtonBounds = 5;
    private const int AbmGetState = 4;
    private const int AbmGetTaskbarPos = 5;
    private const int AbsAutoHide = 1;
    private const int AbeTop = 1;
    private static readonly string[] UiaNames = ["Minimize", "Maximize", "Restore", "Close"];

    public nint TopWindowOver(PcRect area, IReadOnlySet<nint> ignore)
    {
        for (var window = GetTopWindow(0); window != 0; window = GetWindow(window, GwHwndNext))
        {
            if (ignore.Contains(window)) continue;
            var style = (uint)GetWindowLongPtr(window, GwlStyle);
            if ((style & WsVisible) == 0 || (style & WsMinimize) != 0) continue;
            if (DwmGetWindowAttribute(window, DwmCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            if (!GetWindowRect(window, out var r)) continue;
            if (r.Right <= area.X || r.Left >= area.Right || r.Bottom <= area.Y || r.Top >= area.Bottom) continue;
            return window;
        }
        return 0;
    }

    public IReadOnlyList<PcRect> Buttons(nint window)
    {
        var info = new TitleBarInfoEx { Size = Marshal.SizeOf<TitleBarInfoEx>() };
        var list = new List<PcRect>();
        if (SendMessageTimeout(window, WmGetTitleBarInfoEx, 0, ref info, SmtoAbortIfHung, 100, out _) != 0)
        {
            // rgrect: 0 title bar, 1 reserved, 2 minimize, 3 maximize, 4 help, 5 close.
            foreach (var i in new[] { 2, 3, 5 })
            {
                var state = info.State(i);
                if ((state & (StateInvisible | StateOffscreen | StateUnavailable)) != 0) continue;
                var r = info.Rect(i);
                if (r.Right > r.Left && r.Bottom > r.Top) list.Add(new PcRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top));
            }
        }
        if (list.Count > 0) return list;
        if (DwmGetWindowAttribute(window, DwmCaptionButtonBounds, out NativeRect bounds, Marshal.SizeOf<NativeRect>()) == 0
            && bounds.Right > bounds.Left && GetWindowRect(window, out var frame))
            return [new PcRect(frame.Left + bounds.Left, frame.Top + bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top)];
        return FromUia(window);
    }

    public int? TopTaskbarBottom(PcRect monitor)
    {
        var data = new AppBarData { Size = Marshal.SizeOf<AppBarData>() };
        var state = (int)SHAppBarMessage(AbmGetState, ref data);
        if ((state & AbsAutoHide) == 0) return null;
        if (SHAppBarMessage(AbmGetTaskbarPos, ref data) == 0 || data.Edge != AbeTop) return null;
        var bar = data.Rect;
        if (bar.Right <= monitor.X || bar.Left >= monitor.Right) return null;
        // Hidden, an auto-hide bar leaves a 2 px sliver on the screen; showing, its bottom is well inside the monitor.
        return bar.Bottom - monitor.Y > 4 ? bar.Bottom : null;
    }

    private IReadOnlyList<PcRect> FromUia(nint window)
    {
        if (uia is null || !GetWindowRect(window, out var frame)) return [];
        try
        {
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
            var read = uia.Tree(window, cancel.Token);
            if (!read.Wait(TimeSpan.FromMilliseconds(150)) || read.Result is not { } tree) return [];
            var found = new List<PcRect>();
            Walk(tree, frame.Top, found, 0);
            return found;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static void Walk(Marrow.Core.Port.PcControl.PcUiNode node, int top, List<PcRect> found, int depth)
    {
        if (depth > 8 || found.Count >= 3) return;
        if (string.Equals(node.Role, "button", StringComparison.OrdinalIgnoreCase) && node.Bounds.Y - top < 64
            && UiaNames.Any(n => string.Equals(n, node.Name, StringComparison.OrdinalIgnoreCase) || string.Equals(n, node.AutomationId, StringComparison.OrdinalIgnoreCase)))
            found.Add(node.Bounds);
        foreach (var child in node.Children) Walk(child, top, found, depth + 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TitleBarInfoEx
    {
        public int Size;
        public NativeRect TitleBar;
        public uint S0, S1, S2, S3, S4, S5;
        public NativeRect R0, R1, R2, R3, R4, R5;

        public readonly uint State(int i) => i switch { 0 => S0, 1 => S1, 2 => S2, 3 => S3, 4 => S4, _ => S5 };

        public readonly NativeRect Rect(int i) => i switch { 0 => R0, 1 => R1, 2 => R2, 3 => R3, 4 => R4, _ => R5 };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public int Size;
        public nint Window;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public nint Param;
    }

    [DllImport("user32.dll")]
    private static extern nint GetTopWindow(nint parent);

    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    private static extern nint SendMessageTimeout(nint window, uint message, nint wParam, ref TitleBarInfoEx lParam, uint flags, uint timeout, out nint result);

    [DllImport("shell32.dll")]
    private static extern nuint SHAppBarMessage(int message, ref AppBarData data);
}
