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

    /// <summary>The running turn, its prompt and the chat's model; null when no turn runs (a version is then not tied to one).</summary>
    public ArtifactTurn? ArtifactTurn() => currentTurnID is { } turn ? new ArtifactTurn(turn, ArtifactPrompt(turn), Thread?.Model) : null;

    private string? ArtifactPrompt(Guid turnID) =>
        turns.FirstOrDefault(turn => turn.Id == turnID) is { UserItemID: { } itemID }
        && entryIndex.TryGetValue(itemID, out var entry) && entry.Item.TimelineItemContent is TimelineItem.Content.User user
            ? user.Value.Text : null;

    /// <summary>
    /// The events the timeline handles for one event of the session: answers with nth-artifact blocks show chips and save
    /// the blocks (ArtifactFenceFilter); with artifacts off, the event itself.
    /// </summary>
    private IReadOnlyList<ProviderEvent> ArtifactEvents(ProviderEvent @event)
    {
        var host = ArtifactHost.Shared.Current;
        if (!host.IsOn) return [@event];
        artifactFences ??= new ArtifactFenceFilter(ThreadID, host, () =>
        {
            var turn = ArtifactTurn();
            var provider = Thread?.Provider ?? ProviderKind.Claude;
            return (new ArtifactAuthor(provider.Id, turn?.Model, turn?.Turn), turn?.Prompt);
        });
        return artifactFences.Filter(@event);
    }
}
