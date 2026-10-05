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

// OD-0075 (owner, 2026-10-02: "Window corner: the wedge is filled with the window's scrim"; settles OD-0072); the owner's intent of 2026-10-05: Windows
// rounds window corners less than the app's 26 pt glass shape, so the glass is extended to the window's edge and reads as one glass surface out to the OS
// edge (ED-NEW-corner-seam); ED-236 UNRESOLVED (a).
//
// Native proof for the MAIN window's corners, on a real window and real screen pixels. Until the first version of this test the main window's wedge was
// proven only headless (a grey stand-in for the blur); the native capture (GlassWindowNativeTests) covers the Settings window, and every other native test
// hosts a bare rig (WindowManagerNativeContent, or a Border) in the main window, where RootView, and so its corners, is absent.
//
// Here the production factory (WindowManager.ShowMain, WindowChrome.Configure, the system's Acrylic) makes the main window with its real RootView, over
// a backdrop that is solid and bright, and again over one of broad bands of four colours. The system's window is a rectangle that DWM rounds by 8 px;
// RootView's glass shape is 26 pt. Around each corner a pixel inside the rounding and outside the shape is the wedge, and the pixel in the same column
// just inside the shape is the body. The first version of this test found the wedge wearing the scrim alone (black over the backdrop at the scrim's
// alpha in the dark theme, white in the light one) while the body wore the scrim, the legibility tint and the detail sheet's wash: a lighter or darker band
// followed the curve, and the glass seemed to stop before the window's edge. Both see the same backdrop in a column (it is the same down every column),
// so the two must be the same colour, within CornerSeam.Tolerance (3 of 255) on each channel.
[Collection("WindowManagerNative")]
public sealed class MainWindowCornerWedgeNativeTests(WindowManagerNativeSession native)
{
    private static readonly Color Bright = Color.FromRgb(240, 200, 40); // a bright, saturated backdrop: any other colour in the corner is plain to see
    private const int Tolerance = 7; // levels of 255 on each channel for the bare backdrop reading: the system's blur adds noise of a level or two

    // Broad vertical bands of four colours, the same down every column (so a wedge pixel and the body pixel in its column see the same backdrop) and
    // different across the window (so a layer that is one colour everywhere cannot pass by luck).
    internal static IBrush Bands() => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute),
        EndPoint = new RelativePoint(360, 0, RelativeUnit.Absolute),
        SpreadMethod = GradientSpreadMethod.Repeat,
        GradientStops =
        [
            new GradientStop(Color.FromRgb(240, 200, 40), 0), new GradientStop(Color.FromRgb(240, 200, 40), 0.25),
            new GradientStop(Color.FromRgb(30, 60, 220), 0.25), new GradientStop(Color.FromRgb(30, 60, 220), 0.5),
            new GradientStop(Color.FromRgb(220, 40, 120), 0.5), new GradientStop(Color.FromRgb(220, 40, 120), 0.75),
            new GradientStop(Color.FromRgb(40, 200, 90), 0.75), new GradientStop(Color.FromRgb(40, 200, 90), 1),
        ],
    };

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Decision", "OD-0075")]
    [Trait("Req", "UI-LAUNCH-001")] // the main window's clear glass: Acrylic plus the app's scrim (OD-0014)
    public void The_real_main_windows_four_corners_outside_the_26_point_shape_are_the_body_just_inside_it_over_a_solid_and_a_busy_backdrop_in_both_themes() => native.Run(subject =>
    {
        var screen = native.OnUi(() => subject.Main.Screens.Primary ?? throw new InvalidOperationException("No native screen is available."));
        var work = screen.WorkingArea;
        var scale = screen.Scaling;
        if (work.Width < 1000 || work.Height < 700)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the main window test needs a working area of at least 1000 x 700 pixels.");

        // The bare rigs of the fixture are out of the way; a topmost solid backdrop covers the working area.
        Border fill = null!;
        var backdrop = native.OnUi(() =>
        {
            fill = new Border { Background = new SolidColorBrush(Bright) };
            subject.Settings.Hide();
            subject.Main.Hide();
            var window = new AvaloniaWindow
            {
                WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Position = work.Position,
                Width = work.Width / scale, Height = work.Height / scale, Content = fill,
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
        // the window's launch reveal and the system's blur take a moment: wait for the glass to show over the backdrop before measuring
        string lastSeen = "nothing read";
        try
        {
            WaitFor(() =>
            {
                // raise the window above the backdrop again each time: another window can take the top of the topmost band while it settles
                native.OnUi(() => { main.Topmost = false; main.Topmost = true; WindowManagerNativeForeground.Acquire(main.TryGetPlatformHandle()!.Handle, native.ThreadId); });
                Thread.Sleep(250);
                var shown = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(main.TryGetPlatformHandle()!.Handle)).VisibleBounds!;
                var (r, g, b) = CornerSeam.ScreenMean(shown.Left + 24, shown.Top + 24);
                lastSeen = $"the window is {shown.Left},{shown.Top} to {shown.Right},{shown.Bottom}; the screen inside its top left shows ({r:F0}, {g:F0}, {b:F0})";
                return shown.Right - shown.Left > 880 * scale && Math.Abs(r - Bright.R) + Math.Abs(g - Bright.G) + Math.Abs(b - Bright.B) > 40;
            }, "the main window's glass never showed over the backdrop", 10000);
        }
        catch (TimeoutException timeout) { throw new TimeoutException(timeout.Message + ": " + lastSeen); }
        var problems = new List<string>();
        foreach (var busy in new[] { false, true })
        {
            native.OnUi(() => fill.Background = busy ? Bands() : new SolidColorBrush(Bright));
            foreach (var (theme, dark) in new[] { (Marrow.UI.Theme.AppTheme.Dark, true), (Marrow.UI.Theme.AppTheme.Light, false) })
            {
                var opacity = native.OnUi(() => { manager.Model.Settings.Theme = theme; return manager.Model.Settings.BackdropOpacity; });
                Thread.Sleep(1000); // the window's colours ease (OD-0088); the system blurs the backdrop behind
                var handle = native.OnUi(() => main.TryGetPlatformHandle()!.Handle);
                var visible = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle)).VisibleBounds!;
                Assert.InRange(WindowBackdrop.Scrim(opacity, dark), 0.05, 0.95); // glass under a scrim, not a solid window
                // each wedge pixel (inside DWM's 8 px rounding, outside the 26 pt shape) against the body pixel in its column just inside the shape
                var pairs = CornerSeam.OnScreen(visible.Left, visible.Top, visible.Right, visible.Bottom, Chrome.WindowCornerRadius, scale, [3, 4, 5, 6], [3, 4, 5], clearance: 1, block: 1);
                var name = $"{theme}, {(busy ? "banded" : "solid")} backdrop";
                CornerSeam.WaitSettled(pairs, 1);
                var colours = CornerSeam.AssertOnScreen(pairs, name, block: 1, problems: problems);
                if (!busy)
                    // the instrument sees the glass and not the bare backdrop: the corner is plainly not the backdrop, in the wedge or the body
                    for (var index = 0; index < pairs.Length; index++)
                    {
                        var c = colours[index];
                        Assert.True(Math.Abs(c.Wedge.R - Bright.R) + Math.Abs(c.Wedge.G - Bright.G) + Math.Abs(c.Wedge.B - Bright.B) > 40,
                            $"{name}: the {pairs[index].Corner} wedge {pairs[index].Wedge} shows ({c.Wedge.R:F0}, {c.Wedge.G:F0}, {c.Wedge.B:F0}), the backdrop's colour unchanged; the window is {visible.Left},{visible.Top} to {visible.Right},{visible.Bottom}");
                    }
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
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
