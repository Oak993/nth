using Avalonia.Controls;
using Avalonia.Threading;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.UI.Markdown;
using Xunit;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppDelegate.swift (B3 52bc792), :9-34 applicationDidFinishLaunching(_:) and :36-39
// applicationShouldHandleReopen(_:hasVisibleWindows:), written by the Boot unit now that App.Window.WindowManager is in
// the tree (it closes STOP App.AppDelegate #1 and #2 of AppDelegateTests.cs, except the lines named there that stay
// open). Every expected value is transcribed from the cited Swift line or from the record, never from the C#:
//   :13 URLCache.shared is reconfigured to 8 MiB memory-only, before :14 and :16
//   :14 MarkdownDelegationCard.render is set before :16 reads WindowManager.shared
//   :16-17 the model is WindowManager.shared.model; :22 showMain() before :23 openThreadOnLaunch()
//   :24-26 hasSeenTour false: a main-actor task sleeps 700 ms, then Tour.present(model:); true: nothing
//   :27 Task { await windows.model.bootstrap() }: started, not awaited
//   :28 UpdateChecker.shared.startBackgroundChecks()
//   :30-33 consumeRelaunchMarker() true: armCelebration(), then showSettings(page: .about)
//   :36-39 no visible window: showMain(); either way the answer is true
// Headless (R-TEST-7): launch shows real windows. The tour and the bootstrap are replaced by recorders on the delegate
// (AppDelegate.PresentTour, AppDelegate.BootstrapModel): the tour window is a process-wide singleton
// (TourWindowController.shared) and the bootstrap starts providers and git; both are tested in their own units.
[Collection("Headless")]
public sealed class AppDelegateThirdTests(HeadlessSession headless)
{
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion"; // App/AppUpdater.swift:242
    private const string HasSeenTourKey = "hasSeenTour"; // App/AppSettings.swift:178

    // One project with one unused thread (Library.swift:204-226 shapes), the project remembered as the last one, so
    // openThreadOnLaunch() selects that thread (AppModel.swift:574-598).
    private static Guid WriteProjectWithUnusedThread(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(project.Id));
        return thread.Id;
    }

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

    // ===== applicationDidFinishLaunching(_:) :9-34 =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-029")] // App/AppDelegate.swift:23; App/AppModel.swift:580-590
    public void Launch_reuses_the_newest_unused_thread_and_excludes_each_of_the_seven_disqualifying_states()
    {
        Guid newest = default;
        var count = 0;
        WithLaunch(h =>
        {
            var project = AppModelSamples.Project("Last project", h.NewFolder("last"));
            var other = AppModelSamples.Project("Other", h.NewFolder("other"));
            var now = SwiftDate.Now;
            var older = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-3) };
            var unused = AppModelSamples.Thread(project) with { CreatedAt = now.AddHours(-2) };
            newest = unused.Id;
            ChatThread[] threads =
            [
                older, unused,
                AppModelSamples.Thread(other) with { CreatedAt = now },
                AppModelSamples.Thread(project) with { CreatedAt = now, IsArchived = true },
                AppModelSamples.Thread(project) with { CreatedAt = now, IsSettled = true },
                AppModelSamples.Thread(project) with { CreatedAt = now, ParentThreadID = older.Id },
                AppModelSamples.Thread(project) with { CreatedAt = now, LastStatus = TurnStatus.Completed },
                AppModelSamples.Thread(project) with { CreatedAt = now, HasCustomTitle = true },
                AppModelSamples.Thread(project) with { CreatedAt = now, Title = "Named chat" },
            ];
            count = threads.Length;
            h.WriteLibrary([project, other], threads);
            h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(project.Id));
        }, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Assert.Equal(newest, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID));
            Assert.Equal(count, launch.Harness.Main.OnMain(() => launch.Manager.Model.Threads.Count));
            Assert.Null(launch.SelectedWhenMainWindowBuilt); // :22 showMain precedes :23 selection
        });
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // Today's sequence only; no requirement is claimed
    // NOT a proof of PR-LAUNCH-071 (Req withdrawn, STOP App.AppDelegate #7 in AppDelegateProofTests): App/AppDelegate.swift:22-33 as it
    // is today; OD-0096's first-run "Getting ready" sheet and the Welcome voice question are not built.
    public void First_launch_opens_the_chat_then_only_the_delayed_tour_with_no_onboarding_alert()
    {
        WithLaunch(h => WriteProjectWithUnusedThread(h), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Assert.NotNull(launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID));
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert));
            Assert.Equal(["main window built", "bootstrap"], launch.Events);
            launch.Harness.Main.AdvanceMilliseconds(700); // :25
            Assert.Equal(["main window built", "bootstrap", "tour"], launch.Events);
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert));
            Assert.DoesNotContain("settings window built", launch.Events); // :30 no relaunch marker
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-026")] // App/AppDelegate.swift:7, :16-17
    public void Launch_takes_the_window_managers_model_then_shows_the_main_window_before_opening_the_launch_thread()
    {
        Guid unused = default;
        WithLaunch(h => unused = WriteProjectWithUnusedThread(h), launch =>
        {
            Assert.Null(launch.Delegate.Model); // :7 nil until :17

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            Assert.Same(launch.Manager.Model, launch.Delegate.Model); // :16-17 the one model WindowManager owns
            // :22 before :23: the main window's content was built while no thread was selected yet
            Assert.Equal("main window built", launch.Events[0]);
            Assert.Null(launch.SelectedWhenMainWindowBuilt);
            var window = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(window);
            Assert.True(window.IsVisible);
            Assert.Equal(unused, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID)); // :23
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-083")] // App/AppDelegate.swift:10-13
    [Trait("Req", "PR-LAUNCH-025")] // App/AppDelegate.swift:10-13, now the first statement
    public void Launch_reconfigures_the_shared_URLCache_to_8_MiB_memory_only_before_it_reaches_the_window_manager()
    {
        WithLaunch(null, launch =>
        {
            var before = URLCache.Shared;

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            // :13 URLCache.shared = URLCache(memoryCapacity: 8 * 1024 * 1024, diskCapacity: 0), set before :16.
            Assert.NotSame(before, launch.UrlCacheWhenMainWindowBuilt);
            Assert.NotNull(launch.UrlCacheWhenMainWindowBuilt);
            Assert.Equal(8 * 1024 * 1024, launch.UrlCacheWhenMainWindowBuilt!.MemoryCapacity);
            Assert.Equal(0, launch.UrlCacheWhenMainWindowBuilt!.DiskCapacity);
            Assert.Same(launch.UrlCacheWhenMainWindowBuilt, URLCache.Shared); // still the shared instance after launch
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-026")] // App/AppDelegate.swift:14, :16
    public void Launch_installs_the_delegation_block_renderer_before_it_reaches_the_window_manager()
    {
        WithLaunch(null, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            // :14 MarkdownDelegationCard.render = { AnyView(HydraDelegationBlock(json: $0, sent: $1)) }, set before :16.
            // Registration-only check here; AppDelegateProofTests exercises the now-landed renderer and its card.
            Assert.NotNull(launch.DelegationBlockWhenMainWindowBuilt);
            Assert.Same(launch.DelegationBlockWhenMainWindowBuilt, MarkdownDelegationCard.Render);
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-070")] // App/AppDelegate.swift:24-26
    [Trait("Req", "PR-LAUNCH-037")] // App/AppDelegate.swift:24-26
    [Trait("Req", "PR-UPDATE-090")] // App/AppDelegate.swift:22-26; App/AppSettings.swift:178
    [Trait("Req", "UI-LAUNCH-002")] // App/AppDelegate.swift:24-26; the clean-data native capture: porting/verification/native/launch-2026-10-02
    public void On_a_first_launch_the_tour_is_presented_exactly_700_ms_after_the_main_window()
    {
        WithLaunch(null, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            Assert.DoesNotContain("tour", launch.Events); // :25 not synchronous

            launch.Harness.Main.AdvanceMilliseconds(699);
            Assert.DoesNotContain("tour", launch.Events);
            launch.Harness.Main.AdvanceMilliseconds(1); // :25 Task.sleep(for: .milliseconds(700))

            Assert.Equal(["main window built", "tour"], launch.Events.Where(e => e is "main window built" or "tour"));
            Assert.Same(launch.Manager.Model, Assert.Single(launch.TourModels)); // Tour.present(model: windows.model)
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(5));
            Assert.Single(launch.TourModels); // once
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-070")] // App/AppDelegate.swift:24
    [Trait("Req", "PR-LAUNCH-037")] // App/AppDelegate.swift:24
    [Trait("Req", "PR-UPDATE-090")] // App/AppDelegate.swift:24; App/AppSettings.swift:178
    [Trait("Req", "UI-LAUNCH-002")] // App/AppDelegate.swift:24
    public void When_the_tour_has_been_seen_launch_never_presents_it()
    {
        WithLaunch(h => h.Defaults.Set(HasSeenTourKey, true), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(5));

            Assert.Empty(launch.TourModels);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-026")] // App/AppDelegate.swift:27
    public void Launch_starts_the_bootstrap_on_the_window_managers_model_without_waiting_for_it()
    {
        WithLaunch(null, launch =>
        {
            var startedDuringLaunch = true;
            launch.Harness.Main.OnMain(() =>
            {
                launch.Delegate.ApplicationDidFinishLaunching();
                startedDuringLaunch = launch.Events.Contains("bootstrap"); // :27 an unstructured Task, not awaited
            });

            Assert.False(startedDuringLaunch);
            Assert.Same(launch.Manager.Model, Assert.Single(launch.BootstrapModels));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-026")] // App/AppDelegate.swift:28; App/UpdateChecker.swift:132-149
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-LAUNCH-046: launch calls startBackgroundChecks (:28); the 8 s, 6 h and stale rules are UpdateCheckerSecondTests, the shell binding AppShellActivationTests
    [Trait("Req", "PR-LAUNCH-046")] // App/AppDelegate.swift:28; App/UpdateChecker.swift:132-149
    public void Launch_starts_the_background_update_checks()
    {
        WithLaunch(null, launch =>
        {
            // Before launch the activation observer is not installed, so an activation spawns nothing (:141-149).
            Assert.Null(launch.Harness.Main.OnMain(launch.Checker.HandleActivation));

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            // :28 startBackgroundChecks() installed it: never checked yet, so an activation is stale and checks
            // (OD-0012 I1: the empty feed makes no request).
            Assert.NotNull(launch.Harness.Main.OnMain(launch.Checker.HandleActivation));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-047")] // App/AppDelegate.swift:30-33; App/AppUpdater.swift:254-259
    [Trait("Req", "PR-WIN-071")] // App/AppDelegate.swift:30-33
    [Trait("Req", "PR-UPDATE-059")] // App/AppDelegate.swift:22-33
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-003: the matching marker arms the celebration and opens Settings on About after the main window; where it opens is AppDelegateFourthTests
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:29-33; App/AppUpdater.swift:254-259
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window
    public void The_first_launch_after_an_install_arms_the_celebration_and_opens_Settings_on_About_after_the_main_window()
    {
        WithLaunch(h => h.Defaults.Set(RelaunchVersionKey, LaunchHarness.RunningVersion), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            Assert.True(launch.Harness.Main.OnMain(() => launch.Progress.HasPendingCelebration)); // :31
            Assert.Equal(SettingsPage.About, launch.Harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage)); // :32
            var settings = LaunchHarness.WindowOf(launch.SettingsContent);
            Assert.NotNull(settings);
            Assert.Same(launch.Manager.MainWindow, settings); // OD-0151: the Settings panel sits in the main window
            Assert.True(launch.Harness.Main.OnMain(() => launch.Manager.IsSettingsOpen));
            Assert.True(settings.IsVisible);
            Assert.Equal(["main window built", "settings window built"], launch.Events.Where(e => e.EndsWith(" built", StringComparison.Ordinal)));
            Assert.Null(launch.Harness.Defaults.Object(RelaunchVersionKey)); // consumed, so never again
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-047")] // App/AppDelegate.swift:30; App/AppUpdater.swift:254-259
    [Trait("Req", "PR-WIN-071")] // App/AppDelegate.swift:30
    [Trait("Rule", "R-TEST-4")] // Partial proof of UI-LAUNCH-003: a marker for another version is consumed and shows nothing
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:30; App/AppUpdater.swift:254-259
    public void A_marker_for_another_version_is_consumed_and_launch_shows_no_Settings_and_arms_nothing()
    {
        WithLaunch(h => h.Defaults.Set(RelaunchVersionKey, "1.6.2"), launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            Assert.False(launch.Harness.Main.OnMain(() => launch.Progress.HasPendingCelebration));
            Assert.DoesNotContain("settings window built", launch.Events);
            Assert.Null(launch.Harness.Defaults.Object(RelaunchVersionKey));
        });
    }

    // ===== applicationShouldHandleReopen(_:hasVisibleWindows:) :36-39 =====

    [Fact]
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36-39
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: the reopen with nothing visible shows the one cached window and answers true
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; App/Window/WindowManager.swift:26-30
    public void A_reopen_with_no_visible_window_shows_the_main_window_and_answers_true()
    {
        WithLaunch(null, launch =>
        {
            var answer = launch.Harness.Main.OnMain(() => launch.Delegate.ApplicationShouldHandleReopen(hasVisibleWindows: false));

            Assert.True(answer); // :38
            var window = LaunchHarness.WindowOf(launch.MainContent); // :37 showMain(): made on first use and shown
            Assert.NotNull(window);
            Assert.True(window.IsVisible);

            launch.Harness.Main.OnMain(window.Hide);
            launch.Harness.Main.OnMain(() => launch.Delegate.ApplicationShouldHandleReopen(hasVisibleWindows: false));
            Assert.True(window.IsVisible); // the same window again (WindowManager.swift:26-30)
            Assert.Single(launch.Events, e => e == "main window built");
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36-39
    [Trait("Rule", "R-TEST-4")] // Partial proof of PR-WIN-072: with a window visible nothing extra happens
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39
    public void A_reopen_with_a_visible_window_creates_and_shows_nothing_and_answers_true()
    {
        WithLaunch(null, launch =>
        {
            var answer = launch.Harness.Main.OnMain(() => launch.Delegate.ApplicationShouldHandleReopen(hasVisibleWindows: true));

            Assert.True(answer);
            Assert.Empty(launch.Events); // no window was made
        });
    }

    [Theory]
    [Trait("Req", "PR-WIN-072")] // App/AppDelegate.swift:36-39; App/Window/WindowManager.swift:57-60; owner_decisions.md OD-0038
    [Trait("Req", "PR-LAUNCH-048")] // App/AppDelegate.swift:36-39 a miniaturized window is not a visible one
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void A_reopen_restores_a_minimised_main_window_to_the_state_it_had_and_brings_it_forward(WindowState before)
    {
        WithLaunch(null, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            var window = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(window);
            launch.Harness.Main.OnMain(() => window.WindowState = before);
            launch.Harness.Main.OnMain(() => window.WindowState = WindowState.Minimized);
            Dispatcher.UIThread.RunJobs();

            launch.Harness.Main.OnMain(() => launch.Delegate.ApplicationShouldHandleReopen(hasVisibleWindows: false));
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, window.WindowState); // OD-0038: restored, to normal or to maximised as it was
            Assert.True(window.IsVisible);
            Assert.True(window.IsActive);
            Assert.Single(launch.Events, e => e == "main window built");
        });
    }

    [Fact]
    [Trait("Req", "PR-WIN-072")] // App/Window/WindowManager.swift:32-38, :57-60; owner_decisions.md OD-0038
    [Trait("Decision", "OD-0151")] // Settings is a panel inside the main window, so Settings restores the main window
    public void Showing_Settings_while_the_main_window_is_minimised_restores_it_with_the_panel_open()
    {
        WithLaunch(null, launch =>
        {
            launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
            var settings = LaunchHarness.WindowOf(launch.SettingsContent);
            Assert.NotNull(settings);
            Assert.Same(launch.Manager.MainWindow, settings); // OD-0151: the panel is in the main window
            Assert.True(launch.Harness.Main.OnMain(() => launch.Manager.IsSettingsOpen));
            launch.Harness.Main.OnMain(() => settings.WindowState = WindowState.Minimized);
            Dispatcher.UIThread.RunJobs();

            launch.Harness.Main.OnMain(() => launch.Manager.ShowSettings());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Normal, settings.WindowState);
            Assert.True(settings.IsVisible);
            Assert.True(launch.Harness.Main.OnMain(() => launch.Manager.IsSettingsOpen));

            // The Settings command (OD-0151: it closes an open panel) restores a minimised window instead, the panel still open.
            launch.Harness.Main.OnMain(() => settings.WindowState = WindowState.Minimized);
            Dispatcher.UIThread.RunJobs();
            launch.Harness.Main.OnMain(() => launch.Manager.ToggleSettings());
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(WindowState.Normal, settings.WindowState);
            Assert.True(settings.IsVisible);
            Assert.True(launch.Harness.Main.OnMain(() => launch.Manager.IsSettingsOpen));
            Assert.Single(launch.Events, e => e == "main window built");
            Assert.Single(launch.Events, e => e == "settings window built");
        });
    }
}

// STOP Boot #1 (build agent, 2026-09-26) - RESOLVED 2026-10-02 (ED-236, OD-0041, the native glass tests, the clean-data capture)
// Kind: missing dependency (ED-103; built 2026-10-01, one visual check open)
// Swift: DroppyCode/App/AppDelegate.swift:22 `windows.showMain()` (B3 52bc792); owner_decisions.md OD-0018
// Requirements: PR-LAUNCH-028 (its OD-0018 deviation), UI-LAUNCH-001
// What is unclear or missing: ED-103 replaces the plain show with the boot sequence (a transparent click-through stage
// over the work area, the mark gathering, turning until ready, then breaking into particles that build the main window).
// ED-103 (7) makes it wait on probe P12 (whether the stage and the main window's Acrylic backdrop compose on Windows 11
// without a flash), which needs the owner's screen, and ED-103 (5) needs the main window's material (F.GlassMaterial,
// built after the app runs, ED-180). F.LoadingMark (the motion, ED-102 (1)-(3), ED-154) is in the tree
// (src/Marrow.UI/Port/LoadingMark.cs); ED-154 (4) gives P12 and the stage window's hosting to the launch side.
// Readings or options: (a) run P12, then build the stage in the shell around ShowMain -> exact; (b) build it now
// unprobed -> a guess at ED-103 (7)'s fallback.
// Needed to proceed: the owner's call on probe P13 (E:\Droppy\work\probes\p13\RESULT.md): an Acrylic main window shows
// no black or white frame at the hand-over, but its blur arrives ~150-200 ms after its tint; and STOP #5 of
// WindowManager (the main window's Acrylic hint) before that can be seen in the app.
// Done so far: (a). Probe P12 (2026-10-01, E:\Droppy\work\probes\p12) drew the click-through stage over the desktop;
// the boot sequence now replaces the plain show (src/Marrow.App/Port/BootStageWindow.cs, BootStageWindowTests: the
// stage, the main window at the outline's landing, its content following the particles, the bootstrap as "ready", the
// tour and About after the hand-over). The launch tests above keep the plain show through AppDelegate.StartBoot.

// STOP Boot #2 (build agent, 2026-09-26) - RESOLVED 2026-10-01 by OD-0040
// Kind: ambiguity
// Swift: DroppyCode/App/UpdateChecker.swift:141-149 (the didBecomeActiveNotification observer), reached from
// AppDelegate.swift:28
// Requirements: PR-LAUNCH-046 (its "every time the app becomes active" half), PR-UPDATE-009 (IMPLEMENTED, but nothing
// in the tree calls UpdateChecker.HandleActivation yet)
// What is unclear or missing: the windows_path is "the main window gaining focus after the app was unfocused". The
// shell can raise that, but whether the launch itself counts is UNRESOLVED. If AppKit posts didBecomeActive for the
// launch activation after applicationDidFinishLaunching returns (believed, not verified: OD-0001 gives no macOS
// access), upstream's observer, installed at :28, runs a catch-up check at launch since lastCheckedAt is nil; the
// port's main window activates inside ShowMain (:22), before :28 installs the observer. No oracle item settles the
// order. Only a Release build reaches it (UpdateChecker.swift:133: nothing starts in a development build).
// Readings or options: (a) count the launch activation (a catch-up check at launch plus the 8 s one); (b) count only
// later activations.
// Needed to proceed: nothing; RESOLVED 2026-10-01 by OD-0040 (a): the launch counts.
// Done so far: the shell's WM_ACTIVATEAPP hook calls HandleActivation (Port/AppShell.cs); with the boot sequence the
// main window is made and hooked in applicationDidFinishLaunching and shown after :28, so its first activation runs
// the catch-up check (AppShellActivationTests The_launch_activation_reaches_the_update_observer...).

// STOP Boot #3 (build agent, 2026-09-26) - RESOLVED 2026-10-01 by OD-0036 (reading (b))
// Kind: ambiguity
// Swift: DroppyCode/App/AppDelegate.swift:44-54; engineering_decisions.md ED-004 (2)
// Requirements: PR-LAUNCH-050, PR-WIN-073 (their session-end note)
// What is unclear or missing: ED-004 (2) says to call ShutdownBlockReasonCreate while a merge holds a session end "so the
// OS shows why", but gives no reason text, and the source has none (the only words are a code comment, :41-43). An
// invented string would be an invented label.
// Readings or options: (a) the owner or an ED names the text -> call it with that text; (b) refuse the session end with
// no reason text (Windows then lists the app as preventing shutdown without a reason) -> done now.
// Needed to proceed: the reason text.
// Done so far: (b): the shell refuses the session end while the merge wait runs and WM_ENDSESSION runs the three steps
// at once (AppShellTests); no ShutdownBlockReasonCreate call.

// HISTORICAL CLOSED Boot #4 (ED-212; opencode audit 2026-09-27)
// Kind: missing decision (a safe manual launch)
// Swift: none (port-only: R-TEST-9's MARROW_PORT_DATA_DIR is a test convention)
// What is unclear or missing: a production launch does not honour MARROW_PORT_DATA_DIR. Only scratch and temp folders
// read it (WorkingTreeWatch.cs:100, CodexSession.cs:880, CopilotSession.cs:1467, PlanLimits.cs:117,
// TextGeneration.cs:219). Everything else is fixed: Storage root %LOCALAPPDATA%\Marrow Code (Storage.cs:128-132:
// library.json, threads, attachments, wallpaper, token activity); %LOCALAPPDATA%\Marrow Code\UserDefaults.json
// (Program.cs, SettingsStore.ProductionPath); %LOCALAPPDATA%\Marrow Code\Logs (HangWatchdog.cs:44, :182);
// %LOCALAPPDATA%\Marrow Code\mcp (MCPBridgeSource.cs:309), \pi (PiCLI.cs:36-39), \CommandCode
// (CommandCodeSession.cs:1065, CommandCodeAPI.cs:632); %USERPROFILE%\.marrow-dev (Debug) or .marrow (Release)
// worktrees and patches (Storage.cs:131, OD-0012 F1); Windows Credential Manager for API keys (SecretStore); the
// user's own provider folders read by TokenLedger (%USERPROFILE%\.codex, TokenLedger.cs:556) and by the provider CLIs
// the bootstrap probes; %TEMP% for update downloads, the ACP catalog and git temporaries.
// Readings or options: (a) a port-only, Debug-only redirect of every root above to MARROW_PORT_DATA_DIR -> needs an
// ED (it is a config key the source does not have); (b) launch as is -> writes the port's own "Marrow Code" data.
// Needed to proceed: a decision on (a) if a manual launch must leave %LOCALAPPDATA%\Marrow Code untouched.
// Done so far (2026-09-27, ED-212): (a), with its own variable (MARROW_DEV_DATA_ROOT, not MARROW_PORT_DATA_DIR,
// which stays a test-only convention). Marrow.Core.Support.DevDataRoot redirects every root this note names, plus
// the two per-user identities (the .marrow-dev state directory and Credential Manager's service names) that were
// still unredirected after a first pass. Debug-only (#if DEBUG), inert in Release. See
// porting/engineering_decisions.md ED-212 and E:/Droppy/work/b3/DEV_LAUNCH.md for the launch and cleanup steps.

// STOP Boot #5 (build agent, 2026-09-26) - RESOLVED 2026-10-01 by OD-0038
// Kind: ambiguity
// Swift: DroppyCode/App/AppDelegate.swift:37 `WindowManager.shared.showMain()`; App/Window/WindowManager.swift:57-60
// `NSApp.activate()` then `window.makeKeyAndOrderFront(nil)`
// Requirements: PR-WIN-072 ("restores and foregrounds the running instance's existing main window")
// What is unclear or missing: the shell counts a minimised window as not visible (NSWindow.isVisible is false while
// miniaturized), so a second launch with the main window minimised runs showMain(). WindowManager.Present is
// Show() then Activate(), which on Win32 leaves a minimised window minimised. Whether makeKeyAndOrderFront
// deminiaturizes a miniaturized window is not in the source and not verified (OD-0001).
// Readings or options: (a) Present restores a minimised window before activating it -> "restores" as the record says,
// a change to App.Window.WindowManager; (b) leave it minimised.
// Needed to proceed: a reading of makeKeyAndOrderFront on a miniaturized window (an oracle item or a decision), owned
// by App.Window.WindowManager.
// Done so far: the reopen path is wired and tested for a hidden, a minimised and a visible main window (OD-0038: Present
// restores a minimised window to its previous state).
