// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3, 12.4; unit AS-22; ED-NEW-dextra-as22-4 to -6, -10). No Swift twin.
// The delegate's way with a question an agent asks (ProviderEvent.Question). The gates run in this order, and the first that closes sends the question
// to the owner: the checks passed; the engine's verdict on a delegate in this chat; the words of the question (DelegatePolicy.BeforeModel); a model
// to ask; the privacy rules of 5.4; room for one more question with the model. Then the model is asked off the main thread, for a bounded time, and
// the app's main thread judges what it said (DelegatePolicy.AfterModel) with every check made again, because the owner may have turned the delegate
// off, the chat may have read something, or the task may have been stopped while the model thought.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class DelegateResponder
{
    private enum Seat { Added, Held, Full }

    /// <summary>
    /// A question of a chat. True when the delegate has it: it answers, or it leaves it to the owner (a card, or the mission runner's card for a mission
    /// chat). False when the delegate is not engaged for this chat, and the question is the chat's own as it always was.
    /// </summary>
    public bool OnQuestion(Guid thread, QuestionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Engaged(thread) is not { } engaged) return false;
        try
        {
            if (DelegatePolicy.BeforeModel(request, o.MayAnswer(thread)) is { } closed) return Leave(engaged, request, closed);
            if (o.Model?.Prepare(engaged.WhoModel) is not { } call) return Leave(engaged, request, DelegateReason.NoModel);
            // 5.4: the delegate reads a chat's words only when the filter lets its provider; a hidden chat was never engaged
            if (FeedPrivacy.Decide(engaged.Facts, o.Brain(call.Provider)) != FeedTextAccess.Full) return Leave(engaged, request, DelegateReason.Private);
            DelegateChatView? view = null;
            try { view = o.Context?.View(thread); }
            catch (Exception error) when (error is not OperationCanceledException) { Fault("A chat could not be read for its delegate: " + error.Message); }
            var cancel = new CancellationTokenSource(o.ModelTimeout, o.Time);
            var token = cancel.Token;                                   // read now: a stop may dispose the source before the pool thread starts
            var work = new Thinking(cancel, engaged, request);
            var taken = Seat.Added;
            lock (gate)
            {
                // The same request again (a provider that sends it twice) is already with the model: it is not asked twice.
                if (pending.ContainsKey((thread, request.Id))) taken = Seat.Held;
                else if (disposed || pending.Count >= MaxPending) taken = Seat.Full;
                else pending[(thread, request.Id)] = work;
            }
            if (taken != Seat.Added)
            {
                cancel.Dispose();
                return taken == Seat.Held || Leave(engaged, request, DelegateReason.TooMuch);
            }
            bool admitted;
            try { admitted = call.Admit?.Invoke(thread) != false; }
            catch
            {
                lock (gate) pending.Remove((thread, request.Id));
                cancel.Dispose();
                call.Release?.Invoke();
                throw;
            }
            if (!admitted)
            {
                lock (gate) pending.Remove((thread, request.Id));
                cancel.Dispose();
                return Leave(engaged, request, DelegateReason.NoModel);
            }
            _ = Task.Run(() => Think(work, view, call, token));
            return true;
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("Dextra could not check a question for you, so it is left to you: " + error.Message);
            return Leave(engaged, request, DelegateReason.NoModel);
        }
    }

    // Off the main thread: gather the context, ask the model, hand the text back to the main thread.
    private async Task Think(Thinking work, DelegateChatView? view, DelegateCall call, CancellationToken cancel)
    {
        var e = work.Engagement;
        var request = work.Request;
        DelegatePrompt? prompt = null;
        string? text = null;
        var failed = false;
        try
        {
            var pack = view is null || o.Context is null ? DelegatePack.Empty : await o.Context.Pack(view, request, cancel).ConfigureAwait(false);
            prompt = DelegateBrief.Build(e.Title, request, pack);
            text = await call.Ask(prompt.Text, cancel).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A stop or a settled request let this work go (Judge finds nothing pending and ends quietly); a timeout leaves the question to the owner.
            failed = true;
        }
        catch (Exception error)
        {
            failed = true;
            Fault("Dextra's delegate could not be asked: " + error.GetType().Name);
        }
        finally { if (call.Release is { } release) o.Agents.Post(release); }
        o.Agents.Post(() => Judge(work, prompt, text, failed));
    }

    // Main thread: every check again, then answer or leave.
    private void Judge(Thinking work, DelegatePrompt? prompt, string? text, bool failed)
    {
        var e = work.Engagement;
        var request = work.Request;
        lock (gate)
        {
            // Settled while the model thought (answered in the chat, stopped, asked again since): nothing more to do. It is this work that is taken, not a later one of the same request.
            if (!pending.TryGetValue((e.Thread, request.Id), out var current) || !ReferenceEquals(current, work)) return;
            pending.Remove((e.Thread, request.Id));
        }
        Release(work);
        try
        {
            if (disposed) return;
            // A mission that is stopping or over takes no answer (the cut turn asks again on Resume), whatever else changed.
            var state = o.MissionState(e.Thread);
            if (state is MissionState.Stopping or MissionState.Stopped or MissionState.Done or MissionState.Failed) return;
            // The owner may have turned it off, the checks may have failed, or the chat may be gone: the question goes back to its own card.
            if (Engaged(e.Thread) is not { } now) { HandBack(e, request); return; }
            // A paused mission keeps the owner's card, which stays answerable while it is paused (12.6b).
            if (state is MissionState.Pausing or MissionState.Paused) { HandBack(e, request); return; }
            if (failed || prompt is null) { Leave(now, request, DelegateReason.NoModel); return; }
            var reply = DelegateReply.Parse(text);
            var outcome = DelegatePolicy.AfterModel(request, reply, prompt.Evidence, o.MayAnswer(e.Thread));
            if (outcome.Answers is null) { Leave(now, request, outcome.Leaves ?? DelegateReason.Unclear); return; }
            Answer(now, request, outcome, reply!);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("Dextra could not judge a question for you, so it is left to you: " + error.Message);
            Leave(e, request, DelegateReason.NoModel);
        }
    }

    // The answer: logged first (with the reasoning), then sent, labelled in the chat, kept where the owner can Override it, and told to the feed.
    private void Answer(DelegateEngagement e, QuestionRequest request, DelegateOutcome outcome, DelegateReply reply)
    {
        var id = Guid.NewGuid();
        var question = QuestionText(request);
        if (!Log(e, "delegate", ActionClass.Agents, "answered: " + outcome.Summary,
            [Plain("question", question), Plain("reasoning", reply.Reason), AuditInput.Numeric("confidence", reply.Confidence)], "delegate:" + id.ToString("D"), e.Title))
        {
            Leave(e, request, DelegateReason.NotLogged);
            return;
        }
        o.Agents.Answer(e.Thread, request, outcome.Answers!);
        o.Book.Add(new DelegateRecord(id, o.Time.GetUtcNow(), e.Thread, e.Title, DelegateKind.Question, question, outcome.Summary, e.Who, reply.Reason)
        {
            Project = e.Chat.ProjectID,
            Confidence = reply.Confidence,
        });
        o.Agents.Note(e.Thread, DelegateWords.AnswerNote(e.Who, outcome.Summary));
        Told(e.Thread, Masked(outcome.Summary, DelegateWords.ShownChars), approval: false);
    }

    // The question goes to the owner. A mission chat's goes back to the runner; any other chat's gets a card in the queue beside its own in the chat.
    private bool Leave(DelegateEngagement e, QuestionRequest request, DelegateReason reason)
    {
        Log(e, "delegate", ActionClass.Agents, "left to the owner: " + reason, [Plain("question", QuestionText(request))], null, e.Title);
        o.Agents.Note(e.Thread, DelegateWords.LeftNote(e.Who, reason));
        if (e.IsMission) return o.LeaveToRunner(e.Thread, request);
        return Raise(e, request, reason);
    }

    // The delegate is out of it (turned off, or the task paused): the question is the chat's or the mission's own again, with no note.
    private void HandBack(DelegateEngagement e, QuestionRequest request)
    {
        if (e.IsMission) o.LeaveToRunner(e.Thread, request);
    }

    private bool Raise(DelegateEngagement e, QuestionRequest request, DelegateReason reason)
    {
        var secret = request.Questions.Any(q => q.IsSecret);
        var single = request.Questions.Count == 1 ? request.Questions[0] : null;
        lock (gate)
        {
            if (cards.Count >= MaxCards && !cards.ContainsKey((e.Thread, request.Id))) return true;   // the chat keeps its own card
            cards[(e.Thread, request.Id)] = request.Id;
        }
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Question, DecisionWeight.Question,
            single?.Prompt ?? string.Join(" ", request.Questions.Select(q => q.Prompt)))
        {
            Inputs = new ValueList<DecisionInput>(request.Questions.Select(q => new DecisionInput(q.Header.Length > 0 ? q.Header : "Question", q.Prompt))),
            Why = DelegateWords.Reason(reason),
            Rule = DelegateWords.RuleLine(reason),
            AssistantId = o.AssistantId(),
            ThreadId = e.Thread,
            Agent = e.Title,
            RequestId = request.Id,
            Options = secret || single is null ? ValueList<string>.Empty : new ValueList<string>(single.Choices.Select(c => c.Label)),
            Note = secret ? "It asks for a secret, so answer it in the chat; nth never keeps a secret on a card." : null,
        };
        o.Queue.Raise(card, (item, answer) => o.Agents.Post(() => Asked(e, item, answer, request)),
            secret ? (_, _) => "It asks for a secret: answer it in the chat." : null);
        return true;
    }

    // The owner answered the card in the queue: the question goes to the chat's agent, unless the chat settled it first.
    private void Asked(DelegateEngagement e, DecisionItem card, DecisionAnswer answer, QuestionRequest request)
    {
        bool open;
        lock (gate) open = cards.Remove((e.Thread, request.Id));
        if (!open) return;
        var text = answer.Text ?? "";
        Log(e, "delegate", ActionClass.Agents, "answered by the owner from the queue: " + answer.Kind, [AuditInput.Typed("answer", text)], "card:" + card.Id.ToString("D"), e.Title);
        var answers = request.Questions.ToDictionary(q => q.Id, _ => new ValueList<string>([text]));
        o.Agents.Answer(e.Thread, request, new ValueMap<string, ValueList<string>>(answers));
    }
}
