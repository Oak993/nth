using System.Net;
using System.Net.Sockets;

namespace Marrow.Testing.Downloads;

/// <summary>
/// PORT-ONLY test fixture (SK-0; ONDEMAND-PLAN section 7: "a .NET SocketsHttpHandler.ConnectCallback guard in the test host"):
/// the connect step of a test's HTTP handler. It lets a connection through only to a loopback address and port that a fake
/// server in this test listens on, and refuses everything else, as a refused connection (SocketError.ConnectionRefused), before a
/// socket is opened and before any name is looked up: a host name other than "localhost" is not resolved at all. Every refusal is
/// recorded in <see cref="Refusals"/>.
/// </summary>
public sealed class NetworkGuard
{
    private readonly IReadOnlyList<IPEndPoint> allowed;
    private readonly Lock gate = new();
    private readonly List<string> refusals = [];

    /// <summary>A guard that lets through exactly the endpoints given (none: it refuses every connection).</summary>
    public NetworkGuard(IEnumerable<IPEndPoint> allowed) => this.allowed = [.. allowed.Select(Normalize)];

    /// <summary>Each refused attempt as "host:port", in order.</summary>
    public IReadOnlyList<string> Refusals
    {
        get { lock (gate) return [.. refusals]; }
    }

    /// <summary>The <c>SocketsHttpHandler.ConnectCallback</c>.</summary>
    public ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken cancellationToken) =>
        Connect(context.DnsEndPoint, cancellationToken);

    /// <summary>Opens a stream to the endpoint, or refuses it (see the type's summary).</summary>
    public async ValueTask<Stream> Connect(DnsEndPoint endpoint, CancellationToken cancellationToken)
    {
        var host = endpoint.Host;
        var port = endpoint.Port;
        foreach (var candidate in Candidates(host))
        {
            var target = Normalize(new IPEndPoint(candidate, port));
            if (!allowed.Contains(target)) continue;
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(target, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
        lock (gate) refusals.Add($"{host}:{port}");
        throw new SocketException((int)SocketError.ConnectionRefused);
    }

    /// <summary>
    /// The addresses a host text may mean without asking a resolver: an IP literal is itself, "localhost" is the two loopback
    /// addresses, anything else is nothing (so it is refused). A literal that is not loopback is still returned, and refused later
    /// because no fake listens on it.
    /// </summary>
    private static IEnumerable<IPAddress> Candidates(string host)
    {
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            if (IPAddress.IsLoopback(literal)) yield return literal;
            yield break;
        }
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            yield return IPAddress.Loopback;
            yield return IPAddress.IPv6Loopback;
        }
    }

    private static IPEndPoint Normalize(IPEndPoint endpoint) =>
        new(endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address, endpoint.Port);
}
