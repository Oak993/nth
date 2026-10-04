using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Window;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.Services.Port;
using Marrow.Services.Store;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-1 (OD-0062, OD-0100): the whole install through the engine to the hand-off, on the throwaway settings file of AppSettingsScope (the hand-off
// writes the relaunch marker, AppUpdater.swift:396, and asks the main window to close, :405-409, which is a recorder here: the test host never quits).
// The engine and the boundary are fakes; Update.exe never starts.
[Collection("AppSettings")]
public sealed class AppUpdaterVelopackHandoffTests
{
    private const string RelaunchVersionKey = "appUpdater_pendingRelaunchVersion";

    private static string Names(IEnumerable<string> calls) => string.Join(",", calls.Select(c => c[..(c.IndexOf('(', StringComparison.Ordinal) is var i and >= 0 ? i : c.Length)]));

    [Fact]
    [Trait("Decision", "OD-0100")]
    public void An_install_downloads_through_the_engine_then_stages_verifies_and_asks_the_updater_to_restart()
    {
        using var scope = AppSettingsScope.Create();
        using var development = AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterVelopackHandoffTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        using var storage = Storage.Locations.Override(StorageLocations.Create(Path.Combine(scope.Folder, "root"), Path.Combine(scope.Folder, "home")));
        var manager = scope.Runtime.OnMain(() => new WindowManager());
        using var windows = WindowManager.OverrideShared(manager);
        var closed = 0;
        manager.MainWindowClose = () => closed++;
        var engine = new FakeSelfUpdateEngine();
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "1.3.0" };
        var update = new AvailableUpdate
        {
            Version = "1.3.0", Tag = "v1.3.0", Notes = "",
            DownloadURL = SwiftURL.Create(MarrowReleaseFeed.LatestReleasePage)!, PageURL = SwiftURL.Create(MarrowReleaseFeed.LatestReleasePage)!,
        };
        scope.OnMain(() => new AppUpdater(boundary, engine).Install(update));
        scope.Runtime.Loop.RunUntil(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase == UpdateInstallProgress.Phase.Handoff, TimeSpan.FromSeconds(20));
        scope.Runtime.Loop.RunUntilIdle();
        Assert.Equal(["Download(1.3.0)", "Apply"], engine.Calls);
        // Velopack's package is staged, verified and read by the boundary as the macOS image was (:299-341); install.ps1 is not used
        Assert.Equal("Stage,Verify,ReadVersion", Names(boundary.Calls));
        Assert.Equal($"Stage({engine.PackagePath}, 1.3.0)", boundary.Calls[0]);
        Assert.Equal(UpdateInstallProgress.Phase.Handoff, scope.Runtime.OnMain(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase));
        Assert.Equal("1.3.0", scope.Defaults.Object(RelaunchVersionKey));                   // :396, read back by the relaunched build
        scope.Runtime.Advance(TimeSpan.FromMilliseconds(400));                              // :407: the quit follows 350 ms later, through the shell's own request
        Assert.Equal(1, closed);
    }
}
