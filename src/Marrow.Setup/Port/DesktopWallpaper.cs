using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Marrow.Setup.Support;
using SkiaSharp;

namespace Marrow.Setup.Port;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a, "The wallpaper in the preview"; unit U2, ED-NEW-inst-u2-1). Windows'
/// <c>IDesktopWallpaper</c> (shobjidl_core.h), declared with <c>[GeneratedComInterface]</c> because the setup is AOT and
/// built-in COM interop is not AOT-safe. Only the reading methods are called; the setters keep their v-table places and
/// are never used (nothing here changes the desktop).
/// </summary>
[GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
[Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B")]
internal partial interface IDesktopWallpaper
{
    [PreserveSig]
    int SetWallpaper(string? monitorId, string wallpaper);

    [PreserveSig]
    int GetWallpaper(string? monitorId, out string? wallpaper);

    [PreserveSig]
    int GetMonitorDevicePathAt(uint monitorIndex, out string? monitorId);

    [PreserveSig]
    int GetMonitorDevicePathCount(out uint count);

    [PreserveSig]
    int GetMonitorRECT(string monitorId, out NativeRect displayRect);

    [PreserveSig]
    int SetBackgroundColor(uint color);

    [PreserveSig]
    int GetBackgroundColor(out uint color);

    [PreserveSig]
    int SetPosition(int position);

    [PreserveSig]
    int GetPosition(out int position);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>
/// The real <see cref="IWallpaperSource"/>: a snapshot of <c>IDesktopWallpaper</c> read once on its own STA thread (the
/// monitors, each one's wallpaper, the position mode and the background colour), the transcoded copy under
/// <c>%APPDATA%\Microsoft\Windows\Themes</c>, and images decoded with <c>Bitmap.DecodeToWidth</c>. Read only.
/// </summary>
public sealed partial class WindowsWallpaperSource : IWallpaperSource
{
    private static readonly Guid DesktopWallpaperClass = new("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD");
    private static readonly Guid DesktopWallpaperInterface = new("B92B56A9-8B55-4E14-9A89-0199BBB6F93B");
    private readonly IReadOnlyList<WallpaperMonitor> monitors;
    private readonly Dictionary<string, string?> wallpapers;
    private readonly WallpaperPosition position;
    private readonly WallpaperColor? background;

    private WindowsWallpaperSource(IReadOnlyList<WallpaperMonitor> monitors, Dictionary<string, string?> wallpapers, WallpaperPosition position, WallpaperColor? background)
    {
        this.monitors = monitors;
        this.wallpapers = wallpapers;
        this.position = position;
        this.background = background;
    }

    /// <summary>True when the snapshot read anything from <c>IDesktopWallpaper</c>.</summary>
    public bool HasMonitors => monitors.Count > 0;

    /// <summary>Reads the snapshot on a fresh STA thread (COM's shell objects want one), off the UI thread.</summary>
    public static Task<WindowsWallpaperSource> ReadAsync()
    {
        var done = new TaskCompletionSource<WindowsWallpaperSource>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { done.SetResult(ReadNow()); }
            catch (Exception error) { done.SetException(error); }
        })
        { IsBackground = true, Name = "nth wallpaper read" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    private static WindowsWallpaperSource ReadNow()
    {
        var found = new List<WallpaperMonitor>();
        var paths = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var mode = WallpaperPosition.Fill;
        WallpaperColor? colour = null;
        var clsid = DesktopWallpaperClass;
        var iid = DesktopWallpaperInterface;
        if (CoCreateInstance(in clsid, 0, 0x17, in iid, out var pointer) < 0 || pointer == 0) return new WindowsWallpaperSource(found, paths, mode, colour);
        try
        {
            var wrappers = new StrategyBasedComWrappers();
            var wallpaper = (IDesktopWallpaper)wrappers.GetOrCreateObjectForComInstance(pointer, CreateObjectFlags.None);
            if (wallpaper.GetMonitorDevicePathCount(out var count) >= 0)
            {
                for (uint i = 0; i < count && i < 16; i++)
                {
                    if (wallpaper.GetMonitorDevicePathAt(i, out var id) < 0 || string.IsNullOrEmpty(id)) continue;
                    if (wallpaper.GetMonitorRECT(id, out var rect) < 0) continue;
                    found.Add(new WallpaperMonitor(id, new WallpaperRect(rect.Left, rect.Top, rect.Right, rect.Bottom)));
                    paths[id] = wallpaper.GetWallpaper(id, out var path) >= 0 ? path : null;
                }
            }
            if (wallpaper.GetPosition(out var value) >= 0 && Enum.IsDefined((WallpaperPosition)value)) mode = (WallpaperPosition)value;
            if (wallpaper.GetBackgroundColor(out var colorRef) >= 0) colour = WallpaperColor.FromColorRef(colorRef);
        }
        catch (Exception error) when (error is COMException or InvalidCastException)
        {
            // a desktop without the shell's wallpaper object (Server Core, a locked-down session): the theme gradient shows
        }
        finally
        {
            Marshal.Release(pointer);
        }
        return new WindowsWallpaperSource(found, paths, mode, colour);
    }

    public IReadOnlyList<WallpaperMonitor> Monitors() => monitors;

    public string? WallpaperPath(string monitorDevicePath) => wallpapers.GetValueOrDefault(monitorDevicePath);

    public WallpaperPosition Position() => position;

    public WallpaperColor? BackgroundColor() => background;

    public string? TranscodedWallpaperPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Themes", "TranscodedWallpaper");
        return File.Exists(path) ? path : null;
    }

    public WallpaperFile? Inspect(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var network = path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith(@"\\?\", StringComparison.Ordinal) || PathIsNetworkPathW(path);
        if (network) return new WallpaperFile(0, true);
        var file = new FileInfo(path);
        return file.Exists ? new WallpaperFile(file.Length, false) : null;
    }

    public Task<WallpaperImage?> DecodeAsync(string path, int width, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = File.OpenRead(path);
        int sourceWidth, sourceHeight;
        using (var codec = SKCodec.Create(stream))
        {
            if (codec is null) return null;
            sourceWidth = codec.Info.Width;
            sourceHeight = codec.Info.Height;
        }
        cancellationToken.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var bitmap = Bitmap.DecodeToWidth(stream, Math.Max(1, Math.Min(width, sourceWidth)), BitmapInterpolationMode.MediumQuality);
        cancellationToken.ThrowIfCancellationRequested();
        var size = bitmap.PixelSize;
        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, stride); }
        finally { handle.Free(); }
        if (bitmap.Format == PixelFormats.Rgba8888)
            for (var i = 0; i < pixels.Length; i += 4) (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
        if (bitmap.AlphaFormat == AlphaFormat.Unpremul)
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a == 255) continue;
                pixels[i] = (byte)(pixels[i] * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }
        return new WallpaperImage(size.Width, size.Height, pixels) { SourceWidth = sourceWidth, SourceHeight = sourceHeight };
    }, cancellationToken);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);

    [LibraryImport("shlwapi.dll", EntryPoint = "PathIsNetworkPathW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PathIsNetworkPathW(string path);
}
