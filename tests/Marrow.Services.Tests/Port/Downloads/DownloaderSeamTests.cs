using System.Net;
using Marrow.Services.Port.Downloads;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Services.Tests.Port.Downloads;

// SK-3 (ED-293 (6)): the product seam fails closed in a test assembly. The downloader's ambient transport is SK-0's handler with no fake server,
// so a test that forgets to give its Downloader a handler cannot reach the network, loopback included.
[Trait("Spec", "DL-3")]
public sealed class DownloaderSeamTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(1_000, seed: 61);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public void The_ambient_transport_in_this_test_assembly_is_the_one_with_no_fake_server()
    {
        var handler = Assert.IsType<GuardedDownloadHandler>(DownloaderSeams.Handler.Current);
        Assert.True(handler.NoFakeConfigured);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_downloader_made_without_a_handler_is_refused_before_any_connection_even_to_a_live_loopback_server()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var downloader = new Downloader(rig.PartFolder, new DownloadPolicy(["127.0.0.1"]));

        var outcome = await downloader.Start(rig.Request("/a.bin", Artifact)).Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Offline), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal(0, rig.Server.ConnectionCount);
        Assert.Contains(((GuardedDownloadHandler)DownloaderSeams.Handler.Current).Refusals, r => r.StartsWith("GET https://127.0.0.1:", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task A_test_can_override_the_ambient_transport_for_its_own_flow()
    {
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        using var scope = DownloaderSeams.Handler.Override(rig.Handler);

        var outcome = await new Downloader(rig.PartFolder, new DownloadPolicy(["127.0.0.1"])).Start(rig.Request("/a.bin", Artifact)).Completion
            .WaitAsync(FakeDownloadServer.Guard);

        Assert.True(outcome.Succeeded);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public void The_production_transport_follows_no_redirect_decompresses_nothing_sends_no_cookie_and_uses_the_system_proxy()
    {
        using var handler = Assert.IsType<SocketsHttpHandler>(DownloaderSeams.CreateProductionHandler());
        Assert.Equal((false, DecompressionMethods.None, false, true, (IWebProxy?)null),
            (handler.AllowAutoRedirect, handler.AutomaticDecompression, handler.UseCookies, handler.UseProxy, handler.Proxy));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-3")]
    public async Task On_the_production_transport_a_loopback_server_this_PC_does_not_trust_is_a_tls_failure()
    {
        // Windows review: the real handler (SChannel on Windows, the system proxy), not SK-0's guarded one, against the loopback fake only. The url is
        // 127.0.0.1, and the test stops before sending if this PC's proxy settings would carry it anywhere else.
        await using var rig = new DownloadRig();
        rig.Server.Serve("/a.bin", Artifact);
        var url = rig.Server.Url("/a.bin");
        if (!HttpClient.DefaultProxy.IsBypassed(url) && HttpClient.DefaultProxy.GetProxy(url) is { } proxy && proxy != url) return;
        using var handler = DownloaderSeams.CreateProductionHandler();

        var outcome = await new Downloader(rig.PartFolder, new DownloadPolicy(["127.0.0.1"]), handler: handler).Start(rig.Request("/a.bin", Artifact, source: "nodejs.org"))
            .Completion.WaitAsync(FakeDownloadServer.Guard);

        Assert.Equal((DownloadFailureKind.Unreachable, DownloadCause.Tls), (outcome.Failure!.Kind, outcome.Failure.Cause));
        Assert.Equal("Couldn't reach nodejs.org.", outcome.Failure.Message);
        Assert.True(rig.Server.ConnectionCount >= 1, "the handshake reached the loopback server");
        Assert.Empty(rig.Server.Requests);
    }
}
