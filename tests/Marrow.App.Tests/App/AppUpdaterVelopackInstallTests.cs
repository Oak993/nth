using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Port.SelfUpdate;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// UE-1 (OD-0062 Velopack; OD-0100 the owner's GitHub feed): AppUpdater.Install over an ISelfUpdateEngine. The orchestration is
// AppUpdater.swift:261-283 as before: begin, download (percent from Velopack), verify, stage, hand off; a failure shows the modal
// "Update Failed" alert; the hand-off keeps :372-410's writability gate, relaunch marker, 25 s give-up and quit. The engine and the
// boundary are fakes: nothing downloads, nothing is applied, no process starts and the test host is never asked to quit.
public sealed class AppUpdaterVelopackInstallTests
{
    private static AvailableUpdate MakeUpdate(string version = "1.3.0") => new()
    {
        Version = version,
        Tag = "v" + version,
        DownloadURL = SwiftURL.Create(MarrowReleaseFeed.LatestReleasePage)!,
        Notes = "",
        PageURL = SwiftURL.Create(MarrowReleaseFeed.LatestReleasePage)!,
    };

    private static IDisposable NotDevelopment() =>
        AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterVelopackInstallTests).Assembly, isDevelopment: false));

    [Fact]
    [Trait("Decision", "ED-235")]
    public void The_downloads_percent_moves_the_progress_pill_as_a_fraction()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        var progress = UpdateInstallProgressFixtures.NewProgress();
        using var progressSeam = UpdateInstallProgress.Running.Override(progress);
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var gate = new TaskCompletionSource();
        var engine = new FakeSelfUpdateEngine { Percents = [50], DownloadGate = gate.Task };
        var updater = new AppUpdater(new FakeUpdateInstallBoundary { StageThrows = new UpdateInstallError("stop here") }, engine);
        runtime.OnMain(() => updater.Install(MakeUpdate()));
        runtime.Loop.RunUntilIdle();
        // half the download is 0.05 + (0.55 - 0.05) * 0.5 = 0.30 on the pill (AppUpdater.swift:99-113, UpdateProgressMilestones)
        Assert.Equal(UpdateInstallProgress.Phase.Downloading, runtime.OnMain(() => progress.UpdateInstallProgressPhase));
        Assert.Equal(0.30, runtime.OnMain(() => progress.Fraction), 6);
        gate.SetResult();
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        Assert.Equal("stop here", Assert.Single(presenter.Shown).InformativeText);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_failed_download_shows_the_update_failed_alert_in_the_download_failure_words()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var engine = new FakeSelfUpdateEngine { DownloadThrows = new HttpRequestException("The server returned 404.") };
        var boundary = new FakeUpdateInstallBoundary();
        runtime.OnMain(() => new AppUpdater(boundary, engine).Install(MakeUpdate()));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        var alert = Assert.Single(presenter.Shown);
        Assert.Equal("Update Failed", alert.Title);                                        // :276
        Assert.StartsWith("The update could not be downloaded. ", alert.InformativeText, StringComparison.Ordinal); // :523-527
        Assert.Empty(boundary.Calls);                                                      // nothing was staged
        Assert.DoesNotContain("Apply", engine.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_package_whose_version_is_not_the_one_offered_fails_before_anything_is_applied()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var engine = new FakeSelfUpdateEngine();
        var boundary = new FakeUpdateInstallBoundary { VersionToReturn = "1.4.0" };       // the feed moved on between the check and the download
        runtime.OnMain(() => new AppUpdater(boundary, engine).Install(MakeUpdate("1.3.0")));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        Assert.Equal("The downloaded app is version 1.4.0, not 1.3.0.", Assert.Single(presenter.Shown).InformativeText); // :334-336
        Assert.DoesNotContain("Apply", engine.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_development_build_and_a_second_install_do_nothing()
    {
        using var runtime = new TestRuntime();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var engine = new FakeSelfUpdateEngine();
        using (AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterVelopackInstallTests).Assembly, isDevelopment: true)))
        {
            runtime.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary(), engine).Install(MakeUpdate()));
            runtime.Loop.RunUntilIdle();
        }
        Assert.Empty(engine.Calls);                                                        // :262
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void The_hand_off_refuses_a_folder_it_cannot_write_and_never_calls_the_updater()
    {
        using var runtime = new TestRuntime();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var engine = new FakeSelfUpdateEngine();
        var missing = Path.Combine(Path.GetTempPath(), "ue1-missing-" + Guid.NewGuid().ToString("N"), "app");
        var error = Assert.Throws<UpdateInstallError>(() => new AppUpdater(new FakeUpdateInstallBoundary(), engine).HandOffToVelopack("1.3.0", missing));
        Assert.StartsWith("nth cannot replace itself in ", error.ErrorDescription, StringComparison.Ordinal);   // :377-379
        Assert.EndsWith(" Reinstall it with the installer and try again.", error.ErrorDescription, StringComparison.Ordinal);
        Assert.DoesNotContain("Apply", engine.Calls);
    }

    [Fact]
    [Trait("Decision", "ED-235")]
    public void A_refused_updater_start_fails_the_install_with_an_alert_and_leaves_no_relaunch_marker()
    {
        using var runtime = new TestRuntime();
        using var development = NotDevelopment();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alerts = AlertPresentation.Presenter.Override(presenter);
        var engine = new FakeSelfUpdateEngine { ApplyThrows = new InvalidOperationException("Update.exe could not start.") };
        runtime.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary(), engine).Install(MakeUpdate()));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        Assert.Equal("Update.exe could not start.", Assert.Single(presenter.Shown).InformativeText);
        Assert.Null(runtime.Defaults.Object("appUpdater_pendingRelaunchVersion"));
    }
}
