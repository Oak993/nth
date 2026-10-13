// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "The delegate responder", 12.3 point 2, 12.4, 16 row AS-22; unit AS-22; ED-NEW-dextra-as22-1 to -12). No Swift twin.
// Answers for the owner when he asked it to: a provider approval of a chat of his, and a question an agent asks. Off until he turns it on (first run,
// Settings, or a chat's menu). It answers only what the policy allows, says so in the chat ("Answered by ⟨head⟩ for you"), logs first with its
// reasoning, lists the answer where he can Override it, and leaves to him, as a card in the queue, everything the engine asks about (money, credentials,
// anything sent out, deleting, installing, a promise), a true taste pick, anything it is not sure of, and anything in a chat that read untrusted content.
//   Approvals: decided by the engine with the classifier and the owner's rules, as a mission's are; an Allow by rule or grant is answered yes, an Ask
//     becomes a card, a Deny is left to the chat's own card.
//   Questions: gated by DelegatePolicy before the model is asked and again on what it says; the model only ever sees the prompt nth builds.
// Nothing here calls a provider: the model is the IDelegateModel seam. All state changes happen on the app's main thread (IMissionAgents.Post).
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed class DelegateResponderOptions
{
    public required IMissionAgents Agents { get; init; }

    public required DecisionQueue Queue { get; init; }

    public required DelegateAnswerBook Book { get; init; }

    /// <summary>Where the delegate answers for this chat and as whom; null when it does not (off, hidden, Dextra's own chat). Main thread.</summary>
    public required Func<Guid, DelegateEngagement?> Engage { get; init; }

    /// <summary>Whether the injection self-check passed (16, the ordering rule): until then the delegate answers nothing.</summary>
    public required Func<bool> AutoAnswering { get; init; }

    /// <summary>The engine's verdict on a delegate answering in this chat now (PolicyEngine.DelegateAnswer): Deny while the chat read untrusted content.</summary>
    public required Func<Guid, Verdict> MayAnswer { get; init; }

    /// <summary>The engine's verdict on an approval of an owner's chat, with the classifier and the owner's rules (as a mission's approvals are decided).</summary>
    public required Func<Guid, ApprovalRequest, ApprovalVerdict> DecideApproval { get; init; }

    /// <summary>Writes one activity log line; throws when it cannot (then nothing is done for the owner: 12.5, logged first).</summary>
    public required Action<AuditEntry> Audit { get; init; }

    /// <summary>Dextra's id: the assistant the log lines and the cards name.</summary>
    public required Func<string> AssistantId { get; init; }

    /// <summary>The privacy rules of 5.4 for a delegate whose model runs on this provider.</summary>
    public required Func<ProviderKind?, FeedBrain> Brain { get; init; }

    /// <summary>A mission chat's question the delegate leaves to the owner goes back to the mission runner, which raises its card. False when it did not take it.</summary>
    public required Func<Guid, QuestionRequest, bool> LeaveToRunner { get; init; }

    /// <summary>The state of the mission a chat belongs to, or null for a chat that is in none.</summary>
    public Func<Guid, MissionState?> MissionState { get; init; } = _ => null;

    /// <summary>The grants an Allow used are spent or recorded (a Once grant is spent).</summary>
    public Action<IReadOnlyList<Guid>, string>? GrantsUsed { get; init; }

    /// <summary>A line for the feed: the chat, what happened and whether it was an approval.</summary>
    public Action<Guid, string, bool>? Told { get; init; }

    /// <summary>A message for a chat, now if it is idle, else when its turn ends (the controller's Deliver).</summary>
    public required Action<Guid, string> Deliver { get; init; }

    /// <summary>Whether a chat still exists (a correction for a chat that is gone is saved and not sent).</summary>
    public Func<Guid, bool> ChatExists { get; init; } = _ => true;

    public IDelegateModel? Model { get; init; }

    public IDelegateContext? Context { get; init; }

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>How long the model has before the question goes to the owner.</summary>
    public TimeSpan ModelTimeout { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>A fault the owner should see on the board: a log that could not be written, a model that threw.</summary>
    public Action<string>? Faulted { get; init; }
}

public sealed partial class DelegateResponder : IStopScope, IDisposable
{
    /// <summary>The most questions asked of the model at once; past it a question goes to the owner.</summary>
    public const int MaxPending = 4;

    /// <summary>The most escalated approvals and questions the responder holds cards for at once.</summary>
    public const int MaxCards = 16;

    private readonly DelegateResponderOptions o;
    private readonly object gate = new();
    private readonly Dictionary<(Guid Thread, string Request), Thinking> pending = [];
    private readonly Dictionary<(Guid Thread, string Request), string> cards = [];
    private bool disposed;

    private sealed class Thinking(CancellationTokenSource cancel, DelegateEngagement engagement, QuestionRequest request)
    {
        public CancellationTokenSource Cancel { get; } = cancel;
        public DelegateEngagement Engagement { get; } = engagement;
        public QuestionRequest Request { get; } = request;
    }

    public DelegateResponder(DelegateResponderOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The book of what it answered (the Decisions tab lists it).</summary>
    public DelegateAnswerBook Book => o.Book;

    /// <summary>How many questions are with the model now.</summary>
    public int Pending
    {
        get { lock (gate) return pending.Count; }
    }

    string IStopScope.Name => "Answers for you";

    /// <summary>
    /// Stop all (12.6): every question with the model is let go, its model call cancelled. Each stays in its chat as it was, for the owner; nothing is
    /// answered after a stop. Only sets flags; never waits.
    /// </summary>
    void IStopScope.Stop() => CancelAll();

    /// <summary>The request was settled without the delegate (the owner answered in the chat, the agent cancelled, a stop): its work and its card go.</summary>
    public void OnRequestResolved(Guid thread, string requestId)
    {
        Thinking? let;
        bool card;
        lock (gate)
        {
            pending.Remove((thread, requestId), out let);
            card = cards.Remove((thread, requestId));
        }
        Release(let);
        if (card) o.Queue.WithdrawRequest(thread, requestId, MissionRunner.RequestSettled);
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
        }
        CancelAll();
    }

    private void CancelAll()
    {
        List<Thinking> all;
        lock (gate)
        {
            all = [.. pending.Values];
            pending.Clear();
        }
        foreach (var let in all) Release(let);
    }

    private static void Release(Thinking? let)
    {
        // Each piece of work is let go once: it leaves the table under the lock before this runs, so its source is never cancelled after it was disposed.
        if (let is null) return;
        let.Cancel.Cancel();
        let.Cancel.Dispose();
    }

    // ---- the one place a delegate's state is checked ----

    /// <summary>The engagement for a chat, or null: off, hidden, not a chat of the owner's, the checks not passed, or nth could not read the chat.</summary>
    private DelegateEngagement? Engaged(Guid thread)
    {
        if (disposed) return null;
        try
        {
            if (o.Engage(thread) is not { } engaged) return null;
            return o.AutoAnswering() ? engaged : null;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("nth could not tell whether Dextra answers in a chat: " + error.Message);
            return null;
        }
    }

    private void Fault(string text)
    {
        try { o.Faulted?.Invoke(text); }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            // a fault report cannot fail a chat
            _ = error;
        }
    }

    private void Told(Guid thread, string line, bool approval)
    {
        try { o.Told?.Invoke(thread, line, approval); }
        catch (Exception error) when (error is not OperationCanceledException) { Fault("The feed could not show an answer: " + error.Message); }
    }

    // One activity log line. False when it could not be written: the caller then does nothing for the owner.
    private bool Log(DelegateEngagement e, string tool, ActionClass actionClass, string result, IEnumerable<AuditInput> inputs, string? allowedBy = null, string? target = null)
    {
        try
        {
            o.Audit(new AuditEntry
            {
                Assistant = o.AssistantId(),
                Mission = e.Chat.MissionID?.ToString("D"),
                Agent = e.Thread.ToString("D"),
                Tool = tool,
                ActionClass = actionClass.ToString(),
                Target = target is null ? null : Masked(target, 200),
                Inputs = [.. inputs],
                Result = Masked(result, 400),
                AllowedBy = allowedBy,
            });
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("The activity log could not be written: " + error.Message);
            return false;
        }
    }

    // Words from a chat are masked for secrets before they reach a log line (12.5), whatever the writer does after.
    internal static string Masked(string? text, int chars)
    {
        var line = DelegateWords.Clip(FeedShield.Mask(text ?? ""), chars);
        return line;
    }

    internal static AuditInput Plain(string name, string? text, int chars = 600) => AuditInput.Plain(name, Masked(text, chars));

    // The question as a card and the log say it: each question's header (or "Question") and its prompt.
    internal static string QuestionText(QuestionRequest request) =>
        string.Join(" / ", request.Questions.Select(q => q.Prompt.Trim()).Where(p => p.Length > 0));

    internal static string AnswerText(ValueMap<string, ValueList<string>> answers) =>
        string.Join("; ", answers.Values.Select(v => string.Join(", ", v)));
}
