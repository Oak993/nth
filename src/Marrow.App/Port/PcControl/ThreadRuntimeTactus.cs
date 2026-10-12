// PORT-ONLY: Tactus, the chat mirror in the chat's runtime (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1 "Resume after the
// turn ended", 10.2 path 6, 10.4 "Resolution", 10.7). No Swift twin. The runtime's side of TactusChatBridge: it writes the
// Tactus rows into the timeline (notices, saved with the chat), decides a resume from its own last turn (the four
// conditions), sends the fixed continue message as a turn shown by its chip, shows the chat's Tactus asks (badges, and
// pc_ask questions in the question sheet), lists the provider's own asks for the broker and answers one from the pill, and
// says when the chat's Stop interrupts a turn, when the user writes, and when a pc_ask question is answered in the chat.
// ThreadRuntime.cs holds only the hooks (StartTurn's chip and "the user wrote", Interrupt, Answer).
using System.ComponentModel;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.PcControl;
using Marrow.Core.Support;
using Marrow.Services.Port.PcControl;

namespace Marrow.App.Runtime;

public sealed partial class ThreadRuntime : Marrow.App.Port.PcControl.ITactusChat
{
    /// <summary>A pc_ask question in the chat's question sheet: this prefix, then its ask id.</summary>
    internal const string TactusQuestionPrefix = "tactus:";

    /// <summary>The id of the pc_ask question's only question in the sheet.</summary>
    internal const string TactusQuestionID = "answer";

    private readonly AsyncLocal<string?> tactusChipTurn = new();
    private ValueList<PcPendingAsk> tactusAsks = ValueList<PcPendingAsk>.Empty;
    private Guid? tactusTurnID;
    private Action? providerAsksChanged;

    /// <summary>The chat's Stop interrupted a running turn (thread id).</summary>
    internal static event Action<string>? TactusInterrupted;

    /// <summary>The user wrote in a chat (thread id): its pc_ask questions that wait end ("The chat has moved on").</summary>
    internal static event Action<string>? TactusUserWroteIn;

    /// <summary>A pc_ask question was answered in the chat's sheet (thread id, ask id, answer).</summary>
    internal static event Action<string, string, PcAnswer>? TactusQuestionAnswered;

    /// <summary>The chat's Tactus approvals, confirms and show-window asks: each is a badge in the timeline.</summary>
    public ValueList<PcPendingAsk> TactusAsks { get => Read(tactusAsks); private set => Set(ref tactusAsks, value); }

    string Marrow.App.Port.PcControl.ITactusChat.Thread => ThreadID.ToString();

    IReadOnlyList<QuestionRequest> Marrow.App.Port.PcControl.ITactusChat.ProviderQuestions =>
        Questions.Where(q => !q.Id.StartsWith(TactusQuestionPrefix, StringComparison.Ordinal)).ToList();

    IReadOnlyList<ApprovalRequest> Marrow.App.Port.PcControl.ITactusChat.ProviderApprovals => Approvals;

    event Action? Marrow.App.Port.PcControl.ITactusChat.ProviderAsksChanged
    {
        add
        {
            if (providerAsksChanged is null) PropertyChanged += TactusProperty;
            providerAsksChanged += value;
        }
        remove
        {
            providerAsksChanged -= value;
            if (providerAsksChanged is null) PropertyChanged -= TactusProperty;
        }
    }

    void Marrow.App.Port.PcControl.ITactusChat.Row(string text)
    {
        // the turn Tactus last spoke in: a resume continues only while it is still the chat's latest
        tactusTurnID = currentTurnID ?? Turns.LastOrDefault()?.Id;
        AppendNotice(Notice.Level.Info, text);
    }

    PcResumeOutcome Marrow.App.Port.PcControl.ITactusChat.Resume(bool continuesChat, DateTimeOffset now)
    {
        var last = Turns.LastOrDefault();
        // a running turn needs no message; an ended one without its end time counts as long ago
        long? ended = Phase != RuntimePhase.Idle ? null : last?.CompletedAt?.ToUnixTimeMilliseconds() ?? 0;
        var wrote = FollowUps.Any(prompt => !skillsFollowUps.Contains(prompt.Id));
        var facts = new PcTurnFacts(ended, last is not null && last.Id == tactusTurnID, wrote);
        return PcChatMirror.Resume(continuesChat, facts, now.ToUnixTimeMilliseconds());
    }

    void Marrow.App.Port.PcControl.ITactusChat.Continue(string message, string chip) => _ = StartTactusTurn(message, chip);

    void Marrow.App.Port.PcControl.ITactusChat.Asks(IReadOnlyList<PcPendingAsk> asks)
    {
        TactusAsks = new ValueList<PcPendingAsk>(asks.Where(a => a.Kind is PcAskKind.Approval or PcAskKind.Confirm or PcAskKind.ShowWindow));
        // pc_ask questions use the chat's own question badge and sheet (DESIGN.md 10.7)
        var waiting = asks.Where(a => a.Kind == PcAskKind.Question && a.Payload is PcAskRequest).ToDictionary(a => TactusQuestionPrefix + a.Id, StringComparer.Ordinal);
        var shown = Questions.Where(q => q.Id.StartsWith(TactusQuestionPrefix, StringComparison.Ordinal)).Select(q => q.Id).ToHashSet(StringComparer.Ordinal);
        if (shown.SetEquals(waiting.Keys)) return;
        Questions = Questions.Mutating(items =>
        {
            items.RemoveAll(q => q.Id.StartsWith(TactusQuestionPrefix, StringComparison.Ordinal) && !waiting.ContainsKey(q.Id));
            foreach (var (id, ask) in waiting)
            {
                if (!shown.Contains(id)) items.Add(TactusQuestion(id, ask));
            }
        });
    }

    void Marrow.App.Port.PcControl.ITactusChat.AnswerProvider(string requestID, PcAnswer answer)
    {
        if (Questions.FirstOrDefault(q => q.Id == requestID) is not { } request || request.Questions.Count == 0) return;
        var said = answer.Choice ?? answer.Text ?? "";
        Answer(request, ValueMap<string, ValueList<string>>.Empty.Mutating(map => map[request.Questions[0].Id] = new ValueList<string>([said])));
    }

    // The user's message (not a continue, a skills follow-up or a keep-warm ping), sent or queued.
    private void TactusUserWrote() => TactusUserWroteIn?.Invoke(ThreadID.ToString());

    private void TactusInterrupt() => TactusInterrupted?.Invoke(ThreadID.ToString());

    // The continue message shows as its chip, not as text the user typed (DESIGN.md 10.1).
    private static TimelineItem TactusChipItem(TimelineItem userItem, string chip) => userItem with
    {
        TimelineItemContent = new TimelineItem.Content.Notice(new Notice { NoticeLevel = Notice.Level.Info, Message = chip }),
    };

    // A pc_ask question answered in the sheet goes to the broker, not to the provider.
    private bool TactusAnswer(QuestionRequest request, ValueMap<string, ValueList<string>> answers)
    {
        if (!request.Id.StartsWith(TactusQuestionPrefix, StringComparison.Ordinal)) return false;
        Questions = Questions.Mutating(items => items.RemoveAll(item => item.Id == request.Id));
        var said = answers.TryGetValue(TactusQuestionID, out var values) ? string.Join(", ", values) : "";
        var choices = request.Questions.Count > 0 ? request.Questions[0].Choices.Select(c => c.Label) : [];
        var answer = choices.Contains(said, StringComparer.Ordinal) ? PcAnswer.Chose(said) : PcAnswer.Typed(said);
        TactusQuestionAnswered?.Invoke(ThreadID.ToString(), request.Id[TactusQuestionPrefix.Length..], answer);
        return true;
    }

    private static QuestionRequest TactusQuestion(string id, PcPendingAsk ask)
    {
        var request = (PcAskRequest)ask.Payload;
        var question = new QuestionRequest.Question(TactusQuestionID, "Tactus", request.Question,
            new ValueList<QuestionRequest.Choice>(request.Choices.Select(c => new QuestionRequest.Choice(c))),
            AllowsMultiple: false, AllowsOther: request.AllowText || request.Choices.Count == 0, IsSecret: ask.ChatOnly);
        return new QuestionRequest(id, new ValueList<QuestionRequest.Question>([question]));
    }

    private async Task StartTactusTurn(string text, string chip)
    {
        var previous = tactusChipTurn.Value;
        try
        {
            tactusChipTurn.Value = chip;
            await EnsureLoaded();
            // the four conditions were read when idle; a turn that started since (the user wrote) wins
            if (Phase != RuntimePhase.Idle) return;
            await StartTurn(text, []);
        }
        finally { tactusChipTurn.Value = previous; }
    }

    private void TactusProperty(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Questions) or nameof(Approvals)) providerAsksChanged?.Invoke();
    }
}
