using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;
using static Marrow.App.Tests.Fixtures.ReleaseFeed;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), the blitz closing pass of the launch records: proofs of clauses
// the earlier suites (AppDelegateTests, AppDelegateSecondTests, AppDelegateThirdTests, AppDelegateProofTests) left to
// other units' tests, or left unasserted. Every expected value is transcribed from the Swift line cited on the test
// (OD-0098: where a record asks for a capture the reference videos do not hold, the pinned source is the proof).
//   :22 windows.showMain(); WindowManager.swift:18-24, :26-30, :57-60, :62-72 (title, 1320 x 860, minimum 860 x 560)
//   :24-26, :27, :28-33 the rest of the launch sequence (AppModel.swift:121-147, :240-287: no onboarding state)
//   :28 UpdateChecker.swift:132-149 startBackgroundChecks(): guard backgroundTask == nil; the loop outlives a failed check
//   :36-39 applicationShouldHandleReopen -> showMain(): the cached window, its content intact (WindowManager.swift:26-30)
//   :29-33 the About window opened by the relaunch marker (WindowManager.swift:21, :32-38, :46-49: 780 x 580, over main)
[Collection("Headless")]
public sealed class AppDelegateFourthTests(HeadlessSession headless)
{
    private void WithLaunch(Action<AppModelHarness>? prepare, Action<LaunchHarness> body) => headless.OnUiThread(() =>
    {
        var page = SettingsNavigation.Shared.RequestedPage;
        using var launch = new LaunchHarness(prepare);
        try
        {
            body(launch);
        }
        finally
        {
            launch.Harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = page);
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static AppInfo Running(string version = "1.0.0", bool isDevelopment = false) =>
        ReleaseFeed.Running(version, isDevelopment);

    private static TaskHandle? BackgroundTaskOf(UpdateChecker checker) =>
        typeof(UpdateChecker).GetField("backgroundTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(checker) as TaskHandle;

    // ===== :22 windows.showMain() =====

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-028: what launch does to the window; its centring is WindowManagerCenteringTests (ED-225), its restart restore WindowManagerRestartTests
    [Trait("Req", "PR-LAUNCH-028")] // App/AppDelegate.swift:22; App/Window/WindowManager.swift:18-24, :26-30, :57-60, :62-72
    public void Launch_shows_the_main_window_made_on_first_use_titled_sized_and_in_front_and_a_second_show_reuses_it()
    {
        WithLaunch(h => h.Defaults.Set("hasSeenTour", true), launch =>
        {
            // OD-0002: the Windows build's CFBundleDisplayName is "Marrow" (AppInfo.name, Text.swift:4, is the title, :63).
            using var name = AppInfo.Running.Override(AppInfo.Read(new Dictionary<string, object?>
            {
                ["CFBundleDisplayName"] = "Marrow",
                ["CFBundleShortVersionString"] = LaunchHarness.RunningVersion,
            }, isDevelopment: false));
            Assert.Null(launch.Manager.MainWindow); // created on first use, not before

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Dispatcher.UIThread.RunJobs();

            var window = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(window);
            Assert.Same(window, launch.Manager.MainWindow);
            Assert.Equal("Marrow", window.Title); // :63
            Assert.Equal(new Size(1320, 860), window.ClientSize); // :19 Size.main
            Assert.Equal(860d, window.MinWidth); // :20, :66 contentMinSize
            Assert.Equal(560d, window.MinHeight);
            Assert.True(window.IsVisible); // :59 makeKeyAndOrderFront
            Assert.True(window.IsActive); // :58 NSApp.activate(), :59 key

            launch.Harness.Main.OnMain(launch.Manager.ShowMain); // a second showMain() reuses the same window (:27)
            Dispatcher.UIThread.RunJobs();
            Assert.Same(window, launch.Manager.MainWindow);
            Assert.Single(launch.Events, e => e == "main window built");
        });
    }

    // ===== :22-33 the whole launch sequence, on a clean data directory =====

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-071 (the clean-data capture of the real exe is porting/verification/native/launch-2026-10-02/RESULT.md)
    [Trait("Req", "PR-LAUNCH-071")] // App/AppDelegate.swift:22-33; App/AppModel.swift:121-147, :240-287
    public void A_launch_on_clean_data_opens_the_chat_then_only_the_700_ms_tour_and_asks_for_no_account_terms_or_folder()
    {
        WithLaunch(null, launch =>
        {
            Assert.Null(launch.Harness.Defaults.Object("hasSeenTour")); // a first launch: the setting is absent (AppSettings.swift:178)

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            // :22 the main window, :27 the bootstrap, and nothing else is asked or shown at launch.
            Assert.Equal(["main window built", "bootstrap"], launch.Events);
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert)); // no terms, no sign-in, no folder alert
            Assert.Empty(launch.Harness.Main.OnMain(() => launch.Manager.Model.Threads.ToList())); // nothing on disk to open

            launch.Harness.Main.AdvanceMilliseconds(699);
            Assert.Equal(["main window built", "bootstrap"], launch.Events);
            launch.Harness.Main.AdvanceMilliseconds(1); // :25 Task.sleep(for: .milliseconds(700))
            Assert.Equal(["main window built", "bootstrap", "tour"], launch.Events);

            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));
            Assert.Equal(["main window built", "bootstrap", "tour"], launch.Events); // the tour is the only extra, and once
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert));
            Assert.Null(launch.Harness.Defaults.Object("hasSeenTour")); // written only when the tour is dismissed (TourPages.swift:26-28)
            Assert.Null(launch.Harness.Defaults.Object("appUpdater_pendingRelaunchVersion")); // :30 no relaunch: no Settings
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-002: a stored false presents the tour like an absent value; the 700 ms and the seen case are AppDelegateThirdTests, the writes are TourPagesTests
    [Trait("Req", "UI-LAUNCH-002")] // App/AppDelegate.swift:24-26 `if !windows.model.settings.hasSeenTour`; App/AppSettings.swift:178
    public void A_stored_false_for_hasSeenTour_presents_the_tour_once_700_ms_after_launch_like_an_absent_value()
    {
        WithLaunch(h => h.Defaults.Set("hasSeenTour", false), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.AdvanceMilliseconds(699);
            Assert.Empty(launch.TourModels);
            launch.Harness.Main.AdvanceMilliseconds(1);
            Assert.Same(launch.Manager.Model, Assert.Single(launch.TourModels));
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));
            Assert.Single(launch.TourModels);
        });
    }

    // ===== :28 UpdateChecker.shared.startBackgroundChecks() =====

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: one schedule only; the delays are UpdateCheckerSecondTests (PR-UPDATE-007/-008/-009/-010)
    [Trait("Req", "PR-LAUNCH-046")] // App/AppDelegate.swift:28; App/UpdateChecker.swift:133 guard backgroundTask == nil
    public void A_second_call_of_the_background_check_start_is_a_no_op_while_the_task_exists()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed()));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        try
        {
            runtime.OnMain(checker.StartBackgroundChecks);
            var first = BackgroundTaskOf(checker);
            Assert.NotNull(first);

            runtime.OnMain(checker.StartBackgroundChecks);

            Assert.Same(first, BackgroundTaskOf(checker)); // exactly one background task: the second call changed nothing
            runtime.Advance(TimeSpan.FromSeconds(8));
            runtime.Loop.RunUntil(() => checker.LastCheckedAt is not null, TimeSpan.FromSeconds(20));
            Assert.Single(server.Requests);
            Assert.Same(first, BackgroundTaskOf(checker));
        }
        finally
        {
            UpdateCheckerLoop.Stop(checker);
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: a failed check does not end the schedule
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:136-139 `while !Task.isCancelled { await self?.check(background: true); sleep }`
    public void A_failed_scheduled_check_does_not_stop_the_loop_and_the_next_one_runs_six_hours_later()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Plain(500, "Status"));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(), [], server.Url("/releases")));
        try
        {
            runtime.OnMain(checker.StartBackgroundChecks);
            runtime.Advance(TimeSpan.FromSeconds(8)); // :135 launchDelay
            runtime.Loop.RunUntil(() => checker.LastError is not null, TimeSpan.FromSeconds(20)); // the check ended in failure
            Assert.Single(server.Requests);
            Assert.Equal("GitLab answered with HTTP 500.", runtime.OnMain(() => checker.LastError));

            runtime.Advance(TimeSpan.FromHours(6) - TimeSpan.FromMilliseconds(1)); // :74 checkInterval
            Assert.Single(server.Requests);
            runtime.Advance(TimeSpan.FromMilliseconds(1));
            runtime.Loop.RunUntil(() => server.Requests.Count >= 2, TimeSpan.FromSeconds(20));
            Assert.Equal(2, server.Requests.Count); // the loop went on after the failure
        }
        finally
        {
            UpdateCheckerLoop.Stop(checker);
        }
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: a development build starts nothing, the activation observer included
    [Trait("Req", "PR-LAUNCH-046")] // App/UpdateChecker.swift:133 !AppInfo.isDevelopment guards the task and the observer alike (:134-149)
    public void A_development_build_starts_no_schedule_and_installs_no_activation_observer()
    {
        using var runtime = new TestRuntime();
        using var server = new LoopbackFeedServer(_ => CannedResponse.Json(Feed()));
        var checker = runtime.OnMain(() => new UpdateChecker(Running(isDevelopment: true), [], server.Url("/releases")));

        runtime.OnMain(checker.StartBackgroundChecks);

        Assert.Null(BackgroundTaskOf(checker));
        Assert.Null(runtime.OnMain(checker.HandleActivation)); // never checked, so an observer would spawn one: none is installed
        runtime.Advance(TimeSpan.FromHours(13));
        Assert.Empty(server.Requests);
    }

    // ===== :36-39 applicationShouldHandleReopen(_:hasVisibleWindows:) =====

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: the cached window with its content intact; the minimised restore and the second launch are AppDelegateThirdTests and AppShellSecondTests
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; App/Window/WindowManager.swift:26-30 `mainWindow ?? makeMainWindow()`
    public void A_reopen_with_no_visible_window_brings_back_the_same_cached_main_window_with_its_content_intact()
    {
        WithLaunch(h => h.Defaults.Set("hasSeenTour", true), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Dispatcher.UIThread.RunJobs();
            var window = launch.Manager.MainWindow;
            Assert.NotNull(window);
            var content = window.Content;
            Assert.NotNull(content);
            Assert.Same(window, LaunchHarness.WindowOf(launch.MainContent)); // the stand-in content sits in this window

            launch.Harness.Main.OnMain(window.Hide); // every window closed (the Dock case)
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);

            var answer = launch.Harness.Main.OnMain(() => launch.Delegate.ApplicationShouldHandleReopen(hasVisibleWindows: false));
            Dispatcher.UIThread.RunJobs();

            Assert.True(answer); // :38
            Assert.Same(window, launch.Manager.MainWindow); // the cached instance, not a new window
            Assert.Same(content, window.Content); // its content intact: nothing was rebuilt
            Assert.Same(window, LaunchHarness.WindowOf(launch.MainContent));
            Assert.True(window.IsVisible);
            Assert.True(window.IsActive);
            Assert.Single(launch.Events, e => e == "main window built");
        });
    }

    // ===== :29-33 the About window after an installed update =====

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-003: the window's own geometry and page; the states are AppDelegateThirdTests, the update tap WindowsToastRoutingTests
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:29-33; App/Window/WindowManager.swift:21, :32-38, :46-49
    public void The_Settings_window_opened_after_an_install_is_780_by_580_on_About_and_centred_over_the_visible_main_window()
    {
        WithLaunch(h =>
        {
            h.Defaults.Set("hasSeenTour", true);
            h.Defaults.Set("appUpdater_pendingRelaunchVersion", LaunchHarness.RunningVersion);
        }, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Dispatcher.UIThread.RunJobs();

            var main = launch.Manager.MainWindow;
            Assert.NotNull(main);
            var settings = LaunchHarness.WindowOf(launch.SettingsContent);
            Assert.NotNull(settings);
            Assert.True(settings.IsVisible);
            Assert.Equal("Settings", settings.Title); // :80-81
            Assert.Equal(new Size(780, 580), settings.ClientSize); // :21 Size.settings
            Assert.Equal(SettingsPage.About, launch.Harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage)); // :32, WindowManager :33
            // :46-49 the origin is the main window's middle less half the settings size; the headless screen (1920 x 1280)
            // holds both, so :50-53's clamp leaves it where it is.
            Assert.Equal(new PixelPoint(main.Position.X + (1320 - 780) / 2, main.Position.Y + (860 - 580) / 2), settings.Position);
        });
    }
}
