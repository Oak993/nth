using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

/// <summary>
/// PORT-ONLY (nth installer PLAN.md 8a and 10, unit U0, ED-NEW-inst-u0-1). An in-memory <see cref="IWallpaperSource"/>
/// for the wallpaper chain's tests (unit U2): monitors, each monitor's wallpaper path (null for Windows Spotlight), the
/// position mode, the background colour, Windows' transcoded copy, and files with a size, a network flag and the image
/// they decode to. A decode can be made slow (through the given <see cref="TimeProvider"/>, so a 400 ms budget is tested
/// in no real time) or made to throw; any member can be made to fail. It records which files were inspected and decoded
/// and which decodes were cancelled. It reads nothing from the real machine.
/// </summary>
public sealed class FakeWallpaperSource : IWallpaperSource
{
    /// <summary>The members, for <see cref="Fail"/> and <see cref="CallCount"/>.</summary>
    public enum Member
    {
        Monitors,
        WallpaperPath,
        Position,
        BackgroundColor,
        TranscodedWallpaperPath,
        Inspect,
        Decode,
    }

    public const string PrimaryMonitor = @"\\?\DISPLAY#NTH0001#1";

    public const string SecondMonitor = @"\\?\DISPLAY#NTH0002#2";

    /// <summary>Windows' transcoded copy on the fake machine (the same long, non-ASCII profile as FakeShellLinks).</summary>
    public const string TranscodedPath = @"C:\Users\Ana-María Østergård-Fernández\AppData\Roaming\Microsoft\Windows\Themes\TranscodedWallpaper";

    private readonly Lock gate = new();
    private readonly TimeProvider time;
    private readonly Dictionary<string, string?> wallpapers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FakeFile> files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Member> failing = [];
    private readonly Dictionary<Member, int> calls = [];
    private readonly List<string> inspected = [];
    private readonly List<(string Path, int Width)> decoded = [];
    private readonly List<string> cancelled = [];
    private List<WallpaperMonitor> monitors = [new(PrimaryMonitor, new WallpaperRect(0, 0, 2560, 1600))];
    private WallpaperPosition position = WallpaperPosition.Fill;
    private WallpaperColor? background = new WallpaperColor(0, 0, 0);
    private string? transcoded;

    /// <param name="time">The clock slow decodes wait on; a ManualTimeProvider makes them wait for the test.</param>
    public FakeWallpaperSource(TimeProvider? time = null) => this.time = time ?? TimeProvider.System;

    // ---- setting the machine up ----

    public void SetMonitors(params WallpaperMonitor[] all)
    {
        lock (gate) monitors = [.. all];
    }

    /// <summary>The path <c>GetWallpaper</c> gives for a monitor; null is Windows Spotlight's empty path.</summary>
    public void SetWallpaper(string monitorDevicePath, string? path)
    {
        lock (gate) wallpapers[monitorDevicePath] = path;
    }

    public void SetPosition(WallpaperPosition value)
    {
        lock (gate) position = value;
    }

    public void SetBackground(WallpaperColor? value)
    {
        lock (gate) background = value;
    }

    /// <summary>Makes Windows' transcoded copy exist (at <see cref="TranscodedPath"/> unless another path is given), decoding to <paramref name="image"/>.</summary>
    public void SetTranscoded(WallpaperImage image, string path = TranscodedPath)
    {
        AddImage(path, image);
        lock (gate) transcoded = path;
    }

    /// <summary>An image file: its size is the pixel bytes unless <paramref name="length"/> says otherwise.</summary>
    public void AddImage(string path, WallpaperImage image, long? length = null, bool network = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        lock (gate) files[path] = new FakeFile(length ?? image.Bgra.LongLength, network, image);
    }

    /// <summary>A file that exists but is not an image the decoder can read (it decodes to null).</summary>
    public void AddFile(string path, long length, bool network = false)
    {
        lock (gate) files[path] = new FakeFile(length, network, null);
    }

    /// <summary>Makes decoding this file take <paramref name="delay"/> on the fake's clock (it stops early when cancelled).</summary>
    public void SlowDecode(string path, TimeSpan delay)
    {
        lock (gate) files[path] = files[path] with { DecodeDelay = delay };
    }

    /// <summary>Makes decoding this file throw (a corrupt image).</summary>
    public void BreakDecode(string path)
    {
        lock (gate) files[path] = files[path] with { DecodeThrows = true };
    }

    /// <summary>Makes every call of <paramref name="member"/> throw.</summary>
    public void Fail(Member member)
    {
        lock (gate) failing.Add(member);
    }

    /// <summary>A plain image of one colour, opaque, for luminance tests.</summary>
    public static WallpaperImage Solid(int width, int height, byte r, byte g, byte b)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }
        return new WallpaperImage(width, height, pixels);
    }

    // ---- what the test reads back ----

    public int CallCount(Member member)
    {
        lock (gate) return calls.GetValueOrDefault(member);
    }

    public IReadOnlyList<string> Inspected { get { lock (gate) return [.. inspected]; } }

    /// <summary>Every decode started, with the width it asked for.</summary>
    public IReadOnlyList<(string Path, int Width)> Decoded { get { lock (gate) return [.. decoded]; } }

    /// <summary>Decodes that were cancelled before they finished.</summary>
    public IReadOnlyList<string> Cancelled { get { lock (gate) return [.. cancelled]; } }

    // ---- IWallpaperSource ----

    public IReadOnlyList<WallpaperMonitor> Monitors()
    {
        Enter(Member.Monitors);
        lock (gate) return [.. monitors];
    }

    public string? WallpaperPath(string monitorDevicePath)
    {
        Enter(Member.WallpaperPath);
        lock (gate) return wallpapers.GetValueOrDefault(monitorDevicePath);
    }

    public WallpaperPosition Position()
    {
        Enter(Member.Position);
        lock (gate) return position;
    }

    public WallpaperColor? BackgroundColor()
    {
        Enter(Member.BackgroundColor);
        lock (gate) return background;
    }

    public string? TranscodedWallpaperPath()
    {
        Enter(Member.TranscodedWallpaperPath);
        lock (gate) return transcoded;
    }

    public WallpaperFile? Inspect(string path)
    {
        Enter(Member.Inspect);
        lock (gate)
        {
            inspected.Add(path);
            return files.TryGetValue(path, out var file) ? new WallpaperFile(file.Length, file.IsNetwork) : null;
        }
    }

    public async Task<WallpaperImage?> DecodeAsync(string path, int width, CancellationToken cancellationToken)
    {
        Enter(Member.Decode);
        FakeFile? file;
        lock (gate)
        {
            decoded.Add((path, width));
            file = files.GetValueOrDefault(path);
        }
        if (file is null) throw new FileNotFoundException("No wallpaper file.", path);
        try
        {
            if (file.DecodeDelay > TimeSpan.Zero) await Task.Delay(file.DecodeDelay, time, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            lock (gate) cancelled.Add(path);
            throw;
        }
        if (file.DecodeThrows) throw new InvalidDataException("A corrupt image: " + path);
        return file.Image is null ? null : Scale(file.Image, width);
    }

    private void Enter(Member member)
    {
        lock (gate)
        {
            calls[member] = calls.GetValueOrDefault(member) + 1;
            if (failing.Contains(member)) throw new InvalidOperationException($"FakeWallpaperSource.{member} failed.");
        }
    }

    // Nearest-neighbour scaling to the asked width, aspect kept: enough for position and luminance tests.
    private static WallpaperImage Scale(WallpaperImage image, int width)
    {
        if (width <= 0 || width == image.Width) return image;
        var height = Math.Max(1, (int)Math.Round((double)image.Height * width / image.Width));
        var pixels = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var from = ((y * image.Height / height * image.Width) + (x * image.Width / width)) * 4;
                Array.Copy(image.Bgra, from, pixels, ((y * width) + x) * 4, 4);
            }
        return new WallpaperImage(width, height, pixels) { SourceWidth = image.SourceWidth, SourceHeight = image.SourceHeight };
    }

    private sealed record FakeFile(long Length, bool IsNetwork, WallpaperImage? Image)
    {
        public TimeSpan DecodeDelay { get; init; }

        public bool DecodeThrows { get; init; }
    }
}
