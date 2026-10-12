// PORT-ONLY: web surfaces W14, Tabula (DESIGN.md 6.3 "Ask to change ... Sending posts a turn in the main thread with the
// artifact attached as context", "Ask to fix, which posts a turn with the error and stack"; 6.5 "nth.send(text) puts text
// in the main composer as a draft. The user sends"; ED-NEW-web-w14-1). No Swift counterpart. The two ways the Tabula pane
// reaches its chat: a turn it posts (sent now when the chat is idle, queued as a follow-up while a turn runs, as the
// composer's own queue does), and a draft an artifact writes into the composer, which only the user sends.
using Marrow.Core.Port;
using Marrow.Core.Models;
using Marrow.App.Views.Artifacts;
using Marrow.Services.Web;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime
{
    // ED-NEW-web-nc6-1: the follow-up queue owns accepted artifact requests. Their UI state belongs to
    // the runtime, so closing the pane cannot erase them. Bind completion to the actual turn identity.
    private readonly Dictionary<Guid, TabulaPendingAsk?> artifactFollowUps = [];
    private readonly Dictionary<Guid, Guid> artifactRequestTurns = [];
    private readonly AsyncLocal<Guid?> artifactRequestContext = new();
    private Guid? artifactDispatchPending;

    /// <summary>Posts <paramref name="text"/> as the user's next turn: now when idle, else as a queued follow-up.</summary>
    public void SendArtifactTurn(string text) => QueueArtifactTurn(text, null);

    public bool TrySendArtifactAsk(TabulaPendingAsk request, string text) => QueueArtifactTurn(text, request);

    private bool QueueArtifactTurn(string text, TabulaPendingAsk? request)
    {
        text = FoundationText.Trim(text ?? "", includeNewlines: true);
        if (text.Length == 0 || IsReverting || Thread is not { IsArchived: false } thread
            || App?.Project(thread.ProjectID) is null) return false;
        var prompt = new FollowUpPrompt { Id = request?.RequestId ?? Guid.NewGuid(), Text = text };
        artifactFollowUps.Add(prompt.Id, request);
        if (request is not null) TabulaPane.StateOf(this).AddAsk(request with { IsQueued = true });
        FollowUps = followUps.Appending(prompt);
        saveRevision++;
        ScheduleSave();
        FlushFollowUpsIfIdle();
        return true;
    }

    private bool IsArtifactFollowUp(Guid id) => artifactFollowUps.ContainsKey(id);

    private void StartArtifactFollowUp(FollowUpPrompt prompt)
    {
        artifactDispatchPending = prompt.Id;
        _ = MainActor.Spawn(async () =>
        {
            var previous = artifactRequestContext.Value;
            artifactRequestContext.Value = prompt.Id;
            try
            {
                await EnsureLoaded();
                if (Phase != RuntimePhase.Idle || IsReverting || Thread?.IsArchived != false)
                {
                    RestoreArtifactFollowUp(prompt.Text, prompt.Attachments);
                    return;
                }
                await StartTurn(prompt.Text, prompt.Attachments);
            }
            finally
            {
                artifactRequestContext.Value = previous;
                if (artifactDispatchPending == prompt.Id) artifactDispatchPending = null;
            }
        });
    }

    private bool RestoreArtifactFollowUp(string text, IReadOnlyList<Attachment> attachments)
    {
        if (artifactRequestContext.Value is not { } id || !IsArtifactFollowUp(id)) return false;
        if (!followUps.Any(p => p.Id == id))
        {
            FollowUps = followUps.Mutating(items => items.Insert(0, new FollowUpPrompt { Id = id, Text = text, Attachments = new ValueList<Attachment>(attachments) }));
            saveRevision++;
            ScheduleSave();
        }
        return true;
    }

    private void BindArtifactRequest(Guid turn)
    {
        if (artifactRequestContext.Value is not { } id || !artifactFollowUps.TryGetValue(id, out var request)) return;
        artifactRequestTurns.Add(turn, id);
        if (artifactDispatchPending == id) artifactDispatchPending = null;
        if (request is null) return;
        var state = TabulaPane.StateOf(this);
        var artifact = ArtifactHost.Shared.Current.Store?.Find(ThreadID, request.Id);
        state.StartAsk(id, artifact?.Current ?? request.FromVersion);
        if (artifact is not null)
            ArtifactHost.Shared.Current.Streams.Begin(ThreadID, TabulaView.AskStreamPrefix + request.Id, request.Id, artifact.Title, artifact.Kind, isUpdate: true);
    }

    private void FinishArtifactRequest(Guid turn)
    {
        if (!artifactRequestTurns.Remove(turn, out var id)) return;
        RemoveArtifactRequest(id);
    }

    private void RemoveArtifactRequest(Guid id)
    {
        if (!artifactFollowUps.Remove(id, out var request)) return;
        if (request is not null)
        {
            TabulaPane.StateOf(this).FinishAsk(id);
            ArtifactHost.Shared.Current.Streams.End(ThreadID, TabulaView.AskStreamPrefix + request.Id);
        }
    }

    /// <summary>An artifact's nth.send: the text joins the composer's draft (after a blank line when it has words); nothing is sent.</summary>
    public void DraftFromArtifact(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var current = Draft.Text;
        Draft = Draft with { Text = FoundationText.Trim(current, includeNewlines: true).Length == 0 ? text : current.TrimEnd() + "\n\n" + text };
    }
}
