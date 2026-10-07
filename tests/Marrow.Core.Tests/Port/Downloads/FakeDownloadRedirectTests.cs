using System.Net;
using System.Net.Http;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the redirects the fake download server can answer. The harness handler follows
// none: a test sees the 3xx and decides, as the downloader under test will (it refuses http, follows https).
[Trait("Spec", "DL-0")]
public sealed class FakeDownloadRedirectTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(2000, seed: 5);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_redirect_to_another_loopback_host_is_answered_as_a_302_and_the_other_host_serves_the_artifact()
    {
        await using var mirror = FakeDownloadServer.StartHttps("127.0.0.2").Serve("/mirror/a.bin", Artifact);
        await using var origin = FakeDownloadServer.StartHttps()
            .Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = mirror.Url("/mirror/a.bin") });
        using var handler = FakeDownloadTransport.CreateHandler(origin, mirror);
        using var client = DownloadFixtures.Client(handler);

        var (redirect, redirectBody) = await DownloadFixtures.Get(client, origin.Url("/a.bin"));
        var location = redirect.Headers.Location!;
        var (followed, body) = await DownloadFixtures.Get(client, location);

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Empty(redirectBody);
        Assert.Equal(mirror.Url("/mirror/a.bin"), location);
        Assert.NotEqual(origin.Url("/a.bin").Host, location.Host);
        Assert.Equal("https", location.Scheme);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
        Assert.Equal(["/a.bin"], (await origin.WaitForRequests(1)).Select(r => r.Path));
        Assert.Equal(["/mirror/a.bin"], (await mirror.WaitForRequests(1)).Select(r => r.Path));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_redirect_to_plain_http_is_answered_and_not_followed_by_the_harness()
    {
        await using var plain = FakeDownloadServer.StartPlainHttp().Serve("/a.bin", Artifact);
        await using var origin = FakeDownloadServer.StartHttps()
            .Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = plain.Url("/a.bin") });
        using var handler = FakeDownloadTransport.CreateHandler(origin, plain);
        using var client = DownloadFixtures.Client(handler);

        var (redirect, _) = await DownloadFixtures.Get(client, origin.Url("/a.bin"));

        Assert.Equal(HttpStatusCode.Found, redirect.StatusCode);
        Assert.Equal("http", redirect.Headers.Location!.Scheme);
        Assert.Empty(plain.Requests);
        Assert.Equal(0, plain.ConnectionCount);
        // The plain server is a real target all the same: a downloader that followed it would have been served.
        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, redirect.Headers.Location!)).Body);
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task The_redirect_status_is_the_one_asked_for(int status)
    {
        await using var server = FakeDownloadServer.StartHttps()
            .Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = new Uri("https://elsewhere.example.test/a.bin"), RedirectStatus = status });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (redirect, _) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(status, (int)redirect.StatusCode);
        Assert.Equal(new Uri("https://elsewhere.example.test/a.bin"), redirect.Headers.Location);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_redirect_to_a_host_off_loopback_is_seen_and_following_it_is_refused_by_the_guard()
    {
        await using var server = FakeDownloadServer.StartHttps()
            .Serve("/a.bin", Artifact, new FakeBehavior { RedirectTo = new Uri("https://download.example.test/a.bin") });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (redirect, _) = await DownloadFixtures.Get(client, server.Url("/a.bin"));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(redirect.Headers.Location!));

        Assert.IsType<System.Net.Sockets.SocketException>(error.InnerException);
        Assert.Equal(["download.example.test:443"], handler.Refusals);
    }
}
