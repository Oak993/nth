// PORT-ONLY: Dextra AS-9 (DESIGN.md rev 5, 13.1 "Windows toasts only when a decision is needed", "Toast buttons"; 4.2 "Batching"; 12 "Answers come only
// from cards and toasts"; ED-NEW-dextra-as9-1, -2, -5, -6). No Swift twin.
// Decision toasts: a card that needs the owner makes a toast, once, a moment after it is raised. Cards raised together are one summary; a card
// answered at once makes none; a toast never follows another within the gap; none is made while the Assistant home is on screen; and a toast is
// taken back when its card is answered, held or withdrawn. A button on a toast answers its card only with the nonce made for that very toast,
// once, and only for a card that still waits and still takes a toast's answer; anything else opens the card for review. The queue's listeners
// run on the caller's thread, so the work here is a list add and a timer; the channel is called from the main thread.
// A duty the owner set to "Notify me" (13.1 "Duty opt-in", ED-NEW-dextra-as9-9) joins the same window and the same gap: its finding is news, not a
// decision, so it waits behind a decision toast, makes no card, keeps no button that answers and needs nothing taken back.
using System.Security.Cryptography;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What a press on a toast did.</summary>
public enum DecisionToastOutcome
{
    /// <summary>The button answered its card; nothing needs to open.</summary>
    Answered,
    /// <summary>The card is to be opened for review; nothing was answered.</summary>
    Opened,
}

public sealed class DecisionToastOptions
{
    /// <summary>The name of the assistant that asks, by its id (Dextra's or an Astur's).</summary>
    public required Func<string, string> NameOf { get; init; }

    /// <summary>Whether the Assistant home is in front now (13.1: no toast then). Called on the main thread.</summary>
    public required Func<bool> HomeOnScreen { get; init; }

    /// <summary>Runs work on the main thread (production: the main actor's post).</summary>
    public Action<Action> OnMain { get; init; } = work => MainActor.Post(work);

    public TimeProvider Time { get; init; } = TimeProvider.System;
}

public sealed class DecisionToasts : IDisposable
{
    /// <summary>How long cards raised one after another are gathered into one toast.</summary>
    public static readonly TimeSpan Together = TimeSpan.FromSeconds(1);

    /// <summary>The least time from one toast to the next: a drip of cards never floods the screen.</summary>
    public static readonly TimeSpan Gap = TimeSpan.FromSeconds(4);

    /// <summary>The most cards one toast gathers (a burst beyond it is not listed; the Decisions tab holds them all).</summary>
    public const int MaxPending = 256;

    /// <summary>The most toasts that keep a button that answers; an older one is taken back.</summary>
    public const int MaxShown = 64;

    private readonly object gate = new();
    private readonly DecisionQueue queue;
    private readonly NotificationChannel channel;
    private readonly DecisionToastOptions options;
    private readonly List<Guid> pending = [];
    private readonly List<DutyFinding> pendingDuties = [];
    private readonly Dictionary<Guid, DecisionNotice> shown = [];
    private readonly List<Guid> shownOrder = [];
    private readonly HashSet<string> reported = [];
    private ITimer? timer;
    private long? lastSent;
    private bool disposed;

    public DecisionToasts(DecisionQueue queue, NotificationChannel channel, DecisionToastOptions options)
    {
        this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
        this.channel = channel ?? throw new ArgumentNullException(nameof(channel));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        queue.Raised += OnRaised;
        queue.Changed += OnChanged;
    }

    /// <summary>A stable event name and the kind of error ("toast.send.failed InvalidOperationException"); never a card's text. Each name is told once.</summary>
    public event Action<string>? Faulted;

    private void OnRaised(DecisionItem item)
    {
        if (!DecisionNotices.NeedsYou(item)) return;
        lock (gate)
        {
            if (disposed || pending.Count >= MaxPending) return;
            pending.Add(item.Id);
            Arm();
        }
    }

    /// <summary>
    /// A run of a "Notify me" duty found something (13.1 "Duty opt-in"). It is gathered with the others of the window into one toast, never
    /// within the gap of the last toast, never while the Assistant home is in front, and after a decision toast that is due.
    /// </summary>
    public void NotifyDuty(DutyFinding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        lock (gate)
        {
            if (disposed || pendingDuties.Count >= MaxPending) return;
            pendingDuties.Add(finding);
            Arm();
        }
    }

    // Called with the lock held. The next toast comes a window after the first card waiting, and never sooner than the gap after the last toast.
    private void Arm()
    {
        if (timer is not null) return;
        var due = Together;
        if (lastSent is { } last)
        {
            var wait = Gap - options.Time.GetElapsedTime(last);
            if (wait > due) due = wait;
        }
        timer = options.Time.CreateTimer(_ => options.OnMain(Flush), null, due, Timeout.InfiniteTimeSpan);
    }

    private void Flush()
    {
        try { FlushCore(); }
        catch (Exception error) when (error is not OperationCanceledException) { Report("toast.flush.failed", error); } // the card is still in the app
    }

    private void FlushCore()
    {
        List<DecisionItem> items;
        bool dutiesWait;
        lock (gate)
        {
            timer?.Dispose();
            timer = null;
            if (disposed) return;
            items = [.. pending.Select(queue.Find).OfType<DecisionItem>().Where(DecisionNotices.NeedsYou)];
            pending.Clear();
            dutiesWait = pendingDuties.Count > 0;
        }
        if (items.Count == 0 && !dutiesWait) return;
        if (options.HomeOnScreen())
        {
            lock (gate) pendingDuties.Clear(); // the home shows the run on the board; the owner is looking at it
            return;
        }
        var made = items.Count == 0 ? null : DecisionNotices.ForMany(items, options.NameOf);
        if (made is null)
        {
            SendDuties();
            return;
        }
        SendDecision(made);
        lock (gate)
        {
            if (!disposed && pendingDuties.Count > 0) Arm(); // a duty's news follows a decision's toast, after the gap
        }
    }

    private void SendDecision(DecisionNotice made)
    {
        var notice = made with { Nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) };
        var evicted = new List<Guid>();
        lock (gate)
        {
            if (disposed) return;
            lastSent = options.Time.GetTimestamp();
            shown[notice.Target] = notice;
            shownOrder.Remove(notice.Target);
            shownOrder.Add(notice.Target);
            while (shownOrder.Count > MaxShown)
            {
                evicted.Add(shownOrder[0]);
                shown.Remove(shownOrder[0]);
                shownOrder.RemoveAt(0);
            }
        }
        foreach (var old in evicted) _ = WithdrawAsync(old);
        _ = SendAsync(notice);
    }

    private void SendDuties()
    {
        DutyNotice? notice;
        lock (gate)
        {
            if (disposed) return;
            notice = DutyNotices.ForMany(pendingDuties);
            pendingDuties.Clear();
            if (notice is null) return;
            lastSent = options.Time.GetTimestamp();
        }
        _ = SendDutyAsync(notice);
    }

    private async Task SendDutyAsync(DutyNotice notice)
    {
        try { await channel.Send(notice).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { Report("toast.duty.failed", error); } // the run is on the board
    }

    private async Task SendAsync(DecisionNotice notice)
    {
        var took = false;
        try { took = await channel.Send(notice).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { Report("toast.send.failed", error); }
        if (!took) Forget(notice.Target);
    }

    private void Forget(Guid target)
    {
        lock (gate)
        {
            shown.Remove(target);
            shownOrder.Remove(target);
        }
    }

    private async Task WithdrawAsync(Guid target)
    {
        try { await channel.Withdraw(target).ConfigureAwait(false); }
        catch (Exception error) when (error is not OperationCanceledException) { Report("toast.withdraw.failed", error); }
    }

    // A toast whose cards no longer wait (answered, held by Stop, withdrawn) is taken back at once, so no stale button waits in the action center.
    private void OnChanged()
    {
        List<Guid> done;
        lock (gate)
        {
            if (disposed || shown.Count == 0) return;
            done = [.. shown.Where(entry => !entry.Value.Decisions.Any(id => queue.Find(id) is { } card && DecisionNotices.NeedsYou(card))).Select(entry => entry.Key)];
            foreach (var target in done)
            {
                shown.Remove(target);
                shownOrder.Remove(target);
            }
        }
        foreach (var target in done) _ = WithdrawAsync(target);
    }

    /// <summary>
    /// A press on a toast (a body click or one of its buttons). A button that answers does so only when this toast really carried that button, the
    /// nonce is the one made for it and has not been used, and its card still waits and takes a toast's answer; the nonce is spent by the answer.
    /// Every other press (Open, Review, a wrong or missing nonce, a card that moved on) opens the card and answers nothing.
    /// </summary>
    public DecisionToastOutcome Activate(Guid target, string? nonce, NoticeAction action)
    {
        if (action is not (NoticeAction.AllowOnce or NoticeAction.Deny)) return DecisionToastOutcome.Opened;
        DecisionNotice? notice;
        lock (gate)
        {
            if (disposed || !shown.TryGetValue(target, out notice) || !Same(notice.Nonce, nonce) || !notice.Actions.Contains(action) || notice.IsSummary)
                return DecisionToastOutcome.Opened;
            shown[target] = notice with { Nonce = "" }; // spent: the toast stays listed until its card settles, and a second press only opens the card
        }
        var card = queue.Find(target);
        if (card is null || !DecisionNotices.AnswersInNotice(card) || DecisionNotices.Answer(action) is not { } answer) return DecisionToastOutcome.Opened;
        return queue.Answer(target, answer) is null ? DecisionToastOutcome.Answered : DecisionToastOutcome.Opened;
    }

    private static bool Same(string expected, string? given) =>
        expected.Length > 0 && given is not null && given.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(expected), System.Text.Encoding.UTF8.GetBytes(given));

    private void Report(string name, Exception error)
    {
        lock (gate)
        {
            if (!reported.Add(name)) return;
        }
        try { Faulted?.Invoke(name + " " + error.GetType().Name); }
        catch (Exception raised) when (raised is not OperationCanceledException) { } // a listener's own trouble is not the toast's
    }

    public void Dispose()
    {
        List<Guid> up;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            timer?.Dispose();
            timer = null;
            pending.Clear();
            pendingDuties.Clear();
            up = [.. shownOrder];
            shown.Clear();
            shownOrder.Clear();
        }
        queue.Raised -= OnRaised;
        queue.Changed -= OnChanged;
        foreach (var target in up) _ = WithdrawAsync(target);
    }
}
