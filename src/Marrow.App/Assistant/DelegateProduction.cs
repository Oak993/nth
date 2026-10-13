// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "using the project's context: the owner's decisions log, briefs, past answers and the code", 5.4; unit AS-22;
// ED-NEW-dextra-as22-10). No Swift twin.
// The app's two halves of the delegate's seams. The model is the provider CLI's one-shot, no-tools mode that titles and commit messages already use (Claude or
// Codex; the engine's own cheap model), so a question costs one short call and the model has no way to act. The context is read from the owner's own project
// and Dextra's own folders only: the project's AGENTS.md and CLAUDE.md (found by ProjectGuidance, which refuses a link that leaves the folder), the notes
// Dextra keeps of his choices (never a quarantined one), the past answers of the project with his corrections first, and the chat's last words.
using System.Text;
using Marrow.Core.Models;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;
using Marrow.Services.Providers;

namespace Marrow.App.Assistant;

/// <summary>The delegate's model: nth's one-shot text engine. Nothing is asked until a question has passed every gate, and the engine is read once, on the main thread.</summary>
public sealed class TextEngineDelegateModel(AppModel app, Func<ProviderKind?> brain) : IDelegateModel
{
    public DelegateCall? Prepare(DelegateWho? who)
    {
        var engine = app.TextEngine(who?.Provider ?? brain());
        ProviderKind? provider = engine switch
        {
            TextGeneration.Engine.Claude => ProviderKind.Claude,
            TextGeneration.Engine.Codex => ProviderKind.Codex,
            _ => null,
        };
        if (engine is null || provider is not { } kind) return null;
        // The engine ends by its own timeout; the caller stops waiting at its own.
        return new DelegateCall(kind, (prompt, cancel) => TextGeneration.Complete(prompt, engine).WaitAsync(cancel));
    }
}

public sealed class AppDelegateContext(AppModel app, DelegateAnswerBook book, MemoryStore? notes, Func<string?> dextraId) : IDelegateContext
{
    /// <summary>The chat's last turns that are read.</summary>
    public const int TurnsRead = 10;

    /// <summary>The notes recalled for a question.</summary>
    public const int NotesRead = 8;

    /// <summary>The past answers read.</summary>
    public const int PastRead = 12;

    public DelegateChatView? View(Guid thread)
    {
        if (app.Thread(thread) is not { } chat) return null;
        var folder = chat.WorktreePath ?? app.Project(chat.ProjectID)?.Path;
        // In a mission's chat the "user" is Dextra briefing its agent, not the owner: only a chat of his own holds his words.
        var owners = chat.MissionID is null;
        var turns = new List<DelegateTurn>();
        if (app.ExistingRuntime(thread) is { } runtime)
        {
            var entries = runtime.Entries;
            for (var i = entries.Count - 1; i >= 0 && turns.Count < TurnsRead; i--)
            {
                switch (entries[i].Item.TimelineItemContent)
                {
                    // An Override's message quotes the agent's own question inside it: it is not the owner's words (his are kept in the book as corrections).
                    case TimelineItem.Content.User user when user.Value.Text.Length > 0 && !DelegateWords.IsCorrection(user.Value.Text):
                        turns.Add(new DelegateTurn(owners, user.Value.Text));
                        break;
                    case TimelineItem.Content.Assistant reply when reply.Value.Text.Length > 0:
                        turns.Add(new DelegateTurn(false, reply.Value.Text));
                        break;
                }
            }
            turns.Reverse();
        }
        return new DelegateChatView(thread, chat.Title, chat.ProjectID, folder, turns);
    }

    public Task<DelegatePack> Pack(DelegateChatView view, QuestionRequest request, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        var decisions = new StringBuilder();
        // The notes Dextra keeps of his choices come first: the prompt cuts the decisions to a fixed length, and a long CLAUDE.md must not push his notes out of it.
        if (notes is not null && dextraId() is { } id)
        {
            var query = string.Join(" ", request.Questions.Select(q => q.Prompt));
            foreach (var recalled in notes.Recall(id, query, NotesRead)) decisions.Append("- ").AppendLine(DelegateWords.Clip(recalled.Note.Text, 300));
        }
        if (view.Folder is { Length: > 0 } folder)
        {
            try
            {
                if (new ProjectGuidance(folder).SystemPromptBlock() is { Length: > 0 } guidance) decisions.AppendLine(guidance);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // a project's guidance that cannot be read is not given; the question is judged without it
                _ = error;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        var (past, corrections) = Past(view.Project);
        return new DelegatePack(decisions.ToString(), past, view.Recent) { Corrections = corrections };
    }, cancellationToken);

    // The project's earlier answers, his corrections first in the book's order. An answer he corrected is not told as an answer: what he wanted is what the model learns
    // (and what a decision may be quoted from); the rest are context, the delegate's own earlier words.
    private (string Past, IReadOnlyList<DelegateCorrection> Corrections) Past(Guid project)
    {
        var text = new StringBuilder();
        var corrections = new List<DelegateCorrection>();
        foreach (var past in book.Past(project, PastRead))
        {
            if (past.Correction is { } correction)
            {
                corrections.Add(new DelegateCorrection(DelegateWords.Clip(past.Question, 200), DelegateWords.Clip(correction, 300)));
                continue;
            }
            text.Append("Q: ").Append(DelegateWords.Clip(past.Question, 200)).Append(" | Answered: ").AppendLine(DelegateWords.Clip(past.Answer, 120));
        }
        return (text.ToString(), corrections);
    }
}
