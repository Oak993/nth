using System.Net;
using System.Net.Security;

namespace Marrow.Testing.Downloads;

// PORT-ONLY test fixture (SK-0): the one way a test gets an HTTP handler that can reach a FakeDownloadServer, and nothing else.
public static class FakeDownloadTransport
{
    /// <summary>
    /// A handler for the code under test (a downloader's seam, or <c>URLSession.Transport.Override</c>): it connects only to the
    /// given fake servers (<see cref="NetworkGuard"/>), trusts only their test certificates, follows no redirect and decompresses
    /// nothing (so a redirect is seen as the 3xx the server sent, and the bytes counted are the bytes on the wire), uses no
    /// system proxy, and has no timeout of its own. With no server given it is configured with no fake and fails closed: every
    /// request is refused before any connection, loopback included.
    /// </summary>
    public static GuardedDownloadHandler CreateHandler(params FakeDownloadServer[] servers) => new(servers);
}

/// <summary>The handler <see cref="FakeDownloadTransport.CreateHandler"/> returns.</summary>
public sealed class GuardedDownloadHandler : DelegatingHandler
{
    private readonly NetworkGuard guard;
    private readonly bool anyFake;
    private readonly Lock gate = new();
    private readonly List<string> unconfigured = [];

    internal GuardedDownloadHandler(IReadOnlyList<FakeDownloadServer> servers) : base(Inner(servers, out var networkGuard))
    {
        guard = networkGuard;
        anyFake = servers.Count > 0;
    }

    /// <summary>True when this handler was made with no fake server: it refuses everything.</summary>
    public bool NoFakeConfigured => !anyFake;

    /// <summary>
    /// Every refused attempt: the connect guard's "host:port", "host:port" of each request that may use HTTP/3, and, for a handler
    /// with no fake, "METHOD url" of each request that was stopped before it reached the connect step.
    /// </summary>
    public IReadOnlyList<string> Refusals
    {
        get
        {
            lock (gate) return [.. guard.Refusals, .. unconfigured];
        }
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!anyFake)
        {
            lock (gate) unconfigured.Add(request.Method.Method + " " + request.RequestUri);
            throw new HttpRequestException(HttpRequestError.ConnectionError, "No fake download server is configured: the request was refused.",
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        }
        // REVIEW fix (unit/integ-sk): HTTP/3 runs over QUIC, which SocketsHttpHandler opens without calling ConnectCallback, so the guard never
        // sees it. A request that may use HTTP/3 (version 3 or higher, or a policy that allows a higher version than asked) is refused here,
        // as the guard refuses, before the inner handler can send a datagram. The fakes speak HTTP/1.1 only.
        if (request.Version.Major >= 3 || request.VersionPolicy == HttpVersionPolicy.RequestVersionOrHigher)
        {
            var uri = request.RequestUri!;
            lock (gate) unconfigured.Add($"{uri.Host}:{uri.Port}");
            throw new HttpRequestException(HttpRequestError.ConnectionError, "HTTP/3 (QUIC) is not allowed through the network guard: the request was refused.",
                new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused));
        }
        return base.SendAsync(request, cancellationToken);
    }

    private static SocketsHttpHandler Inner(IReadOnlyList<FakeDownloadServer> servers, out NetworkGuard networkGuard)
    {
        networkGuard = new NetworkGuard(servers.Select(s => s.EndPoint));
        var trusted = servers.Select(s => s.TrustedCertificate).OfType<System.Security.Cryptography.X509Certificates.X509Certificate2>()
            .Select(c => c.RawData).ToList();
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = networkGuard.Connect,
            SslOptions = new SslClientAuthenticationOptions
            {
                // Trusted only by this handler: the certificate must be byte for byte one a fake server of this test made, and
                // the only chain error allowed is its being self-signed (a name mismatch still fails).
                RemoteCertificateValidationCallback = (_, certificate, _, errors) =>
                    certificate is not null
                    && (errors & ~SslPolicyErrors.RemoteCertificateChainErrors) == 0
                    && trusted.Any(raw => raw.AsSpan().SequenceEqual(certificate.GetRawCertData())),
            },
        };
    }
}
