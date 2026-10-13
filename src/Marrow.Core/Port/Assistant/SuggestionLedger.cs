// PORT-ONLY: Dextra (DESIGN.md rev 5, 5.2 "At most 3 a day, never pushed, never a toast... three dismissals of a kind turn that kind off", 6; unit AS-11;
// ED-NEW-dextra-as11-5). No Swift twin.
// The rules of the Suggestions lane, pure: which of today's candidates are offered (at most three a day, none of a kind the owner turned off, none
// the owner answered before), what Accept and Dismiss do to the books, and when a kind goes off. Each call returns the ledger after it; the
// store (DigestBook) keeps it. The ledger never reads a clock: the caller gives the moment and the local day.
namespace Marrow.Core.Port.Assistant;

public sealed record SuggestionLedger
{
    /// <summary>The most suggestions offered in one local day (5.2: "At most 3 a day"). Answered ones count: the day has three places, not three open ones.</summary>
    public const int MaxPerDay = 3;

    /// <summary>Dismissals of one kind that turn the kind off (5.2).</summary>
    public const int DismissalsToTurnOff = 3;

    /// <summary>The answered keys kept so they are not offered again; the oldest are let go past this.</summary>
    public const int SettledKept = 200;

    public static SuggestionLedger Empty { get; } = new();

    /// <summary>The local day <see cref="Today"/> belongs to.</summary>
    public DateOnly Day { get; init; }

    /// <summary>What was offered on <see cref="Day"/>, oldest first, with how each was answered.</summary>
    public ValueList<Suggestion> Today { get; init; } = ValueList<Suggestion>.Empty;

    /// <summary>How often the owner dismissed each kind (the spotter's lesson).</summary>
    public ValueMap<SuggestionKind, int> Dismissals { get; init; } = ValueMap<SuggestionKind, int>.Empty;

    /// <summary>The kinds the owner dismissed enough to turn off.</summary>
    public ValueList<SuggestionKind> Off { get; init; } = ValueList<SuggestionKind>.Empty;

    /// <summary>The keys the owner answered (accepted or dismissed), newest last: such a thing is not offered again.</summary>
    public ValueList<string> Settled { get; init; } = ValueList<string>.Empty;

    /// <summary>The suggestions still open today.</summary>
    public IReadOnlyList<Suggestion> Open => [.. Today.Where(s => s.State == SuggestionState.Open)];

    public int OfferedToday => Today.Count;

    public bool IsOff(SuggestionKind kind) => Off.Contains(kind);

    public int DismissalsOf(SuggestionKind kind) => Dismissals.GetValueOrDefault(kind);

    /// <summary>The same ledger on a new day: yesterday's offers are let go (their facts are stale), the lessons stay.</summary>
    public SuggestionLedger OnDay(DateOnly day) => day == Day ? this : this with { Day = day, Today = ValueList<Suggestion>.Empty };

    /// <summary>
    /// Offers the candidates that may be offered, most urgent first, until today's three places are used. A candidate is skipped when its kind is off,
    /// when the owner answered the same thing before, or when it is offered already. Returns the ledger and what was newly offered.
    /// </summary>
    public (SuggestionLedger Ledger, IReadOnlyList<Suggestion> Offered) Admit(IEnumerable<SuggestionCandidate> candidates, DateTimeOffset now, DateOnly day)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var ledger = OnDay(day);
        var offered = new List<Suggestion>();
        var today = ledger.Today.ToList();
        foreach (var candidate in candidates.OrderByDescending(c => c.Urgency).ThenBy(c => c.Kind).ThenBy(c => c.Key, StringComparer.Ordinal))
        {
            if (today.Count >= MaxPerDay) break;
            if (ledger.IsOff(candidate.Kind) || ledger.Settled.Contains(candidate.Key) || today.Any(s => s.Key == candidate.Key)) continue;
            var suggestion = new Suggestion(candidate.Key, candidate.Kind, candidate.Line, candidate.Cost, candidate.Plan, now) { LocalOnly = candidate.LocalOnly };
            today.Add(suggestion);
            offered.Add(suggestion);
        }
        return (offered.Count == 0 ? ledger : ledger with { Today = new ValueList<Suggestion>(today) }, offered);
    }

    /// <summary>The line or cost of an open suggestion replaced (a model's wording): the plan and the key never change.</summary>
    public SuggestionLedger Reworded(string key, string line, string cost) =>
        this with { Today = new ValueList<Suggestion>(Today.Select(s => s.Key == key && s.State == SuggestionState.Open ? s with { Line = line, Cost = cost } : s)) };

    /// <summary>Accept: the suggestion is answered and is not offered again. Null when there is no open suggestion by that key.</summary>
    public SuggestionLedger? Accept(string key, DateTimeOffset now) => Answer(key, SuggestionState.Accepted, now);

    /// <summary>
    /// Dismiss: answered, not offered again, and counted against its kind; the third dismissal of a kind turns the kind off, and its other open
    /// suggestions go with it (5.2). Null when there is no open suggestion by that key.
    /// </summary>
    public SuggestionLedger? Dismiss(string key, DateTimeOffset now)
    {
        var answered = Answer(key, SuggestionState.Dismissed, now);
        if (answered is null) return null;
        var kind = Today.First(s => s.Key == key).Kind;
        var count = answered.DismissalsOf(kind) + 1;
        var ledger = answered with { Dismissals = answered.Dismissals.Mutating(m => m[kind] = count) };
        if (count < DismissalsToTurnOff || ledger.IsOff(kind)) return ledger;
        // The kind is off: what else it had open today is taken back (without counting as a lesson, the owner did not see it as a no).
        var rest = ledger.Today.Select(s => s.Kind == kind && s.State == SuggestionState.Open ? s with { State = SuggestionState.Dismissed, Settled = now } : s);
        return ledger with { Off = ledger.Off.Appending(kind), Today = new ValueList<Suggestion>(rest) };
    }

    /// <summary>The owner turns a kind back on (Settings): its count starts again.</summary>
    public SuggestionLedger TurnOn(SuggestionKind kind) =>
        IsOff(kind) || DismissalsOf(kind) > 0
            ? this with { Off = new ValueList<SuggestionKind>(Off.Where(k => k != kind)), Dismissals = Dismissals.Mutating(m => m.Remove(kind)) }
            : this;

    private SuggestionLedger? Answer(string key, SuggestionState state, DateTimeOffset now)
    {
        var found = Today.FirstOrDefault(s => s.Key == key && s.State == SuggestionState.Open);
        if (found is null) return null;
        var settled = Settled.Contains(key) ? Settled : Settled.Appending(key);
        if (settled.Count > SettledKept) settled = new ValueList<string>(settled.Skip(settled.Count - SettledKept));
        return this with
        {
            Today = new ValueList<Suggestion>(Today.Select(s => s.Key == key ? s with { State = state, Settled = now } : s)),
            Settled = settled,
        };
    }
}
