using System.Reflection;
using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port;
using Marrow.App.Theme;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Services.Store;
using Marrow.Testing;
using Marrow.UI.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test support for tests/Marrow.App.Tests/Views/Settings/WallpaperCardTests.cs.
//
// The card (App/Views/Settings/WallpaperCard.swift:8) reads `WallpaperStore.shared` (UI/Theme/Wallpaper.swift:17),
// which the port moves to App (HANDOFF section 9, IMPLEMENTING.md 2.1). A `static let shared` that tests replace is an
// Ambient (R-LIFE-2), and its `private init()` (:85-95) reads UserDefaults once, so each test builds its own store
// with the private constructor while its own in-memory defaults are installed, exactly as the Core.Models.Shortcuts
// tests build ShortcutStore. `WebsiteCaptures.defaults` (:75) is the capture-mode store, not ported (ED-001,
// porting/dead_code.md): outside capture mode it is nil, so the store reads UserDefaults.standard.
//
// Every animation and transition reads the frame clock (ED-098 (1)), so a test drives frames by hand.

/// <summary>One card on screen over its own store, defaults and frame source, for one headless test.</summary>
public sealed class WallpaperCardScope : IDisposable
{
    private readonly List<IDisposable> overrides = [];
    private readonly string baseFolder;

    /// <summary>
    /// The default width, 592, makes the preview 560 wide (592 less the 16-point inset on each side, :100), so the
    /// mock window's 60 percent (:55) is a whole 336 by 90 and its centring offsets a whole 112 by 30: every expected
    /// position is exact whether or not layout rounding is on.
    /// </summary>
    public WallpaperCardScope(double width = 592, double height = 520)
    {
        overrides.Add(UserDefaults.Standard.Override(Defaults));
        overrides.Add(FrameClock.Source.Override(Frames));
        // Every animation reads SwiftClock.Now for its elapsed time (ED-098 (1), AnimatedColor.Set), so the clock
        // advances in lockstep with the frame source or a transition never completes.
        overrides.Add(SwiftClock.Provider.Override(Time));
        // Install (Wallpaper.swift:113-144) copies into Storage.WallpaperDirectory (R-TEST-9): a throwaway root.
        var dataDirectory = Marrow.Testing.TestDataDirectory.Read();
        if (string.IsNullOrEmpty(dataDirectory)) dataDirectory = System.IO.Path.GetTempPath();
        baseFolder = System.IO.Path.Combine(dataDirectory, "WallpaperCardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(baseFolder);
        var locations = StorageLocations.Create(System.IO.Path.Combine(baseFolder, "root"), System.IO.Path.Combine(baseFolder, "home"));
        overrides.Add(Storage.Locations.Override(locations));
        Store = (WallpaperStore)Activator.CreateInstance(typeof(WallpaperStore), nonPublic: true)!;
        overrides.Add(WallpaperStore.Shared.Override(Store));
        overrides.Add(WallpaperFileDialog.Current.Override(Dialog));
        Card = new WallpaperCard();
        Window = new Avalonia.Controls.Window { Width = width, Height = height, Content = Card };
        Window.Show();
        Pump();
    }

    public InMemorySettingsStore Defaults { get; } = new();

    public ManualFrameSource Frames { get; } = new();

    public ManualTimeProvider Time { get; } = new();

    public WallpaperStore Store { get; }

    public FakeWallpaperFileDialog Dialog { get; } = new();

    public WallpaperCard Card { get; }

    public Avalonia.Controls.Window Window { get; }

    /// <summary>Runs queued UI work and the layout it asks for.</summary>
    public void Pump()
    {
        Dispatcher.UIThread.RunJobs();
        Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Advances the frame clock two seconds in 16 ms frames: every animation of this card has ended.</summary>
    public void Settle()
    {
        for (var i = 0; i < 125; i++)
        {
            Time.Advance(TimeSpan.FromMilliseconds(16));
            Frames.Advance(TimeSpan.FromMilliseconds(16));
            Pump();
        }
    }

    /// <summary>
    /// Waits for work the store runs off main (`Task.detached`, Wallpaper.swift:122-143: a real file copy and a real
    /// decode on the pool) and posts back. Nothing can drive the pool, so this pumps the UI thread until the
    /// condition holds, as HeadlessTests' FIFO test does, with a ten-second ceiling that fails the test.
    /// </summary>
    public void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the store's off-main work did not come back within ten seconds");
            Thread.Sleep(1);
            Pump();
        }
        Pump();
    }

    /// <summary>The data a file drag from the desktop carries: one file item per path, in order (IStorageItem
    /// cannot be implemented outside Avalonia in 12.1, so a real file comes from the headless window's own
    /// StorageProvider, exactly as ComposerTextViewHarness.StorageFile does).</summary>
    public DataTransfer Drag(params string[] paths)
    {
        var data = new DataTransfer();
        foreach (var path in paths)
        {
            var lookup = Window.StorageProvider.TryGetFileFromPathAsync(path);
            WaitFor(() => lookup.IsCompleted);
            var file = lookup.GetAwaiter().GetResult() ?? throw new Xunit.Sdk.XunitException($"no storage file for {path}");
            data.Add(DataTransferItem.CreateFile(file));
        }
        return data;
    }

    /// <summary>Installs a picture through the store (Wallpaper.swift:113-144) and lets it arrive and settle.</summary>
    public void InstallAndSettle(string path)
    {
        Store.Install(from: path);
        WaitFor(() => !Store.IsInstalling);
        Assert.Null(Store.LastError);
        Assert.NotNull(Store.Image);
        Assert.True(Store.HasWallpaper);
        Settle();
    }

    public void Dispose()
    {
        Window.Close();
        Dispatcher.UIThread.RunJobs();
        CancelPendingRenders();
        for (var i = overrides.Count - 1; i >= 0; i--) overrides[i].Dispose();
        try { Directory.Delete(baseFolder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Test-only cleanup, no Swift twin: WallpaperStore has no public "cancel everything" method (production never
    // needs one; the real app just exits). A test that installs a picture or drags the softness slider leaves a
    // renderTask or liveTask (Wallpaper.cs:66,69) running in the background; without this, that task can still be
    // mid-flight when this scope disposes, and its MainActor.Run(() => ... Image = ToImage(rendered) ...) callback
    // (Wallpaper.cs:236-242, :273-277) then fires later, after a DIFFERENT concurrently running xUnit collection
    // has moved the shared Avalonia headless session on - observed crashing the whole test host with "Unable to
    // locate IPlatformRenderInterface" blamed on unrelated tests (AppUpdaterInstallTests, this class itself) under
    // the parallel suite. Cancelling both tasks here, the same way UpdateCheckerSecondTests.StopBackgroundChecksForTest
    // reaches into a field production exposes no stop method for, makes each test own the background work it
    // started instead of leaving it to finish on its own schedule. This narrows the crash window (SwiftTask.IsCancelled
    // is checked before Image is ever set) but is cooperative, like every other TaskHandle.Cancel in this tree: it
    // cannot interrupt a callback already past that check at the moment of disposal.
    private void CancelPendingRenders()
    {
        var type = typeof(WallpaperStore);
        (type.GetField("renderTask", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Store) as TaskHandle)?.Cancel();
        (type.GetField("liveTask", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(Store) as TaskHandle)?.Cancel();
    }
}

/// <summary>Stands in for the real Windows file-open dialog behind WallpaperCard.choose() (:168-189, R-TEST-10:
/// no test opens a real system dialog); records what it was called with and hands back a canned result.</summary>
public sealed class FakeWallpaperFileDialog : IWallpaperFileDialog
{
    public string? NextResult { get; set; }

    public int CallCount { get; private set; }

    public Avalonia.Controls.Window? LastOwner { get; private set; }

    public Task<string?> Choose(Avalonia.Controls.Window? owner)
    {
        CallCount++;
        LastOwner = owner;
        return Task.FromResult(NextResult);
    }
}

/// <summary>Real files in a throwaway folder under MARROW_PORT_DATA_DIR (R-TEST-9).</summary>
public static class PictureFiles
{
    /// <summary>
    /// A new empty folder under MARROW_PORT_DATA_DIR. The store copies an installed picture into the app's wallpaper
    /// directory (Wallpaper.swift:128-131), so these tests refuse to run unless the data folder is a throwaway one.
    /// </summary>
    public static string Folder()
    {
        var root = Environment.GetEnvironmentVariable("MARROW_PORT_DATA_DIR");
        Assert.False(string.IsNullOrEmpty(root), "MARROW_PORT_DATA_DIR must point at a throwaway folder (R-TEST-9)");
        var folder = System.IO.Path.Join(root, "WallpaperCardTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// A real opaque PNG of side <paramref name="side"/>, encoded by Avalonia's Skia backend, so any decoder reads it.
    /// A large side makes the store's off-main decode (Wallpaper.swift:137, :264-269) take long enough that the
    /// installing state is still on screen one UI turn after the install starts.
    /// </summary>
    public static string Png(string folder, string name, int side = 8)
    {
        var path = System.IO.Path.Join(folder, name);
        using var bitmap = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var row = new byte[side * 4];
            for (var x = 0; x < side; x++)
            {
                row[x * 4] = unchecked((byte)(x * 7));
                row[x * 4 + 1] = 0x80;
                row[x * 4 + 2] = 0xC0;
                row[x * 4 + 3] = 0xFF;
            }
            for (var y = 0; y < side; y++)
                System.Runtime.InteropServices.Marshal.Copy(row, 0, buffer.Address + y * buffer.RowBytes, row.Length);
        }
        using (var stream = File.Create(path)) bitmap.Save(stream, new PngBitmapEncoderOptions());
        return path;
    }

    /// <summary>A file named like a picture whose bytes no decoder reads: accepted by extension, refused by decode.</summary>
    public static string BrokenPicture(string folder, string name)
    {
        var path = System.IO.Path.Join(folder, name);
        using (var stream = File.Create(path)) stream.Write("not a picture at all"u8);
        return path;
    }

    /// <summary>A plain text file: neither its extension nor its content type is an image.</summary>
    public static string Text(string folder, string name)
    {
        var path = System.IO.Path.Join(folder, name);
        using (var stream = File.Create(path)) stream.Write("plain text\n"u8);
        return path;
    }

}

/// <summary>Queries over the card's controls that do not depend on how the port composes them.</summary>
public static class CardQuery
{
    public static IEnumerable<T> All<T>(Control root) where T : class =>
        new[] { (ILogical)root }.Concat(root.GetLogicalDescendants()).OfType<T>();

    /// <summary>The text a TextBlock shows, whether set as Text or as runs (StyledText, ED-092).</summary>
    public static string TextOf(TextBlock block) =>
        block.Text ?? string.Concat(block.Inlines?.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text) ?? []);

    public static List<TextBlock> TextBlocks(Control root, string text) =>
        All<TextBlock>(root).Where(t => string.Equals(TextOf(t), text, StringComparison.Ordinal)).ToList();

    public static List<string> Texts(Control root) => All<TextBlock>(root).Select(TextOf).ToList();

    /// <summary>A button's title: its string content, or the text its content control shows.</summary>
    public static string LabelOf(Button button) => button.Content switch
    {
        string text => text,
        TextBlock block => TextOf(block),
        Control content => string.Concat(All<TextBlock>(content).Select(TextOf)),
        _ => "",
    };

    public static List<Button> ButtonsLabelled(Control root, string label) =>
        All<Button>(root).Where(b => string.Equals(LabelOf(b), label, StringComparison.Ordinal)).ToList();

    public static List<Control> WithTip(Control root, string tip) =>
        All<Control>(root).Where(c => ToolTip.GetTip(c) is string s && string.Equals(s, tip, StringComparison.Ordinal)).ToList();

    public static List<Control> Named(Control root, string name) =>
        All<Control>(root).Where(c => string.Equals(Avalonia.Automation.AutomationProperties.GetName(c), name, StringComparison.Ordinal)).ToList();

    /// <summary>Every control painted with exactly this colour, as a background or a shape fill.</summary>
    public static List<Control> PaintedWith(Control root, Color color) =>
        All<Control>(root).Where(c => Paint(c) is ISolidColorBrush brush && brush.Color == color).ToList();

    private static IBrush? Paint(Control control) => control switch
    {
        Shape shape => shape.Fill,
        Border border => border.Background,
        Panel panel => panel.Background,
        TemplatedControl templated => templated.Background,
        _ => null,
    };

    /// <summary>The product of every Opacity from the control up to and including the ancestor.</summary>
    public static double EffectiveOpacity(Visual control, Visual ancestor)
    {
        var opacity = 1.0;
        for (Visual? v = control; v is not null; v = v.GetVisualParent())
        {
            opacity *= v.Opacity;
            if (ReferenceEquals(v, ancestor)) break;
        }
        return opacity;
    }

    /// <summary>The horizontal scale from the control's space to the ancestor's, render transforms included.</summary>
    public static double ScaleTo(Visual control, Visual ancestor) => control.TransformToVisual(ancestor)!.Value.M11;

    public static Point Origin(Visual control, Visual relativeTo) => control.TranslatePoint(new Point(0, 0), relativeTo)!.Value;

    public static Point Centre(Visual control, Visual relativeTo) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), relativeTo)!.Value;

    public static bool IsInside(Visual? hit, Visual ancestor)
    {
        for (var v = hit; v is not null; v = v.GetVisualParent())
            if (ReferenceEquals(v, ancestor)) return true;
        return false;
    }

    /// <summary>Clicks a button the way an assistive technology does: the Invoke pattern, which refuses a disabled one.</summary>
    public static void Invoke(Button button) =>
        ControlAutomationPeer.CreatePeerForElement(button).GetProvider<IInvokeProvider>()!.Invoke();

    public static byte[] Utf8(string text) => System.Text.Encoding.UTF8.GetBytes(text);
}
