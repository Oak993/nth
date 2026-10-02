using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Marrow.Testing;

// PORT-ONLY test fixture, no Swift twin (OD-0057): the handler a test puts on URLSession.Transport so a request to an
// address no stand-in server can be (https, a host that only exists in the test) is answered in this process.
//  - A request to a loopback address (127.0.0.1, ::1, localhost) goes to the real client settings of the port, so a
//    loopback listener in the same test (the MCP proxy, LoopbackHttpServer) still behaves as it does without the seam.
//  - Any other request is handed to Answer, which returns the reply or throws the transport failure the test wants
//    (an HttpRequestException, a SocketException). It never reaches a socket.
//  - With no Answer set, any other request fails as a refused connection: the handler never opens a connection to a
//    real host, so a test that forgot to script an address cannot touch the network (R-TEST-9, R-TEST-10).
public sealed class InProcessHttpHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<InProcessRequest> received = new();

    /// <summary>The answer to a request for an address that is not loopback; null refuses it.</summary>
    public Func<InProcessRequest, HttpResponseMessage>? Answer { get; set; }

    public InProcessHttpHandler() : base(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
        UseCookies = false,
    })
    {
    }

    /// <summary>Every request that reached <see cref="Answer"/> (or the refusal), in order.</summary>
    public IReadOnlyList<InProcessRequest> Received => [.. received];

    /// <summary>Forgets the requests seen and the answer set.</summary>
    public void Reset()
    {
        received.Clear();
        Answer = null;
    }

    /// <summary>A reply with a text body, for <see cref="Answer"/>.</summary>
    public static HttpResponseMessage Reply(int status, string body, string contentType = "application/json", params (string Name, string Value)[] headers)
    {
        var reply = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body)) };
        reply.Content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        foreach (var (name, value) in headers) reply.Headers.TryAddWithoutValidation(name, value);
        return reply;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        if (url.IsLoopback) return await base.SendAsync(request, cancellationToken);

        var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var headers = request.Headers.Concat(request.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .Select(pair => (pair.Key, string.Join(", ", pair.Value))).ToList();
        var seen = new InProcessRequest(request.Method.Method, url, headers, body);
        received.Enqueue(seen);
        if (Answer is not { } answer)
            throw new HttpRequestException(HttpRequestError.ConnectionError, "No in-process answer is set for " + url.Host + ".",
                new SocketException((int)SocketError.ConnectionRefused));
        return answer(seen);
    }
}

/// <summary>A request the in-process handler answered: method, address, headers (name and joined value) and body bytes.</summary>
public sealed record InProcessRequest(string Method, Uri Url, IReadOnlyList<(string Name, string Value)> Headers, byte[] Body)
{
    public string BodyText => Encoding.UTF8.GetString(Body);

    public string? Header(string name) =>
        Headers.Where(h => h.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).FirstOrDefault();
}
