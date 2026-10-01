using System.Text;
using System.Threading.Channels;
using Marrow.Testing;

namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture (R-TEST-10): a scripted answerer on 127.0.0.1 for the API-key providers' endpoints
// (DeepSeek, Meta, Z.ai). The registry reaches them through the internal Endpoint boundaries of DeepSeekAPI,
// MetaAPI and ZaiAPI, which a test points at this server. Nothing here leaves the loopback interface.
// A route answers every request to its path (query ignored) with a fixed status and body; a path that is
// intercepted hands each request to the test instead, so the test decides when and how the answer is written.

/// <summary>One request the loopback received: its method, path without the query, and Authorization header.</summary>
public sealed record ProviderApiRequest(string Method, string Path, string? Authorization);

/// <summary>A loopback server that answers by path and records every request in arrival order.</summary>
public sealed class ProviderApiLoopback : IAsyncDisposable
{
    private readonly LoopbackHttpServer server = new();
    private readonly object gate = new();
    private readonly Dictionary<string, (int Status, string Body)> routes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Channel<LoopbackExchange>> intercepted = new(StringComparer.Ordinal);
    private readonly List<ProviderApiRequest> requests = [];
    private volatile bool disposed;

    public ProviderApiLoopback() => _ = Serve();

    /// <summary>The loopback URL for the path and query of a real endpoint: what an Endpoint boundary maps to.</summary>
    public Uri Redirect(Uri endpoint) => server.Url(endpoint.PathAndQuery);

    /// <summary>Answers every later request for <paramref name="path"/> with the status and JSON body.</summary>
    public void Route(string path, int status, string body)
    {
        lock (gate) routes[path] = (status, body);
    }

    /// <summary>Hands every later request for <paramref name="path"/> to <see cref="NextIntercepted"/> instead of answering it.</summary>
    public void Intercept(string path)
    {
        lock (gate) intercepted[path] = Channel.CreateUnbounded<LoopbackExchange>();
    }

    /// <summary>The next intercepted request for the path, in arrival order; the caller writes its answer.</summary>
    public Task<LoopbackExchange> NextIntercepted(string path)
    {
        Channel<LoopbackExchange> channel;
        lock (gate) channel = intercepted[path];
        return channel.Reader.ReadAsync().AsTask().WaitAsync(LoopbackHttpServer.Guard);
    }

    /// <summary>Every request received so far, in arrival order.</summary>
    public IReadOnlyList<ProviderApiRequest> Requests
    {
        get { lock (gate) return [.. requests]; }
    }

    /// <summary>How many requests for the path arrived.</summary>
    public int Count(string path)
    {
        lock (gate) return requests.Count(request => request.Path == path);
    }

    /// <summary>How many requests of any path arrived.</summary>
    public int Total
    {
        get { lock (gate) return requests.Count; }
    }

    private async Task Serve()
    {
        while (!disposed)
        {
            LoopbackExchange exchange;
            try
            {
                exchange = await server.NextExchange();
            }
            catch (TimeoutException)
            {
                continue; // idle: the guard only bounds each wait
            }
            catch (Exception) when (disposed)
            {
                return;
            }
            _ = Answer(exchange);
        }
    }

    private async Task Answer(LoopbackExchange exchange)
    {
        var query = exchange.Target.IndexOf('?', StringComparison.Ordinal);
        var path = query < 0 ? exchange.Target : exchange.Target[..query];
        var authorization = exchange.HeaderValues("Authorization").FirstOrDefault();
        Channel<LoopbackExchange>? channel;
        (int Status, string Body) route;
        lock (gate)
        {
            requests.Add(new ProviderApiRequest(exchange.Method, path, authorization));
            intercepted.TryGetValue(path, out channel);
            if (!routes.TryGetValue(path, out route)) route = (404, "{}");
        }
        if (channel is not null)
        {
            await channel.Writer.WriteAsync(exchange);
            return;
        }
        try
        {
            await exchange.Respond(route.Status, route.Status == 200 ? "OK" : "Error", Encoding.UTF8.GetBytes(route.Body));
        }
        catch (Exception) when (disposed)
        {
            // the test finished first
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await server.DisposeAsync();
    }
}
