using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// SK-0 (DL-0, skills-ONDEMAND-PLAN.md section 7): the network guard. Any connection that is not to the loopback port of a fake server
// of this test is refused, as a refused connection, before a socket is opened; a name is never looked up.
[Trait("Spec", "DL-0")]
public sealed class NetworkGuardTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(1000, seed: 9);

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Every_address_and_name_off_loopback_is_refused_before_any_connection_is_made()
    {
        string[] outside =
        [
            "https://example.com/a",
            "http://example.com:8080/a",
            "https://93.184.216.34/a",
            "http://10.0.0.1/a",
            "http://192.168.1.1/a",
            "http://169.254.169.254/latest/meta-data/",
            "http://[2001:db8::1]/a",
            "http://[::ffff:8.8.8.8]/a",
            "http://0.0.0.0/a",
            "https://localhost.example.test/a",
            "https://127.0.0.1.example.test/a",
            "http://127.0.0.1@evil.example.test/a",
            "https://github.com/anthropics/skills",
            "https://download.documentfoundation.org/libreoffice/x.msi",
        ];
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        foreach (var url in outside)
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(url));
            var refused = Assert.IsType<SocketException>(error.InnerException);
            Assert.Equal(SocketError.ConnectionRefused, refused.SocketErrorCode);
        }

        Assert.Equal(outside.Length, handler.Refusals.Count);
        Assert.Contains("example.com:443", handler.Refusals);
        Assert.Contains("example.com:8080", handler.Refusals);
        Assert.Contains("evil.example.test:80", handler.Refusals);
        Assert.Contains("93.184.216.34:443", handler.Refusals);
        Assert.Empty(server.Requests);
        // The fake still works: the guard is not closed to what it was told to allow.
        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, server.Url("/a.bin"))).Body);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_name_other_than_localhost_is_refused_without_a_lookup_so_even_a_name_that_cannot_resolve_is_a_refusal_not_a_dns_error()
    {
        var guard = new NetworkGuard([new IPEndPoint(IPAddress.Loopback, 5)]);

        var error = await Assert.ThrowsAsync<SocketException>(() =>
            guard.Connect(new DnsEndPoint("no-such-host.invalid", 443), default).AsTask());

        Assert.Equal(SocketError.ConnectionRefused, error.SocketErrorCode);
        Assert.Equal(["no-such-host.invalid:443"], guard.Refusals);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task A_live_loopback_port_that_no_fake_of_the_test_listens_on_is_refused_and_never_connected_to()
    {
        await using var stray = new LoopbackHttpServer();
        await using var fake = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(fake);
        using var client = DownloadFixtures.Client(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(stray.Url("/x")).WaitAsync(FakeDownloadServer.Guard));

        Assert.IsType<SocketException>(error.InnerException);
        Assert.Equal(0, stray.ConnectionCount);
        Assert.Equal([$"127.0.0.1:{stray.Port}"], handler.Refusals);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task Only_the_servers_a_handler_was_built_for_can_be_reached_through_it()
    {
        await using var allowed = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        await using var other = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(allowed);
        using var client = DownloadFixtures.Client(handler);

        Assert.Equal(Artifact.Bytes, (await DownloadFixtures.Get(client, allowed.Url("/a.bin"))).Body);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(other.Url("/a.bin")));

        Assert.Equal(0, other.ConnectionCount);
        Assert.Empty(other.Requests);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task The_name_localhost_reaches_a_fake_on_the_loopback_address_and_no_other_port()
    {
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);

        var (response, body) = await DownloadFixtures.Get(client, new Uri($"https://localhost:{server.EndPoint.Port}/a.bin"));
        var wrongPort = server.EndPoint.Port == 9 ? 10 : 9;
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync($"https://localhost:{wrongPort}/a.bin"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Artifact.Bytes, body);
        Assert.Equal([$"localhost:{wrongPort}"], handler.Refusals);
    }

    [Fact]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    public async Task An_ipv4_mapped_ipv6_spelling_of_an_allowed_endpoint_is_the_same_endpoint()
    {
        // REVIEW fix (unit/integ-sk): the port was a fixed 4242, so the test failed on a shared PC whenever anything listened there (seen:
        // with 127.0.0.1:4242 taken it failed alone twice). The allowed endpoint is now a listener of this test's own, so the allowed connect
        // is proved by a real connection rather than by an error from a port nobody listens on.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var guard = new NetworkGuard([new IPEndPoint(IPAddress.Parse("::ffff:127.0.0.1"), port)]);

            await using (await guard.Connect(new DnsEndPoint("127.0.0.1", port), default))
            using (await listener.AcceptTcpClientAsync().WaitAsync(FakeDownloadServer.Guard))
            {
                Assert.Empty(guard.Refusals);
            }
            var other = port == IPEndPoint.MaxPort ? port - 1 : port + 1;
            await Assert.ThrowsAsync<SocketException>(() =>
                guard.Connect(new DnsEndPoint("127.0.0.1", other), default).AsTask());
            Assert.Equal([$"127.0.0.1:{other}"], guard.Refusals);
        }
        finally
        {
            listener.Stop();
        }
    }
}
