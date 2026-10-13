// PORT-ONLY: Dextra AS-20 (DESIGN.md rev 5, 12.7 "Purchases" (the switch, the card, Windows Hello, limits, the boundary, history,
// never allowed); 16.2's fallback for a build without a fill path; the canvas's section 9 "Purchases", work/design/assistant/
// index.html lines 948-993 and 3174-3285; ED-NEW-dextra-as20-10, -11, -15, -16 and -17). No Swift twin.
// The purchase desk: everything between an agent's prepare_purchase and the money. A request becomes a pay card (purchases on, a
// card kept, within the limits, a fill path to the shop's page) or a hand-over card (the owner pays himself). A pay card is bound
// to its shop, items, total, card and what is left of the limits, and expires in 10 minutes; Pay checks all of it again, reads
// the page's boundary, then the vault asks Windows Hello and fills the shop's own card field with the agents held. Right before
// the order, under one lock with the intent row, it checks again that nothing stopped it (Stop all, the switch, the card, a
// lockout) and that the limits still hold; the intent is written before the order is placed and how it ended after. Paid or
// not, the card comes off the page (each field read back empty, or the page closed) before the agents are let go. A payment
// that is stopped is handed to the owner only once nth is sure it will not pay it. Turning purchases on, saving, replacing or
// removing the card and changing a limit each ask Windows Hello; turning purchases off never does. Three failed checks turn
// purchases off.
// No word the desk gives a model, the log or a card carries a digit of the card beyond its last 4: models read {card}.
using Avalonia.Threading;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

/// <summary>The checkout pages agents hold open, as nth's own fill path reads them (Visus or Tactus; none in this build, 16.2).</summary>
public interface ICheckoutPages
{
    /// <summary>The checkout page this chat holds open at this origin, or null when it holds none.</summary>
    ICheckoutPage? Find(Guid thread, string origin);
}

/// <summary>What purchases are made of: the book, Windows Hello, the vault and the fill path.</summary>
public sealed class PurchaseSetup
{
    public required PurchaseBook Book { get; init; }

    public required IHelloGate Hello { get; init; }

    /// <summary>The vault over the guard the desk makes; production: <see cref="CardVault.Production"/>.</summary>
    public required Func<HelloGuard, CardVault> Vault { get; init; }

    /// <summary>The fill path to the shops' pages. Null (this build): the switch is hidden and every payment is handed to the owner.</summary>
    public ICheckoutPages? Pages { get; init; }

    /// <summary>Plain-http shops on this PC count (shop.localhost, *.test): the checkout fixtures'. Off for real shops.</summary>
    public bool AllowLoopbackHttp { get; init; }

    /// <summary>The zone "today" and "this month" are counted in; null: the PC's.</summary>
    public TimeZoneInfo? Zone { get; init; }
}

/// <summary>What the settings section shows of purchases at one moment.</summary>
public sealed record PurchaseDeskState(bool CanPay, PurchasePolicy Policy, CardVaultState Card, string? LastFour, bool ReadOnly, bool Unreadable,
    string? Status, string? Asking, IReadOnlyList<PurchaseRow> History);

public sealed class PurchaseDeskOptions
{
    public required PurchaseSetup Setup { get; init; }

    public required DecisionQueue Queue { get; init; }

    /// <summary>One activity log line; throws when it cannot be written (then nothing is paid).</summary>
    public required Action<AuditEntry> Audit { get; init; }

    /// <summary>A message for a chat (the controller's Deliver).</summary>
    public required Action<Guid, string> Deliver { get; init; }

    /// <summary>The policy context of a chat (the controller's), for the money check.</summary>
    public required Func<AssistantChat, PolicyContext> Context { get; init; }

    public Func<IReadOnlyList<AssistantProfile>> Profiles { get; init; } = () => [];

    /// <summary>A chat's title, for the card's who line.</summary>
    public Func<Guid, string?> AgentName { get; init; } = _ => null;

    public TimeProvider Time { get; init; } = TimeProvider.System;

    /// <summary>Something the desk could not do and the owner should hear of (the runner's fault line).</summary>
    public Action<string>? Fault { get; init; }
}

public sealed class PurchaseDesk : ObservableModel, IDisposable, IStopScope
{
    /// <summary>A pay card lives this long (12.7 "It expires in 10 minutes").</summary>
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(10);

    public const string Tool = "purchase";

    private readonly PurchaseDeskOptions o;
    private readonly PurchaseSetup setup;
    private readonly PurchaseBook book;
    private readonly AskingGate asking;
    private readonly object gate = new();
    private readonly object placing = new();
    private readonly Dictionary<Guid, Case> cases = [];
    private readonly List<IDisposable> kept = [];
    private int revision;
    private int lockouts;
    private string? status;

    private sealed class Case
    {
        public required Guid Card { get; init; }
        public required PurchaseRequest Request { get; init; }
        public required AssistantChat Chat { get; init; }
        public required bool Pay { get; init; }
        public required DateTimeOffset Raised { get; init; }
        public string? Agent { get; init; }
        public bool Paying { get; set; }
        public ITimer? Timer { get; set; }

        // The payment under way (all under the desk's gate): its stop, the hand-over waiting for it to stop, whether it is past
        // the last check before the order, whether it typed any of the card, and whether its page is kept held.
        public CancellationTokenSource? Halt { get; set; }
        public string? HandOverAfter { get; set; }
        public bool Placing { get; set; }
        public bool Typed { get; set; }
        public bool Kept { get; set; }
    }

    // What the last check before the order decided: the intent row (place it), or why not and what to do once the card is off the page.
    private sealed record Placement(PurchaseRow? Row, string? Said)
    {
        public string? HandOver { get; init; }
        public ValueList<DecisionInput>? Renew { get; init; }
    }

    // Windows Hello as the desk asks it: the reason is shown while Windows asks (the section's "Windows Hello: ..." line).
    private sealed class AskingGate(IHelloGate inner, Action changed) : IHelloGate
    {
        private string? reason;

        public string? Reason => Volatile.Read(ref reason);

        public async Task<HelloResult> Verify(string why, CancellationToken cancellationToken)
        {
            Volatile.Write(ref reason, why);
            changed();
            try { return await inner.Verify(why, cancellationToken).ConfigureAwait(false); }
            finally
            {
                Volatile.Write(ref reason, null);
                changed();
            }
        }
    }

    public PurchaseDesk(PurchaseDeskOptions options)
    {
        o = options ?? throw new ArgumentNullException(nameof(options));
        setup = o.Setup;
        book = setup.Book;
        asking = new AskingGate(setup.Hello, Touch);
        Guard = new HelloGuard(asking);
        Vault = setup.Vault(Guard);
        Guard.LockedOut += OnLockedOut;
        book.Changed += Touch;
        Vault.Changed += Touch;
    }

    /// <summary>The card in Credential Manager. Its last 4 digits are all it ever gives out.</summary>
    public CardVault Vault { get; }

    /// <summary>The one Windows Hello guard behind every purchase step: three failed checks in a row turn purchases off.</summary>
    public HelloGuard Guard { get; }

    /// <summary>Whether nth can fill a shop's card field itself. False in this build: the switch is hidden and every payment is handed over.</summary>
    public bool CanPay => setup.Pages is not null;

    /// <summary>Raised after anything the settings section shows changed (any thread).</summary>
    public event Action? Changed;

    public PurchaseDeskState State
    {
        get
        {
            _ = Read(revision);
            return new PurchaseDeskState(CanPay, book.Policy, Vault.State, Vault.LastFour, book.ReadOnly, book.Unreadable, Volatile.Read(ref status), asking.Reason, book.History);
        }
    }

    /// <summary>The Asturs whose own limits the section lists: every profile under Dextra.</summary>
    public IReadOnlyList<AssistantProfile> Asturs => [.. o.Profiles().Where(p => !p.IsRoot)];

    // ---- the tool: prepare_purchase ----

    /// <summary>An agent's prepare_purchase: refused (never allowed, or not a purchase nth reads), a pay card or a hand-over card.</summary>
    public AssistantToolResult Request(AssistantToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var chat = call.Chat;
        var request = PurchaseRequests.Parse(call.Arguments, out var refusal, setup.AllowLoopbackHttp);
        if (request is null)
        {
            TryAudit(Entry(chat, null, "refused: " + refusal));
            return AssistantToolResult.Failed(refusal);
        }
        var agent = o.AgentName(chat.Thread);
        var why = HandOverReason(request, chat, out var check);
        var raised = why is null
            ? Raise(PayCard(request, chat, check!, agent), request, chat, agent, pay: true)
            : Raise(HandOverCard(request, chat, why, agent), request, chat, agent, pay: false);
        TryAudit(Entry(chat, request, why is null ? "on a card: the owner pays from it after Windows Hello" : "handed to the owner: " + why) with { AllowedBy = "card:" + raised.Id.ToString("D") });
        var text = why is null ? PurchaseWords.ToAgentWaiting(raised.Id) : PurchaseWords.ToAgentHandOver(raised.Id, why);
        return AssistantToolResult.Done(text) with { DecisionId = raised.Id.ToString("D") };
    }

    /// <summary>Whether this answer is the desk's to act on: Pay on a purchase card (the queue never settles one alone).</summary>
    public bool Takes(Guid card, DecisionAnswer answer) =>
        answer.Kind == DecisionAnswerKind.DoIt && o.Queue.Find(card)?.Kind == DecisionKind.Purchase;

    /// <summary>The owner's Pay from the Decisions tab: the payment starts (Windows Hello first); a refusal known at once is returned.</summary>
    public string? Start(Guid card)
    {
        if (Claim(card, out var refused) is not { } claimed) return refused;
        _ = Run(claimed, CancellationToken.None);
        return null;
    }

    /// <summary>The owner's Pay, awaited: null once the card was settled (paid, handed over, renewed), else what the card now says.</summary>
    public async Task<string?> Pay(Guid card, CancellationToken cancellationToken = default)
    {
        if (Claim(card, out var refused) is not { } claimed) return refused;
        return await Run(claimed, cancellationToken).ConfigureAwait(false);
    }

    private Case? Claim(Guid card, out string? refused)
    {
        lock (gate)
        {
            refused = null;
            if (!cases.TryGetValue(card, out var c) || !c.Pay) { refused = PurchaseWords.NoSuchCard; return null; }
            if (c.Paying) { refused = PurchaseWords.Underway; return null; }
            c.Paying = true;
            c.Halt = new CancellationTokenSource();
            c.HandOverAfter = null;
            c.Placing = c.Typed = c.Kept = false;
            return c;
        }
    }

    private async Task<string?> Run(Case c, CancellationToken cancellationToken)
    {
        CancellationTokenSource halt;
        lock (gate) halt = c.Halt!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, halt.Token);
        string? said;
        try { said = await PayNow(c, linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (halt.IsCancellationRequested) { said = PurchaseWords.Stopped; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Something nth did not expect: nothing more happens on this card. Only the kind of fault is told, never its text.
            said = PurchaseWords.PageUnread;
            o.Fault?.Invoke("A purchase stopped: " + error.GetType().Name);
        }
        bool open, halted, typed, keptHeld;
        string? after;
        lock (gate)
        {
            c.Paying = false;
            c.Placing = false;
            open = cases.ContainsKey(c.Card);
            halted = halt.IsCancellationRequested;
            after = c.HandOverAfter;
            c.HandOverAfter = null;
            typed = c.Typed;
            keptHeld = c.Kept;
        }
        if (keptHeld)
        {
            o.Fault?.Invoke(PurchaseWords.KeptHeld);
            if (open) said = PurchaseWords.KeptHeld;
        }
        // The switch turned off, the card removed or a lockout while this payment ran: only now that nth will not pay it is the
        // payment handed to the owner, so he is never told to pay while nth may still be paying.
        if (open && (after ?? Gone()) is { } why)
        {
            HandOverInstead(c, why);
            said = null;
            open = false;
        }
        else if (open && halted && !keptHeld) said = typed ? PurchaseWords.StoppedEmptied : PurchaseWords.Stopped;
        // A card whose 10 minutes ran out while Windows asked goes now (its timer let it be while the payment was under way).
        if (open && o.Time.GetUtcNow() - c.Raised >= Expiry) Expire(c.Card, force: false);
        if (said is not null) o.Queue.Annotate(c.Card, said);
        Touch();
        return said;
    }

    // Why a waiting pay card can no longer be paid from it: the switch is off or the card is gone; else null.
    private string? Gone() =>
        !book.Policy.On ? PurchaseWords.TurnedOff : Vault.State != CardVaultState.Held ? PurchaseWords.HandedNoCard : null;

    private async Task<string?> PayNow(Case c, CancellationToken cancellationToken)
    {
        var request = c.Request;
        var item = o.Queue.Find(c.Card);
        if (item is null || item.State is DecisionState.Answered or DecisionState.Withdrawn) return PurchaseWords.NoSuchCard;
        if (item.State == DecisionState.Held) return null;      // a held card says what holds it
        var now = o.Time.GetUtcNow();
        if (now - c.Raised >= Expiry)
        {
            Expire(c.Card, force: true);
            return null;
        }
        // Everything the card was bound to is checked again: the switch, the card, the limits, what is left of them.
        if (HandOverReason(request, c.Chat, out var check) is { } why)
        {
            HandOverInstead(c, why);
            return null;
        }
        var inputs = PayInputs(request, check!);
        if (DecisionRules.Bind(DecisionKind.Purchase, item.Title, inputs) != item.Binding)
        {
            Renew(c, item, inputs);
            return null;
        }
        if (setup.Pages?.Find(c.Chat.Thread, request.Origin) is not { } page)
        {
            Stop(c, PurchaseWords.PageGone);
            return null;
        }
        // The boundary is read before Windows Hello: a card field in a payment provider's frame, or a form that posts to a third
        // origin, is handed to the owner without asking him to verify anything. The vault reads it again at the fill itself.
        CheckoutSnapshot before;
        try { before = await page.Snapshot(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { return PurchaseWords.PageUnread; }
        var approved = CheckoutApproval.Of(request);
        if (CheckoutBoundary.Check(approved, before).Refusal is { } early)
        {
            HandOverInstead(c, early.Words);
            return null;
        }

        o.Queue.Annotate(c.Card, PurchaseWords.Asking(PurchaseWords.PayReason(request)));
        // The agents stay held from the fill until the card is off the page again: every field read back empty, or the page
        // closed. When neither works the hold is kept, and the agents never read what is left.
        var held = page.HoldAgents();
        var typed = ValueList<string>.Empty;
        var keep = false;
        try
        {
            var locks = Volatile.Read(ref lockouts);
            var fill = await Vault.Fill(page, approved, PurchaseWords.PayReason(request), cancellationToken, () => StillWanted(c)).ConfigureAwait(false);
            keep = fill.Stuck;
            if (fill.Fields.Count > 0) lock (gate) c.Typed = true;
            switch (fill.Outcome)
            {
                case CardFillOutcome.Filled:
                    typed = fill.Fields;
                    break;
                case CardFillOutcome.NotVerified:
                    return Volatile.Read(ref lockouts) != locks ? null : NotVerified(fill.Hello, PurchaseWords.NothingFilled);
                case CardFillOutcome.Withdrawn:
                    return fill.Fields.Count > 0 ? PurchaseWords.StoppedEmptied : PurchaseWords.Stopped;
                case CardFillOutcome.Refused:
                    HandOverInstead(c, fill.Refusal!.Words);
                    return null;
                case CardFillOutcome.NoCard:
                    HandOverInstead(c, PurchaseWords.HandedNoCard);
                    return null;
                case CardFillOutcome.Damaged:
                    HandOverInstead(c, PurchaseWords.CardDamaged);
                    return null;
                case CardFillOutcome.FieldRefused:
                    return PurchaseWords.FieldRefused;
                default:
                    return PurchaseWords.PageUnread;
            }

            // 12.5: the owner's Pay is logged before the order is placed; a line that cannot be written pays nothing.
            try { o.Audit(Entry(c.Chat, request, "paying: the owner's Pay, after Windows Hello") with { AllowedBy = "card:" + c.Card.ToString("D") }); }
            catch (Exception error) when (error is not OutOfMemoryException) { return PurchaseWords.LogNotWritten; }
            var placement = Place(c, item, request);
            if (placement.Row is not { } row)
            {
                // Not placed: the card comes off the page before the owner or the agent hears what happens instead.
                keep = !await CardVault.Scrub(page, typed).ConfigureAwait(false) || keep;
                typed = ValueList<string>.Empty;
                if (placement.HandOver is { } handOver) HandOverInstead(c, handOver);
                else if (placement.Renew is { } renewed) Renew(c, item, renewed);
                return placement.Said;
            }
            // The intent, before the order: a crash after the order leaves an "unknown" row that counts and is never paid again.
            CheckoutOutcome outcome;
            try { outcome = await page.Submit(cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OutOfMemoryException) { outcome = CheckoutOutcome.Unknown; }
            // Paid or not, the card comes off the page before anyone hears how it ended: a shop's page can keep its form in place
            // behind an "order placed" overlay.
            keep = !await CardVault.Scrub(page, typed).ConfigureAwait(false) || keep;
            typed = ValueList<string>.Empty;
            Settle(row, outcome);
            Finish(c, outcome);
            return null;
        }
        finally
        {
            // A payment that ended any other way (a fault included): none of the card stays on the page once the agents go.
            if (typed.Count > 0) keep = !await CardVault.Scrub(page, typed).ConfigureAwait(false) || keep;
            if (keep) KeepHeld(c, held);
            else held.Dispose();
        }
    }

    // The last moment a payment can stop. Payments are placed one at a time: the stop, the switch, the card, the queue, the
    // limits and what is left of them are read again, and the intent row written, under one lock, so two cards paid together
    // can never pass a limit between them (12.7 "A purchase over any limit cannot be paid"). From the moment it passes the
    // stop check the payment is "placing": a stop, the switch or a removed card no longer withdraws it, this check decides.
    private Placement Place(Case c, DecisionItem item, PurchaseRequest request)
    {
        lock (placing)
        {
            lock (gate)
            {
                if (c.Halt?.IsCancellationRequested != false) return new Placement(null, null);     // Run says how it stopped
                c.Placing = true;
            }
            PurchaseRow? row = null;
            try
            {
                if (HandOverReason(request, c.Chat, out var check) is { } why) return new Placement(null, null) { HandOver = why };
                if (!StillWanted(c)) return new Placement(null, PurchaseWords.StoppedEmptied);
                var inputs = PayInputs(request, check!);
                if (DecisionRules.Bind(DecisionKind.Purchase, item.Title, inputs) != item.Binding) return new Placement(null, null) { Renew = inputs };
                try { row = book.Begin(request, c.Chat.MissionId, c.Chat.AssistantId, o.Time.GetUtcNow()); }
                catch (Exception error) when (error is not OutOfMemoryException) { return new Placement(null, PurchaseWords.NotWritten); }
                return new Placement(row, null);
            }
            finally
            {
                if (row is null) lock (gate) c.Placing = false;
            }
        }
    }

    // A page whose card fields would not empty and would not close: its hold is never let go, so its agents never read them.
    private void KeepHeld(Case c, IDisposable held)
    {
        lock (gate)
        {
            kept.Add(held);
            c.Kept = true;
        }
        TryAudit(Entry(c.Chat, c.Request, "the card fields would not empty and the page would not close: its agents stay held"));
    }

    private bool StillWanted(Case c)
    {
        lock (gate)
        {
            if (!cases.ContainsKey(c.Card) || c.Halt?.IsCancellationRequested != false) return false;
        }
        return book.Policy.On && o.Queue.Find(c.Card)?.State == DecisionState.Open;
    }

    private void Settle(PurchaseRow row, CheckoutOutcome outcome)
    {
        var state = outcome switch
        {
            CheckoutOutcome.Paid => PurchaseRowState.Paid,
            CheckoutOutcome.NotPaid => PurchaseRowState.NotPaid,
            _ => PurchaseRowState.Unknown,
        };
        try { book.Settle(row.Id, state); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // The row stays "paying" on disk and reads "unknown" at the next start: it still counts against the limits.
            o.Fault?.Invoke("The purchase history could not be written: " + error.GetType().Name);
        }
    }

    private void Finish(Case c, CheckoutOutcome outcome)
    {
        Forget(c);
        var request = c.Request;
        var by = "card:" + c.Card.ToString("D");
        switch (outcome)
        {
            case CheckoutOutcome.Paid:
                o.Queue.Close(c.Card, DecisionAnswer.DoIt, PurchaseWords.Paid(request));
                TryAudit(Entry(c.Chat, request, PurchaseWords.Paid(request)) with { AllowedBy = by });
                o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentPaid(request));
                break;
            case CheckoutOutcome.NotPaid:
                o.Queue.Withdraw(c.Card, PurchaseWords.NotTaken);
                TryAudit(Entry(c.Chat, request, "not paid: the shop did not take it") with { AllowedBy = by });
                o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentNotTaken(request));
                break;
            default:
                // Never paid again from this card: it closes, and the owner checks with the shop.
                o.Queue.Close(c.Card, DecisionAnswer.DoIt, PurchaseWords.CheckWithShop);
                TryAudit(Entry(c.Chat, request, "unknown: nth could not tell whether it was paid") with { AllowedBy = by });
                o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentUnknown(request));
                break;
        }
    }

    // ---- the cards ----

    private DecisionItem Raise(DecisionItem card, PurchaseRequest request, AssistantChat chat, string? agent, bool pay)
    {
        var raised = o.Queue.Raise(card, Answered, Check);
        List<Case> replaced;
        lock (gate)
        {
            // One purchase card per chat: an earlier one with other inputs gives way to this one (a changed amount asks again).
            replaced = [.. cases.Values.Where(c => c.Chat.Thread == chat.Thread && c.Card != raised.Id && !c.Paying)];
            foreach (var old in replaced) ForgetLocked(old);
            if (!cases.ContainsKey(raised.Id))
            {
                var made = new Case { Card = raised.Id, Request = request, Chat = chat, Pay = pay, Raised = raised.Raised, Agent = agent };
                cases[raised.Id] = made;
                if (pay)
                {
                    var due = Expiry - (o.Time.GetUtcNow() - raised.Raised);
                    made.Timer = o.Time.CreateTimer(_ => Expire(raised.Id, force: false), null, due > TimeSpan.Zero ? due : TimeSpan.Zero, Timeout.InfiniteTimeSpan);
                }
            }
        }
        foreach (var old in replaced) o.Queue.Withdraw(old.Card, PurchaseWords.Changed);
        Touch();
        return raised;
    }

    private DecisionItem PayCard(PurchaseRequest request, AssistantChat chat, PurchaseLimitCheck check, string? agent) =>
        new(Guid.NewGuid(), DecisionKind.Purchase, DecisionWeight.Irreversible, PurchaseWords.Title(request))
        {
            Inputs = PayInputs(request, check),
            Why = PurchaseWords.Bound,
            Rule = PurchaseWords.Rule,
            AssistantId = chat.AssistantId,
            MissionId = chat.MissionId,
            ThreadId = chat.Thread,
            Agent = agent,
            Class = ActionClass.Money,
        };

    private static DecisionItem HandOverCard(PurchaseRequest request, AssistantChat chat, string why, string? agent) =>
        new(Guid.NewGuid(), DecisionKind.Purchase, DecisionWeight.Reversible, PurchaseWords.Title(request))
        {
            Inputs = new ValueList<DecisionInput>(Basics(request)),
            Why = why,
            Rule = PurchaseWords.HandOverRule,
            AssistantId = chat.AssistantId,
            MissionId = chat.MissionId,
            ThreadId = chat.Thread,
            Agent = agent,
            Options = new ValueList<string>([PurchaseWords.IllPay, PurchaseWords.DontBuy]),
        };

    private static IEnumerable<DecisionInput> Basics(PurchaseRequest request)
    {
        string Money(decimal amount) => PurchaseMoney.Format(amount, request.Currency);
        yield return new DecisionInput("Shop", request.Shop + " · " + CheckoutOrigins.Host(request.Origin));
        yield return new DecisionInput("Items", string.Join("\n", request.Items.Select(i => i.Name + "  " + Money(i.Amount))));
        yield return new DecisionInput("Shipping", Money(request.Shipping));
        yield return new DecisionInput("Tax", Money(request.Tax));
        yield return new DecisionInput("Total", Money(request.Total));
        yield return new DecisionInput("Ship to", request.ShipTo);
        yield return new DecisionInput("Checkout", PurchaseRequests.Clean(request.CheckoutUrl, PurchaseRequest.MaxShownUrl));
    }

    private ValueList<DecisionInput> PayInputs(PurchaseRequest request, PurchaseLimitCheck check)
    {
        var policy = book.Policy;
        return new ValueList<DecisionInput>(Basics(request).Concat(
        [
            new DecisionInput("Card", PurchaseWords.LastFour(Vault.LastFour)),
            new DecisionInput("Left after", PurchaseWords.Left(check, policy.Limits, policy.Currency)),
        ]));
    }

    // Why this request is the owner's to pay, or null when nth may pay it from a card: the switch, the card, the currency, the
    // policy's money rule (never allowed, the owner's Never) and the limits, per purchase, day, month and Astur.
    private string? HandOverReason(PurchaseRequest request, AssistantChat chat, out PurchaseLimitCheck? check)
    {
        check = null;
        if (!CanPay) return PurchaseWords.HandOverWhy;
        if (book.ReadOnly) return book.Unreadable ? PurchaseWords.UnreadableFile : PurchaseWords.NewerFile;
        var policy = book.Policy;
        if (!policy.On) return PurchaseWords.HandedOff;
        if (Vault.State != CardVaultState.Held) return Vault.State == CardVaultState.NoCard ? PurchaseWords.HandedNoCard : PurchaseWords.CardDamaged;
        if (!string.Equals(request.Currency, policy.Currency, StringComparison.Ordinal)) return PurchaseWords.OtherCurrency(request.Currency, policy.Currency);
        PolicyContext context;
        try { context = o.Context(chat) with { PurchasesOn = true }; }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException) { return PurchaseWords.UnknownAssistant; }
        if (context.Chain.Count == 0) return PurchaseWords.UnknownAssistant;
        var money = new Classification(ActionClass.Money, "pays " + CheckoutOrigins.Host(request.Origin)) { Money = MoneyKind.Purchase, Target = request.Origin };
        var decision = PolicyEngine.Decide(money, context);
        if (decision.Verdict == Verdict.Deny) return PurchaseWords.OverHandOver(decision.Reason);
        var astur = IsAstur(chat.AssistantId) ? chat.AssistantId : null;
        check = PurchaseLimitRules.Check(request.Total, policy.Limits, book.Spend(o.Time.GetUtcNow(), Zone), astur);
        return check.StoppedBy is { } kind
            ? PurchaseWords.OverHandOver(PurchaseWords.Over(kind, policy.Limits, policy.Currency, astur, astur is null ? null : NameOf(astur)))
            : null;
    }

    private TimeZoneInfo Zone => setup.Zone ?? TimeZoneInfo.Local;

    private bool IsAstur(string assistantId) => o.Profiles().FirstOrDefault(p => p.Id == assistantId) is { IsRoot: false };

    private string? NameOf(string assistantId) => o.Profiles().FirstOrDefault(p => p.Id == assistantId)?.Name;

    // The queue's check: Pay on a purchase card is the desk's (after Windows Hello), and nothing answers a card whose payment is under way.
    private string? Check(DecisionItem item, DecisionAnswer answer)
    {
        lock (gate)
        {
            if (cases.TryGetValue(item.Id, out var c) && c.Paying) return PurchaseWords.Underway;
        }
        return answer.Kind == DecisionAnswerKind.DoIt ? PurchaseWords.PayOnCard : null;
    }

    // Don't buy, or (handed over) I'll pay and Don't buy.
    private void Answered(DecisionItem item, DecisionAnswer answer)
    {
        Case? c;
        lock (gate)
        {
            if (!cases.TryGetValue(item.Id, out c)) return;
            ForgetLocked(c);
        }
        var ownerPays = answer.Kind == DecisionAnswerKind.Pick && answer.Text == PurchaseWords.IllPay;
        TryAudit(Entry(c.Chat, c.Request, ownerPays ? "handed to the owner: he pays himself" : "not bought: the owner's answer") with { AllowedBy = "card:" + item.Id.ToString("D") });
        o.Deliver(c.Chat.Thread, ownerPays ? PurchaseWords.ToAgentOwnerPays(c.Request) : PurchaseWords.ToAgentNotBought(c.Request));
        Touch();
    }

    private void HandOverInstead(Case c, string why)
    {
        Forget(c);
        o.Queue.Withdraw(c.Card, why);
        var raised = Raise(HandOverCard(c.Request, c.Chat, why, c.Agent), c.Request, c.Chat, c.Agent, pay: false);
        TryAudit(Entry(c.Chat, c.Request, "handed to the owner: " + why) with { AllowedBy = "card:" + raised.Id.ToString("D") });
        o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentHandOver(raised.Id, why));
    }

    private void Renew(Case c, DecisionItem item, ValueList<DecisionInput> inputs)
    {
        var card = item.Inputs.FirstOrDefault(i => i.Name == "Card")?.Value != inputs.FirstOrDefault(i => i.Name == "Card")?.Value;
        var note = card ? PurchaseWords.CardChanged : PurchaseWords.LimitsChanged;
        Forget(c);
        o.Queue.Withdraw(c.Card, note);
        var fresh = item with { Id = Guid.NewGuid(), Inputs = inputs, Note = null };
        var raised = Raise(fresh, c.Request, c.Chat, c.Agent, pay: true);
        TryAudit(Entry(c.Chat, c.Request, "asked again: " + note) with { AllowedBy = "card:" + raised.Id.ToString("D") });
        o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentWaiting(raised.Id));
    }

    private void Stop(Case c, string why)
    {
        Forget(c);
        o.Queue.Withdraw(c.Card, why);
        TryAudit(Entry(c.Chat, c.Request, "not paid: " + why));
        o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentStopped(c.Request, why));
    }

    // 10 minutes after it was raised a pay card is withdrawn (its timer), or at Pay (force), unless a payment is under way.
    private void Expire(Guid card, bool force)
    {
        Case? c;
        lock (gate)
        {
            if (!cases.TryGetValue(card, out c) || (c.Paying && !force)) return;
            ForgetLocked(c);
        }
        if (!o.Queue.Withdraw(card, PurchaseWords.Expired)) return;
        TryAudit(Entry(c.Chat, c.Request, "expired after 10 minutes"));
        o.Deliver(c.Chat.Thread, PurchaseWords.ToAgentExpired(c.Request));
        Touch();
    }

    private void Forget(Case c)
    {
        lock (gate) ForgetLocked(c);
    }

    private void ForgetLocked(Case c)
    {
        cases.Remove(c.Card);
        c.Timer?.Dispose();
        c.Timer = null;
    }

    // The pay cards the switch going off, the card going or a lockout withdraws: the waiting ones are returned to be handed over
    // now; one being paid is told to stop and is handed over by its own run once it has stopped (Run); one already being
    // placed goes on and its card says how it ended.
    private List<Case> Withdrawing(string why, out bool placingOne)
    {
        var waiting = new List<Case>();
        placingOne = false;
        lock (gate)
        {
            foreach (var c in cases.Values.Where(c => c.Pay))
            {
                if (!c.Paying) waiting.Add(c);
                else if (c.Placing) placingOne = true;
                else
                {
                    c.HandOverAfter ??= why;
                    _ = c.Halt?.CancelAsync();      // the token flips now; its callbacks run on the pool, never on this thread
                }
            }
        }
        return waiting;
    }

    // ---- Stop all (12.6) ----

    /// <summary>The scope's name in Stop all's fault line.</summary>
    public string Name => PurchaseWords.Section;

    /// <summary>
    /// Stop all: a payment not yet being placed stops before it fills more or places anything. Only flags here; the payment
    /// takes the card off the page itself, and its card stays open (Stop all holds it). One already being placed goes on.
    /// </summary>
    public void Stop()
    {
        lock (gate)
        {
            foreach (var c in cases.Values)
                if (c.Paying && !c.Placing) _ = c.Halt?.CancelAsync();
        }
    }

    // ---- the settings: the switch, the card, the limits ----

    /// <summary>Turns purchases on: only with a fill path, a card and valid limits, and only after Windows Hello. Null when on, else why not.</summary>
    public async Task<string?> TurnOn(CancellationToken cancellationToken = default)
    {
        if (!CanPay) return Say(PurchaseWords.NeedsFill);
        if (book.ReadOnly) return Say(book.Unreadable ? PurchaseWords.UnreadableFile : PurchaseWords.NewerFile);
        if (Vault.State != CardVaultState.Held) return Say(Vault.State == CardVaultState.NoCard ? PurchaseWords.NeedsCard : PurchaseWords.CardDamaged);
        if (book.Policy.Limits.Problem() is { } bad) return Say(bad);
        if (book.Policy.On) return Say(null);
        var locks = Volatile.Read(ref lockouts);
        var verdict = await Guard.Check("turn purchases on", cancellationToken).ConfigureAwait(false);
        if (verdict != HelloResult.Verified)
            return Volatile.Read(ref lockouts) != locks ? Say(PurchaseWords.LockedOut) : Say(NotVerified(verdict, PurchaseWords.NothingChanged));
        // What Windows Hello approved is checked again: the card or the file may have changed while it asked.
        if (Vault.State != CardVaultState.Held) return Say(PurchaseWords.NeedsCard);
        try { book.SetPolicy(book.Policy with { On = true }); }
        catch (Exception error) when (error is not OutOfMemoryException) { return Say(PurchaseWords.NotSaved); }
        TryAudit(Owner("purchases turned on after Windows Hello"));
        Say(PurchaseWords.TurnedOn);
        return null;
    }

    /// <summary>Turns purchases off. Never asks; every pay card waiting is handed to the owner. Returns what the section says.</summary>
    public string TurnOff()
    {
        var stored = book.TurnOff();
        var waiting = Withdrawing(PurchaseWords.TurnedOff, out var placingOne);
        foreach (var c in waiting) HandOverInstead(c, PurchaseWords.TurnedOff);
        TryAudit(Owner(stored ? "purchases turned off" : "purchases turned off; not saved: " + (book.Fault ?? "read-only file")));
        return Say(!stored && !book.ReadOnly ? PurchaseWords.OffNotSaved : placingOne ? PurchaseWords.OffWhilePlacing : PurchaseWords.NowOff)!;
    }

    /// <summary>Saves (or replaces) the card from the add-card sheet, after Windows Hello. The entry is cleared when it is saved.</summary>
    public async Task<CardVaultResult> SaveCard(CardEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var replacing = Vault.State == CardVaultState.Held;
        var locks = Volatile.Read(ref lockouts);
        var result = await Vault.Save(entry, o.Time.GetUtcNow(), cancellationToken).ConfigureAwait(false);
        Say(result.Outcome switch
        {
            CardVaultOutcome.Done => PurchaseWords.CardSaved,
            CardVaultOutcome.Invalid => Problem(result.Problem),
            CardVaultOutcome.NotVerified => Volatile.Read(ref lockouts) != locks ? PurchaseWords.LockedOut : NotVerified(result.Hello, PurchaseWords.NothingChanged),
            _ => PurchaseWords.CardNotSaved,
        });
        if (result.Outcome == CardVaultOutcome.Done) TryAudit(Owner(replacing ? "card replaced after Windows Hello" : "card saved after Windows Hello"));
        return result;
    }

    /// <summary>Removes the card after Windows Hello; purchases turn off with it, and waiting pay cards are handed over.</summary>
    public async Task<CardVaultResult> RemoveCard(CancellationToken cancellationToken = default)
    {
        var locks = Volatile.Read(ref lockouts);
        var result = await Vault.Remove(cancellationToken).ConfigureAwait(false);
        if (result.Outcome == CardVaultOutcome.Done)
        {
            book.TurnOff();
            foreach (var c in Withdrawing(PurchaseWords.HandedNoCard, out _)) HandOverInstead(c, PurchaseWords.HandedNoCard);
            TryAudit(Owner("card removed after Windows Hello"));
        }
        Say(result.Outcome switch
        {
            CardVaultOutcome.Done => PurchaseWords.CardRemoved,
            CardVaultOutcome.NoCard => PurchaseWords.NoCardToRemove,
            CardVaultOutcome.NotVerified => Volatile.Read(ref lockouts) != locks ? PurchaseWords.LockedOut : NotVerified(result.Hello, PurchaseWords.NothingChanged),
            _ => PurchaseWords.CardNotSaved,
        });
        return result;
    }

    /// <summary>Changes one limit after Windows Hello; the old one holds until then. Null when it holds, else why not.</summary>
    public async Task<string?> SetLimit(PurchaseLimitKind kind, decimal value, string? astur = null, CancellationToken cancellationToken = default)
    {
        if (kind == PurchaseLimitKind.PerAstur && string.IsNullOrEmpty(astur)) throw new ArgumentException("An Astur's limit names the Astur.", nameof(astur));
        if (PurchaseLimits.Invalid(value) is { } bad) return Say(bad);
        if (book.ReadOnly) return Say(book.Unreadable ? PurchaseWords.UnreadableFile : PurchaseWords.NewerFile);
        if (book.Policy.Limits.Of(kind, astur) == value) return Say(null);
        var locks = Volatile.Read(ref lockouts);
        var verdict = await Guard.Check("change a purchase limit", cancellationToken).ConfigureAwait(false);
        if (verdict != HelloResult.Verified)
            return Volatile.Read(ref lockouts) != locks ? Say(PurchaseWords.LockedOut) : Say(NotVerified(verdict, PurchaseWords.NothingChanged));
        var policy = book.Policy;
        var before = policy.Limits.Of(kind, astur);
        try { book.SetPolicy(policy with { Limits = policy.Limits.With(kind, value, astur) }); }
        catch (Exception error) when (error is not OutOfMemoryException) { return Say(PurchaseWords.NotSaved); }
        TryAudit(Owner($"{PurchaseWords.LimitName(kind, astur is null ? null : NameOf(astur))}: {PurchaseMoney.Format(before, policy.Currency)} to {PurchaseMoney.Format(value, policy.Currency)} after Windows Hello"));
        Say(PurchaseWords.LimitSaved);
        return null;
    }

    private void OnLockedOut()
    {
        Interlocked.Increment(ref lockouts);
        book.TurnOff();
        foreach (var c in Withdrawing(PurchaseWords.LockedOut, out _)) HandOverInstead(c, PurchaseWords.LockedOut);
        TryAudit(Owner("purchases turned off: three failed Windows Hello checks"));
        Say(PurchaseWords.LockedOut);
    }

    private string NotVerified(HelloResult? result, string nothing) => result switch
    {
        HelloResult.Busy => PurchaseWords.HelloBusy,
        HelloResult.Unavailable => PurchaseWords.HelloUnavailable,
        _ => PurchaseWords.NotVerified(Guard.Failures, nothing),
    };

    private static string Problem(CardEntryProblem? problem) => problem switch
    {
        CardEntryProblem.BadNumber => PurchaseWords.BadNumber,
        CardEntryProblem.BadMonth or CardEntryProblem.PastExpiry => PurchaseWords.PastExpiry,
        CardEntryProblem.BadCode => PurchaseWords.BadCode,
        _ => PurchaseWords.Incomplete,
    };

    // ---- log lines and the section's state ----

    private static AuditEntry Entry(AssistantChat chat, PurchaseRequest? request, string result) => new()
    {
        Assistant = chat.AssistantId,
        Mission = chat.MissionId?.ToString("D"),
        Agent = chat.Thread.ToString("D"),
        Tool = Tool,
        ActionClass = nameof(ActionClass.Money),
        Target = request is null ? null : CheckoutOrigins.Host(request.Origin),
        Result = request is null ? result : result + " (" + PurchaseMoney.Format(request.Total, request.Currency) + ")",
    };

    private AuditEntry Owner(string result) => new()
    {
        Assistant = o.Profiles().FirstOrDefault(p => p.IsRoot)?.Id ?? "",
        Tool = Tool,
        ActionClass = nameof(ActionClass.Money),
        Result = result,
        AllowedBy = "owner",
    };

    private void TryAudit(AuditEntry entry)
    {
        try { o.Audit(entry); }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A line about what already happened, or was refused: reported, never thrown.
            o.Fault?.Invoke("The activity log could not be written: " + error.GetType().Name);
        }
    }

    private string? Say(string? text)
    {
        Volatile.Write(ref status, text);
        Touch();
        return text;
    }

    private void Touch()
    {
        if (Dispatcher.UIThread.CheckAccess()) Bump();
        else Dispatcher.UIThread.Post(Bump);
        Changed?.Invoke();
    }

    private void Bump() => Set(ref revision, revision + 1, property: nameof(State));

    public void Dispose()
    {
        Guard.LockedOut -= OnLockedOut;
        book.Changed -= Touch;
        Vault.Changed -= Touch;
        Stop();
        lock (gate)
        {
            foreach (var c in cases.Values) c.Timer?.Dispose();
        }
    }
}
