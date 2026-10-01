using System.Reflection;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Marrow.UI.Port;
using Marrow.UI.Theme;

namespace Marrow.UI.Tests.Fixtures;

// PORT-ONLY test support for the WallpaperStore tests (Wallpaper.swift:15-270). The store is `static let shared`
// with a `private init()` (:85-95) that reads UserDefaults and the wallpaper directory once, so each test builds its
// own store with the private constructor while its own defaults and storage root are installed, exactly as the
// App.Tests wallpaper card scope does, and a "restart" is a second store built over the same defaults and folder.

/// <summary>One change of the store's rendered picture, with the animation transaction it was published under.</summary>
public sealed record PublishedImage(IImage? Image, Animation? Animation);

/// <summary>Defaults, storage root and the stores built over them, for one headless test.</summary>
public sealed class WallpaperStoreRig : IDisposable
{
    private readonly List<IDisposable> overrides = [];
    private readonly List<WallpaperStore> stores = [];

    public WallpaperStoreRig()
    {
        var data = TestDataDirectory.Read();
        Assert.False(string.IsNullOrEmpty(data), "MARROW_PORT_DATA_DIR must name a throwaway test-data directory.");
        Root = Path.Combine(data!, "WallpaperStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Sources = Path.Combine(Root, "sources");
        Directory.CreateDirectory(Sources);
        overrides.Add(UserDefaults.Standard.Override(Defaults));
        overrides.Add(StoragePaths.Locations.Override(StoragePathsLocations.Create(Path.Combine(Root, "data"))));
    }

    public string Root { get; }

    /// <summary>A folder for the pictures a test "picks" (outside the library).</summary>
    public string Sources { get; }

    public InMemorySettingsStore Defaults { get; } = new();

    /// <summary>The library folder the store keeps its one picture in (StoragePaths.swift:15).</summary>
    public string Library => StoragePaths.WallpaperDirectory;

    /// <summary>Wallpaper.swift:85-95 `private init()`: a store reading the defaults and library as they are now.</summary>
    public WallpaperStore NewStore() => NewStore(out _);

    public WallpaperStore NewStore(out List<PublishedImage> published)
    {
        var store = (WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!;
        stores.Add(store);
        var seen = new List<PublishedImage>();
        store.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WallpaperStore.Image)) seen.Add(new PublishedImage(store.Image, Transaction.Current));
        };
        published = seen;
        return store;
    }

    public string[] LibraryFiles() => Directory.Exists(Library) ? Directory.GetFiles(Library).Select(f => Path.GetFileName(f)!).OrderBy(n => n, StringComparer.Ordinal).ToArray() : [];

    /// <summary>Puts a picture in the library as a previous run would have left it, and stores its name.</summary>
    public string PlaceInLibrary(string name, string source)
    {
        Directory.CreateDirectory(Library);
        File.Copy(source, Path.Combine(Library, name));
        Defaults.Set("wallpaperFile", name);
        Defaults.Writes.Clear();
        return name;
    }

    /// <summary>
    /// A store that was started over a library holding one picture of this size (as after a restart) and has finished
    /// its start-up render, so both cached copies exist (Wallpaper.swift:173-174). <paramref name="published"/> is
    /// cleared once the first picture is in.
    /// </summary>
    public WallpaperStore Loaded(out List<PublishedImage> published, out string file, int width = 2000, int height = 40, double softness = 0)
    {
        var picture = WallpaperPictures.Png(Sources, $"p{Guid.NewGuid():N}.png", width, height);
        var name = $"wallpaper-{Guid.NewGuid():N}.png";
        PlaceInLibrary(name, picture);
        file = Path.Combine(Library, name);
        if (softness > 0) Defaults.Set("wallpaperSoftness", softness);
        Defaults.Writes.Clear();
        var store = NewStore(out published);
        var seen = published;
        WaitFor(() => store.Image is not null, "the start-up render");
        seen.Clear();
        return store;
    }

    /// <summary>Runs queued main-actor work.</summary>
    public static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>Pumps the main loop until the condition holds (the store's off-main work posts back to it).</summary>
    public static void WaitFor(Func<bool> condition, string what, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        Pump();
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"timed out waiting for: {what}");
            Thread.Sleep(1);
            Pump();
        }
    }

    /// <summary>Lets the pool work already started run to its hop back to main, then runs those hops: for the
    /// negative claims ("nothing is published"), which have no event to wait for.</summary>
    public static void Settle(int milliseconds = 400)
    {
        var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < until)
        {
            Thread.Sleep(5);
            Pump();
        }
    }

    public static TaskHandle? RenderTask(WallpaperStore store) => Field(store, "renderTask") as TaskHandle;

    public static TaskHandle? LiveTask(WallpaperStore store) => Field(store, "liveTask") as TaskHandle;

    private static object? Field(WallpaperStore store, string name) =>
        typeof(WallpaperStore).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store);

    /// <summary>The pixels of a published picture, as the store handed them to the backdrop.</summary>
    public static DecodedBitmap Read(IImage? image)
    {
        var bitmap = Assert.IsType<WriteableBitmap>(image);
        using var locked = bitmap.Lock();
        var width = locked.Size.Width;
        var height = locked.Size.Height;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            System.Runtime.InteropServices.Marshal.Copy(locked.Address + y * locked.RowBytes, pixels, y * width * 4, width * 4);
        return new DecodedBitmap(width, height, width * 4, pixels);
    }

    public static bool SamePixels(DecodedBitmap a, DecodedBitmap b) =>
        a.Width == b.Width && a.Height == b.Height && a.Pixels.AsSpan().SequenceEqual(b.Pixels);

    public void Dispose()
    {
        // The store's background work posts back to main after the test body: let each store's own tasks finish
        // (or notice their cancellation) while the headless session is still this test's, as the App.Tests card
        // scope does.
        foreach (var store in stores)
        {
            try
            {
                WaitFor(() => !store.IsInstalling, "an install to end", 15);
                store.IsAdjustingSoftness = false;
                RenderTask(store)?.Cancel();
                foreach (var handle in new[] { RenderTask(store), LiveTask(store) })
                    if (handle is not null) WaitFor(() => handle.Completion.IsCompleted, "a render to end", 15);
            }
            catch (Exception)
            {
                // Clean-up only: the test's own assertions already spoke.
            }
        }
        for (var i = overrides.Count - 1; i >= 0; i--) overrides[i].Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Real pictures made in the test (no file of anyone's own is ever read).</summary>
public static class WallpaperPictures
{
    /// <summary>Dark and light bands, 20 pixels each, left to right: a picture a blur visibly changes.</summary>
    public static (byte B, byte G, byte R) Stripes(int x) => (x / 20) % 2 == 0 ? ((byte)20, (byte)20, (byte)20) : ((byte)235, (byte)235, (byte)235);

    /// <summary>A real opaque PNG, encoded by Avalonia's Skia backend so any decoder reads it.</summary>
    public static string Png(string folder, string name, int width, int height, Func<int, (byte B, byte G, byte R)>? column = null)
    {
        column ??= Stripes;
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var locked = bitmap.Lock())
        {
            var row = new byte[width * 4];
            for (var x = 0; x < width; x++)
            {
                var (b, g, r) = column(x);
                row[x * 4] = b;
                row[x * 4 + 1] = g;
                row[x * 4 + 2] = r;
                row[x * 4 + 3] = 0xFF;
            }
            for (var y = 0; y < height; y++)
                System.Runtime.InteropServices.Marshal.Copy(row, 0, locked.Address + y * locked.RowBytes, row.Length);
        }
        using (var stream = File.Create(path)) bitmap.Save(stream, new PngBitmapEncoderOptions());
        return path;
    }

    /// <summary>A file named like a picture whose bytes no decoder reads: accepted by its extension, refused by decode.</summary>
    public static string Broken(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, "this is not a picture"u8.ToArray());
        return path;
    }

    /// <summary>A text file: neither its extension nor its content is a picture.</summary>
    public static string Text(string folder, string name)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, "plain text\n"u8.ToArray());
        return path;
    }

    /// <summary>An opaque frame whose every pixel is one colour, as raw pixels (no file, no decoder).</summary>
    public static DecodedBitmap Flat(int width, int height, byte value)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = value;
            pixels[i + 1] = value;
            pixels[i + 2] = value;
            pixels[i + 3] = 0xFF;
        }
        return new DecodedBitmap(width, height, width * 4, pixels);
    }

    /// <summary>An opaque grey frame that is 0 before <paramref name="edge"/> and 255 from it on, along x (or along y).</summary>
    public static DecodedBitmap Step(int width, int height, int edge, bool alongY = false)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var offset = (y * width + x) * 4;
                var value = (alongY ? y : x) >= edge ? (byte)255 : (byte)0;
                pixels[offset] = value;
                pixels[offset + 1] = value;
                pixels[offset + 2] = value;
                pixels[offset + 3] = 0xFF;
            }
        return new DecodedBitmap(width, height, width * 4, pixels);
    }

    /// <summary>The standard normal cumulative distribution (Abramowitz and Stegun 26.2.17, error under 1e-7).</summary>
    public static double NormalCdf(double z)
    {
        var t = 1 / (1 + 0.2316419 * Math.Abs(z));
        var density = Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI);
        var tail = density * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return z >= 0 ? 1 - tail : tail;
    }
}
