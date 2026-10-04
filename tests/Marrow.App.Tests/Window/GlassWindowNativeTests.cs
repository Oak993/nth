using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Tour;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.UI.Port;
using AvaloniaWindow = Avalonia.Controls.Window;
using N = Marrow.App.Tests.Window.Native.WindowManagerNativeMethods;

namespace Marrow.App.Tests.Window.Native;

// Native evidence for ED-236 (OD-0005, OD-0014, OD-0041; closes WindowManager STOP #5), on real windows and real screen
// pixels. The pattern is a window of black and white vertical stripes six pixels wide (period 12), the sharpest thing a
// blur can be asked about: seen straight, ScreenGrab.StripeContrast reads about 42.5 on it; seen through a blur, about 0.
//   - the real factory main window (WindowManager.MakeWindow, WindowChrome.Configure) lying over the stripes shows them
//     blurred, with the app-drawn scrim over the blur, and keeps them blurred when it is not the active window;
//   - the real tour window blurs the stripes behind its card;
//   - the boot hand-over (BootStageWindow, ED-236) shows the main window with the blur already in place: not one sampled
//     frame is the scrim over sharp stripes, and nothing of the window shows before the hand-over; the same launch without
//     the hand-over shows about 200 ms of the scrim over sharp stripes (probe P13).
// The pixels are the composed screen (BitBlt with CAPTUREBLT), so what is measured is what the user sees. The window
// geometry that is not under test (the stripes' place, the sampled blocks) is derived from the working area.
//
// NOT ASSERTED, recorded in ED-236: the corners of a window outside the 26 pt shape its content draws show the Acrylic
// without the scrim (the window is rectangular, DWM rounds it by 8 px); and a user-opened Settings window shows the same
// 200 ms gap on its first show (OD-0041 names only the launch's boot sequence).
[Collection("WindowManagerNative")]
public sealed class GlassWindowNativeTests(WindowManagerNativeSession native)
{
    private const double Sharp = 42.5;      // StripeContrast of the bare stripes
    private const double Blurred = 3;       // below this the stripes are gone
    private const double ScrimAlpha = 66 / 255.0; // the test's stand-in for the app-drawn scrim, black

    private static void Themeless(AvaloniaWindow window) =>
        window.Template = new FuncControlTemplate<AvaloniaWindow>((owner, _) => new ContentPresenter { Content = owner.Content });

    // A topmost window of stripes over the physical-pixel rectangle, shown without taking the foreground.
    private AvaloniaWindow StripeWindow(PixelRect area, double scale)
    {
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = area.Position,
            Width = area.Width / scale, Height = area.Height / scale, Content = new Stripes(),
        };
        Themeless(window);
        native.Register(window);
        window.Show();
        return window;
    }

    // A small topmost window away from the pattern: activating it makes the windows over the pattern inactive.
    private AvaloniaWindow Thief(PixelRect work, double scale)
    {
        var window = new AvaloniaWindow
        {
            WindowDecorations = WindowDecorations.None, ShowInTaskbar = false, Topmost = true, CanResize = false, ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(work.Right - 120, work.Bottom - 120),
            Width = 80 / scale, Height = 80 / scale, Content = new Border { Background = Brushes.DimGray },
        };
        Themeless(window);
        native.Register(window);
        window.Show();
        return window;
    }

    private (PixelRect Work, double Scale) Work(AvaloniaWindow any)
    {
        var screen = any.Screens.Primary ?? throw new InvalidOperationException("No native screen is available.");
        if (screen.WorkingArea.Width < 1400 || screen.WorkingArea.Height < 900)
            throw new InvalidOperationException("NATIVE_PRECONDITION: the glass tests need a working area of at least 1400 x 900 pixels.");
        return (screen.WorkingArea, screen.Scaling);
    }

    // Stripes over the middle of the working area, big enough for any window these tests show; the fixture's own windows hidden.
    private AvaloniaWindow Backdrop(WindowManagerNativeSubject subject, PixelRect work, double scale)
    {
        subject.Settings.Hide();
        subject.Main.Hide();
        const int width = 1200, height = 800;
        return StripeWindow(new PixelRect(work.X + (work.Width - width) / 2, work.Y + (work.Height - height) / 2, width, height), scale);
    }

    private void Activate(AvaloniaWindow window) => native.OnUi(() =>
    {
        WindowManagerNativeForeground.Acquire(window.TryGetPlatformHandle()!.Handle, native.ThreadId);
        Assert.True(window.IsActive);
    });

    private static ScreenGrab Grab(PixelPoint at, int width = 120, int height = 36) => ScreenGrab.Of(at.X, at.Y, width, height);

    // Reads a small block of the screen as fast as it can from its own thread (a few hundred times a second), so every
    // frame the compositor presents there is likely to be seen.

    // The frames that show the glass window's tint over stripes that are still sharp: the P13 gap.
    private static GlassSample[] TintedOverSharp(IEnumerable<GlassSample> samples, double bareMean) =>
        samples.Where(s => s.Contrast >= Blurred && s.Mean < bareMean - 1.5).ToArray();

    private static void WaitFor(Func<bool> condition, string reason, int milliseconds = 4000) => N.Until(condition, reason, milliseconds);

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "UI-SETTINGS-001")] // WindowChrome.swift:116-117 through WindowManager.swift:101; OD-0014, OD-0005
    [Trait("Req", "UI-LAUNCH-001")] // the main window's clear glass: Acrylic plus the app's scrim (OD-0014)
    public void The_real_main_window_blurs_the_stripes_behind_it_under_the_scrim_and_stays_blurred_when_it_is_inactive() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var (stripes, thief, at) = native.OnUi(() =>
        {
            var stripes = Backdrop(subject, work, scale);
            var thief = Thief(work, scale);
            // the fixture's stand-in content is opaque white: clear it, and draw the scrim a production RootView draws
            subject.MainContent.Background = null;
            subject.MainContent.Children.Insert(0, new Border { Background = new SolidColorBrush(Color.FromArgb(66, 0, 0, 0)), CornerRadius = new CornerRadius(26), IsHitTestVisible = false });
            subject.Main.Topmost = true;
            subject.Main.Position = new PixelPoint(work.X + (work.Width - 900) / 2, work.Y + (work.Height - 600) / 2); // over the stripes
            return (stripes, thief, subject.Main.PointToScreen(new Point(300, 200)));
        });
        Thread.Sleep(800);
        var bare = Grab(at);
        var bareMean = bare.MeanLuma();
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3); // the instrument sees the stripes straight
        Assert.InRange(bareMean, 120, 135);

        native.OnUi(() => subject.Main.Show());
        Activate(subject.Main);
        WaitFor(() => Grab(at).StripeContrast() < Blurred, "the stripes behind the main window never blurred");
        Thread.Sleep(300);
        var active = Grab(at);
        // blurred, and tinted by exactly the scrim over a blur that adds no tint of its own (probe P3)
        Assert.True(active.StripeContrast() < Blurred, $"active: contrast {active.StripeContrast():F2}");
        Assert.InRange(active.MeanLuma(), bareMean * (1 - ScrimAlpha) - 4, bareMean * (1 - ScrimAlpha) + 4);

        Activate(thief);
        Thread.Sleep(700);
        Assert.False(native.OnUi(() => subject.Main.IsActive));
        var inactive = Grab(at);
        // Avalonia's Acrylic does not flatten when its window is inactive (probe P3): the blur is the same.
        Assert.True(inactive.StripeContrast() < Blurred, $"inactive: contrast {inactive.StripeContrast():F2}");
        Assert.InRange(inactive.MeanLuma(), active.MeanLuma() - 2, active.MeanLuma() + 2);
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-UPDATE-094")] // TourView.swift:79-86 over TourWindowController.swift:82-83
    public void The_real_tour_window_blurs_the_stripes_behind_its_card() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var stripes = native.OnUi(() => Backdrop(subject, work, scale));
        Thread.Sleep(800);
        var bare = Grab(new PixelPoint(work.X + work.Width / 2 - 500, work.Y + work.Height / 2), 60, 12);
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3);
        var (at, size) = native.OnUi(() =>
        {
            var tour = TourWindowController.Shared.Present(Tour.Pages);
            Themeless(tour);
            native.Register(tour);
            return (tour.PointToScreen(new Point(0, 0)), tour.ClientSize);
        });
        // the blank margin at the bottom left of the card, under the description and left of the button
        using var sampler = new Sampler(new PixelPoint(at.X + 30, at.Y + (int)(size.Height * scale) - 52));
        Thread.Sleep(1500);
        var last = sampler.Stop()[^1];
        // the card's tint and scrim (black in the dark theme, white in the light one) over a blur: the stripes' mean moved, no stripes left
        Assert.True(last.Contrast < Blurred, $"tour: contrast {last.Contrast:F2}");
        Assert.True(Math.Abs(last.Mean - bare.MeanLuma()) > 15, $"tour: mean {last.Mean:F1} against the stripes' {bare.MeanLuma():F1}");
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // One launch of the real factory main window under the boot stage (BootStageWindow), over the stripes. Returns what
    // the screen showed at a block inside the window's top left (away from the centre, where the mark is) from before the
    // stage began until it had closed, the stripes' own mean luma, and when the hand-over showed the main window.
    private (GlassSample[] Timeline, double BareMean, double ShownAt) Launch(WindowManagerNativeSubject subject, PixelRect work, bool handOver, bool reduceMotion)
    {
        var frame = new PixelPoint(work.X + (work.Width - 900) / 2, work.Y + (work.Height - 600) / 2);
        var (manager, window) = native.OnUi(() =>
        {
            var manager = new WindowManager { MainContent = () => new Border { Background = new SolidColorBrush(Color.FromArgb(66, 0, 0, 0)) } };
            var window = manager.PrepareMain();
            Themeless(window);
            window.ShowInTaskbar = false;
            window.Topmost = true; // above the stripes, which are topmost; production main is not topmost
            window.Width = 860;
            window.Height = 560;
            window.Position = frame;
            native.Register(window);
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
            return (manager, window);
        });
        var sampler = new Sampler(new PixelPoint(frame.X + 90, frame.Y + 100));
        double shownAt = -1;
        var stage = native.OnUi(() =>
        {
            var launched = BootStageWindow.Start(window, reduceMotion, manager.ShowMain, handOver ? null : (_, _) => false);
            launched.MainWindowShown += (_, _) => shownAt = sampler.Now;
            launched.Ready();
            return launched;
        });
        WaitFor(() => !native.OnUi(() => stage.Window.IsVisible), "the boot stage never closed", 15000);
        Thread.Sleep(400);
        var timeline = sampler.Stop();
        native.OnUi(() => window.Hide());
        Thread.Sleep(600); // the system fades a hidden window out
        var bareMean = timeline.TakeWhile(s => s.Milliseconds < 150).Average(s => s.Mean);
        return (timeline, bareMean, shownAt);
    }

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-LAUNCH-028")] // AppDelegate.swift:22 (OD-0018); OD-0041; ED-103 (6)
    public void With_reduce_motion_the_boot_hand_over_shows_the_main_window_blurred_and_never_as_scrim_over_sharp_stripes() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var stripes = native.OnUi(() => Backdrop(subject, work, scale));
        Thread.Sleep(800);

        // Control: the same launch without the hand-over (the window is shown at the hand-over, as before ED-236). The
        // instrument must see the P13 gap in it, or seeing none with the hand-over would prove nothing.
        var (plain, plainBare, plainShown) = Launch(subject, work, handOver: false, reduceMotion: true);
        var plainGap = TintedOverSharp(plain.Where(s => s.Milliseconds >= plainShown - 30), plainBare); // from the hand-over on
        Assert.True(plainGap.Length >= 20, $"the instrument saw only {plainGap.Length} samples of scrim over sharp stripes without the hand-over");
        Assert.InRange(plainGap[^1].Milliseconds - plainGap[0].Milliseconds, 80, 600); // P13: about 150 to 200 ms

        var (timeline, bareMean, shownAt) = Launch(subject, work, handOver: true, reduceMotion: true);
        Assert.InRange(bareMean, 120, 135);
        // nothing of the main window shows before the hand-over, though it has been shown (cloaked) for 0.5 s ...
        var before = timeline.Where(s => s.Milliseconds < shownAt - 30).ToArray();
        Assert.True(before.Length > 100, $"{before.Length} samples before the hand-over");
        var bareContrast = timeline.TakeWhile(s => s.Milliseconds < 150).Average(s => s.Contrast);
        Assert.InRange(bareContrast, Sharp - 6, Sharp + 3);
        Assert.All(before, s => Assert.True(Math.Abs(s.Mean - bareMean) < 1.5 && s.Contrast > bareContrast - 3, $"displayed before the hand-over: {s}"));
        // ... and from the hand-over on, not one frame is the scrim over sharp stripes, and it ends blurred under the scrim
        Assert.Empty(TintedOverSharp(timeline, bareMean));
        var after = timeline.Where(s => s.Milliseconds >= shownAt - 30).ToArray();
        Assert.Contains(after, s => s.Contrast < Blurred);
        Assert.True(after[^1].Contrast < Blurred && after[^1].Mean < bareMean * (1 - ScrimAlpha) + 4, $"ended: {after[^1]}");
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-LAUNCH-028")] // AppDelegate.swift:22 (OD-0018); OD-0041; ED-103 (4)-(5)
    [Trait("Req", "UI-LAUNCH-001")] // shown automatically at launch, through the boot hand-over (OD-0018, OD-0041)
    public void Under_the_full_boot_sequence_the_hand_over_shows_the_main_window_blurred_and_never_as_scrim_over_sharp_stripes() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var stripes = native.OnUi(() => Backdrop(subject, work, scale));
        Thread.Sleep(800);
        var (timeline, bareMean, shownAt) = Launch(subject, work, handOver: true, reduceMotion: false);
        Assert.InRange(bareMean, 120, 135);
        // The window particles cross the sampled block until they have landed, so what is judged is the hand-over: from
        // 30 ms before it (the uncloak) on, no frame is the scrim over sharp stripes; and the window ends blurred under the scrim.
        var after = timeline.Where(s => s.Milliseconds >= shownAt - 30).ToArray();
        Assert.True(after.Length > 100, $"{after.Length} samples from the hand-over");
        Assert.Empty(TintedOverSharp(after, bareMean));
        Assert.True(after[^1].Contrast < Blurred && after[^1].Mean < bareMean * (1 - ScrimAlpha) + 4, $"ended: {after[^1]}");
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"), the owner's intent of 2026-10-05 (the glass is extended to the window's edge
    // and reads as one surface out to the OS edge; ED-NEW-corner-seam), ED-236 UNRESOLVED (a): the production Settings window (the real SettingsView,
    // WindowManager.swift:81) over the stripes, and again over broad bands of four colours. The window is a rectangle of the system's Acrylic that DWM rounds
    // by 8 px, and its content draws a 26 pt shape (SettingsView.swift:118-122); the corner between them is the wedge. A wedge pixel (inside the rounding,
    // outside the shape) must show what the body shows in its column just inside the shape: the same colour, within 3 of 255 on each channel, in both
    // themes (the backdrop is the same down every column). The first version of this test found the wedge wearing the scrim alone.
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "UI-SETTINGS-001")] // SettingsView.swift:118-122 with OD-0075
    public void The_real_Settings_window_corners_outside_the_26_point_shape_are_the_body_just_inside_it_over_the_blur_in_both_themes() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var stripes = native.OnUi(() => Backdrop(subject, work, scale));
        Thread.Sleep(800);
        var bare = Grab(new PixelPoint(work.X + work.Width / 2 - 500, work.Y + work.Height / 2), 60, 12);
        Assert.InRange(bare.StripeContrast(), Sharp - 6, Sharp + 3);
        var bareMean = bare.MeanLuma();
        Assert.InRange(bareMean, 120, 135);

        WindowManager manager = null!;
        AvaloniaWindow settings = null!;
        native.OnUi(() =>
        {
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings)); // the Rise open asks for frames
            manager = new WindowManager { MainContent = () => new Border() };
            manager.Model.Settings.Theme = Marrow.UI.Theme.AppTheme.Dark;
            manager.ShowSettings();
            settings = (AvaloniaWindow)typeof(WindowManager).GetField("settingsWindow", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!;
            native.Register(settings);
            Themeless(settings);
            settings.ShowInTaskbar = false;
            settings.Topmost = true; // above the stripes, which are topmost
        });
        WaitFor(() => native.OnUi(() => manager.SettingsOpening is not { Waiting: true }), "the Settings open never finished", 8000);
        Activate(settings);

        var problems = new List<string>();
        foreach (var busy in new[] { false, true })
        {
            if (busy) native.OnUi(() => ((Stripes)stripes.Content!).Fill = MainWindowCornerWedgeNativeTests.Bands());
            foreach (var (theme, dark) in new[] { (Marrow.UI.Theme.AppTheme.Dark, true), (Marrow.UI.Theme.AppTheme.Light, false) })
            {
                var opacity = native.OnUi(() => { manager.Model.Settings.Theme = theme; return manager.Model.Settings.BackdropOpacity; });
                Thread.Sleep(1000); // the window's colours ease 0.32 s (OD-0088); the backdrop behind is blurred by the system
                var handle = native.OnUi(() => settings.TryGetPlatformHandle()!.Handle);
                var visible = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle)).VisibleBounds!;
                Assert.InRange(Marrow.UI.Chrome.WindowBackdrop.Scrim(opacity, dark), 0.05, 0.95); // glass under a scrim, not a solid window
                var pairs = CornerSeam.OnScreen(visible.Left, visible.Top, visible.Right, visible.Bottom, Marrow.UI.Chrome.Chrome.WindowCornerRadius, scale, [3, 4, 5], [4, 5], clearance: 2);
                var name = $"{theme}, {(busy ? "banded" : "striped")} backdrop";
                CornerSeam.WaitSettled(pairs, 3);
                var colours = CornerSeam.AssertOnScreen(pairs, name, problems: problems);
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // OD-0075, the owner's intent of 2026-10-05, ED-NEW-corner-seam: the real tour window (TourWindowController.Present) is the same kind of window. Its source draws window-shaped
    // glass (TourView.swift:62-96, a 20 pt continuous shape for the glass, the tint, the clip, the hairline and the scrim; the Mac window is clear and the size of that shape), and
    // the port's window is the system's Acrylic with the card drawn over it, so the card's corner leaves a wedge of unscrimmed blur (measured: (234,195,39) over a (240,200,40)
    // backdrop). The wedge pixel is the card pixel in its column just inside the 20 pt shape, over the stripes and over bands of four colours, in both themes.
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "PR-UPDATE-094")] // TourView.swift:62-96 over TourWindowController.swift:60-90
    public void The_real_tour_windows_corners_outside_the_20_point_card_are_the_card_just_inside_it_over_the_blur_in_both_themes() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var stripes = native.OnUi(() => Backdrop(subject, work, scale));
        Thread.Sleep(800);
        AvaloniaWindow tour = null!;
        native.OnUi(() =>
        {
            tour = TourWindowController.Shared.Present(Tour.Pages);
            Themeless(tour);
            native.Register(tour);
        });
        Thread.Sleep(1200);
        var problems = new List<string>();
        foreach (var busy in new[] { false, true })
        {
            if (busy) native.OnUi(() => ((Stripes)stripes.Content!).Fill = MainWindowCornerWedgeNativeTests.Bands());
            foreach (var (variant, dark) in new[] { (Avalonia.Styling.ThemeVariant.Dark, true), (Avalonia.Styling.ThemeVariant.Light, false) })
            {
                native.OnUi(() => tour.RequestedThemeVariant = variant);
                Thread.Sleep(1000);
                var handle = native.OnUi(() => tour.TryGetPlatformHandle()!.Handle);
                var visible = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle)).VisibleBounds!;
                var pairs = CornerSeam.OnScreen(visible.Left, visible.Top, visible.Right, visible.Bottom, 20, scale, [3, 4, 5], [3, 4], clearance: 0.6);
                CornerSeam.WaitSettled(pairs, 1);
                CornerSeam.AssertOnScreen(pairs, $"tour, {(dark ? "dark" : "light")}, {(busy ? "banded" : "striped")} backdrop", block: 1, problems: problems);
            }
        }
        native.OnUi(() => TourWindowController.Shared.Close());
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));
}
