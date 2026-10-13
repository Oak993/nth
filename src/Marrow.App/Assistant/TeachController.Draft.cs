// PORT-ONLY: Dextra (DESIGN.md rev 5, 4, 10 steps 2 and 3; unit AS-19; ED-NEW-dextra-as19-4). No Swift twin.
// The draft side of teach mode: a recording is drafted (by code, then by the model if one is bound), the draft is kept beside the recording,
// the owner edits it, and the questions the recording could not settle go to the decision queue as cards, where he answers them like any
// other. An answer changes the draft the way TeachDraftModel says; a question answered in the draft's own view withdraws its card.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>A draft, or why there is none.</summary>
public sealed record TeachDraftResult(TeachDraft? Draft, string? Problem)
{
    public bool Ok => Draft is not null;
}

public sealed partial class TeachController
{
    private readonly object draftGate = new();
    private readonly Dictionary<string, Dictionary<string, Guid>> cards = [];

    /// <summary>
    /// Drafts a recording that has ended (or was cut short by a crash). If it is already drafted, the draft is opened as it is; use
    /// <see cref="Redraft"/> to make it again. Raises the draft's open questions as cards.
    /// </summary>
    public async Task<TeachDraftResult> Draft(string id, CancellationToken cancel = default)
    {
        var info = recordings.Info(id);
        if (info is null) return new TeachDraftResult(null, "That recording is gone or cannot be read.");
        if (info.Newer) return new TeachDraftResult(null, "This recording was made by a newer nth. Open it there.");
        if (info.State is TeachRecordingState.Recording or TeachRecordingState.Paused) return new TeachDraftResult(null, "It is still recording. Stop it first.");
        if (info.State is TeachRecordingState.Saving or TeachRecordingState.Saved) return new TeachDraftResult(null, "It was already turned into a skill.");
        if (info.State == TeachRecordingState.Drafted && Open(id) is { } existing) return new TeachDraftResult(existing, null);
        var events = recordings.Events(id, out var skipped);
        var narration = recordings.Narration(id);
        if (events.Count == 0)
            return new TeachDraftResult(null, "Nothing was recorded, so there is nothing to draft. Delete it and teach again.");
        var draft = await drafter.Draft(info, events, narration, cancel).ConfigureAwait(false);
        if (skipped > 0) draft = draft with { Notes = draft.Notes.Appending($"{skipped} line{(skipped == 1 ? "" : "s")} of the recording could not be read and {(skipped == 1 ? "was" : "were")} left out.") };
        if (draft.Steps.Count == 0) return new TeachDraftResult(null, "The recording has no steps to draft.");
        lock (draftGate)
        {
            if (recordings.Info(id) is not { Newer: false } now) return new TeachDraftResult(null, "That recording went away while it was being drafted.");
            recordings.SaveDraft(id, draft);
            recordings.SaveInfo(now with { State = TeachRecordingState.Drafted, Changed = o.Time.GetUtcNow() });
        }
        Log("draft", $"recording {id}: {draft.Steps.Count} steps, {draft.Guarded.Count} that ask, {draft.Questions.Count} questions, model {(draft.ModelWritten ? "wrote it" : "did not")}");
        RaiseQuestions(id, draft);
        Changed?.Invoke();
        return new TeachDraftResult(draft, null);
    }

    /// <summary>Drafts again from the recording, throwing the draft (and the owner's edits and answers) away. Withdraws its cards.</summary>
    public Task<TeachDraftResult> Redraft(string id, CancellationToken cancel = default)
    {
        WithdrawCards(id, "The draft was made again.");
        lock (draftGate)
        {
            if (recordings.Info(id) is { State: TeachRecordingState.Drafted } info && !info.Newer)
            {
                o.Store.DeleteTeachFile(o.AssistantId, id, AssistantStore.TeachDraftFile);
                recordings.SaveInfo(info with { State = TeachRecordingState.Recorded, Changed = o.Time.GetUtcNow() });
            }
        }
        return Draft(id, cancel);
    }

    /// <summary>The saved draft of a recording, with its open questions on the queue; null when there is none or it does not read.</summary>
    public TeachDraft? Open(string id)
    {
        TeachDraft? draft;
        lock (draftGate) draft = recordings.Draft(id);
        if (draft is null) return null;
        RaiseQuestions(id, draft);
        return draft;
    }

    /// <summary>
    /// The owner's edit of the draft: a rename, a reworded step, a flag, a step taken out, an input. The change is applied to the saved draft and
    /// saved. Null when there is no draft or it came from a newer nth (it is left as it is).
    /// </summary>
    public TeachDraft? Edit(string id, Func<TeachDraft, TeachDraft> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        TeachDraft updated;
        lock (draftGate)
        {
            if (recordings.Draft(id) is not { } draft || draft.Version > TeachDraft.CurrentVersion) return null;
            updated = change(draft) with { RecordingId = id };
            recordings.SaveDraft(id, updated);
            if (recordings.Info(id) is { Newer: false } info) recordings.SaveInfo(info with { Changed = o.Time.GetUtcNow() });
        }
        Changed?.Invoke();
        return updated;
    }

    /// <summary>Answers one of the draft's questions (from the draft's own view, or from its card). Its card, if it is waiting, is withdrawn.</summary>
    public TeachDraft? Answer(string id, string questionId, string answer)
    {
        var updated = Edit(id, draft => TeachDraftModel.Answer(draft, questionId, answer));
        if (updated is null) return null;
        Guid? card = null;
        lock (gate)
        {
            if (cards.TryGetValue(id, out var open) && open.Remove(questionId, out var found)) card = found;
        }
        if (card is { } waiting) o.Queue.Withdraw(waiting, "You answered it in the draft.");
        return updated;
    }

    // ---- the questions as cards ----

    private void RaiseQuestions(string id, TeachDraft draft)
    {
        foreach (var question in draft.OpenQuestions)
        {
            lock (gate)
            {
                if (!cards.TryGetValue(id, out var open)) cards[id] = open = [];
                if (open.TryGetValue(question.Id, out var existing) && o.Queue.Find(existing) is { State: DecisionState.Open or DecisionState.Held }) continue;
            }
            var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question, question.Text)
            {
                Why = $"From your recording, for the skill “{draft.Name}”. Your answer is written into the draft.",
                Rule = "Asks first: teaching",
                AssistantId = o.AssistantId,
                Options = question.Options,
            };
            var questionId = question.Id;
            var raised = o.Queue.Raise(card, (_, answer) =>
            {
                lock (gate)
                {
                    if (cards.TryGetValue(id, out var open)) open.Remove(questionId);
                }
                if (answer.Text is { Length: > 0 } text) Answer(id, questionId, text);
            });
            lock (gate) cards[id][question.Id] = raised.Id;
        }
    }

    private void WithdrawCards(string id, string note)
    {
        List<Guid> waiting;
        lock (gate)
        {
            waiting = cards.TryGetValue(id, out var open) ? [.. open.Values] : [];
            cards.Remove(id);
        }
        foreach (var card in waiting) o.Queue.Withdraw(card, note);
    }
}
