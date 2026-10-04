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

    // OD-0075 (owner, 2026-10-02: "the wedge is filled with the window's scrim"), ED-236 UNRESOLVED (a): the production Settings window
    // (the real SettingsView, WindowManager.swift:81) over the stripes. The window is a rectangle of the system's Acrylic that DWM rounds by
    // 8 px, and its content draws a 26 pt shape (SettingsView.swift:118-122); the corner between them is the wedge. A block of the screen
    // just inside each corner of the window, outside the shape, must show the blurred stripes under the window's scrim, not the bare blur
    // (which read luma 126 beside 93 inside the shape).
    [Fact]
    [Trait("Decision", "ED-236")]
    [Trait("Req", "UI-SETTINGS-001")] // SettingsView.swift:118-122 with OD-0075
    public void The_real_Settings_window_corners_outside_the_26_point_shape_wear_the_scrim_over_the_blur_in_both_themes() => native.Run(subject =>
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

        var wedge = native.OnUi(() => ((Marrow.App.Views.Settings.SettingsView)typeof(WindowManager).GetField("settingsView", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(manager)!).RiseWedge);
        foreach (var (theme, dark) in new[] { (Marrow.UI.Theme.AppTheme.Dark, true), (Marrow.UI.Theme.AppTheme.Light, false) })
        {
            var opacity = native.OnUi(() => { manager.Model.Settings.Theme = theme; return manager.Model.Settings.BackdropOpacity; });
            Thread.Sleep(900); // the window's colours ease 0.32 s (OD-0088); the stripes behind are blurred by the system
            var handle = native.OnUi(() => settings.TryGetPlatformHandle()!.Handle);
            var visible = native.OnUi(() => WindowManagerNativePlacementGeometry.ReadVisibleBounds(handle)).VisibleBounds!;
            var alpha = Marrow.UI.Chrome.WindowCornerWedge.Alpha(opacity, dark);
            Assert.InRange(alpha, 0.05, 0.95); // glass under a scrim, not a solid window
            // a block of 4 x 4 pixels 4 px in from each corner of the window: inside DWM's 8 px rounding, outside the 26 pt shape
            var corners = new[]
            {
                (visible.Left + 4, visible.Top + 4, "top left"), (visible.Right - 8, visible.Top + 4, "top right"),
                (visible.Left + 4, visible.Bottom - 8, "bottom left"), (visible.Right - 8, visible.Bottom - 8, "bottom right"),
            };
            // what the system shows there without the wedge (the control: the wedge's layer hidden), then with it
            native.OnUi(() => wedge.Opacity = 0);
            Thread.Sleep(400);
            var without = corners.Select(corner => Grab(new PixelPoint(corner.Item1, corner.Item2), 4, 4)).ToArray();
            native.OnUi(() => wedge.Opacity = 1);
            Thread.Sleep(400);
            for (var index = 0; index < corners.Length; index++)
            {
                var with = Grab(new PixelPoint(corners[index].Item1, corners[index].Item2), 4, 4);
                var name = corners[index].Item3;
                Assert.True(without[index].StripeContrast() < Blurred && with.StripeContrast() < Blurred,
                    $"{theme} {name}: contrast {without[index].StripeContrast():F2} without and {with.StripeContrast():F2} with the wedge, the stripes show sharp in the corner");
                // the wedge is the window's scrim over what the corner showed: the setting's opacity and the theme (Chrome.swift:211-215)
                var expected = without[index].MeanLuma() * (1 - alpha) + (dark ? 0 : 255) * alpha;
                Assert.True(Math.Abs(with.MeanLuma() - expected) <= 5, $"{theme} {name}: luma {with.MeanLuma():F1} with the wedge, {without[index].MeanLuma():F1} without it, the scrim (alpha {alpha:F2}) over it is {expected:F1}");
                Assert.True(Math.Abs(with.MeanLuma() - without[index].MeanLuma()) > 12, $"{theme} {name}: the wedge changes the corner by {with.MeanLuma() - without[index].MeanLuma():F1} only");
            }
        }
        GC.KeepAlive(stripes);
    }, factType: typeof(GlassWindowNativeTests));

    // The archive question popover (UI-SIDEBAR-031, -032), opened from a real thread row of the real main window as the archive shortcut asks for it
    // (AppModel.AppModelArchiveRequest, SidebarView.swift:198-202), in a real popup window. Headless cannot open a Popup, so only the glass's metadata was
    // ever asserted; here the pixels of the real popup are read. The glass material (OD-0005) draws a rim light and an inner shade at the edge of its shape
    // and tints the prominent one, so the ring just inside each button's capsule differs from the flat card behind it where the material is drawn and not
    // where it falls out of the composition tree (review/glass-material-placement 090e8b843). SwiftUI's `.focusEffectDisabled`-like look (:945) is that no
    // ring surrounds the focused Archive button: the band just outside its capsule is the card.
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
        var popup = native.OnUi(() =>
        {
            var column = window.GetVisualDescendants().OfType<Marrow.App.Views.Sidebar.SidebarView>().Single();
            model.AppModelArchiveRequest = new AppModel.ArchiveRequest(thread);
            return column;
        });
        Popup archive = null!;
        WaitFor(() => native.OnUi(() =>
        {
            archive = LogicalQuery.SelfAndDescendants<Popup>(popup).FirstOrDefault(p => PopoverHost.Content(p) is Marrow.App.Views.Sidebar.ArchiveThreadPopover)!;
            return archive is not null && archive.IsOpen;
        }), "The archive question never opened on the thread's row.", 15000);
        Thread.Sleep(1500); // the popover's own appearance has finished and the glass has been composed
        var (deleteRect, archiveRect, focused) = native.OnUi(() =>
        {
            var content = PopoverHost.Content(archive)!;
            var scale = TopLevel.GetTopLevel(content)!.RenderScaling;
            var buttons = LogicalQuery.SelfAndDescendants<Button>(content).ToList();
            Button Named(string name) => buttons.Single(b => LogicalQuery.SelfAndDescendants<TextBlock>(b).Any(label => label.Text == name));
            PixelRect Rect(Control c) { var p = c.PointToScreen(default); return new PixelRect(p.X, p.Y, (int)Math.Round(c.Bounds.Width * scale), (int)Math.Round(c.Bounds.Height * scale)); }
            return (Rect(Named("Delete")), Rect(Named("Archive")), Named("Archive").IsFocused);
        });
        Assert.True(focused, "the Archive button holds the focus (:945)");
        Assert.True(deleteRect.Height > 0 && archiveRect.Height > 0);
        // The card behind: the empty stretch of the buttons' row to the left of Delete.
        var margin = 10;
        var left = Math.Min(deleteRect.X, archiveRect.X) - 40;
        var top = Math.Min(deleteRect.Y, archiveRect.Y) - margin;
        var width = Math.Max(deleteRect.Right, archiveRect.Right) + margin - left;
        var height = Math.Max(deleteRect.Height, archiveRect.Height) + 2 * margin;
        var grab = ScreenGrab.Of(left, top, width, height);
        var evidence = Path.Combine(Path.GetTempPath(), "archive-popover-native.png");
        grab.SavePng(evidence);
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
        Console.WriteLine(report);
        Assert.True(deleteRing.Max() >= 12, "Delete (.glass) draws no material on the popup: " + report);
        Assert.True(archiveRing.Average() >= 20, "Archive (.glassProminent) draws no tinted material on the popup: " + report);
        Assert.True(archiveRing.Average() > deleteRing.Average() + 10, "Archive is not tinted beyond Delete: " + report);
        Assert.True(archiveOuter.Max() < 8, "a ring or adorner surrounds the focused Archive button: " + report);
    }, factType: typeof(GlassWindowNativeTests));
}
