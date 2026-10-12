// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1-6.2; ED-NEW-web-w12-1). The process's artifact store and the one
// question the tools ask the app: which turn of a chat is running (so a version records its author's turn and the prompt
// that caused it). Off (no store) until the app binds it at start-up, so a test or a tool that never binds it offers no
// artifact tools and launches every provider exactly as before. No Swift counterpart.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Services.Web;

/// <summary>A chat's running turn: its id, the user's prompt and the model it runs on (null when the chat is idle).</summary>
public sealed record ArtifactTurn(Guid? Turn, string? Prompt, string? Model);

public sealed class ArtifactHost(ArtifactStore? store, Func<Guid, Task<ArtifactTurn?>>? turn)
{
    /// <summary>The app's host; the default has no store, so artifacts are off.</summary>
    public static Ambient<ArtifactHost> Shared { get; } = new(new ArtifactHost(null, null));

    public ArtifactStore? Store { get; } = store;

    public bool IsOn => Store is not null;

    /// <summary>The artifacts the chats are writing right now (W14: Tabula opens on them with a skeleton, DESIGN 6.4).</summary>
    public ArtifactStreams Streams { get; } = new();

    /// <summary>Raised when the user opens an artifact's chip in an answer; the Tabula pane opens it.</summary>
    public event Action<Guid, string>? OpenRequested;

    public void RequestOpen(Guid thread, string id) => OpenRequested?.Invoke(thread, id);

    /// <summary>
    /// Who makes a version through a tool call now: the session's provider and model, the chat's running turn, and that
    /// turn's prompt. A turn seam that fails leaves the turn and prompt unknown, never the call.
    /// </summary>
    public async Task<(ArtifactAuthor Author, string? Prompt)> Caller(Guid thread, ProviderKind provider, string? model)
    {
        ArtifactTurn? current = null;
        if (turn is not null)
        {
            try { current = await turn(thread).ConfigureAwait(false); }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { current = null; }
        }
        var by = current?.Model is { Length: > 0 } running ? running : model;
        return (new ArtifactAuthor(provider.Id, by is { Length: > 0 } ? by : null, current?.Turn), current?.Prompt);
    }
}
