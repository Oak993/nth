using Avalonia;
using Avalonia.Platform;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Port;

// PORT-ONLY: ED-225 binding of NSWindow.center() at WindowManager.swift:43, :68, :82 (B3 52bc792).
// The owner monitor, otherwise the pointer monitor; working area; one third of the free height above.
// UNVERIFIED OD-0001: Apple's exact vertical bias is not specified by the pinned source.
internal static class WindowCentering
{
    internal static Ambient<ICursorPlatform> Cursor { get; } = new(() => new WindowsCursorPlatform());
    internal static Ambient<Func<Avalonia.Controls.WindowBase, (PixelRect WorkingArea, double Scaling)?>> OwnerScreen { get; } =
        new(() => owner => Monitor(owner.Screens.ScreenFromWindow(owner)));
    internal static Ambient<Func<AvaloniaWindow, PixelPoint, (PixelRect WorkingArea, double Scaling)?>> PointerScreen { get; } =
        new(() => (window, point) => Monitor(window.Screens.ScreenFromPoint(point)));

    private static (PixelRect WorkingArea, double Scaling)? Monitor(Screen? screen) =>
        screen is null ? null : (screen.WorkingArea, screen.Scaling);

    // ED-225: select the monitor independently of the unshown window's initial platform position.
    // The frame is the VISIBLE window in DIP (the content rect of the factory's chrome: WindowInvisibleBorder); the selected
    // monitor's working area and the final position are in pixels. The HWND's invisible strip on the left, the right and the
    // bottom (the selected monitor's DPI, so a window about to move between monitors is placed for where it lands) lies
    // outside the visible frame: the visible window is centred and kept inside the working area, and the HWND is placed so.
    internal static void Center(AvaloniaWindow window, Size frame)
    {
        var monitor = window.Owner is { } owner
            ? OwnerScreen.Current(owner)
            : Cursor.Current.ScreenPosition is { } point ? PointerScreen.Current(window, point) : null;
        monitor ??= Monitor(window.Screens.Primary); // unavailable pointer/display: platform main screen
        if (monitor is not { } selected) return; // no display exists to place a window on
        var work = selected.WorkingArea;
        var border = WindowInvisibleBorder.Pixels.Current(window, selected.Scaling);
        var width = frame.Width * selected.Scaling;
        var height = frame.Height * selected.Scaling;
        var x = work.X + (work.Width - width) / 2 - border; // the HWND's left edge: the visible window's, less the strip
        var y = work.Y + (work.Height - height) / 3; // no strip above
        // Clamp without changing the fixed source frame. If it exceeds the work area, keeping every edge inside
        // is impossible; its leading edge stays on screen and its size is never shrunk.
        x = Math.Max(work.X - border, Math.Min(x, work.Right - width - border));
        y = Math.Max(work.Y, Math.Min(y, work.Bottom - height));
        var inset = window.Content is SettingsShell shell ? shell.Inset * selected.Scaling : 0;
        window.Position = new PixelPoint(checked((int)SwiftNumber.Rounded(x - inset)), checked((int)SwiftNumber.Rounded(y - inset)));
    }
}
