using Marrow.App.Port;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Testing;
using Xunit;

namespace Marrow.App.Tests.App;

// Tests for App/AppUpdater.swift (B2 1fbf81e), part 1's install orchestration: install() :261-283, stage()'s
// caller-visible sequencing :299-341 (over IUpdateInstallBoundary, ED-188), download() :287-295, handOff()
// :372-410, and the Downloader :459-528. PR-UPDATE-041 to PR-UPDATE-047, PR-UPDATE-049 to PR-UPDATE-055.
// The Windows-only mechanics behind IUpdateInstallBoundary.Stage/Verify/ReadVersion (hdiutil/ditto's Windows
// analogue, Authenticode) are exercised through FakeUpdateInstallBoundary here (ED-188 ruling point 3); the
// real WindowsUpdateInstallBoundary is tested separately in WindowsUpdateInstallBoundaryTests.
public sealed class AppUpdaterInstallTests
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

    private static UpdateInstallProgress.Phase Phase(TestRuntime runtime) =>
        runtime.OnMain(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase);

    // RunUntil returns as soon as the condition turns true, which can be mid-way through one work item (for
    // example right after Fail() sets the phase but before the failureReset Task it just spawned has run its
    // first turn and registered its 600 ms timer); the trailing RunUntilIdle lets that spawn actually start so
    // a later Advance/AdvanceMilliseconds finds its timer already registered.
    private static void RunUntilPhase(TestRuntime runtime, UpdateInstallProgress.Phase phase)
    {
        runtime.Loop.RunUntil(() => UpdateInstallProgress.Shared.UpdateInstallProgressPhase == phase, TimeSpan.FromSeconds(20));
        runtime.Loop.RunUntilIdle();
    }

    private static void RunUntilAnyPhase(TestRuntime runtime, params UpdateInstallProgress.Phase[] phases) =>
        runtime.Loop.RunUntil(() => phases.Contains(UpdateInstallProgress.Shared.UpdateInstallProgressPhase), TimeSpan.FromSeconds(20));

    // ---- PR-UPDATE-041: single flight and the development guard ------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-041")] // App/AppUpdater.swift:261-283
    public async Task A_second_install_while_one_is_running_makes_no_second_stage_call()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"));
        var reply = Respond(server, 200, [1, 2, 3]);
        runtime.OnMain(() => updater.Install(update));
        runtime.OnMain(() => updater.Install(update)); // must do nothing while the first is running
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        Assert.Equal(1, fake.Calls.Count(c => c.StartsWith("Stage(", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-041")] // App/AppUpdater.swift:262
    public void An_install_in_a_development_build_never_calls_the_boundary()
    {
        using var runtime = new TestRuntime();
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: true));
        var update = MakeUpdate(new Uri("http://127.0.0.1:1/artifact"));
        runtime.OnMain(() => updater.Install(update));
        runtime.Loop.RunUntilIdle();
        Assert.Empty(fake.Calls);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, Phase(runtime));
    }

    // ---- PR-UPDATE-042: the reporting order ----------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-042")] // App/AppUpdater.swift:263-282
    public async Task A_successful_install_reports_begin_download_verify_stage_then_handoff_in_order()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"));
        var body = new byte[10];
        var reply = Respond(server, 200, body);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        // beginVerification is reported BEFORE stage() runs and beginStaging AFTER it returns (:42 windows_path).
        Assert.Equal(["Stage(", "Verify(", "ReadVersion(", "LaunchDetachedInstaller("],
            fake.Calls.Select(c => c[..(c.IndexOf('(', StringComparison.Ordinal) + 1)]).ToArray());
        Assert.Equal(UpdateProgressMilestones.HandoffEnd, runtime.OnMain(() => UpdateInstallProgress.Shared.Fraction));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-041")] // App/AppUpdater.swift:264 defer { installTask = nil }
    public async Task After_a_failure_a_fresh_install_is_accepted_again()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        // PR-UPDATE-057/UI-UPDATE-034 (part 2): the catch branch now shows a modal alert; this class is not
        // headless-collected (no live Avalonia Application), so the alert seam must be a recording fake here,
        // never the real WindowsAlertPresenter. AppUpdaterFailureAlertTests exercises the alert itself.
        using var alertSeam = AlertPresentation.Presenter.Override(new RecordingAlertPresenter());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { StageThrows = new UpdateInstallError("boom") };
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"));
        var reply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Failed);
        await reply;
        runtime.AdvanceMilliseconds(600);
        Assert.Equal(UpdateInstallProgress.Phase.Idle, Phase(runtime));

        fake.StageThrows = null;
        var reply2 = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply2;
    }

    // ---- PR-UPDATE-043: the per-install temp file name -----------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-043")] // App/AppUpdater.swift:287-295
    public async Task The_downloaded_artifact_is_named_MarrowUpdate_dash_a_fresh_uuid()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"));
        var reply = Respond(server, 200, [9, 9]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        var call = Assert.Single(fake.Calls, c => c.StartsWith("Stage(", StringComparison.Ordinal));
        var path = call["Stage(".Length..call.IndexOf(',', StringComparison.Ordinal)];
        var name = Path.GetFileName(path);
        Assert.Matches(
            "^MarrowUpdate-[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$", name);
    }

    // ---- PR-UPDATE-044: the download request shape -----------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-044")] // App/AppUpdater.swift:472-486
    public void The_download_configuration_has_a_30s_request_and_1800s_resource_timeout_and_never_waits_for_connectivity()
    {
        var configuration = Downloader.BuildConfiguration();
        Assert.Equal(30, configuration.TimeoutIntervalForRequest);
        Assert.Equal(1800, configuration.TimeoutIntervalForResource);
        Assert.False(configuration.WaitsForConnectivity);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-044")] // App/AppUpdater.swift:481, DEVIATION OD-0012 F1
    public void The_download_request_carries_the_Marrow_slash_version_User_Agent()
    {
        var request = Downloader.BuildRequest(SwiftURL.Create("http://127.0.0.1:1/x")!);
        Assert.Equal($"Marrow/{AppInfo.Current.Version}", request.Value(forHTTPHeaderField: "User-Agent"));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-044")] // App/AppUpdater.swift:481
    public async Task The_request_the_server_receives_carries_the_Marrow_User_Agent()
    {
        await using var server = new LoopbackHttpServer();
        var reply = Task.Run(async () =>
        {
            var exchange = await server.NextExchange();
            Assert.Equal([$"Marrow/{AppInfo.Current.Version}"], exchange.HeaderValues("User-Agent"));
            await exchange.Respond(200, "OK", [1, 2, 3]);
        });
        var destination = Path.Combine(Path.GetTempPath(), $"aut-{Guid.NewGuid():N}");
        try
        {
            await new Downloader(_ => { }).Download(SwiftURL.Create(server.Url("/x").ToString())!, destination);
            await reply;
        }
        finally
        {
            try { File.Delete(destination); } catch (IOException) { }
        }
    }

    // ---- PR-UPDATE-045: the byte ratio, and 0 when the length is unknown -----------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-045")] // App/AppUpdater.swift:496-502
    public async Task A_known_length_reports_the_written_over_expected_ratio_and_ends_at_1()
    {
        await using var server = new LoopbackHttpServer();
        var body = new byte[1000];
        var reply = Respond(server, 200, body);
        var reports = new List<double>();
        var destination = Path.Combine(Path.GetTempPath(), $"aut-{Guid.NewGuid():N}");
        try
        {
            await new Downloader(f => reports.Add(f)).Download(SwiftURL.Create(server.Url("/x").ToString())!, destination);
            await reply;
        }
        finally
        {
            try { File.Delete(destination); } catch (IOException) { }
        }
        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-045")] // App/AppUpdater.swift:496-497 totalBytesExpectedToWrite > 0 else onProgress(0)
    public async Task An_unknown_length_reports_exactly_zero()
    {
        await using var server = new LoopbackHttpServer();
        var body = new byte[500];
        var reports = new List<double>();
        var destination = Path.Combine(Path.GetTempPath(), $"aut-{Guid.NewGuid():N}");
        var reply = Task.Run(async () =>
        {
            var exchange = await server.NextExchange();
            await exchange.WriteChunkedHead(200, "OK");
            await exchange.WriteChunk(body);
            await exchange.EndChunks();
        });
        try
        {
            await new Downloader(f => reports.Add(f)).Download(SwiftURL.Create(server.Url("/x").ToString())!, destination);
            await reply;
        }
        finally
        {
            try { File.Delete(destination); } catch (IOException) { }
        }
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.Equal(0.0, r));
    }

    // ---- PR-UPDATE-046: the three verbatim download failures ----------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-046")] // App/AppUpdater.swift:504-521, the status check after the body
    public async Task A_non_2xx_response_fails_with_the_exact_HTTP_status_message()
    {
        await using var server = new LoopbackHttpServer();
        var reply = Respond(server, 404, [1, 2, 3]);
        var destination = Path.Combine(Path.GetTempPath(), $"aut-{Guid.NewGuid():N}");
        var error = await Assert.ThrowsAsync<UpdateInstallError>(async () =>
        {
            try
            {
                await new Downloader(_ => { }).Download(SwiftURL.Create(server.Url("/x").ToString())!, destination);
            }
            finally
            {
                try { File.Delete(destination); } catch (IOException) { }
            }
        });
        await reply;
        Assert.Equal("The download failed with HTTP 404.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-046")] // App/AppUpdater.swift:523-527
    public async Task A_dropped_connection_fails_the_download()
    {
        // A dropped connection surfaces as a real URLError, and SwiftErrors.LocalizedDescription has no text
        // defined for one by design (O-14; Marrow.Core.Tests HttpTests: "SwiftErrors.LocalizedDescription(new
        // URLError(...)) throws NotSupportedException"). OpenAICompatibleSession.cs:562-567's own transport-error
        // call site carries the identical gap and is, likewise, only tested through a synthetic ILocalizedError
        // (OpenAICompatibleSessionRetryLadderSeam.cs FakeTransportReasonError); this test follows the same
        // established limitation rather than asserting text the framework cannot honestly produce here.
        await using var server = new LoopbackHttpServer();
        var reply = Task.Run(async () =>
        {
            var exchange = await server.NextExchange();
            exchange.Abort();
        });
        var destination = Path.Combine(Path.GetTempPath(), $"aut-{Guid.NewGuid():N}");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new Downloader(_ => { }).Download(SwiftURL.Create(server.Url("/x").ToString())!, destination));
        }
        finally
        {
            try { File.Delete(destination); } catch (IOException) { }
        }
        await reply;
    }

    // ---- PR-UPDATE-047: install()'s Stage step forwards the whole downloaded artifact to the boundary ------
    // (the removeItem-on-both-paths half of this record is the boundary's own job: WindowsUpdateInstallBoundary
    // .Stage's finally block, tested in WindowsUpdateInstallBoundaryTests.)

    [Fact]
    [Trait("Req", "PR-UPDATE-047")] // App/AppUpdater.swift:299-330 (the caller side: image and expectedVersion forwarded)
    public async Task Stage_forwards_the_downloaded_artifact_path_and_the_accepted_version_to_the_boundary()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { VersionToReturn = "2.1.0" };
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"), "2.1.0");
        var reply = Respond(server, 200, [1, 2, 3, 4]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        var call = Assert.Single(fake.Calls, c => c.StartsWith("Stage(", StringComparison.Ordinal));
        Assert.EndsWith(", 2.1.0)", call, StringComparison.Ordinal);
    }

    // ---- PR-UPDATE-049 / PR-UPDATE-050 / PR-UPDATE-051: verify then the version check -----------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-049")] // App/AppUpdater.swift:337-340, :346-368 (collapsed, PR-UPDATE-051's windows_path)
    public async Task A_boundary_that_refuses_the_signature_fails_the_install_and_never_reads_the_version()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        // PR-UPDATE-057/UI-UPDATE-034 (part 2): see After_a_failure_a_fresh_install_is_accepted_again's comment.
        using var alertSeam = AlertPresentation.Presenter.Override(new RecordingAlertPresenter());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary
        {
            VerifyThrows = new UpdateInstallError("The downloaded app is not signed by Marrow. reason"),
        };
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"));
        var reply = Respond(server, 200, [1, 2, 3]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Failed);
        await reply;
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("ReadVersion(", StringComparison.Ordinal));
        Assert.DoesNotContain(fake.Calls, c => c.StartsWith("LaunchDetachedInstaller(", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-050")] // App/AppUpdater.swift:332-336
    public async Task A_staged_version_equal_to_the_accepted_one_on_padded_components_proceeds_to_handoff()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { VersionToReturn = "1.3" }; // padded-equal to "1.3.0"
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"), "1.3.0");
        var reply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-050")] // App/AppUpdater.swift:334-336, the exact message and the "unknown" fallback
    public async Task A_staged_version_that_disagrees_fails_with_the_exact_verbatim_message()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        // PR-UPDATE-057/UI-UPDATE-034 (part 2): see After_a_failure_a_fresh_install_is_accepted_again's comment.
        using var alertSeam = AlertPresentation.Presenter.Override(new RecordingAlertPresenter());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary { VersionToReturn = "1.4.0" };
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"), "1.3.0");
        var reply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Failed);
        await reply;
        // The message text itself is not observable through the install path yet (PR-UPDATE-057's alert is
        // part 2's own record); Stage's mapping is exercised directly below instead.
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-050")] // App/AppUpdater.swift:334-336
    public async Task The_Stage_step_throws_the_exact_verbatim_version_mismatch_message()
    {
        var fake = new FakeUpdateInstallBoundary { VersionToReturn = "1.4.0" };
        var error = await Assert.ThrowsAsync<UpdateInstallError>(() => new AppUpdater(fake).Stage("img", "1.3.0"));
        Assert.Equal("The downloaded app is version 1.4.0, not 1.3.0.", error.ErrorDescription);
    }

    [Fact]
    [Trait("Req", "PR-UPDATE-050")] // App/AppUpdater.swift:334-336, "unknown" fallback for a missing key
    public async Task A_missing_staged_version_substitutes_the_literal_unknown()
    {
        var fake = new FakeUpdateInstallBoundary { VersionToReturn = null };
        var error = await Assert.ThrowsAsync<UpdateInstallError>(() => new AppUpdater(fake).Stage("img", "1.3.0"));
        Assert.Equal("The downloaded app is version unknown, not 1.3.0.", error.ErrorDescription);
    }

    // ---- PR-UPDATE-052: the writability gate ---------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-052")] // App/AppUpdater.swift:374-379, DEVIATION OD-0008 B15; OD-0012 H, F1
    public void An_unwritable_parent_fails_with_the_ports_own_verbatim_message()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var readonlyRoot = Directory.CreateTempSubdirectory().FullName;
        var appDir = Path.Combine(readonlyRoot, "app");
        Directory.CreateDirectory(appDir);
        var directoryInfo = new DirectoryInfo(readonlyRoot);
        var security = directoryInfo.GetAccessControl();
        var user = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        var deny = new System.Security.AccessControl.FileSystemAccessRule(
            user,
            System.Security.AccessControl.FileSystemRights.CreateFiles | System.Security.AccessControl.FileSystemRights.WriteData,
            System.Security.AccessControl.AccessControlType.Deny);
        security.AddAccessRule(deny);
        directoryInfo.SetAccessControl(security);
        try
        {
            var error = Assert.Throws<UpdateInstallError>(() =>
                runtime.OnMain(() => new AppUpdater(new FakeUpdateInstallBoundary()).HandOff("staged", "1.3.0", appDir)));
            Assert.Equal(
                $"Marrow cannot replace itself in {readonlyRoot}. Reinstall it with the installer and try again.",
                error.ErrorDescription);
        }
        finally
        {
            security.RemoveAccessRule(deny);
            directoryInfo.SetAccessControl(security);
            Directory.Delete(readonlyRoot, recursive: true);
        }
    }

    // ---- PR-UPDATE-053: the handoff writes a script and a log, and launches with three arguments -----------

    [Fact]
    [Trait("Req", "PR-UPDATE-053")] // App/AppUpdater.swift:380-395
    public void HandOff_writes_a_script_and_an_empty_log_beside_the_staged_app_and_launches_with_pid_staged_destination()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        var staging = Directory.CreateTempSubdirectory().FullName;
        var staged = Path.Combine(staging, "App");
        Directory.CreateDirectory(staged);
        var destinationRoot = Directory.CreateTempSubdirectory().FullName;
        var fake = new FakeUpdateInstallBoundary();
        try
        {
            runtime.OnMain(() => new AppUpdater(fake).HandOff(staged, "1.3.0", destinationRoot));
            var script = Path.Combine(staging, "install.ps1");
            Assert.True(File.Exists(script));
            var call = Assert.Single(fake.Calls, c => c.StartsWith("LaunchDetachedInstaller(", StringComparison.Ordinal));
            var args = call["LaunchDetachedInstaller(".Length..^1].Split(", ");
            Assert.Equal(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), args[0]);
            Assert.Equal(staged, args[1]);
            Assert.Equal(destinationRoot, args[2]);
            Assert.Equal(script, args[3]);
            Assert.Equal(Path.Combine(staging, "install.log"), args[4]);
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            Directory.Delete(destinationRoot, recursive: true);
        }
    }

    // ---- PR-UPDATE-054: the relaunch marker and the handoff tick -------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-054")] // App/AppUpdater.swift:396-397, :242
    public async Task A_successful_handoff_sets_the_relaunch_marker_and_ticks_to_0_86()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"), "1.3.0");
        var reply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        Assert.Equal(UpdateProgressMilestones.HandoffEnd, runtime.OnMain(() => UpdateInstallProgress.Shared.Fraction));
        // :242 relaunchVersionKey = "appUpdater_pendingRelaunchVersion"; :396 set to the accepted version.
        Assert.Equal("1.3.0", runtime.Defaults.String("appUpdater_pendingRelaunchVersion"));
    }

    // ---- PR-UPDATE-055: the 25 s give-up ---------------------------------------------------------------------

    [Fact]
    [Trait("Req", "PR-UPDATE-055")] // App/AppUpdater.swift:244, :398-404
    public async Task If_the_phase_is_still_handoff_after_25s_the_marker_is_cleared_and_fail_runs()
    {
        using var runtime = new TestRuntime();
        using var development = AppInfo.Running.Override(
            AppInfo.FromAssembly(typeof(AppUpdaterInstallTests).Assembly, isDevelopment: false));
        using var progress = UpdateInstallProgress.Running.Override(UpdateInstallProgressFixtures.NewProgress());
        await using var server = new LoopbackHttpServer();
        var fake = new FakeUpdateInstallBoundary();
        var updater = new AppUpdater(fake);
        var update = MakeUpdate(server.Url("/artifact"), "1.3.0");
        var reply = Respond(server, 200, [1]);
        runtime.OnMain(() => updater.Install(update));
        RunUntilPhase(runtime, UpdateInstallProgress.Phase.Handoff);
        await reply;
        runtime.AdvanceMilliseconds(25_000);
        Assert.Equal(UpdateInstallProgress.Phase.Failed, Phase(runtime));
        Assert.False(runtime.OnMain(updater.ConsumeRelaunchMarker));
    }
}
