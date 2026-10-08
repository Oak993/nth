using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Testing;
using Marrow.UI.Port;
using Xunit;
using AvaloniaWindow = Avalonia.Controls.Window;

namespace Marrow.App.Tests.Port;

// PORT-ONLY src/Marrow.App/Port/BootStageWindow.cs and its use at AppDelegate.swift:22 (OD-0018, ED-103, PR-LAUNCH-028's
// deviation). Expected values come from ED-103 and the timeline of F.LoadingMark (BootSequence, ED-102, ED-154), never
// from the C# under test:
//   ED-103 (1) the stage is a transparent, borderless, topmost, taskbar-less window that takes no input
//   ED-103 (3) the mark turns until the app is ready; ED-103 (5) the main window appears when the outline has landed,
//   its content's opacity and scale following the share of particles landed, and the stage closes 0.35 s after the last
//   particle lands, leaving the content at full opacity and no scale
//   ED-103 (6) reduce motion: the still mark, the main window at 2.5 s, the stage closed at 2.9 s
//   The launch: the tour's 700 ms and About's showSettings follow the main window's appearance; "the app is ready" is
//   the end of AppModel.bootstrap() (AppDelegate.swift:27), the file note's ENGINEERING DECISION.
// OD-0113 supersedes the Comet-specific timeline/scale with locked Boot B. ED-236 now cloaks on the first stage frame, before preparation.
// Headless (R-TEST-7): the frame clock and SwiftClock are manual, so every time below is exact.
[Collection("Headless")]
public sealed class BootStageWindowTests(HeadlessSession headless)
{
    private const double Step = 0.05;

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Preparing_the_cloaked_window_waits_until_the_render_callback_returns() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = MainWindow(new Border());
        var attempts = 0;
        var boot = BootStageWindow.Start(main, false, main.Show, (_, _) => { attempts++; return false; });
        try
        {
            Dispatcher.UIThread.RunJobs();
            time.Advance(TimeSpan.FromSeconds(.05));
            frames.Advance(TimeSpan.FromSeconds(.05));
            Assert.Equal(0, attempts);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, attempts);
            Frame(time, frames, .05);
            Assert.Equal(1, attempts);
        }
        finally { boot.Window.Close(); main.Close(); }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_stage_closes_after_the_render_callback_has_returned() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = MainWindow(new Border());
        var boot = BootStageWindow.Start(main, true, main.Show);
        try
        {
            Dispatcher.UIThread.RunJobs();
            boot.Ready();
            time.Advance(TimeSpan.FromSeconds(2.1));
            frames.Advance(TimeSpan.FromSeconds(2.1));
            Assert.True(boot.Stage.Frame.StageClosed);
            Assert.True(boot.Window.IsVisible); // no HWND destruction during MediaContext.Render
            Dispatcher.UIThread.RunJobs();
            Assert.False(boot.Window.IsVisible);
            Assert.Equal(0, frames.Pending);
        }
        finally { boot.Window.Close(); main.Close(); }
    });

    private static void Frame(ManualTimeProvider time, ManualFrameSource frames, double seconds)
    {
        time.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
    }

    private static AvaloniaWindow MainWindow(Control content) => new()
    {
        Width = 900,
        Height = 600,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Position = new PixelPoint(120, 80),
        Content = content,
    };

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_stage_is_a_transparent_topmost_click_through_window_and_the_main_window_waits_for_it() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = MainWindow(new Border());
        var shows = 0;
        var boot = BootStageWindow.Start(main, reduceMotion: false, () => { shows++; main.Show(); });
        try
        {
            Dispatcher.UIThread.RunJobs();
            var stage = boot.Window;
            Assert.True(stage.IsVisible);
            Assert.False(main.IsVisible);
            Assert.Equal(WindowDecorations.None, stage.WindowDecorations);
            Assert.True(stage.Topmost);
            Assert.False(stage.ShowInTaskbar);
            Assert.False(stage.ShowActivated);
            Assert.False(stage.IsHitTestVisible);
            Assert.False(stage.Focusable);
            Assert.Equal([WindowTransparencyLevel.Transparent], stage.TransparencyLevelHint);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(stage.Background).Color);
            Assert.Same(boot.Stage, stage.Content);

            // ED-103 (3): not ready, the mark keeps turning and the main window never appears.
            for (var t = 0.0; t < 12; t += Step) Frame(time, frames, Step);
            Assert.Equal(0, shows);
            Assert.False(main.IsVisible);
            Assert.True(stage.IsVisible);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void Once_ready_B_grows_for_one_second_content_follows_its_rectangle_and_the_stage_closes_after_the_glow() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var content = new Border();
        var main = MainWindow(content);
        var shownAt = new List<double>();
        BootStageWindow? boot = null;
        boot = BootStageWindow.Start(main, reduceMotion: false, () => { shownAt.Add(boot!.Stage.Elapsed); main.Show(); });
        var raised = 0;
        boot.MainWindowShown += (_, _) => raised++;
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (var t = 0.0; t < 2; t += Step) Frame(time, frames, Step);
            var readyAt = boot.Stage.Elapsed;
            boot.Ready();
            var showAt = Math.Max(readyAt, 1.8); // locked H_()
            var closesAt = showAt + 1 + 1.2; // GROW + GLOW
            Assert.True(showAt < closesAt);

            var followed = 0;
            for (var i = 0; i < 400 && boot.Window.IsVisible; i++) // bounded: a stage that never closes fails, not hangs
            {
                Frame(time, frames, Step);
                if (shownAt.Count == 0)
                {
                    Assert.False(main.IsVisible);
                    Assert.Equal(1, content.Opacity); // untouched until the hand-over
                    continue;
                }
                if (!boot.Window.IsVisible) break;
                // ED-103 (5): the root content follows this frame's share of particles landed.
                var frame = boot.Stage.Frame;
                Assert.Equal(frame.ContentOpacity, content.Opacity);
                Assert.Null(content.RenderTransform); // the real window never scales
                if (frame.Progress < 1)
                {
                    var clip = Assert.IsType<RectangleGeometry>(content.Clip);
                    Assert.Equal(frame.ContentClip, clip.Rect);
                    Assert.Equal(frame.CornerRadius, clip.RadiusX);
                }
                else Assert.Null(content.Clip);
                followed++;
            }

            var at = Assert.Single(shownAt);
            Assert.InRange(at, showAt, showAt + Step + 1e-9);
            Assert.Equal(1, raised);
            Assert.True(main.IsVisible);
            Assert.True(followed > 3);
            Assert.False(boot.Window.IsVisible); // closed
            Assert.InRange(boot.Stage.Elapsed, closesAt, closesAt + Step + 1e-9);
            Assert.Equal(1, content.Opacity);
            Assert.Null(content.RenderTransform);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void With_reduce_motion_the_main_window_appears_at_1_3_s_and_the_stage_closes_at_1_9_s() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = MainWindow(new Border());
        var shownAt = new List<double>();
        BootStageWindow? boot = null;
        boot = BootStageWindow.Start(main, reduceMotion: true, () => { shownAt.Add(boot!.Stage.Elapsed); main.Show(); });
        try
        {
            Dispatcher.UIThread.RunJobs();
            boot.Ready();
            for (var i = 0; i < 100 && boot.Window.IsVisible; i++) Frame(time, frames, Step);
            Assert.InRange(Assert.Single(shownAt), 1.3, 1.3 + Step + 1e-9);
            Assert.False(boot.Window.IsVisible);
            Assert.InRange(boot.Stage.Elapsed, 1.9, 1.9 + Step + 1e-9);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    // ED-284 (1): the main HWND's origin lies one invisible strip (8 px at 100 %, stood in here as 12 px) left of the visible window
    // and none above it; the outline the boot hands over to is the VISIBLE window. Expected from the screen pixels alone: the stage
    // window's origin plus the outline's DIP frame at the screen's scale is the visible rect, and the visible rect is the main HWND's
    // position moved right by the strip, 900 x 600 DIP.
    [Fact]
    [Trait("Req", "PR-LAUNCH-028")] // App/AppDelegate.swift:22 (its OD-0018 deviation); App/Window/WindowManager.swift:43-54 with ED-284 (1)
    public void The_boot_outline_is_the_visible_main_window_not_its_HWND_with_the_invisible_strip() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        using var strip = WindowInvisibleBorder.Pixels.Override((_, scale) => (int)Math.Round(12 * scale));
        var main = MainWindow(new Border());
        var boot = BootStageWindow.Start(main, reduceMotion: false, () => main.Show());
        try
        {
            Dispatcher.UIThread.RunJobs();
            var scale = (main.Screens.ScreenFromWindow(main) ?? main.Screens.Primary)?.Scaling ?? 1;
            var frame = boot.OutlineLayout.Frame;
            Assert.Equal(main.Position.X + Math.Round(12 * scale), boot.Window.Position.X + frame.X * scale, 1);
            Assert.Equal(main.Position.Y, boot.Window.Position.Y + frame.Y * scale, 1);
            Assert.Equal(900, frame.Width);
            Assert.Equal(600, frame.Height);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_exact_window_layout_keeps_its_38_DIP_title_band_and_uses_the_8_DIP_Windows_corner()
    {
        var layout = BootStageWindow.Layout(new Rect(100, 50, 900, 600));
        Assert.Equal(new Rect(100, 50, 900, 600), layout.Frame);
        Assert.Equal(8, layout.CornerRadius);
        Assert.Equal(
            [
                new Rect(100, 50, 900, 38), // title bar
                new Rect(100, 88, 260, 562), // sidebar
                new Rect(360, 88, 640, 466), // conversation
                new Rect(360, 554, 640, 96), // composer
            ],
            layout.ContentAreas);
    }

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Req", "PR-LAUNCH-028")] // App/AppDelegate.swift:22 (its OD-0018 deviation)
    public void At_launch_the_main_window_waits_for_the_boot_sequence_which_waits_for_the_bootstrap() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        using var source = FrameClock.Source.Override(frames);
        using var launch = new LaunchHarness(h => h.Defaults.Set("hasSeenTour", true));
        var bootstrapped = new TaskCompletionSource();
        launch.Delegate.BootstrapModel = _ => bootstrapped.Task;
        BootStageWindow? boot = null;
        launch.Delegate.StartBoot = (windows, show) =>
            boot = BootStageWindow.Start(windows.PrepareMain(), reduceMotion: false, show);
        try
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(boot);
            var main = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(main); // made (PrepareMain), laid out under the stage
            Assert.False(main.IsVisible);

            // The bootstrap has not finished: the app is not ready, and the main window stays hidden.
            for (var t = 0.0; t < 12; t += Step) Advance(launch, frames, Step);
            Assert.False(main.IsVisible);

            launch.Harness.Main.OnMain(bootstrapped.SetResult);
            launch.Harness.Main.RunUntilIdle();
            for (var t = 0.0; t < 12 && boot.Window.IsVisible; t += Step) Advance(launch, frames, Step);
            Assert.True(main.IsVisible);
            Assert.False(boot.Window.IsVisible);
            Assert.Single(launch.Events, e => e == "main window built");
        }
        finally
        {
            boot?.Window.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Req", "PR-LAUNCH-037")] // App/AppDelegate.swift:24-26
    public void The_first_launch_tour_comes_700_ms_after_the_boot_sequence_shows_the_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness();
        Action? show = null;
        launch.Delegate.StartBoot = (_, run) =>
        {
            show = run;
            return null;
        };
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        Assert.NotNull(show);
        Assert.DoesNotContain("main window built", launch.Events);

        launch.Harness.Main.Advance(TimeSpan.FromSeconds(5)); // the stage is still up
        Assert.DoesNotContain("tour", launch.Events);

        launch.Harness.Main.OnMain(show);
        launch.Harness.Main.AdvanceMilliseconds(699);
        Assert.DoesNotContain("tour", launch.Events);
        launch.Harness.Main.AdvanceMilliseconds(1);
        Assert.Equal(["main window built", "tour"], launch.Events.Where(e => e is "main window built" or "tour"));
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    [Trait("Req", "PR-LAUNCH-047")] // App/AppDelegate.swift:30-33
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-003: About opens after the main window's appearance (the boot order)
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:29-33
    public void After_an_install_About_opens_only_once_the_boot_sequence_has_shown_the_main_window() => headless.OnUiThread(() =>
    {
        using var launch = new LaunchHarness(h => h.Defaults.Set("appUpdater_pendingRelaunchVersion", LaunchHarness.RunningVersion));
        Action? show = null;
        launch.Delegate.StartBoot = (_, run) =>
        {
            show = run;
            return null;
        };
        launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(5));
        Assert.True(launch.Harness.Main.OnMain(() => launch.Progress.HasPendingCelebration)); // :31 at once
        Assert.DoesNotContain("settings window built", launch.Events);

        launch.Harness.Main.OnMain(show!);
        launch.Harness.Main.RunUntilIdle();
        Assert.Equal(["main window built", "settings window built"], launch.Events.Where(e => e.EndsWith(" built", StringComparison.Ordinal)));
    });

    // ---- ED-236 (OD-0041, probes P13 and P14): the main window's Acrylic backdrop is ready before its content appears ----
    // Expected values: the window is shown early and cloaked when the disintegration begins (final rest + 0.1 s, ED-103
    // (4); reduce motion 0.5 s before its 2.5 s hand-over), at least 0.5 s before ED-103 (5) hands over; it is uncloaked
    // at the hand-over, with its content already at that frame's opacity, and only then does the launch show it (Present:
    // Show, Activate). Cloaking itself is a fake here; the real DWM cloak and the screen pixels are GlassWindowNativeTests.

    [Fact]
    [Trait("Decision", "ED-236")]
    public void B_cloaks_before_preparation_and_uncloaks_at_H_with_content_zero_before_the_grow() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var content = new Border();
        var main = MainWindow(content);
        var holder = new BootStageWindow[1];
        var log = new CloakLog(main, holder);
        var boot = BootStageWindow.Start(main, reduceMotion: false, () => { log.Shown(); main.Show(); }, log.Cloak);
        holder[0] = boot;
        try
        {
            Dispatcher.UIThread.RunJobs();
            // Not ready: the mark turns, and nothing of the main window is cloaked, shown or touched.
            for (var t = 0.0; t < 12; t += Step) Frame(time, frames, Step);
            Assert.Equal(["cloak"], log.Events.Select(e => e.Event));
            Assert.True(main.IsVisible); Assert.True(boot.MainIsCloaked);
            Assert.Equal(0, content.Opacity);

            var readyAt = boot.Stage.Elapsed;
            boot.Ready();
            var warmAt = Step; // first stage frame, before bootstrap/preparation
            var showAt = Math.Max(readyAt, 1.8);
            var cloakedFrames = 0;
            for (var i = 0; i < 600 && boot.Window.IsVisible; i++)
            {
                Frame(time, frames, Step);
                if (log.Events.Count > 0 && log.Events.All(e => e.Event == "cloak"))
                {
                    // cloaked and shown, not yet handed over: the content follows the stage
                    Assert.True(main.IsVisible);
                    Assert.Equal(boot.Stage.Frame.ContentOpacity, content.Opacity);
                    Assert.True(boot.MainIsCloaked);
                    cloakedFrames++;
                }
            }

            Assert.Equal(["cloak", "uncloak", "show"], log.Events.Select(e => e.Event));
            var (cloak, uncloak, show) = (log.Events[0], log.Events[1], log.Events[2]);
            // cloaked before it was ever shown, and shown without taking the foreground
            Assert.False(cloak.Visible);
            Assert.False(cloak.ShowActivated);
            Assert.InRange(cloak.Elapsed, warmAt, warmAt + Step + 1e-9);
            Assert.True(main.ShowActivated); // restored: the hand-over activates it as before
            // the lead the backdrop gets: well over the ~0.35 s P13 saw the blur take
            Assert.True(uncloak.Elapsed - cloak.Elapsed >= 0.5, $"lead {uncloak.Elapsed - cloak.Elapsed}");
            Assert.True(uncloak.Elapsed - cloak.Elapsed >= 12);
            // uncloaked at the hand-over, with the content already at the hand-over frame's opacity, before the show runs
            Assert.InRange(uncloak.Elapsed, showAt, showAt + Step + 1e-9);
            Assert.True(uncloak.Visible);
            Assert.Equal(0, uncloak.ContentOpacity); // p below .35 on the first sampled grow frame
            Assert.Equal(uncloak.Elapsed, show.Elapsed);
            Assert.Equal(uncloak.ContentOpacity, show.ContentOpacity);
            Assert.False(boot.MainIsCloaked);
            Assert.Equal(1, content.Opacity); // the stage closed
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-236")]
    public void With_reduce_motion_B_cloaks_before_preparation_and_reveals_at_the_1_2_second_floor_plus_100_ms() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var main = MainWindow(new Border());
        var holder = new BootStageWindow[1];
        var log = new CloakLog(main, holder);
        var boot = BootStageWindow.Start(main, reduceMotion: true, () => { log.Shown(); main.Show(); }, log.Cloak);
        holder[0] = boot;
        try
        {
            Dispatcher.UIThread.RunJobs();
            boot.Ready();
            for (var i = 0; i < 100 && boot.Window.IsVisible; i++) Frame(time, frames, Step);
            Assert.Equal(["cloak", "uncloak", "show"], log.Events.Select(e => e.Event));
            Assert.False(log.Events[0].Visible);
            Assert.False(log.Events[0].ShowActivated);
            Assert.InRange(log.Events[0].Elapsed, Step, Step + 1e-9); // ED-236: prepare on the first B frame.
            Assert.InRange(log.Events[1].Elapsed, 1.3, 1.3 + Step + 1e-9);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-236")]
    public void A_main_window_that_cannot_be_cloaked_is_not_shown_early_and_the_hand_over_is_the_plain_show() => headless.OnUiThread(() =>
    {
        var frames = new ManualFrameSource();
        var time = new ManualTimeProvider();
        using var source = FrameClock.Source.Override(frames);
        using var clock = SwiftClock.Provider.Override(time);
        var content = new Border();
        var main = MainWindow(content);
        var holder = new BootStageWindow[1];
        var log = new CloakLog(main, holder) { Accepts = false };
        var visibleAtShow = new List<bool>();
        var boot = BootStageWindow.Start(main, reduceMotion: false, () => { visibleAtShow.Add(main.IsVisible); log.Shown(); main.Show(); }, log.Cloak);
        holder[0] = boot;
        try
        {
            Dispatcher.UIThread.RunJobs();
            for (var t = 0.0; t < 2; t += Step) Frame(time, frames, Step);
            boot.Ready();
            for (var i = 0; i < 600 && boot.Window.IsVisible; i++)
            {
                Frame(time, frames, Step);
                if (visibleAtShow.Count == 0) Assert.False(main.IsVisible); // never shown early
            }
            Assert.Equal(["cloak", "show"], log.Events.Select(e => e.Event)); // asked once, refused, never uncloaked
            Assert.Equal([false], visibleAtShow);
            Assert.False(boot.MainIsCloaked);
            Assert.Equal(1, content.Opacity);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-236")]
    public void A_window_without_a_win32_handle_cannot_be_cloaked_and_is_left_as_it_was() => headless.OnUiThread(() =>
    {
        var window = MainWindow(new Border());
        try
        {
            Assert.False(WindowCloak.Set(window, true));
            Assert.False(WindowCloak.Set(window, false));
            Assert.False(window.IsVisible);
        }
        finally
        {
            window.Close();
        }
    });

    private static void Advance(LaunchHarness launch, ManualFrameSource frames, double seconds)
    {
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.RunUntilIdle();
    }
}
