// PORT-ONLY: Tactus, the chat mirror (OD-0145, ED-NEW-pcctl-27; DESIGN.md 10.1 "Resume after the turn ended", 10.2 path 6,
// 10.4 "Resolution", 10.7). No Swift twin. Between the chat feed and the chats' runtimes, on the UI thread: each row goes
// into its chat's timeline; a resume continues the chat (the fixed message as a chip) only when all four conditions hold,
// else the chat says "Resumed. Ask to continue." (or shows the resume row while its turn still runs); each chat's Tactus
// asks show as its badges and pc_ask questions; an answer in a badge or in the question sheet resolves the same broker entry
// as the pill (the first answer wins). The chat's Stop stops that chat's streams; the user writing ends its waiting pc_ask
// questions. A chat with Tactus in use hands the broker its provider's own questions and approvals, so the pill shows them
// (a provider approval only points at the chat); one answered in the pill goes back through the runtime's answer path.
// The composition binds it on the shared broker and the pill's card bridge (TactusChatBridge.Current), with ThreadRuntime's
// hooks as ITactusChats.
using Marrow.Core.Models;
using Marrow.Core.Port.PcControl;
using Marrow.Services.Port.PcControl;
using Marrow.UI.Port.PcControl;

namespace Marrow.App.Port.PcControl;

/// <summary>One chat's runtime as the Tactus chat mirror sees it (ThreadRuntime; tests bind fakes).</summary>
public interface ITactusChat
{
    string Thread { get; }

    /// <summary>The provider's own questions waiting in the chat (not Tactus's pc_ask ones).</summary>
    IReadOnlyList<QuestionRequest> ProviderQuestions { get; }

    /// <summary>The provider's own approvals waiting in the chat.</summary>
    IReadOnlyList<ApprovalRequest> ProviderApprovals { get; }

    /// <summary>The provider's questions or approvals changed.</summary>
    event Action? ProviderAsksChanged;

    /// <summary>Writes a Tactus row into the chat's timeline.</summary>
    void Row(string text);

    /// <summary>What a resume does in this chat, from its own last turn (DESIGN.md 10.1).</summary>
    PcResumeOutcome Resume(bool continuesChat, DateTimeOffset now);

    /// <summary>Sends the continue message as a turn shown by its chip.</summary>
    void Continue(string message, string chip);

    /// <summary>The chat's Tactus asks now (its badges and pc_ask questions follow).</summary>
    void Asks(IReadOnlyList<PcPendingAsk> asks);

    /// <summary>Answers the provider's question <paramref name="requestID"/> with the pill's answer.</summary>
    void AnswerProvider(string requestID, PcAnswer answer);
}

/// <summary>The app's chats for the mirror: lookup by thread id and the runtimes' Tactus hooks.</summary>
public interface ITactusChats
{
    ITactusChat? Find(string thread);

    /// <summary>The chat's Stop interrupted a running turn.</summary>
    event Action<string>? Interrupted;

    /// <summary>The user wrote in the chat.</summary>
    event Action<string>? UserWrote;

    /// <summary>A pc_ask question was answered in the chat's sheet (thread, ask id, answer).</summary>
    event Action<string, string, PcAnswer>? QuestionAnswered;
}

public sealed class TactusChatBridge : IDisposable
{
    private readonly PcChatFeed feed;
    private readonly PcAskBroker broker;
    private readonly PillAskBridge cards;
    private readonly ITactusChats chats;
    private readonly Func<bool> continuesChat;
    private readonly Func<DateTimeOffset> now;
    private readonly Action<Action> post;
    private readonly HashSet<string> showing = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITactusChat> watched = new(StringComparer.Ordinal);
    private readonly Action syncLater;
    private bool syncing;
    private bool again;
    // the provider's asks in the broker: (thread, request id) to ask id, and back
    private readonly Dictionary<(string Thread, string Request), string> provider = [];
    private readonly Dictionary<string, (string Thread, string Request)> providerOf = new(StringComparer.Ordinal);

    public TactusChatBridge(PcChatFeed feed, PcAskBroker broker, PillAskBridge cards, ITactusChats chats, Func<bool> continuesChat,
        Func<DateTimeOffset>? now = null, Action<Action>? post = null)
    {
        this.feed = feed;
        this.broker = broker;
        this.cards = cards;
        this.chats = chats;
        this.continuesChat = continuesChat;
        this.now = now ?? (() => DateTimeOffset.Now);
        this.post = post ?? (action => action());
        syncLater = () => this.post(Sync);
        feed.Row += OnRow;
        feed.Resumed += OnResumed;
        feed.Changed += OnChanged;
        broker.Resolved += OnResolved;
        chats.Interrupted += feed.ChatStop;
        chats.UserWrote += broker.UserWrote;
        chats.QuestionAnswered += OnQuestionAnswered;
    }

    private static TactusChatBridge? current;

    /// <summary>The running mirror (bound by the composition while Tactus is on); the strip and the badges read it.</summary>
    public static TactusChatBridge? Current
    {
        get => current;
        set
        {
            if (ReferenceEquals(current, value)) return;
            current = value;
            CurrentChanged?.Invoke();
        }
    }

    /// <summary>The running mirror changed (Tactus turned on or off).</summary>
    public static event Action? CurrentChanged;

    public PcChatFeed Feed => feed;

    /// <summary>The card body for a badge's ask (the pill's own bodies).</summary>
    public PillCardBody? Body(string id) => cards.Body(id);

    /// <summary>A choice in a badge's popover: the same broker entry as the pill's card, answered from the chat.</summary>
    public PcResolve Answer(PillCardAnswer answer) => cards.Answer(answer, PcAnswerSource.Chat);

    /// <summary>A chat closed: nothing more goes to it.</summary>
    public void Forget(string thread)
    {
        post(() =>
        {
            if (watched.Remove(thread, out var chat)) chat.ProviderAsksChanged -= syncLater;
            showing.Remove(thread);
            foreach (var key in provider.Keys.Where(k => k.Thread == thread).ToList())
            {
                providerOf.Remove(provider[key]);
                provider.Remove(key);
            }
        });
    }

    /// <summary>Shows every chat's asks and hands the active chats' provider asks to the broker (also on each change).</summary>
    public void Refresh() => post(Sync);

    public void Dispose()
    {
        feed.Row -= OnRow;
        feed.Resumed -= OnResumed;
        feed.Changed -= OnChanged;
        broker.Resolved -= OnResolved;
        chats.Interrupted -= feed.ChatStop;
        chats.UserWrote -= broker.UserWrote;
        chats.QuestionAnswered -= OnQuestionAnswered;
        foreach (var chat in watched.Values) chat.ProviderAsksChanged -= syncLater;
        watched.Clear();
        if (ReferenceEquals(Current, this)) Current = null;
    }

    private void OnRow(PcChatRow row) => post(() => chats.Find(row.Thread)?.Row(row.Text));

    private void OnResumed(PcChatResume resume) => post(() =>
    {
        if (chats.Find(resume.Thread) is not { } chat) return;
        switch (chat.Resume(continuesChat(), now()))
        {
            case PcResumeOutcome.Continue:
                chat.Continue(resume.Message, resume.Chip);
                break;
            case PcResumeOutcome.AskToContinue:
                chat.Row(PcChatMirror.AskToContinueLine);
                break;
            default:
                chat.Row(resume.Row);
                break;
        }
    });

    private void OnChanged() => post(Sync);

    private void OnQuestionAnswered(string thread, string id, PcAnswer answer)
    {
        if (broker.Find(id) is { } ask && ask.Thread == thread) broker.Resolve(id, ask.Revision, answer, PcAnswerSource.Chat);
    }

    // A provider question answered in the pill goes back through the runtime's own answer path.
    private void OnResolved(PcPendingAsk ask, object answer, PcAnswerSource source)
    {
        if (ask.Kind != PcAskKind.ProviderQuestion || source != PcAnswerSource.Pill || answer is not PcAnswer reply) return;
        post(() =>
        {
            if (!providerOf.Remove(ask.Id, out var key)) return;
            provider.Remove(key);
            chats.Find(key.Thread)?.AnswerProvider(key.Request, reply);
        });
    }

    // On the UI thread. The broker's events while it adds come back here: they run once more after, never inside.
    private void Sync()
    {
        if (syncing)
        {
            again = true;
            return;
        }
        syncing = true;
        try
        {
            do
            {
                again = false;
                SyncOnce();
            }
            while (again);
        }
        finally
        {
            syncing = false;
        }
    }

    private void SyncOnce()
    {
        var pending = broker.Pending;
        var threads = pending.Select(a => a.Thread).Concat(showing).ToHashSet(StringComparer.Ordinal);
        foreach (var thread in threads)
        {
            if (chats.Find(thread) is not { } chat) continue;
            var asks = pending.Where(a => a.Thread == thread).ToList();
            chat.Asks(asks);
            if (asks.Count > 0) showing.Add(thread);
            else showing.Remove(thread);
        }
        // a chat with Tactus in use hands the broker its provider's asks (DESIGN.md 10.7)
        foreach (var thread in feed.Chats)
        {
            if (watched.ContainsKey(thread) || chats.Find(thread) is not { } chat) continue;
            watched[thread] = chat;
            chat.ProviderAsksChanged += syncLater;
        }
        foreach (var chat in watched.Values.ToList()) Provider(chat);
    }

    // The provider's asks in this chat match the broker's: new ones are added, ones the chat answered leave it.
    private void Provider(ITactusChat chat)
    {
        var thread = chat.Thread;
        var live = new HashSet<string>(StringComparer.Ordinal);
        foreach (var request in chat.ProviderQuestions)
        {
            live.Add(request.Id);
            if (provider.ContainsKey((thread, request.Id)) || request.Questions.Count == 0) continue;
            var first = request.Questions[0];
            var payload = new PcProviderAsk(first.Header.Length > 0 ? first.Header : "Question", first.Prompt, first.IsSecret)
            {
                Choices = [.. first.Choices.Select(c => c.Label)],
                Several = request.Questions.Count > 1 || first.AllowsMultiple,
            };
            Add(thread, request.Id, PcAskKind.ProviderQuestion, payload);
        }
        foreach (var request in chat.ProviderApprovals)
        {
            live.Add(request.Id);
            if (provider.ContainsKey((thread, request.Id))) continue;
            Add(thread, request.Id, PcAskKind.ProviderApproval, new PcProviderAsk(request.Headline, request.Title));
        }
        foreach (var key in provider.Keys.Where(k => k.Thread == thread && !live.Contains(k.Request)).ToList())
        {
            // answered in the chat (or gone with the turn): the broker's entry ends, answered from the chat
            var id = provider[key];
            provider.Remove(key);
            providerOf.Remove(id);
            if (broker.Find(id) is { } ask) broker.Resolve(id, ask.Revision, true, PcAnswerSource.Chat);
        }
    }

    private void Add(string thread, string request, PcAskKind kind, PcProviderAsk payload)
    {
        var ask = broker.Add(new PcAskDraft(kind, thread, payload));
        provider[(thread, request)] = ask.Id;
        providerOf[ask.Id] = (thread, request);
    }
}
