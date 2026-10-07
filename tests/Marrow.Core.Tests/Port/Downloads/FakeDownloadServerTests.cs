using System.Net;
using System.Net.Http;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the fake download server's plain behaviour: https with a test certificate trusted
// only by the test's handler, Accept-Ranges on and off, 404 and 500, the request log, holds. Loopback only; nothing leaves the process.
[Trait("Spec", "DL-0")]
public sealed class FakeDownloadServerTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(5000, seed: 7);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task An_https_server_serves_the_artifact_to_the_handler_built_for_it_and_logs_the_request()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/files/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, server.Url("/files/a.bin"));

        Assert.Equal("https", server.Url("/files/a.bin").Scheme);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Equal(5000, response.Content.Headers.ContentLength);
        Assert.Equal(["bytes"], response.Headers.GetValues("Accept-Ranges"));
        var entry = Assert.Single(await server.WaitForRequests(1));
        Assert.Equal(("GET", "/files/a.bin", 200), (entry.Method, entry.Path, entry.Status));
        Assert.Null(entry.Range);
        Assert.Equal((5000L, 5000L, false), (entry.BodyBytesPromised, entry.BodyBytesSent, entry.Dropped));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task The_test_certificate_is_not_trusted_by_the_machine_only_by_the_handler_built_for_its_server()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);

        // The machine's own trust (a plain handler, no proxy) does not accept it: the handshake fails and no request is read.
        using var system = new SocketsHttpHandler { UseProxy = false };
        using var systemClient = DownloadFixtures.Client(system);
        await Assert.ThrowsAsync<HttpRequestException>(() => systemClient.GetAsync(server.Url("/a.bin")));

        Assert.Empty(server.Requests);

        // The handler built for it does.
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);
        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, server.Url("/a.bin"))).Body);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_server_that_presents_an_untrusted_certificate_fails_the_handshake_and_reads_no_request()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        server.PresentUntrustedCertificate = true;
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(server.Url("/a.bin")));

        Assert.Equal(HttpRequestError.SecureConnectionError, error.HttpRequestError);
        Assert.Empty(server.Requests);

        server.PresentUntrustedCertificate = false;
        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, server.Url("/a.bin"))).Body);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task With_ranges_on_a_range_request_gets_206_and_only_the_bytes_from_its_start()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"), "bytes=1000-");

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(Artifact.Bytes[1000..], body);
        Assert.Equal(["bytes 1000-4999/5000"], response.Content.Headers.GetValues("Content-Range"));
        var entry = Assert.Single(await server.WaitForRequests(1));
        Assert.Equal(("bytes=1000-", 1000L, 4000L), (entry.Range, entry.RangeStart, entry.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_range_that_starts_at_or_past_the_end_gets_416_with_the_total_length()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (atEnd, atEndBody) = await DownloadFixtures.Get(client, server.Url("/a.bin"), "bytes=5000-");
        var (past, _) = await DownloadFixtures.Get(client, server.Url("/a.bin"), "bytes=9000-");

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, atEnd.StatusCode);
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, past.StatusCode);
        Assert.Equal(["bytes */5000"], atEnd.Content.Headers.GetValues("Content-Range"));
        Assert.Empty(atEndBody);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task With_ranges_off_there_is_no_Accept_Ranges_header_and_a_range_request_gets_the_whole_body_with_200()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { AcceptRanges = false });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (plain, _) = await DownloadFixtures.Get(client, server.Url("/a.bin"));
        var (ranged, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"), "bytes=1000-");

        Assert.False(plain.Headers.Contains("Accept-Ranges"));
        Assert.Equal(HttpStatusCode.OK, ranged.StatusCode);
        Assert.False(ranged.Content.Headers.Contains("Content-Range"));
        Assert.Equal(Artifact.Bytes, body);
        var entries = await server.WaitForRequests(2);
        Assert.Equal((1000L, 5000L), (entries[1].RangeStart, entries[1].BodyBytesSent));
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task A_status_behaviour_answers_that_status_and_no_artifact_bytes(int status)
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { Status = status });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(status, (int)response.StatusCode);
        Assert.NotEqual(Artifact.Bytes, body);
        Assert.True(body.Length < 50);
        Assert.Equal(status, Assert.Single(await server.WaitForRequests(1)).Status);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_path_that_is_not_served_is_404_and_the_query_is_ignored_when_routing()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (missing, _) = await DownloadFixtures.Get(client, server.Url("/nope.bin"));
        var (queried, body) = await DownloadFixtures.Get(client, server.Url("/a.bin?x=1"));

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Equal(["/nope.bin", "/a.bin?x=1"], (await server.WaitForRequests(2)).Select(r => r.Target));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task The_behaviour_of_a_route_can_be_switched_between_requests()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact, new FakeBehavior { Status = 500 });
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (broken, _) = await DownloadFixtures.Get(client, server.Url("/a.bin"));
        server.Behave("/a.bin", new FakeBehavior());
        var (fixedNow, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal(HttpStatusCode.InternalServerError, broken.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fixedNow.StatusCode);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Throws<InvalidOperationException>(() => server.Behave("/unknown", new FakeBehavior()));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_head_request_gets_the_headers_of_a_get_and_no_body()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, server.Url("/a.bin")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(5000, response.Content.Headers.ContentLength);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
        var entry = Assert.Single(await server.WaitForRequests(1));
        Assert.Equal(("HEAD", 5000L, 0L), (entry.Method, entry.BodyBytesPromised, entry.BodyBytesSent));
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_plain_http_server_serves_over_http_and_needs_no_certificate()
    {
        await using var server = FakeDownloadServer.StartPlainHttp().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, server.Url("/a.bin"));

        Assert.Equal("http", server.Url("/a.bin").Scheme);
        Assert.Null(server.TrustedCertificate);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Held_requests_wait_for_the_release_and_the_peak_counts_how_many_were_in_flight_together()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);
        var hold = server.HoldRequests();

        var downloads = Enumerable.Range(0, 3).Select(_ => DownloadFixtures.Get(client, server.Url("/a.bin"))).ToList();
        await hold.WaitForArrivals(3);

        Assert.All(downloads, d => Assert.False(d.IsCompleted));
        Assert.Empty(server.Requests);
        hold.Release();
        foreach (var download in downloads) Assert.Equal(Artifact.Bytes, (await download).Body);
        Assert.Equal(3, server.PeakConcurrentRequests);
        Assert.Equal(3, (await server.WaitForRequests(3)).Count);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Requests_made_one_after_the_other_never_overlap()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        for (var i = 0; i < 3; i++) await DownloadFixtures.Get(client, server.Url("/a.bin"));
        await server.WaitForRequests(3);

        Assert.Equal(1, server.PeakConcurrentRequests);
    }

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    [InlineData("0.0.0.0")]
    [InlineData("192.168.1.10")]
    [InlineData("::")]
    public void A_server_refuses_to_start_on_an_address_that_is_not_loopback(string address)
    {
        Assert.Throws<ArgumentException>(() => FakeDownloadServer.StartHttps(address));
        Assert.Throws<ArgumentException>(() => FakeDownloadServer.StartPlainHttp(address));
    }
}
