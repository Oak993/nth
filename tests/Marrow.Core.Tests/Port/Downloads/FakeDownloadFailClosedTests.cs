using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Marrow.Core.Port;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the download seam fails closed when no fake is configured. A handler made with no
// fake server refuses every request before any connection, loopback included; the existing HTTP seam (URLSession.Transport) takes
// the same handler.
[Trait("Spec", "DL-0")]
public sealed class FakeDownloadFailClosedTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(1000, seed: 13);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task With_no_fake_configured_every_request_is_refused_even_to_a_live_loopback_server()
    {
        await using var live = new LoopbackHttpServer();
        await using var fake = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler();
        using var client = DownloadFixtures.Client(handler);
        var urls = new[] { live.Url("/x"), fake.Url("/a.bin"), new Uri("https://github.com/anthropics/skills") };

        foreach (var url in urls)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url).WaitAsync(FakeDownloadServer.Guard));
            Assert.Equal(HttpRequestError.ConnectionError, error.HttpRequestError);
            Assert.Equal(SocketError.ConnectionRefused, Assert.IsType<SocketException>(error.InnerException).SocketErrorCode);
        }

        Assert.True(handler.NoFakeConfigured);
        Assert.Equal(urls.Select(u => "GET " + u), handler.Refusals);
        Assert.Equal(0, live.ConnectionCount);
        Assert.Equal(0, fake.ConnectionCount);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_handler_with_a_fake_is_not_the_fail_closed_one()
    {
        await using var fake = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(fake);

        Assert.False(handler.NoFakeConfigured);
        Assert.Empty(handler.Refusals);
        using var client = DownloadFixtures.Client(handler);
        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, fake.Url("/a.bin"))).Body);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Through_the_apps_http_seam_a_fake_serves_the_artifact_and_no_fake_is_a_connection_error()
    {
        await using var fake = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        var session = new URLSession(URLSessionConfiguration.Ephemeral);

        using (var seam = URLSession.Transport.Override(FakeDownloadTransport.CreateHandler(fake)))
        {
            var (data, response) = await session.Data(fake.Url("/a.bin")).WaitAsync(FakeDownloadServer.Guard);
            Assert.Equal(Artifact.Bytes, data);
            Assert.Equal(200, ((HTTPURLResponse)response).StatusCode);
        }

        using (var seam = URLSession.Transport.Override(FakeDownloadTransport.CreateHandler()))
        {
            var error = await Assert.ThrowsAsync<URLError>(() => session.Data(fake.Url("/b.bin")));
            Assert.Equal(URLError.Code.CannotConnectToHost, error.URLErrorCode);
        }

        Assert.Single(await fake.WaitForRequests(1));
    }
}
