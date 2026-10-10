using Avalonia.Threading;
using Avalonia.VisualTree;
using Marrow.App.Port.FirstRun;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views;
using Marrow.App.Views.Tour;
using Marrow.Core.Port;
using Marrow.UI.Common;
using Marrow.UI.Port;

namespace Marrow.App.Tests;

[Collection("Headless")]
public sealed class ParityTailLaunchTests(HeadlessSession headless)
{
    [Theory]
    [Trait("Req", "PR-LAUNCH-071")] // AppDelegate.swift:22-28; AppModel.swift:121-147,240-287; OD-0096/OD-0098 consent is an approved addition.
    [InlineData(false)]
    [InlineData(true)]
    public void Clean_library_launch_shows_Welcome_without_account_setup_and_only_the_unseen_tour_after_700ms(bool seen)
    {
        var voice = VoiceQuestionRig.Pc(); // Offline hardware/probe fixture: no download, microphone, agent or sign-in.
        voice.Prep.Models.ThrowOnAnyTransfer = true;
        LaunchSurfaceRig.WithLaunch(headless, harness => harness.Defaults.Set("hasSeenTour", seen), launch =>
        {
            using var cursor = Marrow.App.Port.WindowCentering.Cursor.Override(new FixedCursorPlatform(null));
            using var dragCursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            launch.Delegate.FirstRun = new FirstRunPreparation(new FirstRunPreparation.Seams(() => true,
                () => [], _ => System.Threading.Tasks.Task.CompletedTask,
                (_, _, _) => System.Threading.Tasks.Task.CompletedTask), TimeProvider.System);
            launch.Manager.MainContent = () => new RootView { VoiceQuestion = voice.Question };
            launch.Delegate.PresentTour = model => { launch.Events.Add("tour"); Tour.Present(model); };
            try
            {
                launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
                var window = launch.Manager.MainWindow!;
                Assert.True(window.IsVisible);
                var root = window.GetVisualDescendants().OfType<RootView>().Single();
                Assert.Null(root.GettingReady); // prepared machine; first-run preparation is covered separately below.
                var welcome = Assert.Single(RootViewRig.All<WelcomeView>(root));
                var scroll = Assert.IsType<Avalonia.Controls.ScrollViewer>(welcome.Child);
                var padded = Assert.IsType<Avalonia.Controls.Border>(scroll.Content);
                var sections = Assert.IsType<Avalonia.Controls.StackPanel>(padded.Child);
                // RootView.swift:306-342 plus OD-0098: identity, Add project, voice consent, Activity list, Providers.
                // No extra wizard/sign-in/terms/data-location page or card replaces the source Welcome.
                Assert.Equal(5, sections.Children.Count);
                Assert.Equal("nth", RootViewRig.All<Avalonia.Controls.TextBlock>(sections.Children[0])[0].Text);
                Assert.Single(RootViewRig.All<Avalonia.Controls.Button>(sections.Children[1]));
                Assert.IsType<WelcomeVoiceSection>(sections.Children[2]);
                Assert.IsType<Marrow.UI.Chrome.ChromeSection>(sections.Children[3]);
                Assert.IsType<Marrow.UI.Chrome.ChromeSection>(sections.Children[4]);
                Assert.Single(RootViewRig.All<ProviderChecklist>(sections.Children[4]));
                Assert.Single(RootViewRig.All<WelcomeVoiceSection>(root));
                Assert.Empty(launch.Manager.Model.Projects);
                Assert.Empty(launch.Manager.Model.Threads);
                Assert.Null(launch.Manager.Model.Alert);
                Assert.DoesNotContain("settings window built", launch.Events);
                if (!seen)
                {
                    var deadline = launch.Harness.Main.Time.Elapsed + TimeSpan.FromMilliseconds(700);
                    launch.Harness.Main.Loop.RunUntil(() => launch.Harness.Main.Time.TimerDeadlines.Contains(deadline), AppModelHarness.Bound);
                }
                launch.Harness.Main.AdvanceMilliseconds(699);
                Assert.DoesNotContain("tour", launch.Events);
                launch.Harness.Main.AdvanceMilliseconds(1);
                Assert.Equal(seen ? 0 : 1, launch.Events.Count(item => item == "tour"));
                Assert.Equal(seen, launch.Manager.Model.Settings.HasSeenTour); // appearance never writes hasSeenTour.
                Assert.Null(launch.Manager.Model.Alert);
                Assert.False(voice.Prep.Preparation.HasConsent); // Welcome asks before any voice model download.
                Assert.Empty(voice.Prep.Models.Installs);
            }
            finally
            {
                TourWindowController.Shared.Close();
                if (launch.Manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
                Dispatcher.UIThread.RunJobs();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-071")] // AppDelegate.swift:24-26; OD-0096 sheet, OD-0113 hidden preparation; tour waits until preparation is clear.
    public void Unprepared_clean_library_does_not_show_the_tour_over_Getting_ready_and_releases_it_700ms_after_the_sheet()
    {
        LaunchSurfaceRig.WithLaunch(headless, null, launch =>
        {
            using var cursor = Marrow.App.Port.WindowCentering.Cursor.Override(new FixedCursorPlatform(null));
            using var dragCursor = Marrow.UI.Chrome.DragHandleView.CursorPlatform.Override(new FixedCursorPlatform(null));
            using var motion = Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Override(new FakeGeniePlatform());
            try
            {
                var frames = new ManualFrameSource();
                using var scope = FrameClock.Source.Override(frames);
                launch.Delegate.FirstRun = new FirstRunPreparation(new FirstRunPreparation.Seams(() => false,
                    () => [], _ => System.Threading.Tasks.Task.CompletedTask,
                    (_, _, _) => System.Threading.Tasks.Task.CompletedTask), TimeProvider.System);
                launch.Manager.MainContent = () => new RootView();
                launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
                var root = launch.Manager.MainWindow!.GetVisualDescendants().OfType<RootView>().Single();
                Assert.NotNull(root.GettingReady);
                launch.Harness.Main.AdvanceMilliseconds(700);
                Assert.DoesNotContain("tour", launch.Events);
                // Drive both clocks until the sheet completes, then measure the tour's separate 700 ms delay.
                for (var index = 0; index < 180 && root.GettingReady is not null; index++)
                {
                    var frame = TimeSpan.FromSeconds(1.0 / 60);
                    launch.Harness.Main.Advance(frame);
                    frames.Advance(frame);
                    launch.Harness.Main.OnMain(() => { });
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Null(root.GettingReady);
                launch.Harness.Main.OnMain(() => { });
                Assert.DoesNotContain("tour", launch.Events);
                var deadline = launch.Harness.Main.Time.Elapsed + TimeSpan.FromMilliseconds(700);
                launch.Harness.Main.Loop.RunUntil(() => launch.Harness.Main.Time.TimerDeadlines.Contains(deadline), AppModelHarness.Bound);
                launch.Harness.Main.AdvanceMilliseconds(699);
                Assert.DoesNotContain("tour", launch.Events);
                launch.Harness.Main.AdvanceMilliseconds(1);
                Assert.Equal(1, launch.Events.Count(item => item == "tour"));
                Assert.Null(launch.Manager.Model.Alert);
                Assert.False(launch.Manager.Model.Settings.HasSeenTour);
            }
            finally
            {
                if (launch.Manager.MainWindow is { } main) { main.Content = null; main.Hide(); }
                Dispatcher.UIThread.RunJobs();
            }
        });
    }
}
