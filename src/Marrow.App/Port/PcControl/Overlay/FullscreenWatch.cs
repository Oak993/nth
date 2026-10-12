// PORT-ONLY: Tactus pill v2, full-screen apps (OD-0145, ED-NEW-pcctl-23; DESIGN.md 10.3 "Auto-dodge" rows 4 and 5, 4.5
// critique C19). Two kinds: a borderless full-screen window (the foreground window, its rect equal to its monitor, no
// caption), where the pill moves to another monitor or shrinks to its 32 DIP dot; and exclusive full screen or
// presentation mode (SHQueryUserNotificationState 3 on the foreground window's monitor, 4 on every monitor), where nothing
// of Tactus is drawn. Read at most every 500 ms, on window events, never per frame.
using System.Runtime.InteropServices;
using Marrow.Core.Port.PcControl;

namespace Marrow.App.Port.PcControl.Overlay;

public enum MonitorShade
{
    /// <summary>Nothing in the way.</summary>
    Clear,

    /// <summary>A borderless full-screen window in front: another monitor, else the dot.</summary>
    BorderlessFullscreen,

    /// <summary>Exclusive full screen or presentation mode: nothing of Tactus is drawn here.</summary>
    NothingDrawn,
}

/// <summary>What the watch reads. The real one never runs in tests.</summary>
public interface IFullscreenSource
{
    /// <summary>SHQueryUserNotificationState (0 when it fails).</summary>
    int NotificationState();

    nint Foreground();

    /// <summary>The window's rect in screen pixels; null when it is gone.</summary>
    PcRect? WindowRect(nint window);

    /// <summary>True when the window has a caption (WS_CAPTION).</summary>
    bool HasCaption(nint window);
}

public sealed class FullscreenWatch(IFullscreenSource source, Func<double> seconds)
{
    public const double FreshSeconds = 0.5;
    public const int RunningD3DFullScreen = 3;
    public const int PresentationMode = 4;

    private double readAt = double.NegativeInfinity;
    private (int State, PcRect? Rect, bool Caption) last;

    /// <summary>Tactus's own overlay windows, never counted as the full-screen window.</summary>
    public HashSet<nint> Ignore { get; } = [];

    /// <summary>The shade on <paramref name="monitor"/> now.</summary>
    public MonitorShade State(OverlayMonitor monitor)
    {
        var now = seconds();
        if (now - readAt >= FreshSeconds)
        {
            readAt = now;
            var front = source.Foreground();
            var counts = front != 0 && !Ignore.Contains(front);
            last = (source.NotificationState(), counts ? source.WindowRect(front) : null, counts && source.HasCaption(front));
        }
        return Classify(last.State, last.Rect, last.Caption, monitor);
    }

    /// <summary>Forgets the last reading (a display change or a foreground change), so the next State reads again.</summary>
    public void Invalidate() => readAt = double.NegativeInfinity;

    /// <summary>The shade for one monitor from one reading. Pure.</summary>
    public static MonitorShade Classify(int notificationState, PcRect? foregroundRect, bool foregroundCaption, OverlayMonitor monitor)
    {
        if (notificationState == PresentationMode) return MonitorShade.NothingDrawn;
        var onThis = foregroundRect is { } rect && monitor.Contains(new PcPoint(rect.X + (rect.Width / 2), rect.Y + (rect.Height / 2)));
        if (notificationState == RunningD3DFullScreen && onThis) return MonitorShade.NothingDrawn;
        if (onThis && foregroundRect == monitor.Bounds && !foregroundCaption) return MonitorShade.BorderlessFullscreen;
        return MonitorShade.Clear;
    }
}

/// <summary>The real reads. Never run by tests.</summary>
public sealed class Win32FullscreenSource : IFullscreenSource
{
    private const int GwlStyle = -16;
    private const uint WsCaption = 0x00C00000;

    public int NotificationState() => SHQueryUserNotificationState(out var state) == 0 ? state : 0;

    public nint Foreground() => GetForegroundWindow();

    public PcRect? WindowRect(nint window) =>
        GetWindowRect(window, out var r) ? new PcRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : null;

    public bool HasCaption(nint window) => ((uint)GetWindowLongPtr(window, GwlStyle) & WsCaption) == WsCaption;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
}
