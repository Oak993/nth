using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Media;
using Avalonia.VisualTree;
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
// The corners of a window outside the 26 pt shape its content would draw (the window is rectangular, DWM rounds it by 8 px) are
// asserted for the Settings window and the tour window (and, in MainWindowCornerWedgeNativeTests, the main window): each is the
// colour of the body just inside the shape, over stripes and over bands of four colours, in both themes (OD-0075, the owner's
// intent of 2026-10-05, ED-338). NOT ASSERTED, recorded in ED-236: a user-opened Settings window shows the same
// 200 ms gap on its first show (OD-0041 names only the launch's boot sequence).
[Collection("WindowManagerNative")]
public sealed class GlassWindowNativeTests(WindowManagerNativeSession native)
{
    private const double Sharp = 42.5;      // StripeContrast of the bare stripes
    private const double Blurred = 3;       // below this the stripes are gone
    private const double ScrimAlpha = 66 / 255.0; // the test's stand-in for the app-drawn scrim, black

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern long GetWindowLongPtr(nint window, int index);

    private static bool Layered(nint window) => (GetWindowLongPtr(window, -20) & 0x80000) != 0; // GWL_EXSTYLE, WS_EX_LAYERED

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
    // stage began until it had closed, the stripes' own mean luma and contrast read before the stage opened, when the
    // hand-over showed the main window, and the stage's scrim and the block's place against the window's clip at each
    // stage frame (ED-NEW-handoverfix-2: Boot B, OD-0113, dims the desktop from its start).
    private BootHandOverRun Launch(WindowManagerNativeSubject subject, PixelRect work, bool handOver, bool reduceMotion)
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
        Thread.Sleep(150); // the bare stripes, before the stage opens
        double shownAt = -1, startedAt = 0;
        var dark = true;
        var frames = new List<(double Milliseconds, double Scrim, int Place)>();
        var stage = native.OnUi(() =>
        {
            startedAt = sampler.Now;
            var launched = BootStageWindow.Start(window, reduceMotion, manager.ShowMain, handOver ? null : (_, _) => false);
            void Record()
            {
                var f = launched.Stage.Frame; var origin = launched.Window.Position; var scaling = launched.Window.RenderScaling;
                var block = new Rect((frame.X + 90 - origin.X) / scaling, (frame.Y + 100 - origin.Y) / scaling, 60 / scaling, 12 / scaling);
                var place = BootHandOverRun.Place(block, f.ContentClip.Translate((Vector)launched.Stage.WindowFrame.Position), f.CornerRadius);
                dark = PlainControls.IsDark(launched.Stage.ActualThemeVariant);
                lock (frames) frames.Add((sampler.Now, f.ScrimOpacity, place));
            }
            Record();
            launched.Stage.FrameChanged += (_, _) => Record();
            launched.MainWindowShown += (_, _) => shownAt = sampler.Now;
            launched.Ready();
            return launched;
        });
        WaitFor(() => !native.OnUi(() => stage.Window.IsVisible), "the boot stage never closed", 15000);
        Thread.Sleep(400);
        var timeline = sampler.Stop();
        native.OnUi(() => window.Hide());
        Thread.Sleep(600); // the system fades a hidden window out
        var bare = timeline.TakeWhile(s => s.Milliseconds < startedAt).ToArray();
        lock (frames) return new BootHandOverRun(timeline, bare.Average(s => s.Mean), bare.Average(s => s.Contrast), shownAt, frames.ToArray(), dark);
    }

    // ED-284 (1): the boot outline is the VISIBLE main window. The real factory main HWND (cloaked by the sequence, shown, so DWM knows
    // its bounds) against the stage's outline frame: the stage window's origin plus the outline at the screen's scale lies within a
    // pixel of DWM's extended frame bounds on the left, the top, the right and the bottom (the HWND's own origin lies one invisible
    // strip further left, which is the defect this proves gone).
    [Fact]
    [Trait("Decision", "ED-284")]
    [Trait("Req", "PR-LAUNCH-028")] // AppDelegate.swift:22 (OD-0018); WindowManager.swift:43-54 with ED-284 (1)
    public void The_boot_outline_lies_on_the_main_windows_visible_frame_as_DWM_reports_it() => native.Run(subject =>
    {
        var (work, scale) = native.OnUi(() => Work(subject.Main));
        var origin = new PixelPoint(work.X + (work.Width - 1320) / 2, work.Y + (work.Height - 860) / 2);
        var (manager, window) = native.OnUi(() =>
        {
            var manager = new WindowManager { MainContent = () => new Border() };
            var window = manager.PrepareMain();
            Themeless(window);
            window.ShowInTaskbar = false;
            window.Position = origin; // the factory main window's own size (1320 x 860), as production shows it
            native.Register(window);
            FrameClock.Source.SetProcessDefault(new AvaloniaFrameSource(subject.Settings));
            return (manager, window);
        });
        var stage = native.OnUi(() =>
        {
            var launched = BootStageWindow.Start(window, reduceMotion: true, manager.ShowMain);
            launched.Ready();
            return launched;
        });
        try
        {
            WaitFor(() => native.OnUi(() => window.IsVisible), "the sequence never showed the main window", 8000); // cloaked 0.5 s before the hand-over
            // Boot B (OD-0113) fades the whole HWND as a layered window while its rectangle grows, and a layered window has no invisible
            // border in DWM's bounds; the presentation restores the style at the exact final frame, where the outline is the frame
            var handle = native.OnUi(() => window.TryGetPlatformHandle()!.Handle);
            WaitFor(() => native.OnUi(() => !Layered(handle)), "the hand-over never restored the main window's own style", 8000);
            var (outline, stageOrigin, visible) = native.OnUi(() =>
                (stage.OutlineLayout.Frame, stage.Window.Position, WindowManagerNativePlacementGeometry.ReadVisibleBounds(window.TryGetPlatformHandle()!.Handle).VisibleBounds!));
            Assert.InRange(stageOrigin.X + outline.X * scale, visible.Left - 1, visible.Left + 1);
            Assert.InRange(stageOrigin.Y + outline.Y * scale, visible.Top - 1, visible.Top + 1);
            Assert.InRange(stageOrigin.X + outline.Right * scale, visible.Right - 1, visible.Right + 1);
            Assert.InRange(stageOrigin.Y + outline.Bottom * scale, visible.Bottom - 1, visible.Bottom + 1);
        }
        finally
        {
            WaitFor(() => !native.OnUi(() => stage.Window.IsVisible), "the boot stage never closed", 15000);
            native.OnUi(() => window.Hide());
        }
    }, factType: typeof(GlassWindowNativeTests));

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
        var plain = Launch(subject, work, handOver: false, reduceMotion: true);
        var plainGap = plain.TintedOverSharp(plain.Timeline.Where(s => s.Milliseconds >= plain.ShownAt - 30), ScrimAlpha); // from the hand-over on
        Assert.True(plainGap.Length >= 20, $"the instrument saw only {plainGap.Length} samples of scrim over sharp stripes without the hand-over");
        Assert.InRange(plainGap[^1].Milliseconds - plainGap[0].Milliseconds, 80, 600); // P13: about 150 to 200 ms

        var run = Launch(subject, work, handOver: true, reduceMotion: true);
        var (timeline, bareMean, shownAt) = (run.Timeline, run.BareMean, run.ShownAt);
        Assert.InRange(bareMean, 120, 135);
        Assert.InRange(run.BareContrast, Sharp - 6, Sharp + 3);
        // nothing of the main window shows before the hand-over, though it has been shown (cloaked) for 0.5 s: every sample is
        // the stripes under the stage's own scrim (Boot B, OD-0113) ...
        var before = timeline.Where(s => s.Milliseconds < shownAt - 30).ToArray();
        Assert.True(before.Length > 100, $"{before.Length} samples before the hand-over");
        Assert.Empty(run.NotTheStageScrim(before));
        // ... and from the hand-over on, not one frame is the scrim over sharp stripes, and it ends blurred under the scrim
        Assert.Empty(run.TintedOverSharp(timeline, ScrimAlpha));
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
        var run = Launch(subject, work, handOver: true, reduceMotion: false);
        var (timeline, bareMean, shownAt) = (run.Timeline, run.BareMean, run.ShownAt);
        Assert.InRange(bareMean, 120, 135);
        // What is judged is the hand-over: from 30 ms before it (the uncloak) on, no frame is the window's scrim over sharp stripes
        // (the stage's own scrim, and its growing frame's edge crossing the block, are the design, OD-0113); and the window ends
        // blurred under the scrim.
        var after = timeline.Where(s => s.Milliseconds >= shownAt - 30).ToArray();
        Assert.True(after.Length > 100, $"{after.Length} samples from the hand-over");
        Assert.Empty(run.TintedOverSharp(after, ScrimAlpha));
        Assert.True(after[^1].Contrast < Blurred && after[^1].Mean < bareMean * (1 - ScrimAlpha) + 4, $"ended: {after[^1]}");
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"), the owner's intent of 2026-10-05 (the glass is extended to the window's edge
    // and reads as one surface out to the OS edge; ED-338), ED-236 UNRESOLVED (a): the production Settings window (the real SettingsView,
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
            settings = manager.SettingsOverlay!.Window; // OD-0140: Settings is a panel in the main window
            native.Register(settings);
            Themeless(settings);
            settings.ShowInTaskbar = false;
            settings.Topmost = true; // above the stripes, which are topmost
        });
        WaitFor(() => native.OnUi(() => manager.SettingsOverlay is { Shown: >= 1 }), "the Settings open never finished", 8000);
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
                var pairs = CornerSeam.OnScreen(visible.Left, visible.Top, visible.Right, visible.Bottom, Marrow.UI.Chrome.Chrome.WindowCornerRadius, scale, [3, 4, 5, 6], [3, 4, 5], clearance: 1, block: 1);
                var name = $"{theme}, {(busy ? "banded" : "striped")} backdrop";
                CornerSeam.WaitSettled(pairs, 1);
                var colours = CornerSeam.AssertOnScreen(pairs, name, block: 1, problems: problems);
            }
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // OD-0075, the owner's intent of 2026-10-05, ED-338: the real tour window (TourWindowController.Present) is the same kind of window. Its source draws window-shaped
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
                // The card's material shades a strip along each edge (about 10 px wide) and lights a rim along the bottom (about 3 px), so a wedge pixel is compared with the pixel in its column
                // deep in the card, and only where it is clear of the bottom rim and of the 45 degree line where the two meet: more rows up than columns in.
                var pairs = CornerSeam.OnScreen(visible.Left, visible.Top, visible.Right, visible.Bottom, 20, scale, [2, 3], [6, 7, 8, 9], clearance: 0.6, block: 1);
                CornerSeam.WaitSettled(pairs, 1);
                CornerSeam.AssertOnScreen(pairs, $"tour, {(dark ? "dark" : "light")}, {(busy ? "banded" : "striped")} backdrop", block: 1, problems: problems, corners: ["bottom left", "bottom right"]); // the top corners are the page's artwork, a picture that differs from row to row
            }
        }
        native.OnUi(() => TourWindowController.Shared.Close());
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // The archive question popover (UI-SIDEBAR-031, -032), opened from a real thread row of the real main window as the archive shortcut asks for it
    // (AppModel.AppModelArchiveRequest, SidebarView.swift:198-202), in a real popup window. Headless cannot open a Popup, so only the glass's metadata was
    // ever asserted; here the pixels of the real popup are read. The glass material (OD-0005) draws a rim light and an inner shade at the edge of its shape
    // and tints the prominent one, so the ring just inside each button's capsule differs from the flat card behind it where the material is drawn and not
    // where it falls out of the composition tree (review/glass-material-placement 090e8b843). SwiftUI's `.focusEffectDisabled`-like look (:945) is that no
    // ring surrounds the focused Archive button (SidebarView.swift:935): the outer band does not change with focus.
    // The g2 glass card can vary across that band, so compare the same screen pixels focused/unfocused.
    [Fact]
    [Trait("Req", "UI-SIDEBAR-031")] // SidebarView.swift:923-941 `.glass` and `.glassProminent` text buttons at `.controlSize(.small)`, :945 the Archive button holds the focus
    [Trait("Req", "UI-SIDEBAR-032")] // :927-940 Delete plain glass with a red label, Archive prominent glass
    public void The_archive_question_popovers_glass_buttons_draw_their_material_on_a_real_popup_and_Archive_shows_no_focus_ring() => native.Run(subject =>
    {
        var thread = Guid.NewGuid();
        native.OnUi(() =>
        {
            var now = Marrow.Core.Port.SwiftDate.Now;
            var folder = Path.Combine(Path.GetTempPath(), "archive-popover-native-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(folder);
            var project = new Marrow.Core.Models.Project("archive-native", folder);
            var chat = new Marrow.Core.Models.ChatThread(project.Id, Marrow.Core.Models.ProviderKind.Claude, "opus", "high", Marrow.Core.Models.RuntimeMode.Supervised) with
            {
                Id = thread, Title = "Archive in a real popup", HasCustomTitle = true, UpdatedAt = now, CreatedAt = now.AddHours(-1), LastStatus = Marrow.Core.Models.TurnStatus.Completed,
            };
            var library = new Marrow.Core.Models.Library() with
            {
                Projects = new Marrow.Core.Port.ValueList<Marrow.Core.Models.Project>([project]),
                Threads = new Marrow.Core.Port.ValueList<Marrow.Core.Models.ChatThread>([chat]),
            };
            Marrow.Services.Store.DiskWriter.Shared.WriteSynchronously(library, Marrow.Services.Store.Storage.LibraryURL);
        });
        var (window, model) = TimelineRowsNativeRig.Open(native, subject, thread);
        // The rig's backdrop (a topmost window over the whole working area) stands over a popup that is not itself topmost on some runs, and then the
        // pixels under the buttons are the backdrop's. The backdrop is the rig's, not the product's: take it out of the topmost band, to the bottom.
        var backdrops = native.OnUi(() =>
        {
            var mainHandle = window.TryGetPlatformHandle()!.Handle;
            var mainRect = N.Rect(mainHandle);
            var found = new List<nint>();
            NativeWindowEnumerator callback = (candidate, _) =>
            {
                var rect = N.Rect(candidate);
                if (candidate != mainHandle && N.IsWindowVisible(candidate) && rect.Left <= mainRect.Left && rect.Top <= mainRect.Top && rect.Right >= mainRect.Right && rect.Bottom >= mainRect.Bottom) found.Add(candidate);
                return true;
            };
            _ = N.EnumThreadWindows(native.ThreadId, callback, 0);
            GC.KeepAlive(callback);
            return found;
        });
        Assert.NotEmpty(backdrops);
        foreach (var backdrop in backdrops) Assert.True(SetWindowPos(backdrop, 1, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010), "SetWindowPos(HWND_BOTTOM) on the rig's backdrop"); // SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE
        var popup = native.OnUi(() =>
        {
            var column = window.GetVisualDescendants().OfType<Marrow.App.Views.Sidebar.SidebarView>().Single();
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(thread);
            return column;
        });
        Popup archive = null!;
        var openBy = Stopwatch.StartNew();
        while (true)
        {
            if (native.OnUi(() =>
            {
                archive = LogicalQuery.SelfAndDescendants<Popup>(popup).FirstOrDefault(p => PopoverHost.Content(p) is Marrow.App.Views.Sidebar.ArchiveThreadPopover)!;
                return archive is not null && archive.IsOpen;
            })) break;
            Assert.True(openBy.ElapsedMilliseconds < 15000, "The archive question never opened on the thread's row.");
            Thread.Sleep(5);
        }
        var sinceOpen = Stopwatch.StartNew(); // from the first moment the popup is seen open

        // One reading of the screen under the popup: the ring just inside each button's capsule, and the band just outside the Archive button.
        (bool Drawn, string Report, double DeleteRingMax, double DeleteRingMean, double ArchiveRingMean, double ArchiveOuterMax, bool Focused, ScreenGrab Pixels, PixelRect ArchiveRect, PixelPoint Origin) Read(bool save, string tag = "focused")
        {
            var (deleteRect, archiveRect, focused) = native.OnUi(() =>
            {
                var content = PopoverHost.Content(archive)!;
                var scale = TopLevel.GetTopLevel(content)!.RenderScaling;
                var buttons = LogicalQuery.SelfAndDescendants<Button>(content).ToList();
                Button Named(string name) => buttons.Single(b => LogicalQuery.SelfAndDescendants<TextBlock>(b).Any(label => label.Text == name));
                PixelRect Rect(Control c) { var p = c.PointToScreen(default); return new PixelRect(p.X, p.Y, (int)Math.Round(c.Bounds.Width * scale), (int)Math.Round(c.Bounds.Height * scale)); }
                return (Rect(Named("Delete")), Rect(Named("Archive")), Named("Archive").IsFocused);
            });
            Assert.True(deleteRect.Height > 0 && archiveRect.Height > 0);
            // The pixels read must be the popup's own: the window on top at the buttons must be the popup's, else what is read is the machine's, not the product's.
            var probe = new NativeWindowPoint(deleteRect.X + deleteRect.Width / 2, deleteRect.Y + deleteRect.Height / 2);
            var onTop = N.RootAt(probe);
            var popupHandle = native.OnUi(() => TopLevel.GetTopLevel(PopoverHost.Content(archive)!)!.TryGetPlatformHandle()?.Handle ?? 0);
            if (onTop != popupHandle)
            {
                _ = N.GetWindowThreadProcessId(onTop, out var owner);
                var popupRect = popupHandle == 0 ? default : N.Rect(popupHandle);
                throw new InvalidOperationException($"NATIVE_PRECONDITION: the window on top at the Delete button ({probe.X},{probe.Y}) is 0x{onTop:X} ({N.ClassName(onTop)}, process {owner}, this one {Environment.ProcessId}), not the popup's 0x{popupHandle:X} (visible {N.IsWindowVisible(popupHandle)}, at {popupRect.Left},{popupRect.Top} {popupRect.Width}x{popupRect.Height}); the main window is 0x{native.OnUi(() => window.TryGetPlatformHandle()?.Handle ?? 0):X}, the covering window is at {N.Rect(onTop).Left},{N.Rect(onTop).Top} {N.Rect(onTop).Width}x{N.Rect(onTop).Height}.");
            }
            // The card behind: the empty stretch of the buttons' row to the left of Delete.
            var margin = 10;
            var left = Math.Min(deleteRect.X, archiveRect.X) - 40;
            var top = Math.Min(deleteRect.Y, archiveRect.Y) - margin;
            var width = Math.Max(deleteRect.Right, archiveRect.Right) + margin - left;
            var height = Math.Max(deleteRect.Height, archiveRect.Height) + 2 * margin;
            var grab = ScreenGrab.Of(left, top, width, height);
            var evidence = Path.Combine(Path.GetTempPath(), $"archive-popover-native-{tag}.png");
            if (save) grab.SavePng(evidence);
            (byte R, byte G, byte B) card = grab.At(8, height / 2);
            double Distance((byte R, byte G, byte B) c) => Math.Sqrt(Math.Pow(c.R - card.R, 2) + Math.Pow(c.G - card.G, 2) + Math.Pow(c.B - card.B, 2));
            // The signed distance from a pixel to a capsule (negative inside): the button's shape.
            double Signed(PixelRect r, int x, int y)
            {
                double radius = r.Height / 2.0, cy = r.Y + radius;
                var along = Math.Clamp(x + 0.5, r.X + radius, r.Right - radius);
                return Math.Sqrt(Math.Pow(x + 0.5 - along, 2) + Math.Pow(y + 0.5 - cy, 2)) - radius;
            }
            List<double> Band(PixelRect r, double from, double to)
            {
                var found = new List<double>();
                for (var y = 0; y < height; y++)
                    for (var x = 0; x < width; x++)
                    {
                        var d = Signed(r, left + x, top + y);
                        if (d >= from && d <= to) found.Add(Distance(grab.At(x, y)));
                    }
                return found;
            }
            var deleteRing = Band(deleteRect, -3, -1);
            var archiveRing = Band(archiveRect, -3, -1);
            var archiveOuter = Band(archiveRect, 2, 5);
            var deleteOuter = Band(deleteRect, 2, 5);
            var report = $"card {card}; delete ring max {deleteRing.Max():F1} mean {deleteRing.Average():F1}; archive ring max {archiveRing.Max():F1} mean {archiveRing.Average():F1}; archive outer max {archiveOuter.Max():F1}; delete outer max {deleteOuter.Max():F1}; evidence {evidence}";
            var drawn = deleteRing.Max() >= 12 && archiveRing.Average() >= 20 && archiveRing.Average() > deleteRing.Average() + 10;
            return (drawn, report, deleteRing.Max(), deleteRing.Average(), archiveRing.Average(), archiveOuter.Max(), focused, grab, archiveRect, new PixelPoint(left,top));
        }

        // The material is composed a frame or so after the popup opens, not at the instant IsOpen turns true: read until both buttons show it, for up to 5 s.
        // The time it took is the product's: glass that pops in late is not the Mac's, so it is reported, not hidden by the wait.
        long toMaterial;
        var started = sinceOpen.ElapsedMilliseconds;
        var reading = Read(save: false);
        while (true)
        {
            if (reading.Drawn) { toMaterial = started; break; }
            if (sinceOpen.ElapsedMilliseconds >= 5000) { toMaterial = sinceOpen.ElapsedMilliseconds; break; }
            Thread.Sleep(5);
            started = sinceOpen.ElapsedMilliseconds;
            reading = Read(save: false);
        }
        reading = Read(save: true);
        Console.WriteLine($"TIME TO MATERIAL {toMaterial} ms after IsOpen was seen{(toMaterial > 300 ? " (SLOW: over 300 ms, a product question if the machine was quiet)" : "")}; {reading.Report}");
        // The same actual popup, composed screen pixels, matching geometry: focus alone must add no outer ring.
        native.OnUi(() => TopLevel.GetTopLevel(PopoverHost.Content(archive)!)!.FocusManager!.Focus(null));
        Thread.Sleep(200);
        var unfocused = Read(save: true, tag: "unfocused");
        Assert.False(unfocused.Focused, "the paired capture actually clears Archive focus");
        Assert.Equal(reading.ArchiveRect, unfocused.ArchiveRect);
        Assert.Equal(reading.Origin, unfocused.Origin);
        Assert.Equal((reading.Pixels.Width,reading.Pixels.Height),(unfocused.Pixels.Width,unfocused.Pixels.Height));
        var focusDifferences = new List<double>();
        var bounds = reading.ArchiveRect;
        for (var y = 0; y < reading.Pixels.Height; y++)
            for (var x = 0; x < reading.Pixels.Width; x++)
            {
                var sx = reading.Origin.X+x+0.5; var sy = reading.Origin.Y+y+0.5;
                var radius = bounds.Height/2.0;
                var along = Math.Clamp(sx,bounds.X+radius,bounds.Right-radius);
                var distance = Math.Sqrt(Math.Pow(sx-along,2)+Math.Pow(sy-bounds.Y-radius,2))-radius;
                if (distance < 2 || distance > 5) continue;
                var before = reading.Pixels.At(x,y); var after = unfocused.Pixels.At(x,y);
                focusDifferences.Add(Math.Sqrt(Math.Pow(before.R-after.R,2)+Math.Pow(before.G-after.G,2)+Math.Pow(before.B-after.B,2)));
            }
        var focusOuterMax = focusDifferences.Max();
        Console.WriteLine($"FOCUS PAIR: focused absolute outer {reading.ArchiveOuterMax:F3}; unfocused absolute outer {unfocused.ArchiveOuterMax:F3}; paired focus outer difference {focusOuterMax:F3}");
        Assert.True(reading.Focused, "the Archive button holds the focus (:945)");
        Assert.True(reading.DeleteRingMax >= 12, "Delete (.glass) draws no material on the popup: " + reading.Report);
        Assert.True(reading.ArchiveRingMean >= 20, "Archive (.glassProminent) draws no tinted material on the popup: " + reading.Report);
        Assert.True(reading.ArchiveRingMean > reading.DeleteRingMean + 10, "Archive is not tinted beyond Delete: " + reading.Report);
        Assert.True(focusOuterMax < 8, $"a ring or adorner changes the focused Archive outer band by {focusOuterMax:F3}: " + reading.Report);
        // Swift's .focusEffectDisabled() also applies to keyboard focus, not only the programmatic onAppear focus.
        native.OnUi(() =>
        {
            var content = PopoverHost.Content(archive)!;
            var button = LogicalQuery.SelfAndDescendants<Button>(content).Single(b => LogicalQuery.SelfAndDescendants<TextBlock>(b).Any(label => label.Text == "Archive"));
            Assert.True(button.Focus(Avalonia.Input.NavigationMethod.Tab));
        });
        Thread.Sleep(200);
        var keyboard = Read(save: true, tag: "keyboard");
        Assert.True(keyboard.Focused);
        Assert.Equal(unfocused.ArchiveRect, keyboard.ArchiveRect);
        Assert.Equal(unfocused.Origin, keyboard.Origin);
        var keyboardOuterMax = 0.0;
        for (var y = 0; y < keyboard.Pixels.Height; y++)
            for (var x = 0; x < keyboard.Pixels.Width; x++)
            {
                var sx = keyboard.Origin.X+x+0.5; var sy = keyboard.Origin.Y+y+0.5;
                var radius = bounds.Height/2.0;
                var along = Math.Clamp(sx,bounds.X+radius,bounds.Right-radius);
                var distance = Math.Sqrt(Math.Pow(sx-along,2)+Math.Pow(sy-bounds.Y-radius,2))-radius;
                if (distance < 2 || distance > 5) continue;
                var before = unfocused.Pixels.At(x,y); var after = keyboard.Pixels.At(x,y);
                keyboardOuterMax = Math.Max(keyboardOuterMax,Math.Sqrt(Math.Pow(before.R-after.R,2)+Math.Pow(before.G-after.G,2)+Math.Pow(before.B-after.B,2)));
            }
        Console.WriteLine($"KEYBOARD FOCUS PAIR: outer difference {keyboardOuterMax:F3}");
        Assert.True(keyboardOuterMax < 8, $"keyboard focus draws an outer ring with difference {keyboardOuterMax:F3}");
    }, factType: typeof(GlassWindowNativeTests));
}
