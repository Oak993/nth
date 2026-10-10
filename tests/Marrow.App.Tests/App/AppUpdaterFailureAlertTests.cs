using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// Tests for App/AppUpdater.swift (B2 1fbf81e) :272-281's catch branch, this part's own record: PR-UPDATE-057
// (the modal "Update Failed" alert any install failure shows) and UI-UPDATE-034 (its exact geometry, text and
// the single dismiss button). The download step is a real loopback server (R-TEST-10), same as
// AppUpdaterInstallTests, so the failure comes from FakeUpdateInstallBoundary.Verify (ED-188 ruling point 3);
// the alert itself goes through AlertPresentation.Presenter, overridden here with RecordingAlertPresenter
// because headless Avalonia cannot open a real Window.
public sealed class AppUpdaterFailureAlertTests
{
    private static AvailableUpdate MakeUpdate(Uri downloadUrl, string version = "1.3.0") => new()
    {
        Version = version,
        Tag = "v" + version,
        DownloadURL = SwiftURL.Create(downloadUrl.ToString())!,
        Notes = "",
        PageURL = SwiftURL.Create("https://example.com/page")!,
    };

    private static Task Respond(LoopbackHttpServer server, int status, byte[] body) => Task.Run(async () =>
    {
        var exchange = await server.NextExchange();
        await exchange.Respond(status, "OK", body);
    });

    private static void RunUntilPhase(TestRuntime runtime, UpdateInstallProgress.Phase phase)
    {
        runtime.Loop.RunUntil(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase == phase, TimeSpan.FromSeconds(20));
        runtime.Loop.RunUntilIdle();
    }

    private static IDisposable UseDevelopmentOverride() =>
        AppInfo.Running.Override(AppInfo.FromAssembly(typeof(AppUpdaterFailureAlertTests).Assembly, isDevelopment: false));

    [Fact]
    [Trait("Req", "PR-UPDATE-057")] // App/AppUpdater.swift:272-281
    [Trait("Req", "UI-UPDATE-034")] // App/AppUpdater.swift:274-280
    public async Task An_install_failure_shows_exactly_one_modal_Update_Failed_alert_with_the_thrown_errors_text()
    {
        using var runtime = new TestRuntime();
        using var development = UseDevelopmentOverride();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alertSeam = AlertPresentation.Presenter.Override(presenter);
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary
        {
            VerifyThrows = new UpdateInstallError("The downloaded app is not signed by nth. reason"),
        };
        var updater = new AppUpdater(fake);
        var reply = Respond(server, 200, [1, 2, 3]);

        runtime.OnMain(() => updater.Install(MakeUpdate(server.Url("/artifact"))));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        await reply;

        var alert = Assert.Single(presenter.Shown);
        Assert.Equal("Update Failed", alert.Title); // :276, title case, no full stop, byte for byte
        Assert.Equal("The downloaded app is not signed by nth. reason", alert.InformativeText); // :277
        Assert.Equal(AlertStyle.Warning, alert.Style); // :278
        Assert.Single(alert.Buttons); // :279, the single default dismiss button; no retry affordance
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-057")] // App/AppUpdater.swift:272-273
    public async Task The_progress_track_fails_before_the_alert_is_shown()
    {
        using var runtime = new TestRuntime();
        using var development = UseDevelopmentOverride();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter();
        using var alertSeam = AlertPresentation.Presenter.Override(presenter);
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { VerifyThrows = new UpdateInstallError("boom") };
        var updater = new AppUpdater(fake);
        var reply = Respond(server, 200, [1]);

        runtime.OnMain(() => updater.Install(MakeUpdate(server.Url("/artifact"))));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Failed); // :273 progress.fail()
        await reply;

        // The alert (:274-280) is shown as part of the very same catch branch, after fail() already ran.
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        Assert.Single(presenter.Shown);
    }

    [Fact]
    [Trait("Req", "UI-UPDATE-034")] // App/AppUpdater.swift:279-280
    public async Task The_install_stays_running_until_the_alert_is_dismissed_then_a_new_install_may_start()
    {
        using var runtime = new TestRuntime();
        using var development = UseDevelopmentOverride();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var presenter = new RecordingAlertPresenter { AutoDismiss = false };
        using var alertSeam = AlertPresentation.Presenter.Override(presenter);
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { VerifyThrows = new UpdateInstallError("boom") };
        var updater = new AppUpdater(fake);
        var firstReply = Respond(server, 200, [1]);

        runtime.OnMain(() => updater.Install(MakeUpdate(server.Url("/artifact"))));
        runtime.Loop.RunUntil(() => presenter.Shown.Count > 0, TimeSpan.FromSeconds(20));
        await firstReply;

        // runModal blocks: a second Install() call before the alert closes is still a no-flight guard hit
        // (installTask is still set because the catch's `finally` has not run yet).
        fake.Calls.Clear();
        runtime.OnMain(() => updater.Install(MakeUpdate(server.Url("/artifact"))));
        runtime.Loop.RunUntilIdle();
        Assert.Empty(fake.Calls);

        runtime.OnMain(() => presenter.Dismiss());
        runtime.Loop.RunUntilIdle();

        // Now that the alert has closed, a fresh install is accepted again.
        var secondReply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(MakeUpdate(server.Url("/artifact"))));
        runtime.Loop.RunUntil(() => fake.Calls.Count > 0, TimeSpan.FromSeconds(20));
        await secondReply;
        Assert.NotEmpty(fake.Calls);
    }
}
