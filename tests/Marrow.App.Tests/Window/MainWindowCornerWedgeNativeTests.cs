using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// OD-0075 (owner, 2026-10-02: "Window corner: the wedge is filled with the window's scrim"; settles OD-0072); ED-236 UNRESOLVED (a).
//
// Native proof for the MAIN window's wedge, on a real window and real screen pixels. Until this test the main window's wedge was proven
// only headless (RootViewCornerWedgeTests: a grey stand-in for the blur); the native capture (GlassWindowNativeTests) covers the Settings
// window, and every other native test hosts a bare rig (WindowManagerNativeContent, or a Border) in the main window, where RootView, and so
// its wedge, is absent.
//
// Here the production factory (WindowManager.ShowMain, WindowChrome.Configure, the system's Acrylic) makes the main window with its real
// RootView, over a solid bright backdrop. The system's window is a rectangle that DWM rounds by 8 px; RootView draws a 26 pt shape. A 4 x 4
// block of the composed screen 4 px in from each corner of the window lies inside the rounding and outside the shape: the wedge. With the
// wedge's layer hidden the block shows the backdrop through the Acrylic (the unscrimmed blur); with it, the backdrop under the window's
// scrim: black over it in the dark theme, white in the light one, at the alpha WindowBackdrop draws (Chrome.swift:211-215).
[Collection("WindowManagerNative")]
public sealed class MainWindowCornerWedgeNativeTests(WindowManagerNativeSession native)
{
    private static readonly Color Bright = Color.FromRgb(240, 200, 40); // a bright, saturated backdrop: any other colour in the corner is plain to see
    private const int Tolerance = 7; // levels of 255 on each channel: the system's blur adds noise of a level or two

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Decision", "OD-0075")]
    [Trait("Req", "UI-LAUNCH-001")] // the main window's clear glass: Acrylic plus the app's scrim (OD-0014)
    public void The_real_main_windows_four_corners_outside_the_26_point_shape_wear_the_scrim_over_the_backdrop_in_both_themes() => native.Run(subject =>
    {
        var screen = native.OnUi(() => subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available."));
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        if (work.Width < 1000 || work.Height < 700)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the main window test needs a working area of at least 1000 x 700 pixels.");

        // The bare rigs of the fixture are out of the way; a topmost solid backdrop covers the working area.
        var backdrop = native.OnUi(() =>
        {
            subject.Settings.Hide();
            subject.Main.Hide();
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = work.Position,
                Width = work.Width / scale, Height = work.Height / scale, Content = new Border { Background = new SolidColorBrush(Bright) },
            };
            Themeless(window);
            native.Register(window);
            window.Show();
            return window;
        });
        Thread.Sleep(800);
        var bare = ScreenGrab.Of(work.X + 40, work.Y + 40, 8, 8);
        AssertNear(Mean(bare), (Bright.R, Bright.G, Bright.B), "the backdrop as the screen shows it");

        // The production main window with its real RootView (the factory's default content).
        WindowManager manager = null!;
        RootView root = null!;
        AvaloniaWindow main = null!;
        native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
            manager = new WindowManager { MainContent = () => root = new RootView() };
            manager.Model.Settings.Theme = Marrow.UI.Theme.AppTheme.Dark;
            manager.ShowMain();
            main = manager.MainWindow ?? throw new InvalidOperationException("ShowMain did not retain its window.");
            native.Register(main);
            Themeless(main);
            main.ShowInTaskbar = false;
            main.Topmost = true; // above the backdrop, which is topmost; production main is not topmost
            main.Width = 900;
            main.Height = 600;
            main.Position = new PixelPoint(work.X + (work.Width - (int)(900 * scale)) / 2, work.Y + (work.Height - (int)(600 * scale)) / 2);
        });
        Assert.NotNull(root);
        WaitFor(() => native.OnUi(() => main.IsVisible), "the main window never showed");
        native.OnUi(() => WindowManagerNativeForeground.Acquire(main.TryGetPlatformHandle()!.Handle, native.ThreadId));
        var wedge = native.OnUi(() => root.CornerWedge);

        foreach (var (theme, dark) in new[] { (Marrow.UI.Theme.AppTheme.Dark, true), (Marrow.UI.Theme.AppTheme.Light, false) })
        {
            var opacity = native.OnUi(() => { manager.Model.Settings.Theme = theme; return manager.Model.Settings.BackdropOpacity; });
            Thread.Sleep(900); // the window's colours ease (OD-0088); the system blurs the backdrop behind
            var handle = native.OnUi(() => main.TryGetPlatformHandle()!.Handle);
            var visible = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle)).VisibleBounds!;
            var alpha = WindowCornerWedge.Alpha(opacity, dark);
            Assert.InRange(alpha, 0.05, 0.95); // glass under a scrim, not a solid window
            var scrim = dark ? 0 : 255;
            Assert.True(native.OnUi(() => wedge.IsVisible && wedge.Bounds.Width > 0), "the RootView's wedge layer has a size");

            // 4 x 4 pixels 4 px in from each corner of the window: inside DWM's 8 px rounding, outside the 26 pt shape
            var corners = new[]
            {
                (visible.Left + 4, visible.Top + 4, "top left"), (visible.Right - 8, visible.Top + 4, "top right"),
                (visible.Left + 4, visible.Bottom - 8, "bottom left"), (visible.Right - 8, visible.Bottom - 8, "bottom right"),
            };
            // the control first: the wedge's layer hidden shows the backdrop through the Acrylic, unscrimmed
            native.OnUi(() => wedge.Opacity = 0);
            Thread.Sleep(400);
            var without = corners.Select(corner => ScreenGrab.Of(corner.Item1, corner.Item2, 4, 4)).ToArray();
            native.OnUi(() => wedge.Opacity = 1);
            Thread.Sleep(400);
            for (var index = 0; index < corners.Length; index++)
            {
                var name = $"{theme} {corners[index].Item3}";
                var unscrimmed = Mean(without[index]);
                var scrimmed = Mean(ScreenGrab.Of(corners[index].Item1, corners[index].Item2, 4, 4));
                // without the wedge the corner is the backdrop (so the instrument sees the blur and not something else) ...
                AssertNear(unscrimmed, (Bright.R, Bright.G, Bright.B), $"{name} without the wedge (the unscrimmed blur)");
                // ... with it, the window's scrim over the backdrop, channel by channel ...
                AssertNear(scrimmed,
                    ((int)Math.Round(Bright.R * (1 - alpha) + scrim * alpha), (int)Math.Round(Bright.G * (1 - alpha) + scrim * alpha), (int)Math.Round(Bright.B * (1 - alpha) + scrim * alpha)),
                    $"{name} with the wedge (the scrim, alpha {alpha:F2}, {(dark ? "black" : "white")}, over the backdrop)");
                // ... and plainly not the backdrop
                var moved = Math.Abs(scrimmed.R - unscrimmed.R) + Math.Abs(scrimmed.G - unscrimmed.G) + Math.Abs(scrimmed.B - unscrimmed.B);
                Assert.True(moved > 40, $"{name}: the wedge changes the corner by {moved} levels only; {scrimmed} with it, {unscrimmed} without it");
            }
        }
        GC.KeepAlive(backdrop);
    }, factType: typeof(MainWindowCornerWedgeNativeTests));

    private static (int R, int G, int B) Mean(ScreenGrab grab)
    {
        double r = 0, g = 0, b = 0;
        for (var y = 0; y < grab.Height; y++)
            for (var x = 0; x < grab.Width; x++)
            {
                var (pr, pg, pb) = grab.At(x, y);
                r += pr; g += pg; b += pb;
            }
        var count = grab.Width * grab.Height;
        return ((int)Math.Round(r / count), (int)Math.Round(g / count), (int)Math.Round(b / count));
    }

    private static void AssertNear((int R, int G, int B) actual, (int R, int G, int B) expected, string what) =>
        Assert.True(Math.Abs(actual.R - expected.R) <= Tolerance && Math.Abs(actual.G - expected.G) <= Tolerance && Math.Abs(actual.B - expected.B) <= Tolerance,
            $"{what}: the screen shows {actual}, expected {expected} within {Tolerance}");

    private static void WaitFor(Func<bool> condition, string reason, int milliseconds = 4000) => N.Until(condition, reason, milliseconds);
}
