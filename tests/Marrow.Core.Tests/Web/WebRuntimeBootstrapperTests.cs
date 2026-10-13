using Marrow.Core.Web;

namespace Marrow.Core.Tests.Web;

// nth web surfaces, unit W23 (DESIGN 2.1): Microsoft's Evergreen bootstrapper as plain rules. Where it comes from, the documented
// silent switches, who must have signed it, how big it may be, the calm sentence for each way the flow can end badly, and the
// two cards the runtime flow adds to DESIGN 2.7's table.
[Trait("Decision", "ED-NEW-web-w23-2")]
[Trait("Feature", "W-23")]
public sealed class WebRuntimeBootstrapperTests
{
    [Fact]
    public void The_bootstrapper_comes_from_Microsofts_documented_link_over_https_with_the_documented_silent_switches()
    {
        Assert.Equal("https://go.microsoft.com/fwlink/p/?LinkId=2124703", WebRuntimeBootstrapper.DownloadUrl);
        Assert.Equal("/silent /install", WebRuntimeBootstrapper.Arguments);
        Assert.Equal("MicrosoftEdgeWebview2Setup.exe", WebRuntimeBootstrapper.FileName);
        Assert.True(WebRuntimeBootstrapper.IsAcceptableSource(new Uri(WebRuntimeBootstrapper.DownloadUrl)));
        Assert.StartsWith("https://", WebRuntimeBootstrapper.RuntimePageUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/x/MicrosoftEdgeWebview2Setup.exe", true)]
    [InlineData("HTTPS://go.microsoft.com/x", true)]
    [InlineData("http://go.microsoft.com/fwlink/p/?LinkId=2124703", false)]
    [InlineData("ftp://go.microsoft.com/x", false)]
    [InlineData("file:///C:/x.exe", false)]
    public void The_download_is_accepted_only_over_https_at_the_end_of_the_redirects(string url, bool accepted) =>
        Assert.Equal(accepted, WebRuntimeBootstrapper.IsAcceptableSource(new Uri(url)));

    [Fact]
    public void A_missing_or_relative_address_is_never_a_source()
    {
        Assert.False(WebRuntimeBootstrapper.IsAcceptableSource(null));
        Assert.False(WebRuntimeBootstrapper.IsAcceptableSource(new Uri("/fwlink/x", UriKind.Relative)));
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(65535L, false)]
    [InlineData(65536L, true)]
    [InlineData(2_000_000L, true)]
    [InlineData(8_388_608L, true)]
    [InlineData(8_388_609L, false)]
    [InlineData(-1L, false)]
    public void Only_a_file_the_size_of_the_bootstrapper_is_accepted(long bytes, bool accepted) =>
        Assert.Equal(accepted, WebRuntimeBootstrapper.IsAcceptableSize(bytes));

    [Theory]
    [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", true)]
    [InlineData("O=Microsoft Corporation, CN=Microsoft Corporation", true)]
    [InlineData("cn=Microsoft Corporation", true)]
    [InlineData("CN=\"Microsoft Corporation\", O=Microsoft Corporation", true)]
    [InlineData("CN=Microsoft Corporation Limited, O=Microsoft Corporation", false)]
    [InlineData("CN=Not Microsoft Corporation", false)]
    [InlineData("CN=Contoso, O=Microsoft Corporation", false)]
    [InlineData("CN=Contoso, OU=CN=Microsoft Corporation", false)]
    [InlineData("O=Microsoft Corporation", false)]
    [InlineData("CN=microsoft corporation", false)]
    [InlineData("CN=\"Contoso, CN=Microsoft Corporation\"", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_signer_must_be_named_Microsoft_Corporation_as_its_common_name_and_a_look_alike_does_not_pass(string? subject, bool accepted) =>
        Assert.Equal(accepted, WebRuntimeBootstrapper.IsAcceptableSigner(subject));

    [Fact]
    public void The_waits_are_bounded()
    {
        Assert.Equal(TimeSpan.FromMinutes(2), WebRuntimeBootstrapper.DownloadTimeout);
        Assert.Equal(TimeSpan.FromMinutes(10), WebRuntimeBootstrapper.InstallTimeout);
    }

    [Fact]
    public void Every_way_the_flow_can_fail_has_its_own_calm_sentence()
    {
        var failures = Enum.GetValues<WebRuntimeInstallFailure>().Where(f => f != WebRuntimeInstallFailure.None).ToArray();
        var reasons = failures.Select(f => WebRuntimeBootstrapper.ReasonFor(f, "100.0.1185.36")).ToArray();
        Assert.Equal("", WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.None));
        Assert.All(reasons, reason =>
        {
            Assert.False(string.IsNullOrWhiteSpace(reason));
            Assert.EndsWith(".", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("\u2014", reason, StringComparison.Ordinal);
            Assert.DoesNotContain("exception", reason, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Equal(reasons.Length, reasons.Distinct().Count());
    }

    [Fact]
    public void A_runtime_that_is_still_too_old_says_which_version_it_found_and_who_can_change_that()
    {
        var reason = WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.StillTooOld, "100.0.1185.36");
        Assert.Contains("100.0.1185.36", reason, StringComparison.Ordinal);
        Assert.Contains("organization", reason, StringComparison.Ordinal);
        Assert.Contains("unknown", WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.StillTooOld, null), StringComparison.Ordinal);
    }

    [Fact]
    public void A_signature_failure_says_the_file_was_not_run()
    {
        Assert.Contains("did not run it", WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.NotSigned), StringComparison.Ordinal);
    }

    [Fact]
    public void The_installing_card_has_no_buttons_and_the_update_wording_when_updating()
    {
        var install = WebFailureCard.RuntimeInstalling(updating: false);
        Assert.Equal(WebFailureKind.RuntimeInstalling, install.Kind);
        Assert.Equal("Installing the WebView2 runtime.", install.Title);
        Assert.Equal("nth is getting it from Microsoft. This takes a moment.", install.Body);
        Assert.Empty(install.Buttons);
        Assert.True(install.ShowsCard);
        Assert.False(install.Dimmed);
        Assert.Equal("Updating the WebView2 runtime.", WebFailureCard.RuntimeInstalling(updating: true).Title);
        Assert.False(WebFailureCard.RuntimeInstalling(updating: false, showsCard: false).ShowsCard);
        Assert.Equal(install, WebFailureCard.For(WebFailureKind.RuntimeInstalling));
    }

    [Fact]
    public void The_failed_card_gives_the_reason_another_go_and_Microsofts_own_page()
    {
        var reason = WebRuntimeBootstrapper.ReasonFor(WebRuntimeInstallFailure.Offline);
        var install = WebFailureCard.RuntimeInstallFailed(updating: false, reason);
        Assert.Equal(WebFailureKind.RuntimeInstallFailed, install.Kind);
        Assert.Equal("The WebView2 runtime did not install.", install.Title);
        Assert.Equal(reason, install.Body);
        Assert.Equal([WebFailureAction.InstallRuntime, WebFailureAction.OpenRuntimePage], install.Buttons.Select(b => b.Action));
        Assert.Equal(["Try again", "Open Microsoft's page"], install.Buttons.Select(b => b.Title));
        Assert.False(install.Dimmed);

        var update = WebFailureCard.RuntimeInstallFailed(updating: true, reason);
        Assert.Equal("The WebView2 runtime did not update.", update.Title);
        Assert.Equal([WebFailureAction.UpdateRuntime, WebFailureAction.OpenRuntimePage], update.Buttons.Select(b => b.Action));
        Assert.Throws<ArgumentException>(() => WebFailureCard.For(WebFailureKind.RuntimeInstallFailed));
    }

    [Theory]
    [InlineData(WebFailureKind.RuntimeMissing, true)]
    [InlineData(WebFailureKind.RuntimeTooOld, true)]
    [InlineData(WebFailureKind.RuntimeInstalling, true)]
    [InlineData(WebFailureKind.RuntimeInstallFailed, true)]
    [InlineData(WebFailureKind.RendererCrashed, false)]
    [InlineData(WebFailureKind.RendererHung, false)]
    [InlineData(WebFailureKind.BrowserGone, false)]
    public void The_runtime_kinds_are_the_four_the_flow_owns(WebFailureKind kind, bool runtime) =>
        Assert.Equal(runtime, kind.IsRuntime());
}
