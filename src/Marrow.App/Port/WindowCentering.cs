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
    // ED-NEW-settingsinapp-3: `above` is the share of the free height left above the window: a third by default (ED-225's NSWindow.center()),
    // a half for the first launch's work-area-sized main window, which the owner asked to be centred (FirstLaunchSize).
    // The monitor a window is placed on (ED-225): the owner's, otherwise the pointer's, otherwise the platform's main screen; null with no display.
    internal static (PixelRect WorkingArea, double Scaling)? SelectMonitor(AvaloniaWindow window)
    {
        var monitor = window.Owner is { } owner
            ? OwnerScreen.Current(owner)
            : Cursor.Current.ScreenPosition is { } point ? PointerScreen.Current(window, point) : null;
        return monitor ?? Monitor(window.Screens.Primary); // unavailable pointer/display: platform main screen
    }

    /// <summary>The share of the work area's width and height the first launch's main window takes (ED-NEW-settingsinapp-3).</summary>
    internal const double FirstLaunchWidthShare = 0.69, FirstLaunchHeightShare = 0.92;

    // PORT-ONLY. ENGINEERING DECISION ED-NEW-settingsinapp-3, by the owner's ruling of 2026-10-11 ("match Claude ... and maybe window size
    // too"): a main window with no saved frame opens at 69 % of its monitor's work area across and 92 % down (Claude's desktop window on the
    // owner's 2560 x 1440 screen, about 1760 x 1333), never below the window's minimum and never past the work area; with no display it keeps
    // the source's fixed size (WindowManager.swift:19). The size is the visible frame, in DIP.
    internal static Size FirstLaunchSize((PixelRect WorkingArea, double Scaling)? monitor, Size fallback, Size minimum)
    {
        if (monitor is not { } selected || selected.Scaling <= 0) return fallback;
        var work = selected.WorkingArea.Size.ToSize(selected.Scaling);
        if (work.Width <= 0 || work.Height <= 0) return fallback;
        static double Fit(double share, double least, double most) => Math.Min(Math.Max(share, least), Math.Max(least, most));
        return new Size(
            Math.Round(Fit(work.Width * FirstLaunchWidthShare, minimum.Width, work.Width)),
            Math.Round(Fit(work.Height * FirstLaunchHeightShare, minimum.Height, work.Height)));
    }

    internal static void Center(AvaloniaWindow window, Size frame, double above = 1.0 / 3, (PixelRect WorkingArea, double Scaling)? monitor = null)
    {
        if ((monitor ?? SelectMonitor(window)) is not { } selected) return; // no display exists to place a window on
        var work = selected.WorkingArea;
        var border = WindowInvisibleBorder.Pixels.Current(window, selected.Scaling);
        var width = frame.Width * selected.Scaling;
        var height = frame.Height * selected.Scaling;
        var x = work.X + (work.Width - width) / 2 - border; // the HWND's left edge: the visible window's, less the strip
        var y = work.Y + (work.Height - height) * above; // no strip above
        // Clamp without changing the fixed source frame. If it exceeds the work area, keeping every edge inside
        // is impossible; its leading edge stays on screen and its size is never shrunk.
        x = Math.Max(work.X - border, Math.Min(x, work.Right - width - border));
        y = Math.Max(work.Y, Math.Min(y, work.Bottom - height));
        window.Position = new PixelPoint(checked((int)SwiftNumber.Rounded(x)), checked((int)SwiftNumber.Rounded(y)));
    }
}
