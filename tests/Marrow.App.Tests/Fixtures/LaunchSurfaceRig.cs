using Avalonia.Threading;
using Marrow.App.Views.Settings;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// Helpers for the tests of what a launch puts in front of the user (DroppyCodeSceneTests, DroppyCodeLaunchSurfaceTests): one
// launch on a LaunchHarness, run on the headless UI thread, with the process-wide Settings navigation put back afterwards, as
// AppDelegateThirdTests does for the launch tests of AppDelegate.swift:9-34. Helper code lives here (R-TEST-3).
public static class LaunchSurfaceRig
{
    public const string HasSeenTourKey = "hasSeenTour"; // App/AppSettings.swift:178

    /// <summary>The first-launch state with the tour out of the way (AppDelegate.swift:24: seen), so only launch itself is observed.</summary>
    public static void TourSeen(AppModelHarness h) => h.Defaults.Set(HasSeenTourKey, true);

    /// <summary>One project with one unused thread, remembered as the last project, so launch selects that thread (AppModel.swift:574-598).</summary>
    public static Guid ProjectWithUnusedThread(AppModelHarness h)
    {
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var thread = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [thread]);
        h.Defaults.Set("lastProjectID", SwiftUUID.UuidString(project.Id));
        TourSeen(h);
        return thread.Id;
    }

    /// <summary>Runs <paramref name="body"/> over one launch rig on the headless UI thread.</summary>
    public static void WithLaunch(HeadlessSession headless, Action<AppModelHarness>? prepare, Action<LaunchHarness> body) => headless.OnUiThread(() =>
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
}
