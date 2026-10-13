// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1 "Isolation that is real", 16.2 AS-16 fallback; unit AS-16; ED-NEW-dextra-as16-7). No Swift twin.
// Web reads for the Asturs while Visus is not in the tree: each assistant has its own HttpClient (and its own handler and connection pool),
// and no client keeps or sends a cookie, so nothing an Astur reads can sign it in, and what Channel B's session would hold is never
// Channel A's. A Cookie header a caller sets is dropped before the request leaves, and a Set-Cookie header never reaches the caller. Signing
// in is handed to the owner (the policy asks for every Login). When Visus lands, the browser profile of AsturIsolation replaces this path.
using System.Net;

namespace Marrow.Services.Assistant;

public sealed class AssistantWebClients : IDisposable
{
    /// <summary>A read gives up after this long.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public const int MaxRedirects = 5;

    private readonly object gate = new();
    private readonly Dictionary<string, HttpClient> clients = new(StringComparer.Ordinal);
    private readonly Func<HttpMessageHandler> inner;
    private bool disposed;

    /// <param name="inner">The handler under the cookie filter; a test supplies a fake. The default is a socket handler with no cookie jar.</param>
    public AssistantWebClients(Func<HttpMessageHandler>? inner = null) => this.inner = inner ?? NewSocketHandler;

    private static HttpMessageHandler NewSocketHandler() => new SocketsHttpHandler
    {
        UseCookies = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = MaxRedirects,
        Credentials = null,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
    };

    /// <summary>The assistant's own client: made on first use, the same one after, never another assistant's.</summary>
    public HttpClient For(string assistantId)
    {
        if (!AssistantStore.IsId(assistantId)) throw new ArgumentException($"\"{assistantId}\" is not an assistant id.", nameof(assistantId));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!clients.TryGetValue(assistantId, out var client))
                clients[assistantId] = client = new HttpClient(new NoCookies(inner()), disposeHandler: true) { Timeout = Timeout };
            return client;
        }
    }

    /// <summary>Lets an assistant's client go (it was retired): its connections close.</summary>
    public void Forget(string assistantId)
    {
        HttpClient? client;
        lock (gate) clients.Remove(assistantId, out client);
        client?.Dispose();
    }

    public void Dispose()
    {
        List<HttpClient> all;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            all = [.. clients.Values];
            clients.Clear();
        }
        foreach (var client in all) client.Dispose();
    }

    /// <summary>Takes the cookies off both directions: none goes out, none comes in.</summary>
    private sealed class NoCookies(HttpMessageHandler next) : DelegatingHandler(next)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Remove("Cookie");
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.Headers.Remove("Set-Cookie");
            return response;
        }
    }
}
