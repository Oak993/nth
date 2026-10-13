using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.Setup.Port;
using Marrow.Setup.Support;
using Marrow.Setup.Ui;

namespace Marrow.Setup.Screens;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a; unit U2, ED-NEW-inst-u2-1). Reads the user's wallpaper for the desktop previews
/// on a background thread after the window's first frame (the theme gradient shows until it arrives, or if it fails), for
/// the monitor the window is on, at the large preview's pixel width.
/// </summary>
public static class WallpaperLoader
{
    /// <summary>The window's centre in physical pixels (which monitor it is on).</summary>
    public static (int X, int Y) Centre(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var scaling = window.RenderScaling;
        var position = window.Position;
        return (position.X + (int)(window.ClientSize.Width * scaling / 2), position.Y + (int)(window.ClientSize.Height * scaling / 2));
    }

    /// <summary>Reads the chain with Windows' own source (call on the UI thread; the reading runs off it).</summary>
    public static Task<WallpaperScene> ReadAsync(Window window)
    {
        var centre = Centre(window);
        var width = (int)Math.Ceiling(DesktopPreview.LargeWidth * window.RenderScaling);
        return Task.Run(async () =>
        {
            var source = await WindowsWallpaperSource.ReadAsync().ConfigureAwait(false);
            return await WallpaperPreview.ReadAsync(source, centre, width).ConfigureAwait(false);
        });
    }

    /// <summary>Starts the read; the session's previews get it when it arrives.</summary>
    public static void Start(SetupSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var reading = ReadAsync(session.Window);
        _ = reading.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully) Dispatcher.UIThread.Post(() => session.SetWallpaper(t.Result));
            else _ = t.Exception;
        }, TaskScheduler.Default);
    }

    /// <summary>True for the errors a wallpaper read can meet (it then keeps the theme gradient).</summary>
    public static bool IsExpected(Exception error) => error is COMException or IOException or UnauthorizedAccessException or InvalidOperationException;
}
