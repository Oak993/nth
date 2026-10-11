using Avalonia.Threading;
using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;
using Xunit;

namespace Marrow.App.Tests;

// AppDelegate.swift:77-92 userNotificationCenter(_:didReceive:) through the production window adapter
// (Port/WindowsToastPlatform.cs AvaloniaToastActivationWindows) and the real WindowManager, not a recorder. OD-0037
// accepts the activation callback as the proof of a toast click (ED-220 (7) keeps real toasts out of tests; one manual
// click of each kind stays on the Windows verification list). Expected values from the Swift lines cited:
//   :82-83 threadID is read as a string; the update branch is the mere presence of "update"
//   :85 showMain() unless it is an update; :86-87 a parsable threadID becomes the selection; :89 activate;
//   :90 an update opens Settings on About (WindowManager.swift:32-38) and skips :85's showMain(); DEVIATION OD-0140: Settings is a
//   panel inside the main window, so opening it shows that window with the panel on About
[Collection("Headless")]
public sealed class WindowsToastRoutingTests(HeadlessSession headless)
{
    private void WithLaunch(Action<LaunchHarness> body) => headless.OnUiThread(() =>
    {
        var page = SettingsNavigation.Shared.RequestedPage;
        using var launch = new LaunchHarness();
        try
        {
            launch.Harness.Main.OnMain(() => launch.Delegate.Model = launch.Manager.Model);
            body(launch);
        }
        finally
        {
            launch.Harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage = page);
            Dispatcher.UIThread.RunJobs();
        }
    });

    private static void Click(LaunchHarness launch, Dictionary<string, object> userInfo)
    {
        launch.Harness.Main.Loop.Run(() => launch.Delegate.UserNotificationCenter(new UNNotificationResponse(new UNNotification(userInfo))));
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:77-92; owner_decisions.md OD-0037
    [Trait("Req", "PR-WIN-076")] // App/AppDelegate.swift:82-90
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:77-92 the thread tap (the records are amended, STOP App.AppDelegate #8)
    public void Clicking_a_thread_notification_shows_the_main_window_and_selects_the_thread_and_opens_no_Settings() => WithLaunch(launch =>
    {
        var thread = Guid.NewGuid();
        Click(launch, new Dictionary<string, object> { ["threadID"] = SwiftUUID.UuidString(thread) });

        var main = LaunchHarness.WindowOf(launch.MainContent);
        Assert.NotNull(main); // :85 made on first use and shown
        Assert.True(main.IsVisible);
        Assert.True(main.IsActive); // :89
        Assert.Equal(thread, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID)); // :86-87
        Assert.DoesNotContain("settings window built", launch.Events);
        Assert.False(launch.Manager.IsSettingsOpen); // OD-0140: no Settings panel in the main window either
    });

    [Theory]
    [Trait("Req", "PR-LAUNCH-065")] // App/AppDelegate.swift:83-90; owner_decisions.md OD-0037
    [Trait("Req", "PR-UPDATE-024")] // App/AppDelegate.swift:71-86; App/Window/WindowManager.swift:32-38
    [Trait("Req", "PR-WIN-076")] // App/AppDelegate.swift:83 presence, whatever the value
    [Trait("Req", "UI-LAUNCH-003")] // App/AppDelegate.swift:84 the update tap opens Settings on About (OD-0140: the panel in the main window)
    [Trait("Req", "UI-LAUNCH-005")] // App/AppDelegate.swift:77-92 the update tap (the records are amended, STOP App.AppDelegate #8)
    [Trait("Decision", "OD-0140")] // Settings is a panel inside the main window
    [InlineData("1.7.1")]
    [InlineData("")]
    public void Clicking_an_update_notification_opens_Settings_on_About_as_the_panel_in_the_main_window(string version) => WithLaunch(launch =>
    {
        var before = launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID);
        Click(launch, new Dictionary<string, object> { ["update"] = version });

        // :85 is skipped; :90's showSettings makes the main window only because Settings is a panel in it (OD-0140), then the panel.
        Assert.Equal(["main window built", "settings window built"], launch.Events);
        var settings = LaunchHarness.WindowOf(launch.SettingsContent);
        Assert.NotNull(settings); // :90
        Assert.Same(launch.Manager.MainWindow, settings);
        Assert.Same(settings, LaunchHarness.WindowOf(launch.MainContent));
        Assert.True(launch.Harness.Main.OnMain(() => launch.Manager.IsSettingsOpen));
        Assert.True(settings.IsVisible);
        Assert.True(settings.IsActive); // WindowManager.swift:37, :58-59
        Assert.Equal(SettingsPage.About, launch.Harness.Main.OnMain(() => SettingsNavigation.Shared.RequestedPage));
        Assert.Equal(before, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID)); // no threadID
    });

    [Fact]
    [Trait("Req", "PR-LAUNCH-064")] // App/AppDelegate.swift:86 UUID(uuidString:) fails: selection unchanged, window still shown
    [Trait("Req", "PR-WIN-076")]
    public void A_thread_notification_with_an_unparsable_id_still_shows_the_main_window_and_keeps_the_selection() => WithLaunch(launch =>
    {
        var before = launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID);
        Click(launch, new Dictionary<string, object> { ["threadID"] = "not-a-uuid" });

        var main = LaunchHarness.WindowOf(launch.MainContent);
        Assert.NotNull(main);
        Assert.True(main.IsVisible);
        Assert.Equal(before, launch.Harness.Main.OnMain(() => launch.Manager.Model.SelectedThreadID));
        Assert.DoesNotContain("settings window built", launch.Events);
        Assert.False(launch.Manager.IsSettingsOpen); // OD-0140: no Settings panel in the main window either
    });
}
