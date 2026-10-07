using Marrow.Services.Providers;

namespace Marrow.App.Tests.Fixtures;

/// <summary>An offline `opencode serve` for the host tests: it offers (or not) the no-reply message and stores what it is given.</summary>
public sealed class ThreadRuntimeOpenCodeServer(bool offersNoReply) : IOpenCodeServer
{
    public List<string> Stored { get; } = [];
    public int Stops { get; private set; }

    public Task<string> OpenApiAsync(CancellationToken ct) => Task.FromResult(offersNoReply
        ? "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{\"content\":{\"application/json\":{\"schema\":{\"properties\":{\"noReply\":{}}}}}}}}}}"
        : "{\"paths\":{\"/session/{id}/message\":{\"post\":{\"requestBody\":{}}}}}");

    public Task<string> CreateSessionAsync(CancellationToken ct) => Task.FromResult("ses_standby");

    public Task InjectAsync(string sessionId, string text, CancellationToken ct)
    {
        Stored.Add(text);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(Stored);
    public Task DeleteSessionAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;
    public void Stop() => Stops++;
}
