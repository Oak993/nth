// PORT-ONLY: Tactus, PC control (OD-0145, ED-NEW-pcctl-12 item 4, ED-NEW-pcctl-16; DESIGN.md 10.4, 10.7). Every pending
// ask, from every chat, in arrival order: app approvals, confirms, show-window asks, pc_ask questions and the provider's
// own questions and approvals in a chat with a Tactus stream. The pill's card and the chat are two views of it. An
// answer resolves an ask once: Resolve is a compare-and-set on its id and its revision, so the first answer wins and a
// click after the ask ended, or on content that was read again, does nothing. Lifetimes follow AskLifetimes. Shared by
// every chat, like PcRunControl; it never references the app (ThreadRuntime registers adapters).
using System.Globalization;

namespace Marrow.Services.Port.PcControl;

public sealed class PcAskBroker : IDisposable
{
    private readonly Lock gate = new();
    private readonly IPcClock clock;
    private readonly PcRunControl? control;
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private long sequence;

    /// <param name="clock">The clock a question's 30 minutes run on.</param>
    /// <param name="control">When given, a stop withdraws the asks it covers (questions stay).</param>
    public PcAskBroker(IPcClock clock, PcRunControl? control = null)
    {
        this.clock = clock;
        this.control = control;
        if (control is not null) control.Stopped += OnStopped;
    }

    public event Action<PcPendingAsk>? Added;

    /// <summary>The ask's subject was read again and differs (a new revision).</summary>
    public event Action<PcPendingAsk>? Revised;

    public event Action<PcPendingAsk, object, PcAnswerSource>? Resolved;

    public event Action<PcPendingAsk, PcWithdrawal>? Withdrawn;

    /// <summary>The pending asks in arrival order, across chats.</summary>
    public IReadOnlyList<PcPendingAsk> Pending
    {
        get
        {
            lock (gate) return entries.Values.OrderBy(e => e.Order).Select(e => e.Ask).ToList();
        }
    }

    public int Count
    {
        get
        {
            lock (gate) return entries.Count;
        }
    }

    /// <summary>The pending asks of one chat, in arrival order.</summary>
    public IReadOnlyList<PcPendingAsk> Waiting(string thread)
    {
        lock (gate) return entries.Values.Where(e => e.Ask.Thread == thread).OrderBy(e => e.Order).Select(e => e.Ask).ToList();
    }

    /// <summary>The ask as it stands now; null once it was answered or withdrawn.</summary>
    public PcPendingAsk? Find(string id)
    {
        lock (gate) return entries.TryGetValue(id, out var entry) ? entry.Ask : null;
    }

    /// <summary>
    /// Queues an ask. <paramref name="step"/> is the token of the step it belongs to: an approval, confirm or show-window ask
    /// is withdrawn when it is cancelled. A question has its own 30 minutes and ignores it.
    /// </summary>
    public PcPendingAsk Add(PcAskDraft draft, CancellationToken step = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        Entry entry;
        lock (gate)
        {
            var order = ++sequence;
            var id = "ask-" + order.ToString(CultureInfo.InvariantCulture);
            var done = new TaskCompletionSource<PcAskOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ask = new PcPendingAsk(id, draft.Kind, draft.Thread, draft.Payload, clock.NowMs, done.Task)
            {
                App = draft.App,
                AppName = draft.AppName,
                Head = draft.Head,
                Model = draft.Model,
                Chat = draft.Chat,
            };
            entry = new Entry(ask, order, done);
            entries[id] = entry;
        }
        Added?.Invoke(entry.Ask);
        if (AskLifetimes.LifetimeMs(draft.Kind) is { } life) _ = Expire(entry, life);
        if (AskLifetimes.EndsWithStep(draft.Kind) && step.CanBeCanceled)
        {
            var id = entry.Ask.Id;
            entry.Step = step.UnsafeRegister(_ => Withdraw(id, PcWithdrawal.StepEnded), null);
        }
        return entry.Ask;
    }

    /// <summary>The first answer wins: Done once; TooLate after the ask ended; Changed for an older revision; Refused for an answer it does not take from that view.</summary>
    public PcResolve Resolve(string id, int revision, object answer, PcAnswerSource source)
    {
        ArgumentNullException.ThrowIfNull(answer);
        Entry? entry;
        lock (gate)
        {
            if (!entries.TryGetValue(id, out entry)) return PcResolve.TooLate;
            if (entry.Ask.Revision != revision) return PcResolve.Changed;
            if (!AskLifetimes.Accepts(entry.Ask, answer, source)) return PcResolve.Refused;
            entries.Remove(id);
        }
        entry.End();
        entry.Done.TrySetResult(new PcAskOutcome(PcAskStatus.Answered, answer, source));
        Resolved?.Invoke(entry.Ask, answer, source);
        return PcResolve.Done;
    }

    /// <summary>Takes an ask out of the queue unanswered; false when it had already ended.</summary>
    public bool Withdraw(string id, PcWithdrawal reason)
    {
        Entry? entry;
        lock (gate)
        {
            if (!entries.Remove(id, out entry)) return false;
        }
        entry.End();
        entry.Done.TrySetResult(new PcAskOutcome(PcAskStatus.Withdrawn, Reason: reason));
        Withdrawn?.Invoke(entry.Ask, reason);
        return true;
    }

    /// <summary>The user's "Dismiss" on a question.</summary>
    public bool Dismiss(string id)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(id, out var entry) || entry.Ask.Kind is not (PcAskKind.Question or PcAskKind.ProviderQuestion)) return false;
        }
        return Withdraw(id, PcWithdrawal.Dismissed);
    }

    /// <summary>The ask's subject read again (a confirm at the click): a new revision, so an answer to the old one is Changed.</summary>
    public PcPendingAsk? Revise(string id, object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        PcPendingAsk revised;
        lock (gate)
        {
            if (!entries.TryGetValue(id, out var entry)) return null;
            entry.Ask = revised = entry.Ask with { Payload = payload, Revision = entry.Ask.Revision + 1 };
        }
        Revised?.Invoke(revised);
        return revised;
    }

    /// <summary>The chat turn ended: its approvals, confirms and show-window asks go; its questions stay.</summary>
    public void EndTurn(string thread) => WithdrawWhere(a => a.Thread == thread && AskLifetimes.EndsWithTurn(a.Kind), PcWithdrawal.TurnEnded);

    /// <summary>The user wrote in the chat: its unanswered questions have moved on.</summary>
    public void UserWrote(string thread) => WithdrawWhere(a => a.Thread == thread && AskLifetimes.EndsWhenUserWrites(a.Kind), PcWithdrawal.MovedOn);

    public void Dispose()
    {
        if (control is not null) control.Stopped -= OnStopped;
        List<Entry> left;
        lock (gate) left = [.. entries.Values];
        foreach (var entry in left) entry.End();
    }

    private void OnStopped(PcScope scope, PcStopSource source)
    {
        if (source == PcStopSource.Sleep) return;
        WithdrawWhere(a => AskLifetimes.EndsWithStop(a.Kind) && scope.Covers(a.Thread, a.App ?? ""), PcWithdrawal.Stopped);
    }

    private void WithdrawWhere(Func<PcPendingAsk, bool> which, PcWithdrawal reason)
    {
        List<string> ids;
        lock (gate) ids = entries.Values.Where(e => which(e.Ask)).OrderBy(e => e.Order).Select(e => e.Ask.Id).ToList();
        foreach (var id in ids) Withdraw(id, reason);
    }

    private async Task Expire(Entry entry, int lifeMs)
    {
        try
        {
            await clock.Delay(lifeMs, entry.ExpiryToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        Withdraw(entry.Ask.Id, PcWithdrawal.Expired);
    }

    private sealed class Entry : IDisposable
    {
        private readonly CancellationTokenSource expiry = new();
        private int ended;

        public Entry(PcPendingAsk ask, long order, TaskCompletionSource<PcAskOutcome> done)
        {
            Ask = ask;
            Order = order;
            Done = done;
            ExpiryToken = expiry.Token;
        }

        public PcPendingAsk Ask { get; set; }

        public long Order { get; }

        public TaskCompletionSource<PcAskOutcome> Done { get; }

        /// <summary>Kept apart from its source, so a question answered before its clock started never reads a disposed source.</summary>
        public CancellationToken ExpiryToken { get; }

        public CancellationTokenRegistration Step { get; set; }

        // Never waits for a running callback (Unregister), so it is safe from inside the step's own cancellation.
        public void End()
        {
            if (Interlocked.Exchange(ref ended, 1) != 0) return;
            Step.Unregister();
            expiry.Cancel();
            expiry.Dispose();
        }

        public void Dispose() => End();
    }
}
