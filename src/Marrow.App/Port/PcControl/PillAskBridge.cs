// PORT-ONLY: Tactus pill v2, the pill's view of the ask broker (OD-0145, ED-NEW-pcctl-16, ED-NEW-pcctl-25, ED-NEW-pcctl-26;
// DESIGN.md 10.4 "What can ask", "Queue", "Resolution", 10.7). The broker holds every pending ask; the pill's card and the
// chat are two views of it. This turns each ask into what the pill shows (PillAsk for the header and the badge, a
// PillCardBody for the card) and each card choice into the broker's answer, so a choice in the pill and one in the chat
// resolve the same entry once (compare-and-set on its id and revision). A confirm's content is read again at the click:
// when it changed, nothing is sent, the ask takes a new revision, and the card shows the new content with its changed rows
// marked. A click that comes too late does nothing and says so for 2 s. An ask that ended while its card held a draft
// stays as the draft ("The model stopped waiting for this." / "Answered in the chat. Your draft is still here.") until
// the user sends it to the chat or discards it.
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl;

public sealed class PillAskBridge : IDisposable
{
    private readonly Lock gate = new();
    private readonly PcAskBroker broker;
    private readonly Func<string?> helper;
    private readonly Func<PcPendingAsk, PcConfirmContent?>? reread;
    private readonly Dictionary<string, PcPendingAsk> seen = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> drafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<string>> changed = new(StringComparer.Ordinal);
    private readonly List<(PcPendingAsk Ask, PillWithdrawnBody Body)> kept = [];

    /// <param name="helper">The fast helper's model while it is on (the approval card's egress line), else null.</param>
    /// <param name="reread">Reads a confirm's content from its window again at the click; null leaves the content as shown.</param>
    public PillAskBridge(PcAskBroker broker, Func<string?>? helper = null, Func<PcPendingAsk, PcConfirmContent?>? reread = null)
    {
        this.broker = broker;
        this.helper = helper ?? (() => null);
        this.reread = reread;
        broker.Added += OnChanged;
        broker.Revised += OnChanged;
        broker.Resolved += OnResolved;
        broker.Withdrawn += OnWithdrawn;
    }

    /// <summary>The queue changed (an ask came, was answered, withdrawn or read again): the pill refreshes.</summary>
    public event Action? Changed;

    /// <summary>"Open chat": the chat of <c>thread</c> opens at the ask, with the card's draft when there is one.</summary>
    public event Action<string, string, string?>? OpenChat;

    /// <summary>"Send to chat": the draft goes to that chat as a normal message.</summary>
    public event Action<string, string>? SendToChat;

    /// <summary>A click came too late: the pill says this for 2 s (AskLifetimes.TooLate).</summary>
    public event Action<string, string>? TooLate;

    /// <summary>The asks the pill carries, in arrival order across chats, then the drafts of asks that ended.</summary>
    public IReadOnlyList<PillAsk> Asks
    {
        get
        {
            var pending = broker.Pending;
            lock (gate)
            {
                foreach (var ask in pending) seen[ask.Id] = ask;
                return [.. pending.Select(Ask), .. kept.Select(k => Ask(k.Ask) with { Withdrawn = true })];
            }
        }
    }

    /// <summary>The card body for the ask <paramref name="id"/>; null when it is gone.</summary>
    public PillCardBody? Body(string id)
    {
        lock (gate)
        {
            if (kept.FirstOrDefault(k => k.Ask.Id == id) is { Body: { } draft }) return draft;
        }
        return broker.Find(id) is { } ask ? BodyOf(ask) : null;
    }

    /// <summary>The card's reply field holds <paramref name="draft"/> for the ask (empty: no draft).</summary>
    public void Draft(string id, string draft)
    {
        lock (gate)
        {
            if (string.IsNullOrWhiteSpace(draft)) drafts.Remove(id);
            else drafts[id] = draft;
        }
    }

    /// <summary>
    /// Applies a choice from the card: Done when it answered, Changed when a confirm's content changed (asked again),
    /// TooLate when the ask had ended (the pill says so), Refused when the ask does not take it from the pill.
    /// </summary>
    public PcResolve Answer(PillCardAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (Kept(answer) is { } done) return done;
        var ask = broker.Find(answer.AskId);
        if (ask is null)
        {
            Late(answer.AskId);
            return PcResolve.TooLate;
        }
        switch (answer.Choice)
        {
            case PillCardChoice.OpenChat:
                OpenChat?.Invoke(ask.Thread, ask.Id, answer.Text ?? DraftOf(ask.Id));
                return PcResolve.Done;
            case PillCardChoice.Dismiss:
                return broker.Dismiss(ask.Id) ? Forget(ask.Id, PcResolve.Done) : Late(ask.Id);
            case PillCardChoice.Confirm when reread?.Invoke(ask) is { } live && ask.Payload is PcConfirmContent shown && shown.Changes(live) is { Count: > 0 } changes:
                lock (gate) changed[ask.Id] = changes;
                broker.Revise(ask.Id, live);
                return PcResolve.Changed;
        }
        var reply = Reply(ask, answer);
        if (reply is null) return PcResolve.Refused;
        var result = broker.Resolve(ask.Id, answer.Revision, reply, PcAnswerSource.Pill);
        if (result == PcResolve.TooLate) Late(ask.Id);
        return result == PcResolve.Done ? Forget(ask.Id, result) : result;
    }

    public void Dispose()
    {
        broker.Added -= OnChanged;
        broker.Revised -= OnChanged;
        broker.Resolved -= OnResolved;
        broker.Withdrawn -= OnWithdrawn;
    }

    // ---- the pill's view ----

    private static PillAsk Ask(PcPendingAsk ask) => new(
        ask.Id,
        ask.Kind switch
        {
            PcAskKind.Approval => PillAskKind.Approval,
            PcAskKind.Confirm => PillAskKind.Confirm,
            PcAskKind.ShowWindow => PillAskKind.ShowWindow,
            PcAskKind.Question or PcAskKind.ProviderQuestion => PillAskKind.Question,
            _ => PillAskKind.Provider,
        },
        ask.AppName ?? "",
        ask.Chat ?? "",
        ask.Model ?? "",
        ask.Kind == PcAskKind.ProviderApproval && ask.Payload is PcProviderAsk provider ? provider.Title : "")
    {
        Head = ask.Head,
    };

    private PillCardBody BodyOf(PcPendingAsk ask)
    {
        var model = ask.Model is { Length: > 0 } m ? m : "The model";
        switch (ask.Payload)
        {
            case PcApprovalAsk approval:
                return new PillApprovalBody(ask.Id, ask.Revision, ask.AppName ?? "", PathOf(ask.App), approval.Risky, approval.Capability == PcCapability.Act, approval.AllowAlways,
                    ask.Head is { Length: > 0 } head ? head : "This chat", helper());
            case PcConfirmContent content:
            {
                IReadOnlyList<string> marks;
                lock (gate) marks = changed.GetValueOrDefault(ask.Id) ?? [];
                var rows = content.Rows.Select(r => new PillConfirmRow(r.Label, r.Shown, r.Unreadable, marks.Contains(r.Label), Long: r.Label is "Message" or "Body")).ToList();
                return new PillConfirmBody(ask.Id, ask.Revision, content.App, content.Verb, content.Verb, rows, rows.Any(r => r.Unreadable), marks.Count > 0 ? content.ChangedLine(marks) : null);
            }
            case PcShowAsk show:
                return new PillShowBody(ask.Id, ask.Revision, ask.AppName ?? show.WindowTitle, show.Why);
            case PcAskRequest question:
                return new PillQuestionBody(ask.Id, ask.Revision, model, question.Question, question.Choices, AllowText: question.AllowText, Secret: question.AsksForSecret);
            case PcProviderAsk provider when ask.Kind == PcAskKind.ProviderQuestion:
                return new PillQuestionBody(ask.Id, ask.Revision, model, provider.FirstLine, provider.Choices, Secret: provider.Secret, CanDismiss: false);
            case PcProviderAsk provider:
                return new PillProviderBody(ask.Id, ask.Revision, $"{model} wants to {Lower(provider.Title)}.", provider.FirstLine);
            default:
                return new PillProviderBody(ask.Id, ask.Revision, AskLifetimes.Title(ask) + ".", "");
        }
    }

    // ---- the broker's answer ----

    private object? Reply(PcPendingAsk ask, PillCardAnswer answer) => (ask.Kind, answer.Choice) switch
    {
        (PcAskKind.Approval, PillCardChoice.Deny) => new PcApprovalReply(PcApproval.Denied, answer.Use, answer.Text),
        (PcAskKind.Approval, PillCardChoice.AllowChat) => new PcApprovalReply(PcApproval.Chat, answer.Use),
        (PcAskKind.Approval, PillCardChoice.AllowAlways) => new PcApprovalReply(PcApproval.Always, answer.Use),
        (PcAskKind.Confirm, PillCardChoice.Confirm) => new PcConfirmReply(true),
        (PcAskKind.Confirm, PillCardChoice.Cancel) => new PcConfirmReply(false, answer.Text),
        (PcAskKind.ShowWindow, PillCardChoice.Show) => PcShowAnswer.Show,
        (PcAskKind.ShowWindow, PillCardChoice.NotNow) => PcShowAnswer.NotNow,
        (PcAskKind.Question, PillCardChoice.Chip) when answer.Text is { } chip => PcAnswer.Chose(chip),
        (PcAskKind.Question, PillCardChoice.Send) when answer.Text is { Length: > 0 } text => new PcAnswer(null, text),
        (PcAskKind.ProviderQuestion, PillCardChoice.Chip) when answer.Text is { } chip => PcAnswer.Chose(chip),
        (PcAskKind.ProviderQuestion, PillCardChoice.Send) when answer.Text is { Length: > 0 } text => new PcAnswer(null, text),
        _ => null,
    };

    // The draft of an ask that ended: Send to chat or Discard, and it leaves the queue.
    private PcResolve? Kept(PillCardAnswer answer)
    {
        (PcPendingAsk Ask, PillWithdrawnBody Body) entry;
        lock (gate)
        {
            var at = kept.FindIndex(k => k.Ask.Id == answer.AskId);
            if (at < 0) return null;
            entry = kept[at];
            if (answer.Choice is not (PillCardChoice.Discard or PillCardChoice.SendToChat)) return PcResolve.Refused;
            kept.RemoveAt(at);
            drafts.Remove(answer.AskId);
        }
        if (answer.Choice == PillCardChoice.SendToChat) SendToChat?.Invoke(entry.Ask.Thread, entry.Body.Draft);
        Changed?.Invoke();
        return PcResolve.Done;
    }

    private PcResolve Late(string id)
    {
        PcPendingAsk? ask;
        lock (gate) ask = seen.GetValueOrDefault(id);
        if (ask is not null) TooLate?.Invoke(ask.Id, AskLifetimes.TooLate(ask));
        return PcResolve.TooLate;
    }

    private PcResolve Forget(string id, PcResolve result)
    {
        lock (gate)
        {
            drafts.Remove(id);
            changed.Remove(id);
        }
        return result;
    }

    private string? DraftOf(string id)
    {
        lock (gate) return drafts.GetValueOrDefault(id);
    }

    private void OnChanged(PcPendingAsk ask)
    {
        lock (gate) seen[ask.Id] = ask;
        Changed?.Invoke();
    }

    // An answer in the chat while the card holds a draft for it: the draft stays ("Answered in the chat. Your draft is
    // still here.").
    private void OnResolved(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        lock (gate)
        {
            if (source == PcAnswerSource.Chat && drafts.TryGetValue(ask.Id, out var draft)) kept.Add((ask, new PillWithdrawnBody(ask.Id, ask.Revision + 1, AnsweredInChat: true, draft)));
            else drafts.Remove(ask.Id);
            changed.Remove(ask.Id);
        }
        Changed?.Invoke();
    }

    // Withdrawn with a draft open: the card keeps the text ("The model stopped waiting for this.").
    private void OnWithdrawn(PcPendingAsk ask, PcWithdrawal reason)
    {
        lock (gate)
        {
            if (reason != PcWithdrawal.Dismissed && drafts.TryGetValue(ask.Id, out var draft)) kept.Add((ask, new PillWithdrawnBody(ask.Id, ask.Revision + 1, AnsweredInChat: false, draft)));
            else drafts.Remove(ask.Id);
            changed.Remove(ask.Id);
        }
        Changed?.Invoke();
    }

    // "exe:c:\program files\google\chrome\application\chrome.exe" -> the path; an AUMID shows as it is.
    private static string PathOf(string? key) => key switch
    {
        null => "",
        _ when key.StartsWith("exe:", StringComparison.Ordinal) => key[4..],
        _ when key.StartsWith("aumid:", StringComparison.Ordinal) => key[6..],
        _ => key,
    };

    private static string Lower(string title) => title.Length > 0 ? char.ToLowerInvariant(title[0]) + title[1..] : title;
}
