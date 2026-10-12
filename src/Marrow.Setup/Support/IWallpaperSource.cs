namespace Marrow.Setup.Support;

/// <summary>How Windows lays the wallpaper on a monitor (<c>DESKTOP_WALLPAPER_POSITION</c>, the same numbers).</summary>
public enum WallpaperPosition
{
    /// <summary><c>DWPOS_CENTER</c>: drawn at scale on the background colour.</summary>
    Center = 0,

    /// <summary><c>DWPOS_TILE</c>: repeated at scale.</summary>
    Tile = 1,

    /// <summary><c>DWPOS_STRETCH</c>: scaled to the monitor, ignoring the aspect ratio.</summary>
    Stretch = 2,

    /// <summary><c>DWPOS_FIT</c>: letterboxed on the background colour.</summary>
    Fit = 3,

    /// <summary><c>DWPOS_FILL</c>: cropped to cover.</summary>
    Fill = 4,

    /// <summary><c>DWPOS_SPAN</c>: one image across the whole virtual desktop; each monitor shows its part.</summary>
    Span = 5,
}

/// <summary>A rectangle in virtual-desktop pixels (<c>GetMonitorRECT</c>'s <c>RECT</c>).</summary>
public readonly record struct WallpaperRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>A monitor as <c>IDesktopWallpaper</c> names it: <c>GetMonitorDevicePathAt</c> and <c>GetMonitorRECT</c>.</summary>
public sealed record WallpaperMonitor(string DevicePath, WallpaperRect Bounds);

/// <summary>The desktop background colour (<c>GetBackgroundColor</c>'s <c>COLORREF</c>, 0x00BBGGRR).</summary>
public readonly record struct WallpaperColor(byte R, byte G, byte B)
{
    public static WallpaperColor FromColorRef(uint colorRef) =>
        new(unchecked((byte)colorRef), unchecked((byte)(colorRef >> 8)), unchecked((byte)(colorRef >> 16)));
}

/// <summary>What the chain needs to know about an image file before it decides to decode it.</summary>
/// <param name="Length">The file's size in bytes.</param>
/// <param name="IsNetwork">A UNC path or one <c>PathIsNetworkPathW</c> calls a network path: skipped.</param>
public sealed record WallpaperFile(long Length, bool IsNetwork);

/// <summary>
/// A decoded wallpaper: <paramref name="Width"/> × <paramref name="Height"/> pixels, rows top to bottom, four bytes per
/// pixel in B, G, R, A order (premultiplied), stride <c>Width * 4</c>. Neutral of the UI toolkit, so the chain, the
/// position maths and the label luminance are tested without it.
/// </summary>
public sealed record WallpaperImage(int Width, int Height, byte[] Bgra);

/// <summary>The fixed limits of the wallpaper read (PLAN.md 8a).</summary>
public static class WallpaperLimits
{
    /// <summary>A larger file is skipped and the next source in the chain is used.</summary>
    public const long MaxFileBytes = 64L * 1024 * 1024;

    /// <summary>A decode still running after this is cancelled and the next source is used.</summary>
    public static readonly TimeSpan DecodeBudget = TimeSpan.FromMilliseconds(400);

    /// <summary>Decoded at this many times the preview's pixel width.</summary>
    public const int DecodeWidthFactor = 2;

    /// <summary>Above this luminance under a label, labels are ink with a pale halo; otherwise white with a dark shadow.</summary>
    public const double InkLabelLuminance = 0.6;
}

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, unit U0, ED-NEW-inst-u0-1). Everything the desktop preview reads about the user's
/// wallpaper, so the chain in <c>Support/WallpaperPreview.cs</c> (unit U2: <c>GetWallpaper</c> for the window's monitor,
/// then Windows' transcoded copy, then the background colour, then the theme gradient; the position modes; the luminance
/// under the labels) is tested on a fake without Windows. The production source wraps <c>IDesktopWallpaper</c>
/// (<c>[GeneratedComInterface]</c> in <c>Port/DesktopWallpaper.cs</c>, AOT-safe) and is read on a background thread after
/// the first frame. Nothing is copied or sent anywhere. A member that fails throws; the chain treats that as "this source
/// has nothing" and moves on.
/// </summary>
public interface IWallpaperSource
{
    /// <summary>Every monitor, in <c>GetMonitorDevicePathAt</c> order, with its rectangle.</summary>
    IReadOnlyList<WallpaperMonitor> Monitors();

    /// <summary><c>GetWallpaper</c> for one monitor; null when Windows gives an empty path (Windows Spotlight, some slideshows).</summary>
    string? WallpaperPath(string monitorDevicePath);

    /// <summary><c>GetPosition</c>.</summary>
    WallpaperPosition Position();

    /// <summary><c>GetBackgroundColor</c>; null when it cannot be read.</summary>
    WallpaperColor? BackgroundColor();

    /// <summary>
    /// The transcoded copy Windows keeps of the current wallpaper (<c>%APPDATA%\Microsoft\Windows\Themes\TranscodedWallpaper</c>),
    /// or null when there is none.
    /// </summary>
    string? TranscodedWallpaperPath();

    /// <summary>The file's size and whether it is on the network, or null when it does not exist.</summary>
    WallpaperFile? Inspect(string path);

    /// <summary>
    /// Decodes the image scaled to <paramref name="width"/> pixels wide (aspect kept), or null when it is not an image it
    /// can read. Honours <paramref name="cancellationToken"/>: the chain cancels it at <see cref="WallpaperLimits.DecodeBudget"/>.
    /// </summary>
    Task<WallpaperImage?> DecodeAsync(string path, int width, CancellationToken cancellationToken);
}
