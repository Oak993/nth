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
// Headless (R-TEST-7): the frame clock and SwiftClock are manual, so every time below is exact.
[Collection("Headless")]
public sealed class BootStageWindowTests(HeadlessSession headless)
{
    private const double Step = 0.05;

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
    public void Once_ready_the_main_window_appears_when_the_outline_lands_its_content_follows_the_particles_and_the_stage_closes() => headless.OnUiThread(() =>
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
            var sequence = new BootSequence(BootStageWindow.Layout(new Rect(0, 0, 900, 600)), reduceMotion: false);
            var showAt = sequence.MainWindowShownAt(readyAt);
            var closesAt = sequence.StageClosesAt(readyAt);
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
                var scale = Assert.IsType<ScaleTransform>(content.RenderTransform);
                Assert.Equal(frame.ContentScale, scale.ScaleX);
                Assert.Equal(frame.ContentScale, scale.ScaleY);
                Assert.Equal(RelativePoint.Center, content.RenderTransformOrigin);
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
    public void With_reduce_motion_the_main_window_appears_at_2_5_s_and_the_stage_closes_at_2_9_s() => headless.OnUiThread(() =>
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
            Assert.InRange(Assert.Single(shownAt), 2.5, 2.5 + Step + 1e-9);
            Assert.False(boot.Window.IsVisible);
            Assert.InRange(boot.Stage.Elapsed, 2.9, 2.9 + Step + 1e-9);
        }
        finally
        {
            boot.Window.Close();
            main.Close();
        }
    });

    [Fact]
    [Trait("Decision", "ED-103")]
    public void The_window_particles_land_on_the_title_bar_sidebar_conversation_and_composer_of_the_main_frame()
    {
        var layout = BootStageWindow.Layout(new Rect(100, 50, 900, 600));
        Assert.Equal(new Rect(100, 50, 900, 600), layout.Frame);
        Assert.Equal(14, layout.CornerRadius);
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

    private static void Advance(LaunchHarness launch, ManualFrameSource frames, double seconds)
    {
        launch.Harness.Main.Advance(TimeSpan.FromSeconds(seconds));
        frames.Advance(TimeSpan.FromSeconds(seconds));
        Dispatcher.UIThread.RunJobs();
        launch.Harness.Main.RunUntilIdle();
    }
}
