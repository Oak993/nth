using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.App;

// PR-UPDATE-054's verification_method is a "restart test": the handoff writes "appUpdater_pendingRelaunchVersion" (App/AppUpdater.swift:396)
// into the persistent defaults, the next launch of the app reads it back and consumes it once (:254-259). The throwaway settings file of
// AppSettingsScope is the production store on disk, so Relaunch() is a real reopen of the file the way the next launch would read it.
[Collection("AppSettings")]
public sealed class AppUpdaterRestartTests
{
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    [Fact]
    [Trait("Req", "PR-UPDATE-054")] // App/AppUpdater.swift:242, :396 (marker), :254-259 (consumed once by the relaunched build)
    public void The_relaunch_marker_survives_a_restart_and_the_relaunched_build_consumes_it_once()
    {
        using var scope = AppSettingsScope.Create();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        using var storage = Storage.Locations.Override(StorageLocations.Create(Path.Combine(scope.Folder, "root"), Path.Combine(scope.Folder, "home")));
        var manager = scope.Runtime.OnMain(() => new WindowManager());
        using var windows = WindowManager.OverrideShared(manager);
        manager.MainWindowClose = () => { }; // never terminate the test host
        var staging = Directory.CreateTempSubdirectory().FullName;
        var staged = Path.Combine(staging, "App");
        Directory.CreateDirectory(staged);
        var destination = Path.Combine(staging, "destination");
        Directory.CreateDirectory(destination);
        try
        {
            scope.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary()).HandOff(staged, "1.3.0", destination));
            scope.Relaunch(); // the app quits and the file is read again by the next launch
            Assert.Equal("1.3.0", scope.Defaults.Object(RelaunchVersionKey));

            using var running = AppInfo.Running.Override(AppInfo.Read(
                new Dictionary<string, object?> { ["CFBundleShortVersionString"] = "1.3.0" }, isDevelopment: false));
            Assert.True(AppUpdater.Shared.ConsumeRelaunchMarker());
            Assert.False(AppUpdater.Shared.ConsumeRelaunchMarker());
            scope.Relaunch(); // the removal reached the file: a second launch sees no marker
            Assert.Null(scope.Defaults.Object(RelaunchVersionKey));
            Assert.False(scope.AnyFileContains(RelaunchVersionKey));
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
        }
    }
}
