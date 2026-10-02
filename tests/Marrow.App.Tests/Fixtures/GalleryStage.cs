using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.UI.Port;
using Marrow.UI.Theme;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// The stage a gallery Fact works on: the backdrop over the working area, the windows brought to the front over it, and the
// capture of each in the dark and the light theme. All of it runs in the native child, on the child's real desktop.
internal sealed class GalleryStage
{
    private static readonly AppTheme[] Themes = [AppTheme.Dark, AppTheme.Light];

    private readonly WindowManagerNativeSession native;
    private readonly WindowManagerNativeSubject subject;
    private readonly string folder = GalleryRun.OutputFolder();
    private AvaloniaWindow? backdrop;

    /// <summary>The popups the app has presented (a real presenter underneath); set up by <see cref="Begin"/>.</summary>
    internal GalleryPresenter Popups { get; private set; } = null!;

    internal GalleryStage(WindowManagerNativeSession native, WindowManagerNativeSubject subject)
    {
        this.native = native;
        this.subject = subject;
    }

    internal WindowManagerNativeSession Native => native;

    /// <summary>Has the fixture close the window when the Fact ends.</summary>
    internal void Register(AvaloniaWindow window) => native.OnUi(() => native.Register(window));

    /// <summary>The primary screen's working area in physical pixels, and its scale (1.0 at 100 %).</summary>
    internal (PixelRect Work, double Scale) Work() => native.OnUi(() =>
    {
        var screen = subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
        if (screen.WorkingArea.Width < 1400 || screen.WorkingArea.Height < 900)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the gallery needs a working area of at least 1400 x 900 pixels.");
        return (screen.WorkingArea, screen.Scaling);
    });

    /// <summary>Clears the fixture's own two windows away and lays the backdrop over the working area.</summary>
    internal void Begin()
    {
        var (work, scale) = Work();
        native.OnUi(() =>
        {
            subject.Settings.Hide();
            subject.Main.Hide();
            Popups = GalleryPresenter.Install();
            backdrop = GalleryBackdrop.Cover(work, scale);
            native.Register(backdrop);
            backdrop.Show();
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(backdrop)); // as Program.cs binds the frame clock (ED-083), before any view that animates
        });
        Thread.Sleep(600);
        backdropShot = ScreenGrab.Of(work.X, work.Y, work.Width, work.Height);
    }

    // The working area with the backdrop alone on it: a capture that equals it shows no window at all.
    private ScreenGrab? backdropShot;

    /// <summary>True when the grab (taken at the screen position x, y) is not just the backdrop showing through where the window should be.</summary>
    private bool ShowsSomethingBeyondTheBackdrop(ScreenGrab grab, int x, int y)
    {
        if (backdropShot is not { } bare) return true;
        var (work, _) = Work();
        double sum = 0;
        var count = 0;
        for (var row = 0; row < grab.Height; row += 7)
            for (var column = 0; column < grab.Width; column += 7)
            {
                var bx = x - work.X + column;
                var by = y - work.Y + row;
                if (bx < 0 || by < 0 || bx >= bare.Width || by >= bare.Height) continue;
                sum += Math.Abs(grab.Luma(column, row) - bare.Luma(bx, by));
                count++;
            }
        return count == 0 || sum / count > 3;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    /// <summary>Puts a window at the top of the topmost band, above the backdrop, without activating it.</summary>
    private static void RaiseAboveBackdrop(nint window) => _ = SetWindowPos(window, -1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // HWND_TOPMOST; NOSIZE, NOMOVE, NOACTIVATE

    /// <summary>Puts a window over the backdrop at the front, active, with the pointer parked outside it.</summary>
    internal void Front(AvaloniaWindow window)
    {
        native.OnUi(() =>
        {
            window.Topmost = true;
            if (!window.IsVisible) window.Show();
        });
        Exception? last = null;
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                native.OnUi(() => WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId));
                last = null;
                break;
            }
            catch (Exception error)
            {
                last = error;
                Thread.Sleep(400);
            }
        }
        RaiseAboveBackdrop(native.OnUi(() => window.TryGetPlatformHandle()!.Handle));
        if (last is not null) Console.WriteLine("Gallery: the window could not take the foreground (" + last.Message + "); it is captured as it is.");
        var (work, _) = Work();
        _ = N.SetCursorPos(work.X + 8, work.Y + 8);
    }

    /// <summary>The first control of a type under the window (visual order) that satisfies the test, or null.</summary>
    internal T? Find<T>(AvaloniaWindow window, Func<T, bool> test) where T : Control =>
        native.OnUi(() => window.GetVisualDescendants().OfType<T>().FirstOrDefault(test));

    /// <summary>The first text block under the window whose text starts as given, or null.</summary>
    internal TextBlock? FindText(AvaloniaWindow window, string startsWith) =>
        Find<TextBlock>(window, block => block.IsEffectivelyVisible && (block.Text ?? "").StartsWith(startsWith, StringComparison.Ordinal));

    /// <summary>The physical screen point at the middle of a control.</summary>
    internal PixelPoint CenterOf(Control control) => native.OnUi(() =>
        control.PointToScreen(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2)));

    /// <summary>
    /// A real left click on the screen at a point, through SendInput, so hover, press and release reach the app as a person's would.
    /// It is sent only when the window under the point belongs to this process; the pointer is parked outside the app afterwards.
    /// </summary>
    internal void Click(PixelPoint point)
    {
        var root = N.RootAt(new NativeWindowPoint(point.X, point.Y));
        _ = N.GetWindowThreadProcessId(root, out var owner);
        if (owner != Environment.ProcessId) throw new InvalidOperationException("The gallery's click would land on a window of another process.");
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(250);
        N.Mouse(N.MOUSE_LEFTDOWN, N.MOUSE_LEFTUP);
        Thread.Sleep(150);
        Park();
    }

    /// <summary>Moves the pointer to a corner of the backdrop, away from every window of the app.</summary>
    internal void Park()
    {
        var (work, _) = Work();
        _ = N.SetCursorPos(work.X + 8, work.Y + 8);
    }

    /// <summary>Presses and releases Escape (closes a popover, as a person's key does); the window in front is the app's own.</summary>
    internal void Escape()
    {
        keybd_event(0x1B, 0, 0, 0);
        Thread.Sleep(60);
        keybd_event(0x1B, 0, 2, 0);
        Thread.Sleep(500);
    }

    /// <summary>Types letters, digits and spaces with real key presses into whatever has the keyboard focus.</summary>
    internal void Type(string text)
    {
        foreach (var character in text.ToUpperInvariant())
        {
            keybd_event((byte)character, 0, 0, 0);
            Thread.Sleep(40);
            keybd_event((byte)character, 0, 2, 0);
            Thread.Sleep(60);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void keybd_event(byte key, byte scan, uint flags, nuint extra);

    /// <summary>Moves the pointer onto a point and leaves it there (a hover).</summary>
    internal void Hover(PixelPoint point)
    {
        N.Check(N.SetCursorPos(point.X, point.Y), "SetCursorPos");
        N.Mouse(N.MOUSE_MOVE);
        Thread.Sleep(300);
    }

    /// <summary>The window's visible frame as it is now.</summary>
    internal (int X, int Y, int Width, int Height) Frame(AvaloniaWindow window) =>
        GalleryShot.FrameOf(native.OnUi(() => window.TryGetPlatformHandle()!.Handle));

    // The rectangle a capture covers: the window, the window with the popup windows it opened, or the popups alone with a margin.
    private Func<(int X, int Y, int Width, int Height)> Area(nint handle, GalleryCrop crop)
    {
        var backdropHandle = native.OnUi(() => backdrop!.TryGetPlatformHandle()!.Handle);
        var (work, _) = Work();
        return () =>
        {
            var main = GalleryShot.FrameOf(handle);
            if (crop == GalleryCrop.Window) return main;
            var popups = GalleryShot.OtherFrames(native.ThreadId, handle, backdropHandle);
            if (crop == GalleryCrop.WithPopups) return GalleryShot.Union(popups.Prepend(main));
            if (popups.Count == 0) throw new InvalidOperationException("The scene opened no popup window to capture.");
            var (x, y, width, height) = GalleryShot.Union(popups);
            const int margin = 24;
            var left = Math.Max(work.X, x - margin);
            var top = Math.Max(work.Y, y - margin);
            return (left, top, Math.Min(work.Right, x + width + margin) - left, Math.Min(work.Bottom, y + height + margin) - top);
        };
    }

    /// <summary>Captures the window as it stands, once settled, as scene-theme.png.</summary>
    internal void Shoot(AvaloniaWindow window, string scene, string theme, int settleMilliseconds = 1500, int boundMilliseconds = 9000, GalleryCrop crop = GalleryCrop.Window, bool animated = false)
    {
        var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
        var area = Area(handle, crop);
        for (var attempt = 1; ; attempt++)
        {
            var (grab, settled, change) = GalleryShot.Settle(area, settleMilliseconds, boundMilliseconds);
            var (x, y, _, _) = area();
            if (ShowsSomethingBeyondTheBackdrop(grab, x, y))
            {
                GalleryShot.Save(folder, scene + "-" + theme, grab, settled, change, animated);
                return;
            }
            // The window is not on screen over the backdrop (behind it, or not painted yet): raise it, give it time, and look again.
            Console.WriteLine($"Gallery: {scene}-{theme} showed only the backdrop (attempt {attempt}); root window at its centre: 0x{N.RootAt(new NativeWindowPoint(x + grab.Width / 2, y + grab.Height / 2)):X}, the window is 0x{handle:X}");
            if (attempt == 4) throw new InvalidOperationException($"The window of {scene}-{theme} never showed over the backdrop.");
            Front(window);
            Thread.Sleep(1500);
        }
    }

    /// <summary>Sets each of the dark and the light theme on the app, brings the window to the front, and runs the scene in it.</summary>
    internal void ForEachTheme(AppModel model, AvaloniaWindow window, Action<string> scene)
    {
        foreach (var theme in Themes)
        {
            native.OnUi(() => model.Settings.Theme = theme);
            Thread.Sleep(900);
            Front(window);
            scene(theme == AppTheme.Dark ? "dark" : "light");
        }
    }

    /// <summary>Captures the window in each theme; <paramref name="prepare"/> puts the scene into its state on the UI thread for each.</summary>
    internal void BothThemes(AppModel model, AvaloniaWindow window, string scene, Action? prepare = null, int settleMilliseconds = 1500, GalleryCrop crop = GalleryCrop.Window) =>
        ForEachTheme(model, window, label =>
        {
            if (prepare is not null) native.OnUi(prepare);
            Shoot(window, scene, label, settleMilliseconds, crop: crop);
        });
}
