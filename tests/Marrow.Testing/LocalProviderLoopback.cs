using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Marrow.Testing;

// PORT-ONLY: MA-3's HTTP stand-in. Every socket binds to loopback; no model or external service is contacted.
public sealed class LocalProviderLoopback : IAsyncDisposable
{
    private readonly LoopbackHttpServer server = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(int Status, string Body, (string, string)[] Headers)>> routes = new();
    private readonly Channel<LoopbackExchange> received = Channel.CreateUnbounded<LoopbackExchange>();
    private readonly ConcurrentDictionary<string, bool> stalled = new();
    private readonly ConcurrentQueue<LoopbackExchange> requests = new();
    private bool disposed;

    public LocalProviderLoopback() => _ = Serve();
    public Uri BaseURL => server.Url("/");
    public IReadOnlyList<LoopbackExchange> Requests => requests.ToArray();
    public void Reply(string path, string body, int status = 200, params (string, string)[] headers) =>
        routes.GetOrAdd(path, _ => new()).Enqueue((status, body, headers));
    public void Stall(string path) => stalled[path] = true;
    public Task<LoopbackExchange> NextRequest() => received.Reader.ReadAsync().AsTask().WaitAsync(LoopbackHttpServer.Guard);

    private async Task Serve()
    {
        while (!disposed)
        {
            LoopbackExchange exchange;
            try { exchange = await server.NextExchange(); }
            catch (Exception) when (disposed) { return; }
            catch (TimeoutException) { continue; }
            requests.Enqueue(exchange);
            await received.Writer.WriteAsync(exchange);
            if (stalled.ContainsKey(exchange.Target)) continue;
            var reply = routes.TryGetValue(exchange.Target, out var queue) && queue.TryDequeue(out var next)
                ? next : (Status: 404, Body: "{}", Headers: Array.Empty<(string, string)>());
            await exchange.Respond(reply.Status, "Fake", Encoding.UTF8.GetBytes(reply.Body),
                [.. reply.Headers, ("Content-Type", exchange.Target.EndsWith("/chat/completions", StringComparison.Ordinal)
                    ? "text/event-stream" : "application/json")]);
        }
    }

    public async ValueTask DisposeAsync()
    {
        disposed = true;
        await server.DisposeAsync();
    }
}
