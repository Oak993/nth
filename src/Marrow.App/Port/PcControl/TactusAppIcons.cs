// PORT-ONLY: Tactus, the apps' own icons (ED-NEW-pcctl-46; DESIGN.md 4.5, 4.7, 10.4, 10.7): the source PcAppIcons reads in
// the pill's rows, its approval card, the chat's strip and Settings. The shell draws an app's icon at 96 px
// (IShellItemImageFactory, icon only), enough for the largest place (34 DIP) at 275 % scale, once per app: an "exe:" key's
// exe, and an "aumid:" key's entry in the shell's Apps folder (shell:AppsFolder\<AUMID>), spelled as a window of that app
// spells it when one is open (the key is lower-cased). An icon that cannot be read is remembered as none (the lettered tile
// shows), except an app known by its id with no window open, which is asked again when it has one. On the UI thread, where
// the tiles are made.
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl;

/// <summary>
/// The icon source over the shell (<paramref name="where"/>: an app key to the shell's name for it and whether that name
/// is sure; <paramref name="load"/>: the icon of a shell name).
/// </summary>
public sealed class TactusAppIcons(Func<string, (string Name, bool Sure)?> where, Func<string, IImage?> load) : IPcAppIconSource
{
    /// <summary>The size the shell draws an icon at, in pixels.</summary>
    public const int Pixels = 96;

    /// <summary>The shell's folder of installed apps, where an app is found by its AUMID.</summary>
    public const string AppsFolder = "shell:AppsFolder\\";

    private readonly Dictionary<string, IImage?> icons = new(StringComparer.Ordinal);

    public IImage? Icon(string key)
    {
        if (icons.TryGetValue(key, out var known)) return known;
        if (where(key) is not { Name.Length: > 0 } place) return null;
        var icon = load(place.Name);
        if (icon is not null || place.Sure) icons[key] = icon;
        return icon;
    }

    /// <summary>
    /// The shell's name for an app key: an "exe:" key's path (sure); an "aumid:" key's Apps folder entry, as a window of that
    /// app among <paramref name="windows"/> spells its AUMID (sure), else as the key spells it (not sure); null for any other key.
    /// </summary>
    public static (string Name, bool Sure)? Where(string key, Func<IEnumerable<PcWindowInfo>> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        if (key.StartsWith("exe:", StringComparison.Ordinal)) return (key[4..], true);
        if (!key.StartsWith("aumid:", StringComparison.Ordinal) || key.Length == 6) return null;
        var open = windows().FirstOrDefault(w => w.Aumid is { Length: > 0 } && string.Equals(w.App.Key, key, StringComparison.Ordinal));
        return open is { Aumid: { } aumid } ? (AppsFolder + aumid, true) : (AppsFolder + key[6..], false);
    }

    /// <summary>The shell's icon for an exe path or an Apps folder entry at <see cref="Pixels"/> px; null when it cannot be read.</summary>
    public static IImage? Shell(string name) => ShellAppIcon.Load(name, Pixels);
}

// IShellItemImageFactory.GetImage with SIIGBF_ICONONLY: the item's icon drawn at the asked size, as a 32 bpp DIB section with
// premultiplied alpha, copied top-down into an Avalonia bitmap.
internal static class ShellAppIcon
{
    private const int SiigbfIconOnly = 0x4;
    private static readonly Guid ImageFactory = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static Bitmap? Load(string path, int pixels)
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (!path.StartsWith(TactusAppIcons.AppsFolder, StringComparison.OrdinalIgnoreCase) && !File.Exists(path)) return null;
        nint bitmap = 0;
        try
        {
            var riid = ImageFactory;
            if (SHCreateItemFromParsingName(path, 0, ref riid, out var factory) != 0 || factory is null) return null;
            try
            {
                if (factory.GetImage(new SIZE { cx = pixels, cy = pixels }, SiigbfIconOnly, out bitmap) != 0 || bitmap == 0) return null;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }
            return Copy(bitmap);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            if (bitmap != 0) DeleteObject(bitmap);
        }
    }

    private static Bitmap? Copy(nint bitmap)
    {
        var info = new BITMAP();
        if (GetObject(bitmap, Marshal.SizeOf<BITMAP>(), ref info) == 0 || info.bmWidth <= 0 || info.bmHeight <= 0) return null;
        var width = info.bmWidth;
        var height = info.bmHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        var header = new BITMAPINFOHEADER { biSize = Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = width, biHeight = -height, biPlanes = 1, biBitCount = 32 };
        var screen = GetDC(0);
        try
        {
            if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref header, 0) == 0) return null;
        }
        finally
        {
            ReleaseDC(0, screen);
        }
        var image = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = image.Lock();
        for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * stride, buffer.Address + (y * buffer.RowBytes), stride);
        return image;
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out nint bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public nint bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? factory);

    [DllImport("gdi32.dll")]
    private static extern int GetObject(nint handle, int size, ref BITMAP bitmap);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);
}
