// PORT-ONLY: OD-0116, OD-0122, ED-439. The HTTP server `opencode serve` offers: the one place OpenCode documents a message that is stored without a reply (`noReply`).
namespace Marrow.Services.Providers;

public interface IOpenCodeServer
{
    /// <summary>The server's own OpenAPI document (JSON).</summary>
    Task<string> OpenApiAsync(CancellationToken ct);

    /// <summary>`POST /session`: a new, empty session; returns its id.</summary>
    Task<string> CreateSessionAsync(CancellationToken ct);

    /// <summary>`POST /session/{id}/message` with `noReply: true` and one text part: stored in the session, no model turn.</summary>
    Task InjectAsync(string sessionId, string text, CancellationToken ct);

    /// <summary>`GET /session/{id}/message`: the text of every stored part, to read the injection back.</summary>
    Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct);

    /// <summary>`DELETE /session/{id}`.</summary>
    Task DeleteSessionAsync(string sessionId, CancellationToken ct);

    /// <summary>Stops the server process. The stored session stays on disk.</summary>
    void Stop();
}
