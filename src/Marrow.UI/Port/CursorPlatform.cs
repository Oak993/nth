// PORT-ONLY: the platform read WindowChrome.swift:493 makes (window.mouseLocationOutsideOfEventStream), over Windows.
using System.Runtime.InteropServices;
using Avalonia;

namespace Marrow.UI.Port;

/// <summary>
/// The one AppKit read <c>SidebarResizeHandleView.refreshHover()</c> makes: the cursor's actual screen position,
/// asked fresh on every layout pass rather than trusted from the last enter/exit event (WindowChrome.swift:486-495,
/// PR-WIN-056). Seamed like <see cref="IGeniePlatform"/> seams NSWorkspace's reduce-motion read, so a test can
/// script the cursor position deterministically instead of moving the real OS pointer.
/// </summary>
public interface ICursorPlatform
{
    /// <summary>The cursor's current position in screen pixels, or null when it cannot be read.</summary>
    PixelPoint? ScreenPosition { get; }
}

/// <summary>The Windows 11 answer: <c>GetCursorPos</c>, read live at each call.</summary>
public sealed class WindowsCursorPlatform : ICursorPlatform
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public PixelPoint? ScreenPosition => GetCursorPos(out var point) ? new PixelPoint(point.X, point.Y) : null;
}
