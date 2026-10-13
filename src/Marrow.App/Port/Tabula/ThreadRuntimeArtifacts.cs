// PORT-ONLY: web surfaces W12, Tabula (DESIGN.md 6.1-6.2; ED-NEW-web-w12-1, ED-NEW-web-w12-2). One chat's runtime as the
// artifact tools see it: the running turn and the user's prompt that a new version records, and the fenced fallback that
// every event of the chat's session passes through on its way to the timeline (ThreadRuntime.cs holds only that hook).
using Marrow.Core.Models;
using Marrow.Services.Providers;
using Marrow.Services.Web;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    private ArtifactFenceFilter? artifactFences;

    /// <summary>The key prefix of a stream that an artifact_create tool call started (web W14).</summary>
    public const string ToolStreamPrefix = "tool:";

    /// <summary>The running turn, its prompt and the chat's model; null when no turn runs (a version is then not tied to one).</summary>
    public ArtifactTurn? ArtifactTurn() => currentTurnID is { } turn ? new ArtifactTurn(turn, ArtifactPrompt(turn), Thread?.Model) : null;

    private string? ArtifactPrompt(Guid turnID) =>
        turns.FirstOrDefault(turn => turn.Id == turnID) is { UserItemID: { } itemID }
        && entryIndex.TryGetValue(itemID, out var entry) && entry.Item.TimelineItemContent is TimelineItem.Content.User user
            ? user.Value.Text : null;

    /// <summary>
    /// The events the timeline handles for one event of the session: answers with nth-artifact blocks show chips and save
    /// the blocks (ArtifactFenceFilter), then fenced live views and the live-answer tools' rows are read (LiveAnswerFlow, W24); with artifacts off, the event itself.
    /// </summary>
    private IReadOnlyList<ProviderEvent> ArtifactEvents(ProviderEvent @event)
    {
        var host = ArtifactHost.Shared.Current;
        if (!host.IsOn) return [@event];
        // PORT-ONLY (web W14, DESIGN 6.4; ED-NEW-web-w14-1): Tabula opens with a skeleton the moment artifact_create starts.
        // The call's own result (the store's Created) or the end of the turn ends the stream.
        // A live-answer call is never one: its arguments are a view's text, which may name nth-ui and artifact_create (W24).
        if (@event is ProviderEvent.ToolStarted started && !LiveAnswerFlow.IsLiveCall(started.Call.Title) && ArtifactStreams.IsCreateCall(started.Call.Title, started.Call.Detail))
            host.Streams.Begin(ThreadID, ToolStreamPrefix + started.Id, null, null, null);
        artifactFences ??= new ArtifactFenceFilter(ThreadID, host, () =>
        {
            var turn = ArtifactTurn();
            var provider = Thread?.Provider ?? ProviderKind.Claude;
            return (new ArtifactAuthor(provider.Id, turn?.Model, turn?.Turn), turn?.Prompt);
        });
        return LiveEvents(host, artifactFences.Filter(@event)); // + W24: the live-answer flow reads what is left
    }
}
