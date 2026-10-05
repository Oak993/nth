using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-5 (OD-0100 "Restart Marrow"): Update everything has already downloaded Marrow's package through the engine, so AppUpdater.ApplyDownloaded is Install from the verify step on:
// verify, stage, hand off to Velopack's updater, and the same "Update Failed" alert when a step fails. The engine and the boundary are fakes: nothing downloads, nothing is applied,
// no process starts and the test host is never asked to quit.
public sealed class AppUpdaterApplyDownloadedTests
{
    private static IDisposable NotDevelopment() =>
        AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterApplyDownloadedTests).Assembly, isDevelopment: false));

    [Fact]
    [Trait("Decision", "ED-319")]
    public void A_downloaded_package_is_verified_staged_and_handed_to_the_updater_and_leaves_the_relaunch_marker()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var engine = new FakeSelfUpdateEngine();
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "1.3.0" };
        runtime.OnMain(() => new AppUpdater(boundary, engine).ApplyDownloaded(engine.PackagePath, "1.3.0"));
        runtime.Loop.RunUntilIdle();
        Assert.Equal([$"Stage({engine.PackagePath}, 1.3.0)", $"Verify({boundary.StagedPathToReturn})", $"ReadVersion({boundary.StagedPathToReturn})"], boundary.Calls);
        Assert.Contains("Apply", engine.Calls);
        Assert.DoesNotContain(engine.Calls, c => c.StartsWith("Download", StringComparison.Ordinal)); // it downloads nothing: the run did
        Assert.Equal("1.3.0", runtime.Defaults.Object("appUpdater_pendingRelaunchVersion"));
    }

    [Fact]
    [Trait("Decision", "ED-319")]
    public void A_package_of_another_version_fails_before_anything_is_applied_with_the_update_failed_alert()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var engine = new FakeSelfUpdateEngine();
        runtime.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary { VersionToReturn = "1.4.0" }, engine).ApplyDownloaded(engine.PackagePath, "1.3.0"));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        var alert = Assert.Single(presenter.Shown);
        Assert.Equal("Update Failed", alert.Title);
        Assert.Equal("The downloaded app is version 1.4.0, not 1.3.0.", alert.InformativeText);
        Assert.DoesNotContain("Apply", engine.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-319")]
    public void A_development_build_and_an_updater_without_an_engine_do_nothing()
    {
        using var runtime = new TestRuntime();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var engine = new FakeSelfUpdateEngine();
        var boundary = new FakeUpdateInstallBoundary();
        using (AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterApplyDownloadedTests).Assembly, isDevelopment: true)))
        {
            runtime.OnMain(() => new AppUpdater(boundary, engine).ApplyDownloaded(engine.PackagePath, "1.3.0"));
            runtime.Loop.RunUntilIdle();
        }
        using (NotDevelopment())
        {
            runtime.OnMain(() => new AppUpdater(boundary).ApplyDownloaded(engine.PackagePath, "1.3.0"));
            runtime.Loop.RunUntilIdle();
        }
        Assert.Empty(engine.Calls);
        Assert.Empty(boundary.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-319")]
    public void A_second_press_while_the_first_is_still_going_changes_nothing()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var engine = new FakeSelfUpdateEngine();
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "1.4.0" }; // fails, and waits in the alert
        var updater = new AppUpdater(boundary, engine);
        runtime.OnMain(() =>
        {
            updater.ApplyDownloaded(engine.PackagePath, "1.3.0");
            updater.ApplyDownloaded(engine.PackagePath, "1.3.0");
        });
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        Assert.Single(presenter.Shown);
        Assert.Equal(1, boundary.Calls.Count(c => c.StartsWith("Stage(", StringComparison.Ordinal)));
    }
}
