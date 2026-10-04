using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing.Downloads;

namespace Marrow.Core.Tests.Port.Downloads;

// REVIEW (unit/integ-sk, the Windows verification of SK-0 = DL-0): findings of the adversarial review, each a test that failed on the merged
// branch before its fix. HTTP/3 runs over QUIC (UDP), and SocketsHttpHandler opens a QUIC connection without ever calling ConnectCallback, so
// the guard never saw it: a request for HTTP/3 to any address went out as UDP datagrams (observed on Windows 11: 4 to 5 QUIC datagrams reached
// a loopback UDP socket standing where an outside host would be, and no refusal was recorded). A loopback UDP socket is used here so the test
// itself sends nothing off the machine.
public sealed class DownloadHarnessReviewTests
{
    private static readonly FakeArtifact Artifact = FakeArtifact.Pattern(1000, seed: 21);

    [Theory]
    [Trait("Rule", "R-TEST-10")]
    [Trait("Feature", "SK-0")]
    [InlineData(HttpVersionPolicy.RequestVersionExact)]
    [InlineData(HttpVersionPolicy.RequestVersionOrHigher)]
    public async Task A_request_that_may_use_HTTP_3_is_refused_by_the_guard_before_any_datagram_is_sent(HttpVersionPolicy policy)
    {
        using var outside = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        outside.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)outside.LocalEndPoint!).Port;
        await using var server = FakeDownloadServer.StartHttps().Serve("/a.bin", Artifact);
        using var handler = FakeDownloadTransport.CreateHandler(server);
        using var client = DownloadFixtures.Client(handler);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://127.0.0.1:{port}/a.bin")
        {
            Version = HttpVersion.Version30,
            VersionPolicy = policy,
        };

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.SendAsync(request).WaitAsync(FakeDownloadServer.Guard));

        Assert.Equal(SocketError.ConnectionRefused, Assert.IsType<SocketException>(error.InnerException).SocketErrorCode);
        Assert.Equal([$"127.0.0.1:{port}"], handler.Refusals);
        Assert.Equal(0, outside.Available);
        Assert.Equal(0, server.ConnectionCount);
    }
}
