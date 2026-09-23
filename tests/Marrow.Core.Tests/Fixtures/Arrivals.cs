namespace Marrow.Core.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (F.HTTP, ED-118 item 4 tests).
/// <summary>
/// Requests as the loopback server receives them, when a test must tell whether a request reached it at all. A read
/// of the server's queue that is still waiting when the request is answered from the cache is kept for the next
/// request, so no exchange is lost.
/// </summary>
public sealed class Arrivals(LoopbackHttpServer server)
{
    private Task<LoopbackExchange>? waiting;

    /// <summary>The next exchange the server receives.</summary>
    public Task<LoopbackExchange> Next()
    {
        var next = waiting ?? server.NextExchange();
        waiting = null;
        return next;
    }

    /// <summary>True when a request reaches the server before <paramref name="pending"/> completes.</summary>
    public async Task<bool> Reached(Task pending)
    {
        waiting ??= server.NextExchange();
        var winner = await Task.WhenAny(pending, waiting).WaitAsync(LoopbackHttpServer.Guard);
        return winner == waiting;
    }
}
