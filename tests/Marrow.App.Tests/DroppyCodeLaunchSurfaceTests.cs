using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Services.Store;
using static Marrow.App.Tests.Fixtures.LaunchSurfaceRig;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/DroppyCodeApp.swift (B3 52bc792), :7-19 (the prologue presents nothing, makes no network call) with
// App/AppDelegate.swift:8-28 (the only things launch presents are the main window, the tour after 700 ms if unseen, and the
// Settings on About after an update relaunch, a panel in the main window under OD-0140), App/AppModel.swift:106 (alert is never set by launch-path code) and
// Services/Store/Storage.swift:60-63 (a library that fails to load opens empty, silently).
// PR-LAUNCH-068 / UI-LAUNCH-009: no splash, no progress UI, no migration dialog and no error dialog on the launch path.
// DEVIATION OD-0008 A10, OD-0012 E4: one failure is not silent: a library.json that exists but cannot be read or decoded is
// surfaced in the main window with the exact text below, and the file is never overwritten.
// DEVIATION OD-0018/OD-0040/OD-0041: the port's boot stage window covers start-up; these tests stand it in with the plain show
// (LaunchHarness.StartBoot), as every launch test of AppDelegate.swift:9-34 does, so what is observed is the Swift launch path.
// PR-LAUNCH-079: no network call before the main window is on screen; the update check no sooner than 8 s after launch
// (UpdateChecker.swift:134-135), and under OD-0012 I1 none at all while the feed value is empty.
[Collection("Headless")]
public sealed class DroppyCodeLaunchSurfaceTests(HeadlessSession headless)
{
    // OD-0012 E4, byte for byte.
    private const string LibraryUnreadableMessage =
        "nth could not read library.json, so your projects and threads are hidden. The file will not be changed until it can be read.";

    private static readonly string[] OnlyTheMainWindow = ["main window built", "bootstrap"];

    [Fact]
    [Trait("Req", "PR-LAUNCH-068")] // App/AppDelegate.swift:8-28; App/AppModel.swift:106; App/DroppyCodeApp.swift:7-19
    [Trait("Req", "UI-LAUNCH-009")] // the whole launch: the main window with a chat in it, and nothing else
    public void A_normal_launch_shows_the_main_window_with_a_chat_selected_and_no_other_surface()
    {
        Guid chat = default;
        WithLaunch(headless, h => chat = ProjectWithUnusedThread(h), launch =>
        {
            var alerts = new RecordingAlertPresenter();
            using var seam = AlertPresentation.Presenter.Override(alerts);

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30)); // long after any delayed presentation

            var main = LaunchHarness.WindowOf(launch.MainContent);
            Assert.NotNull(main);
            Assert.True(main.IsVisible);
            Assert.True(main.IsEnabled); // nothing modal holds it
            Assert.Equal(chat, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID)); // :23 a chat is selected
            Assert.Equal(OnlyTheMainWindow, launch.Events); // no Settings (:30-33 needs a relaunch marker), no tour (seen)
            Assert.Null(LaunchHarness.WindowOf(launch.SettingsContent));
            Assert.False(launch.Manager.IsSettingsOpen); // OD-0140: nor the Settings panel in the main window
            Assert.Empty(launch.TourModels);
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert)); // AppModel.swift:106 never set by launch
            Assert.Empty(alerts.Shown);
            Assert.False(launch.Harness.Main.OnMain(() => launch.Manager.Model.IsCommandPalettePresented));
        });
    }

    [Fact]
    [Trait("Req", "UI-LAUNCH-009")] // states: "Slow bootstrap: the window is already usable; there is no blocking indicator"
    public void While_the_bootstrap_is_still_running_the_window_is_already_up_and_nothing_blocks_it()
    {
        WithLaunch(headless, h => ProjectWithUnusedThread(h), launch =>
        {
            var pending = new TaskCompletionSource();
            launch.Delegate.BootstrapModel = _ => pending.Task; // AppDelegate.swift:27 not awaited: it never finishes here
            try
            {
                launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
                launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));

                var main = LaunchHarness.WindowOf(launch.MainContent)!;
                Assert.False(pending.Task.IsCompleted);
                Assert.True(main.IsVisible);
                Assert.True(main.IsEnabled);
                Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert));
                Assert.Equal(["main window built"], launch.Events);
                Assert.NotNull(launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID));
            }
            finally
            {
                pending.SetResult();
                launch.Harness.Main.Loop.RunUntilIdle();
            }
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-068")] // App/AppModel.swift:126-128; Services/Store/Storage.swift:60-63; OD-0008 A10, OD-0012 E4
    [Trait("Req", "UI-LAUNCH-009")] // state "Corrupt or unreadable library"
    public void A_library_that_cannot_be_decoded_is_the_one_error_shown_and_the_file_is_left_untouched()
    {
        var garbage = System.Text.Encoding.UTF8.GetBytes("{not a library}");
        WithLaunch(headless, h =>
        {
            TourSeen(h);
            File.WriteAllBytes(Storage.LibraryURL, garbage);
        }, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));
            DiskWriter.Shared.WaitForQueuedWrites();

            var alert = launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert);
            Assert.NotNull(alert);
            Assert.Equal(LibraryUnreadableMessage, alert.Message);
            Assert.Empty(launch.Harness.Main.OnMain(() => launch.Manager.Model.Projects)); // "your projects and threads are hidden"
            Assert.Empty(launch.Harness.Main.OnMain(() => launch.Manager.Model.Threads));
            Assert.Equal(garbage, File.ReadAllBytes(Storage.LibraryURL)); // "The file will not be changed"
            Assert.Equal(OnlyTheMainWindow, launch.Events); // the alert is the only surface: no Settings, no tour
            Assert.Null(LaunchHarness.WindowOf(launch.SettingsContent));
            Assert.False(launch.Manager.IsSettingsOpen); // OD-0140: nor the Settings panel in the main window
            Assert.True(LaunchHarness.WindowOf(launch.MainContent)!.IsVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-068")] // OD-0012 E4: "shown in the main window"; App/Views/RootView.swift:80-91 presents the model's alert
    [Trait("Req", "UI-LAUNCH-009")] // state "Corrupt or unreadable library": the error text is exactly OD-0012 E4
    public void The_main_windows_root_view_presents_the_library_error_as_the_only_alert_with_the_exact_text()
    {
        WithLaunch(headless, h =>
        {
            TourSeen(h);
            File.WriteAllBytes(Storage.LibraryURL, System.Text.Encoding.UTF8.GetBytes("{not a library}"));
        }, launch =>
        {
            var alerts = new RecordingAlertPresenter { AutoDismiss = false };
            using var seam = AlertPresentation.Presenter.Override(alerts);
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            launch.Harness.Main.OnMain(() =>
            {
                // The main window's content is the RootView over the manager's model (WindowManager.swift:62-72, :104); its
                // alert binding reads model.alert when it is attached (RootView.swift:80-91).
                var host = new Avalonia.Controls.Border();
                AppEnvironment.SetAppModel(host, launch.Manager.Model);
                host.Child = new Marrow.App.Views.RootView();
                var window = new Avalonia.Controls.Window { Content = host };
                try
                {
                    window.Show();
                    var shown = Assert.Single(alerts.Shown);
                    Assert.Equal(LibraryUnreadableMessage, shown.InformativeText);
                    Assert.Equal("OK", Assert.Single(shown.Buttons).Title); // one button, the user's way out
                }
                finally
                {
                    window.Close();
                }
            });
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-068")] // OD-0008 A10: "exists but cannot be read"; Services/Store/Storage.swift:60-63
    [Trait("Req", "UI-LAUNCH-009")] // state "Corrupt or unreadable library"
    public void A_library_that_exists_but_cannot_be_read_gets_the_same_single_error()
    {
        WithLaunch(headless, h =>
        {
            TourSeen(h);
            Directory.CreateDirectory(Storage.LibraryURL); // a folder where the file belongs: present, but not readable as a file
        }, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));
            DiskWriter.Shared.WaitForQueuedWrites();

            Assert.Equal(LibraryUnreadableMessage, launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert)?.Message);
            Assert.True(Directory.Exists(Storage.LibraryURL)); // nothing replaced it
            Assert.Equal(OnlyTheMainWindow, launch.Events);
            Assert.True(LaunchHarness.WindowOf(launch.MainContent)!.IsVisible);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-068")] // Services/Store/Storage.swift:60-63 (a missing library is an empty one); UI-LAUNCH-009 "Failed store-root creation"
    [Trait("Req", "UI-LAUNCH-009")] // state "Failed store-root creation: the app opens and later writes fail silently"
    public void When_the_store_root_cannot_be_made_the_app_opens_and_later_writes_fail_without_a_dialog()
    {
        WithLaunch(headless, h =>
        {
            TourSeen(h);
            var root = Path.GetDirectoryName(Storage.LibraryURL)!;
            Directory.Delete(root, recursive: true);
            File.WriteAllText(root, "a file where the store folder belongs");
        }, launch =>
        {
            var alerts = new RecordingAlertPresenter();
            using var seam = AlertPresentation.Presenter.Override(alerts);

            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);
            launch.Harness.Main.OnMain(() => launch.Manager.Model.AddProject(new Marrow.Core.Models.Project("Late", launch.Harness.NewFolder("late")).Url)); // a write that cannot land
            launch.Harness.Main.Advance(TimeSpan.FromSeconds(30));
            DiskWriter.Shared.WaitForQueuedWrites();

            Assert.True(LaunchHarness.WindowOf(launch.MainContent)!.IsVisible);
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Manager.Model.Alert)); // silent
            Assert.Empty(alerts.Shown);
            Assert.Equal(OnlyTheMainWindow, launch.Events);
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-079")] // App/UpdateChecker.swift:134-135; App/AppDelegate.swift:8-28; DEVIATION OD-0012 I1
    public void With_no_update_feed_the_launch_makes_no_request_and_the_first_check_waits_8_seconds()
    {
        WithLaunch(headless, TourSeen, launch =>
        {
            launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

            Assert.True(LaunchHarness.WindowOf(launch.MainContent)!.IsVisible); // the window is up before anything is checked
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Checker.LastCheckedAt));
            launch.Harness.Main.AdvanceMilliseconds(7_999);
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Checker.LastCheckedAt)); // nothing before 8 s
            Assert.Null(launch.Harness.Main.OnMain(() => launch.Checker.LastError));

            launch.Harness.Main.AdvanceMilliseconds(1); // UpdateChecker.swift:135 Task.sleep(for: .seconds(8))

            Assert.NotNull(launch.Harness.Main.OnMain(() => launch.Checker.LastCheckedAt));
            // OD-0012 I1: the check fails with this text without a request (an attempt at an empty address would say otherwise).
            Assert.Equal("No update feed is set.", launch.Harness.Main.OnMain(() => launch.Checker.LastError));
        });
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-079")] // App/UpdateChecker.swift:134-135 the launch delay precedes the first check
    public void With_a_feed_set_nothing_connects_to_it_before_8_seconds_after_launch_and_one_connection_does_at_8()
    {
        WithLaunch(headless, TourSeen, launch =>
        {
            using var feed = new LoopbackConnectionCounter();
            var checker = launch.Harness.Main.OnMain(() => new UpdateChecker(AppInfo.Current, [], feed.Url("/releases")));
            using var shared = UpdateChecker.OverrideShared(checker);
            try
            {
                launch.Harness.Main.OnMain(launch.Delegate.ApplicationDidFinishLaunching);

                Assert.True(LaunchHarness.WindowOf(launch.MainContent)!.IsVisible); // the window is up before any traffic
                Assert.Equal(0, feed.Connections()); // the launch callback itself reaches no network
                launch.Harness.Main.AdvanceMilliseconds(7_999);
                Thread.Sleep(200); // a connection made now would have arrived by now
                Assert.Equal(0, feed.Connections());

                launch.Harness.Main.AdvanceMilliseconds(1); // UpdateChecker.swift:135 Task.sleep(for: .seconds(8))
                launch.Harness.Main.Loop.RunUntil(() => feed.Connections() > 0, TimeSpan.FromSeconds(20));
                Thread.Sleep(200);

                Assert.Equal(1, feed.Connections()); // the launch check, once
            }
            finally
            {
                UpdateCheckerLoop.Stop(checker);
            }
        });
    }
}
