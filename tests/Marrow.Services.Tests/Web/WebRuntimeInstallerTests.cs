using Marrow.Core.Web;
using Marrow.Services.Web;

namespace Marrow.Services.Tests.Web;

// nth web surfaces, unit W23 (DESIGN 2.1 and 2.7): the runtime flow. When the WebView2 runtime is missing or too old the pane's card
// offers Install or Update; only that press downloads Microsoft's bootstrapper, checks its signature, runs it without elevation, looks
// for the runtime again and gives the waiting panes their page, or a card that says why not. Every seam is a fake here: no test
// reaches Microsoft, checks a real signature or starts an installer (R-TEST-10); the native tests do, alone.
[Trait("Decision", "ED-NEW-web-w23-2")]
[Trait("Feature", "W-23")]
public sealed class WebRuntimeInstallerTests
{
    [Fact]
    public void Nothing_runs_until_a_card_button_is_pressed()
    {
        using var rig = new RuntimeFlowRig();

        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");

        Assert.Equal(WebFailureKind.RuntimeMissing, view.Failure?.Kind);
        Assert.Equal(0, rig.Source.Calls);
        Assert.Empty(rig.Runner.Runs);
        Assert.Equal(WebRuntimeInstallPhase.Idle, rig.Installer.Phase);
        Assert.Null(rig.Installer.LastResult);
    }

    [Fact]
    public void Install_downloads_checks_runs_without_a_window_and_gives_the_waiting_pane_its_page()
    {
        using var rig = new RuntimeFlowRig();
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");
        var phases = new List<WebRuntimeInstallPhase>();
        rig.Installer.PhaseChanged += phases.Add;
        WebFailureCard? shownWhileRunning = null;
        rig.Runner.WhileRunning = () =>
        {
            shownWhileRunning = view.Failure;
            rig.RuntimeArrives();
        };

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(["download", "verify", "run", "file disposed"], rig.Log);
        var run = Assert.Single(rig.Runner.Runs);
        Assert.Equal(@"C:\fake\MicrosoftEdgeWebview2Setup.exe", run.Path);
        Assert.Equal("/silent /install", run.Arguments);
        Assert.Equal(TimeSpan.FromMinutes(10), run.Timeout);
        Assert.Equal([@"C:\fake\MicrosoftEdgeWebview2Setup.exe"], rig.Signature.Checked);
        Assert.Equal(WebFailureKind.RuntimeInstalling, shownWhileRunning?.Kind);
        Assert.Empty(shownWhileRunning!.Buttons);
        Assert.Equal("Installing the WebView2 runtime.", shownWhileRunning.Title);
        Assert.Null(view.Failure);
        Assert.Equal(WebLiveness.Pending, view.Liveness);
        Assert.Null(rig.Web.Surface.RuntimeFlowCard);
        Assert.Equal([WebRuntimeInstallPhase.Downloading, WebRuntimeInstallPhase.Verifying, WebRuntimeInstallPhase.Installing, WebRuntimeInstallPhase.Succeeded], phases);
        Assert.True(rig.Installer.LastResult!.Succeeded);
        Assert.Equal("141.0.3537.71", rig.Installer.LastResult.Version);
        Assert.Equal(0, rig.Installer.LastResult.ExitCode);
        view.AttachNative(1, 2);
        Assert.Contains("Navigate https://a.test/", WebRig.Fake(view).Log);
    }

    [Fact]
    public void Update_for_a_runtime_that_is_too_old_uses_the_update_wording_and_succeeds_the_same_way()
    {
        using var rig = new RuntimeFlowRig(installed: "100.0.1185.36");
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");
        Assert.Equal(WebFailureKind.RuntimeTooOld, view.Failure?.Kind);
        string? title = null;
        rig.Runner.WhileRunning = () =>
        {
            title = view.Failure?.Title;
            rig.RuntimeArrives();
        };

        view.InvokeFailureAction(WebFailureAction.UpdateRuntime);

        Assert.Equal("Updating the WebView2 runtime.", title);
        Assert.Null(view.Failure);
        Assert.True(rig.Installer.LastResult!.Succeeded);
    }

    [Theory]
    [InlineData("CN=Microsoft Corporation Limited, O=Contoso")]
    [InlineData("CN=Contoso, O=Microsoft Corporation")]
    [InlineData("CN=Not Microsoft Corporation")]
    [InlineData(null)]
    public void A_download_with_a_valid_signature_from_anyone_but_Microsoft_is_never_run(string? signer)
    {
        using var rig = new RuntimeFlowRig();
        rig.Signature.Result = new(true, signer);
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Empty(rig.Runner.Runs);
        Assert.Equal(WebRuntimeInstallFailure.NotSigned, rig.Installer.LastResult!.Failure);
        Assert.Equal(WebFailureKind.RuntimeInstallFailed, view.Failure?.Kind);
        Assert.Equal("The file did not pass the signature check, so nth did not run it.", view.Failure!.Body);
        Assert.True(rig.Source.Last!.Disposed);
    }

    [Fact]
    public void A_signature_that_does_not_verify_is_refused_even_with_Microsofts_name_on_it()
    {
        using var rig = new RuntimeFlowRig();
        rig.Signature.Result = new(false, "CN=Microsoft Corporation");
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Empty(rig.Runner.Runs);
        Assert.Equal(WebRuntimeInstallFailure.NotSigned, rig.Installer.LastResult!.Failure);
        Assert.Equal(WebRuntimeInstallPhase.Failed, rig.Installer.Phase);
        Assert.True(rig.Source.Last!.Disposed);
    }

    [Theory]
    [InlineData(WebRuntimeInstallFailure.Offline, "nth could not reach Microsoft. Check your connection and try again.")]
    [InlineData(WebRuntimeInstallFailure.DownloadFailed, "The download from Microsoft did not finish. Try again in a moment.")]
    public void A_download_that_fails_shows_its_reason_and_nothing_is_checked_or_run(WebRuntimeInstallFailure failure, string reason)
    {
        using var rig = new RuntimeFlowRig();
        rig.Source.Fail = failure;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(["download"], rig.Log);
        Assert.Equal(failure, rig.Installer.LastResult!.Failure);
        Assert.Equal(WebFailureKind.RuntimeInstallFailed, view.Failure?.Kind);
        Assert.Equal("The WebView2 runtime did not install.", view.Failure!.Title);
        Assert.Equal(reason, view.Failure.Body);
        Assert.Equal([WebFailureAction.InstallRuntime, WebFailureAction.OpenRuntimePage], view.Failure.Buttons.Select(b => b.Action));
        Assert.Equal(WebLiveness.Closed, view.Liveness);
    }

    [Theory]
    [InlineData(WebRuntimeInstallFailure.CouldNotStart)]
    [InlineData(WebRuntimeInstallFailure.TimedOut)]
    public void An_installer_that_will_not_start_or_does_not_finish_in_time_ends_with_its_reason_and_the_file_is_removed(WebRuntimeInstallFailure failure)
    {
        using var rig = new RuntimeFlowRig();
        rig.Runner.Fail = failure;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(failure, rig.Installer.LastResult!.Failure);
        Assert.Equal(WebRuntimeBootstrapper.ReasonFor(failure), view.Failure!.Body);
        Assert.Null(rig.Installer.LastResult.ExitCode);
        Assert.True(rig.Source.Last!.Disposed);
        Assert.Equal(0, rig.Delays);
    }

    [Fact]
    public void The_runtime_found_afterwards_decides_not_the_exit_code()
    {
        using var rig = new RuntimeFlowRig();
        rig.Runner.Exit = 5;
        rig.Runner.WhileRunning = () => rig.RuntimeArrives();
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.True(rig.Installer.LastResult!.Succeeded);
        Assert.Equal(5, rig.Installer.LastResult.ExitCode);
        Assert.Null(view.Failure);
    }

    [Fact]
    public void A_failing_exit_with_no_runtime_is_a_failed_installer_and_does_not_wait()
    {
        using var rig = new RuntimeFlowRig();
        rig.Runner.Exit = 1603;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(WebRuntimeInstallFailure.InstallerFailed, rig.Installer.LastResult!.Failure);
        Assert.Equal(1603, rig.Installer.LastResult.ExitCode);
        Assert.Equal(0, rig.Delays);
        Assert.Equal(WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.InstallerFailed), view.Failure!.Body);
    }

    [Fact]
    public void A_clean_exit_waits_for_windows_to_report_the_runtime()
    {
        using var rig = new RuntimeFlowRig();
        rig.OnDelay = count =>
        {
            if (count == 3) rig.RuntimeArrives();
        };
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(3, rig.Delays);
        Assert.True(rig.Installer.LastResult!.Succeeded);
        Assert.Null(view.Failure);
    }

    [Fact]
    public void A_clean_exit_that_leaves_no_runtime_ends_after_the_probe_window()
    {
        using var rig = new RuntimeFlowRig();
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal((int)(WebRuntimeInstaller.ProbeWindow / WebRuntimeInstaller.ProbeInterval), rig.Delays);
        Assert.Equal(WebRuntimeInstallFailure.StillMissing, rig.Installer.LastResult!.Failure);
        Assert.Equal(WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.StillMissing), view.Failure!.Body);
    }

    [Fact]
    public void An_update_that_changes_nothing_names_the_version_it_found()
    {
        using var rig = new RuntimeFlowRig(installed: "100.0.1185.36");
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.UpdateRuntime);

        Assert.Equal(WebRuntimeInstallFailure.StillTooOld, rig.Installer.LastResult!.Failure);
        Assert.Equal("100.0.1185.36", rig.Installer.LastResult.Version);
        Assert.Equal("The WebView2 runtime did not update.", view.Failure!.Title);
        Assert.Contains("100.0.1185.36", view.Failure.Body, StringComparison.Ordinal);
        Assert.Equal([WebFailureAction.UpdateRuntime, WebFailureAction.OpenRuntimePage], view.Failure.Buttons.Select(b => b.Action));
    }

    [Fact]
    public void A_press_while_the_flow_runs_joins_it_one_download_one_installer()
    {
        using var rig = new RuntimeFlowRig();
        var finish = new TaskCompletionSource<int>();
        rig.Runner.Block = finish.Task;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);
        view.InvokeFailureAction(WebFailureAction.InstallRuntime);
        var joined = rig.Installer.RunAsync(updating: false);

        Assert.False(joined.IsCompleted);
        Assert.Equal(1, rig.Source.Calls);
        Assert.Single(rig.Runner.Runs);
        rig.RuntimeArrives();
        finish.SetResult(0);
        Assert.True(WebRig.Done(joined).Succeeded);
        Assert.Equal(1, rig.Source.Calls);
    }

    [Fact]
    public void A_runtime_installed_in_the_meantime_is_used_and_nothing_is_downloaded()
    {
        using var rig = new RuntimeFlowRig();
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://a.test/");
        rig.RuntimeArrives();

        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(0, rig.Source.Calls);
        Assert.Null(view.Failure);
        Assert.Equal(WebLiveness.Pending, view.Liveness);
        Assert.True(rig.Installer.LastResult!.Succeeded);
        Assert.Null(rig.Installer.LastResult.ExitCode);
    }

    [Fact]
    public void A_view_opened_after_the_runtime_turned_up_gets_its_page_without_a_press()
    {
        using var rig = new RuntimeFlowRig();
        var waiting = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");
        rig.RuntimeArrives();

        var later = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1", "https://b.test/");

        Assert.Null(later.Failure);
        Assert.Equal(WebLiveness.Pending, later.Liveness);
        Assert.Null(waiting.Failure);
        Assert.Equal(WebRuntimeState.Ready, rig.Web.Surface.Runtime.State);
    }

    [Fact]
    public void Views_opened_while_it_runs_start_with_the_working_card_and_sandboxed_ones_with_none()
    {
        using var rig = new RuntimeFlowRig();
        var finish = new TaskCompletionSource<int>();
        rig.Runner.Block = finish.Task;
        var first = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");
        first.InvokeFailureAction(WebFailureAction.InstallRuntime);

        var second = rig.Web.Surface.Open(WebSurfaceKind.PreviewTab, "w1");
        var artifact = rig.Web.Surface.Open(WebSurfaceKind.Artifact, "w1");

        Assert.Equal(WebFailureKind.RuntimeInstalling, second.Failure?.Kind);
        Assert.True(second.Failure!.ShowsCard);
        Assert.Equal(WebFailureKind.RuntimeInstalling, artifact.Failure?.Kind);
        Assert.False(artifact.Failure!.ShowsCard);
        rig.RuntimeArrives();
        finish.SetResult(0);
        Assert.Null(first.Failure);
        Assert.Null(second.Failure);
        Assert.Null(artifact.Failure);
    }

    [Fact]
    public void After_a_failure_Try_again_runs_the_flow_again_and_Microsofts_page_opens_in_the_default_browser()
    {
        using var rig = new RuntimeFlowRig();
        rig.Source.Fail = WebRuntimeInstallFailure.Offline;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");
        view.InvokeFailureAction(WebFailureAction.InstallRuntime);
        Assert.Equal(WebFailureKind.RuntimeInstallFailed, view.Failure?.Kind);

        view.InvokeFailureAction(WebFailureAction.OpenRuntimePage);
        Assert.Equal(["https://developer.microsoft.com/microsoft-edge/webview2/consumer/"], rig.Opened);
        Assert.Equal(1, rig.Source.Calls);

        rig.Source.Fail = null;
        rig.Runner.WhileRunning = () => rig.RuntimeArrives();
        view.InvokeFailureAction(WebFailureAction.InstallRuntime);

        Assert.Equal(2, rig.Source.Calls);
        Assert.Null(view.Failure);
        Assert.True(rig.Installer.LastResult!.Succeeded);
    }

    [Fact]
    public void Disposing_stops_a_download_in_flight_and_stops_listening()
    {
        var rig = new RuntimeFlowRig();
        var pending = new TaskCompletionSource<IBootstrapperFile>();
        rig.Source.Pending = pending;
        var view = rig.Web.Surface.Open(WebSurfaceKind.VisusTab, "w1");
        view.InvokeFailureAction(WebFailureAction.InstallRuntime);
        Assert.Equal(1, rig.Source.Calls);

        rig.Installer.Dispose();

        Assert.True(pending.Task.IsCanceled);
        Assert.Equal(WebRuntimeInstallPhase.Idle, rig.Installer.Phase);
        view.InvokeFailureAction(WebFailureAction.InstallRuntime);
        Assert.Equal(1, rig.Source.Calls);
        Assert.Throws<ObjectDisposedException>(() => { _ = rig.Installer.RunAsync(updating: false); });
        Assert.Throws<ObjectDisposedException>(rig.Installer.Attach);
        rig.Web.Dispose();
    }

    [Fact]
    public async Task The_real_seams_refuse_to_act_inside_a_test_host()
    {
        var download = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => new HttpBootstrapperSource().DownloadAsync(CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.Offline, download.Failure);
        var run = await Assert.ThrowsAsync<WebRuntimeInstallException>(() => new ProcessBootstrapperRunner().RunAsync(@"C:\nowhere\x.exe", "/silent /install", TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.Equal(WebRuntimeInstallFailure.CouldNotStart, run.Failure);
    }

    [Fact]
    public void The_platform_factory_builds_the_flow_on_the_real_seams()
    {
        using var rig = new WebRig(installed: null);
        using var installer = WebRuntimePlatform.Create(rig.Surface, _ => { });
        installer.Attach();
        Assert.Equal(WebRuntimeInstallPhase.Idle, installer.Phase);
    }
}
