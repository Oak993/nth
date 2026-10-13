// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.1, 4.2, 12.6 "holds every open decision", 12.6b; unit AS-5; ED-NEW-dextra-as5-1,
// -9). No Swift twin. The owner's decision queue: every card Dextra's agents and tools raise, the answer each takes
// (DecisionRules), and what Stop and Pause do to them. A card acts only through the owner's answer: the queue never
// answers one itself, and until AS-7 every provider approval of a mission lands here, Allow or not (16, the ordering
// rule). Thread-safe; callbacks run outside the lock, on the caller's thread.
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Assistant;

/// <summary>What a card does when the owner answers it.</summary>
public delegate void DecisionAnswered(DecisionItem card, DecisionAnswer answer);

public sealed class DecisionQueue(TimeProvider? time = null)
{
    /// <summary>How many settled cards the queue keeps for the board's history.</summary>
    public const int SettledKept = 200;

    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly List<Entry> entries = [];

    private sealed class Entry(DecisionItem item, DecisionAnswered onAnswer, Func<DecisionItem, DecisionAnswer, string?>? check)
    {
        public DecisionItem Item { get; set; } = item;
        public DecisionAnswered OnAnswer { get; } = onAnswer;
        public Func<DecisionItem, DecisionAnswer, string?>? Check { get; } = check;
    }

    /// <summary>Raised after any card is added, answered, held, released or withdrawn.</summary>
    public event Action? Changed;

    /// <summary>
    /// Says whether an answer to this card is kept until Resume instead of acting now (12.6b "Cards while paused": its
    /// task is paused). The mission runner sets it.
    /// </summary>
    public Func<DecisionItem, bool>? KeepsAnswers { get; set; }

    /// <summary>Every card the queue holds, oldest first: open and held ones, and the latest settled ones.</summary>
    public IReadOnlyList<DecisionItem> Items
    {
        get { lock (gate) return [.. entries.Select(e => e.Item)]; }
    }

    /// <summary>The cards that wait on the owner (open or held), oldest first.</summary>
    public IReadOnlyList<DecisionItem> Waiting
    {
        get { lock (gate) return [.. entries.Select(e => e.Item).Where(Waits)]; }
    }

    public DecisionItem? Find(Guid id)
    {
        lock (gate) return entries.FirstOrDefault(e => e.Item.Id == id)?.Item;
    }

    /// <summary>The waiting cards of one mission.</summary>
    public IReadOnlyList<DecisionItem> Of(Guid mission)
    {
        lock (gate) return [.. entries.Select(e => e.Item).Where(i => i.MissionId == mission && Waits(i))];
    }

    /// <summary>
    /// Puts a card in front of the owner. A waiting card with the same binding for the same mission, chat and request
    /// is not raised twice: that card is returned. <paramref name="check"/> may refuse an answer the card's rules allow
    /// (a question that asks for a secret).
    /// </summary>
    public DecisionItem Raise(DecisionItem item, DecisionAnswered onAnswer, Func<DecisionItem, DecisionAnswer, string?>? check = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(onAnswer);
        DecisionItem raised;
        lock (gate)
        {
            var same = entries.FirstOrDefault(e => Waits(e.Item) && e.Item.MissionId == item.MissionId && e.Item.ThreadId == item.ThreadId
                && e.Item.RequestId == item.RequestId && e.Item.Binding == item.Binding);
            if (same is not null) return same.Item;
            raised = item with { State = DecisionState.Open, HeldBy = null, Answer = null, Settled = null, Raised = clock.GetUtcNow() };
            entries.Add(new Entry(raised, onAnswer, check));
        }
        Changed?.Invoke();
        return raised;
    }

    /// <summary>The owner's answer. Null when the card took it (and its action ran), else why not; then nothing happened.</summary>
    public string? Answer(Guid id, DecisionAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        Entry? entry;
        DecisionItem answered;
        var keptOnly = false;
        lock (gate)
        {
            entry = entries.FirstOrDefault(e => e.Item.Id == id);
            if (entry is null) return "There is no such card.";
            if (DecisionRules.Refusal(entry.Item, answer) is { } refused) return refused;
            if (entry.Check?.Invoke(entry.Item, answer) is { } checkedOut) return checkedOut;
            if (KeepsAnswers?.Invoke(entry.Item) == true)
            {
                // The task is paused: the answer is kept and acts at Resume (ApplyKept); the card stays open.
                entry.Item = entry.Item with { Kept = answer };
                keptOnly = true;
                answered = entry.Item;
            }
            else answered = entry.Item with { State = DecisionState.Answered, Answer = answer, Settled = clock.GetUtcNow(), Kept = null };
            if (!keptOnly)
            {
                entry.Item = answered;
                Trim();
            }
        }
        if (!keptOnly) entry.OnAnswer(answered, answer);
        Changed?.Invoke();
        return null;
    }

    /// <summary>
    /// Acts on the answers kept while a task was paused, for the cards <paramref name="scope"/> picks (Resume): each is
    /// answered as the owner gave it. An answer its card no longer takes is dropped and the card stays open. Returns how
    /// many acted.
    /// </summary>
    public int ApplyKept(Func<DecisionItem, bool> scope)
    {
        var kept = new List<(Guid Id, DecisionAnswer Answer)>();
        lock (gate)
        {
            foreach (var entry in entries.Where(e => e.Item is { State: DecisionState.Open, Kept: not null } && scope(e.Item)))
            {
                kept.Add((entry.Item.Id, entry.Item.Kept!));
                entry.Item = entry.Item with { Kept = null };
            }
        }
        var acted = 0;
        foreach (var (id, answer) in kept)
        {
            if (Answer(id, answer) is null) acted++;
        }
        return acted;
    }

    /// <summary>
    /// Takes an answered card back as held (12.6b "Irreversible cards never resume on their own"): it was approved but had
    /// not run when its task paused, so it needs a new tap after Resume. False when the card is not an answered one.
    /// </summary>
    public bool Reopen(Guid id, DecisionHold hold, string note)
    {
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.Item.Id == id && e.Item.State == DecisionState.Answered);
            if (entry is null) return false;
            entry.Item = entry.Item with { State = DecisionState.Held, HeldBy = hold, Answer = null, Settled = null, Kept = null, Note = note };
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>Withdraws a waiting card (its inputs changed, its agent stopped asking); the note says why.</summary>
    public bool Withdraw(Guid id, string note)
    {
        lock (gate)
        {
            var entry = entries.FirstOrDefault(e => e.Item.Id == id && Waits(e.Item));
            if (entry is null) return false;
            entry.Item = entry.Item with { State = DecisionState.Withdrawn, Note = note, Settled = clock.GetUtcNow() };
            Trim();
        }
        Changed?.Invoke();
        return true;
    }

    /// <summary>The provider cancelled its request (4.2 "Expiry"): the card says so and takes no answer.</summary>
    public bool WithdrawRequest(Guid thread, string requestId, string note)
    {
        Guid? id;
        lock (gate) id = entries.FirstOrDefault(e => e.Item.ThreadId == thread && e.Item.RequestId == requestId && Waits(e.Item))?.Item.Id;
        return id is { } found && Withdraw(found, note);
    }

    /// <summary>
    /// Holds the waiting cards <paramref name="scope"/> picks (4.2): Stop holds every one, Pause only the irreversible and
    /// merge cards (reversible ones stay answerable while paused). Returns the cards it held.
    /// </summary>
    public IReadOnlyList<DecisionItem> Hold(Func<DecisionItem, bool> scope, DecisionHold hold)
    {
        var held = new List<DecisionItem>();
        lock (gate)
        {
            foreach (var entry in entries.Where(e => e.Item.State == DecisionState.Open && scope(e.Item)))
            {
                if (hold == DecisionHold.Pause && !DecisionRules.HeldByPause(entry.Item)) continue;
                entry.Item = entry.Item with { State = DecisionState.Held, HeldBy = hold, Kept = null };
                held.Add(entry.Item);
            }
            // A Stop over a Pause: what Pause held now reads "Held by Stop".
            if (hold == DecisionHold.Stop)
                foreach (var entry in entries.Where(e => e.Item is { State: DecisionState.Held, HeldBy: DecisionHold.Pause } && scope(e.Item)))
                    entry.Item = entry.Item with { HeldBy = DecisionHold.Stop };
        }
        if (held.Count > 0) Changed?.Invoke();
        return held;
    }

    /// <summary>
    /// Re-raises the held cards <paramref name="scope"/> picks, bound to the same inputs (4.2). When
    /// <paramref name="currentInputs"/> says a card's inputs changed while it was held, the old card is withdrawn and a new
    /// one raised with the inputs as they are now. Returns the cards that wait again.
    /// </summary>
    public IReadOnlyList<DecisionItem> Release(Func<DecisionItem, bool> scope, Func<DecisionItem, ValueList<DecisionInput>?>? currentInputs = null)
    {
        var again = new List<DecisionItem>();
        var renewed = new List<(DecisionItem Item, DecisionAnswered OnAnswer, Func<DecisionItem, DecisionAnswer, string?>? Check)>();
        lock (gate)
        {
            foreach (var entry in entries.Where(e => e.Item.State == DecisionState.Held && scope(e.Item)).ToList())
            {
                var now = currentInputs?.Invoke(entry.Item);
                if (now is { } inputs && DecisionRules.Bind(entry.Item.Kind, entry.Item.Title, inputs) != entry.Item.Binding)
                {
                    entry.Item = entry.Item with { State = DecisionState.Withdrawn, HeldBy = null, Note = "Its inputs changed while it was held.", Settled = clock.GetUtcNow() };
                    renewed.Add((entry.Item with { Id = Guid.NewGuid(), Inputs = inputs, Note = null }, entry.OnAnswer, entry.Check));
                    continue;
                }
                entry.Item = entry.Item with { State = DecisionState.Open, HeldBy = null };
                again.Add(entry.Item);
            }
        }
        foreach (var (item, onAnswer, check) in renewed) again.Add(Raise(item, onAnswer, check));
        if (again.Count > 0 || renewed.Count > 0) Changed?.Invoke();
        return again;
    }

    private static bool Waits(DecisionItem item) => item.State is DecisionState.Open or DecisionState.Held;

    private void Trim()
    {
        var settled = entries.Count(e => !Waits(e.Item));
        if (settled <= SettledKept) return;
        foreach (var old in entries.Where(e => !Waits(e.Item)).Take(settled - SettledKept).ToList()) entries.Remove(old);
    }
}
