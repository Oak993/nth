// PORT-ONLY: first-release feature unit SK-7. The whole content of addons\state.json (ONDEMAND-PLAN.md 3.1), as an immutable value.
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Core.Port.Skills.State;

public sealed record AcceptedTerm(string LicenceSha256, string ItemId, DateTimeOffset AcceptedAt);

public sealed record ChatDeclined(string ChatId, IReadOnlyList<string> ItemIds);

public sealed record FoundOnPc(string ItemId, string Path);

public sealed class StateFormatException : Exception
{
    public StateFormatException(string message) : base(message)
    {
    }

    public StateFormatException(string message, Exception inner) : base(message, inner)
    {
    }
}

/// <summary>
/// Installed items and their states, accepted terms keyed by licence hash, per-chat declined offers and found-on-this-PC results (ONDEMAND-PLAN.md 3.1, 2.5, 5.3,
/// 3.6). Immutable: every change returns a new state, and an illegal one throws and changes nothing. Always canonical: an Available item has no record, and every
/// list is sorted ordinally, so one state has one file text.
/// </summary>
public sealed class AddonsState
{
    public const int SchemaVersion = 1;

    public static AddonsState Empty { get; } = new([], [], [], []);

    private AddonsState(IReadOnlyList<ItemRecord> items, IReadOnlyList<AcceptedTerm> acceptedTerms, IReadOnlyList<ChatDeclined> declined, IReadOnlyList<FoundOnPc> found)
    {
        Items = items;
        AcceptedTerms = acceptedTerms;
        Declined = declined;
        Found = found;
    }

    public IReadOnlyList<ItemRecord> Items { get; }

    public IReadOnlyList<AcceptedTerm> AcceptedTerms { get; }

    public IReadOnlyList<ChatDeclined> Declined { get; }

    public IReadOnlyList<FoundOnPc> Found { get; }

    /// <summary>Validates and sorts; throws <see cref="StateFormatException"/> on anything that is not a valid state.</summary>
    public static AddonsState Restore(IEnumerable<ItemRecord> items, IEnumerable<AcceptedTerm> acceptedTerms, IEnumerable<ChatDeclined> declined, IEnumerable<FoundOnPc> found)
    {
        var records = items.ToList();
        foreach (var record in records)
        {
            if (record.Problem() is { } problem) throw new StateFormatException($"item '{record.Id}': {problem}");
            if (record.State == ItemState.Available) throw new StateFormatException($"item '{record.Id}': an available item has no record");
        }
        RejectDuplicates(records.Select(r => r.Id), "item");

        var terms = acceptedTerms.ToList();
        foreach (var term in terms)
        {
            if (!StateRules.IsSha256(term.LicenceSha256)) throw new StateFormatException($"accepted terms: '{term.LicenceSha256}' is not a lowercase SHA-256");
            if (!StateRules.IsId(term.ItemId)) throw new StateFormatException($"accepted terms {term.LicenceSha256}: '{term.ItemId}' is not a usable item id");
            if (term.AcceptedAt == default) throw new StateFormatException($"accepted terms {term.LicenceSha256}: no date");
        }
        RejectDuplicates(terms.Select(t => t.LicenceSha256), "accepted terms hash");

        var chats = declined.ToList();
        foreach (var chat in chats)
        {
            if (string.IsNullOrWhiteSpace(chat.ChatId)) throw new StateFormatException("declined: a chat id is blank");
            if (chat.ItemIds.Count == 0) throw new StateFormatException($"declined chat '{chat.ChatId}': an empty list is not written");
            foreach (var id in chat.ItemIds)
                if (!StateRules.IsId(id)) throw new StateFormatException($"declined chat '{chat.ChatId}': '{id}' is not a usable item id");
            RejectDuplicates(chat.ItemIds, $"declined item of chat '{chat.ChatId}'");
        }
        RejectDuplicates(chats.Select(c => c.ChatId), "declined chat");

        var finds = found.ToList();
        foreach (var find in finds)
        {
            if (!StateRules.IsId(find.ItemId)) throw new StateFormatException($"found: '{find.ItemId}' is not a usable item id");
            if (!StateRules.IsAbsolutePath(find.Path)) throw new StateFormatException($"found {find.ItemId}: '{find.Path}' is not an absolute path");
        }
        RejectDuplicates(finds.Select(f => f.ItemId), "found tool");

        return new AddonsState(
            records.OrderBy(r => r.Id, StringComparer.Ordinal).ToList(),
            terms.OrderBy(t => t.LicenceSha256, StringComparer.Ordinal).Select(t => t with { AcceptedAt = t.AcceptedAt.ToUniversalTime() }).ToList(),
            chats.OrderBy(c => c.ChatId, StringComparer.Ordinal).Select(c => new ChatDeclined(c.ChatId, c.ItemIds.OrderBy(i => i, StringComparer.Ordinal).ToList())).ToList(),
            finds.OrderBy(f => f.ItemId, StringComparer.Ordinal).ToList());
    }

    private static void RejectDuplicates(IEnumerable<string> keys, string what)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
            if (!seen.Add(key)) throw new StateFormatException($"{what} '{key}' appears twice");
    }

    private AddonsState With(IEnumerable<ItemRecord>? items = null, IEnumerable<AcceptedTerm>? terms = null, IEnumerable<ChatDeclined>? declined = null, IEnumerable<FoundOnPc>? found = null) =>
        Restore(items ?? Items, terms ?? AcceptedTerms, declined ?? Declined, found ?? Found);

    private static void RequireId(string id, string what)
    {
        if (!StateRules.IsId(id)) throw new ArgumentException($"'{id}' is not a usable {what}.", nameof(id));
    }

    /// <summary>The item's record; an item with no record is Available.</summary>
    public ItemRecord Get(string id)
    {
        RequireId(id, "item id");
        return Items.FirstOrDefault(r => r.Id == id) ?? ItemRecord.Available(id);
    }

    /// <summary>One legal state change of one item, or <see cref="IllegalTransitionException"/> (see <see cref="ItemStateMachine.Apply"/>).</summary>
    public AddonsState Transition(string id, ItemState to, string? targetPin = null, string? failure = null)
    {
        var next = ItemStateMachine.Apply(Get(id), to, targetPin, failure);
        var others = Items.Where(r => r.Id != id);
        return With(items: next.State == ItemState.Available ? others : others.Append(next));
    }

    /// <summary>
    /// What a state read from disk looks like to a new run (ONDEMAND-PLAN.md DL-19: a download resumes only on the next Download press): every item that was queued,
    /// downloading or being checked settles where a cancel would have put it. A failure stays with its Retry and a removal stays pending for the next GC.
    /// </summary>
    public AddonsState AfterRestart() => With(items: Items.Select(Settle).Where(r => r.State != ItemState.Available));

    private static ItemRecord Settle(ItemRecord record)
    {
        if (record.State is not (ItemState.Queued or ItemState.Downloading or ItemState.Checking)) return record;
        if (record.InstalledPin is null) return ItemRecord.Available(record.Id);
        return record.TargetPin == record.InstalledPin
            ? new ItemRecord(record.Id, ItemState.Damaged, record.InstalledPin, null, null)
            : new ItemRecord(record.Id, ItemState.UpdateAvailable, record.InstalledPin, record.TargetPin, null);
    }

    public bool HasAccepted(string licenceSha256) => AcceptedTerms.Any(t => t.LicenceSha256 == licenceSha256);

    /// <summary>
    /// Records that the user accepted the licence text with this hash (ONDEMAND-PLAN.md 2.5). Keyed by the hash alone; a hash already accepted keeps its first
    /// acceptance (ED-295). The time is the caller's, and is stored as a UTC instant.
    /// </summary>
    public AddonsState AcceptTerms(string licenceSha256, string itemId, DateTimeOffset at)
    {
        if (!StateRules.IsSha256(licenceSha256)) throw new ArgumentException("Terms are keyed by a lowercase SHA-256 of the licence file.", nameof(licenceSha256));
        RequireId(itemId, "item id");
        if (at == default) throw new ArgumentException("An acceptance carries its date.", nameof(at));
        return HasAccepted(licenceSha256) ? this : With(terms: AcceptedTerms.Append(new AcceptedTerm(licenceSha256, itemId, at)));
    }

    public bool IsDeclined(string chatId, string itemId)
    {
        RequireChat(chatId);
        return Declined.Any(c => c.ChatId == chatId && c.ItemIds.Contains(itemId, StringComparer.Ordinal));
    }

    private static void RequireChat(string chatId)
    {
        if (string.IsNullOrWhiteSpace(chatId)) throw new ArgumentException("A chat id is required.", nameof(chatId));
    }

    /// <summary>"Don't ask again in this chat" (ONDEMAND-PLAN.md 5.3): this chat will not be offered this item again.</summary>
    public AddonsState Decline(string chatId, string itemId)
    {
        RequireChat(chatId);
        RequireId(itemId, "item id");
        var others = Declined.Where(c => c.ChatId != chatId);
        var mine = Declined.FirstOrDefault(c => c.ChatId == chatId)?.ItemIds ?? [];
        if (mine.Contains(itemId, StringComparer.Ordinal)) return this;
        return With(declined: others.Append(new ChatDeclined(chatId, [.. mine, itemId])));
    }

    /// <summary>Drops a chat's list when the chat is gone, so the file cannot grow without end (ED-295).</summary>
    public AddonsState ForgetChat(string chatId)
    {
        RequireChat(chatId);
        return Declined.Any(c => c.ChatId == chatId) ? With(declined: Declined.Where(c => c.ChatId != chatId)) : this;
    }

    public bool IsFoundOnPc(string itemId) => Found.Any(f => f.ItemId == itemId);

    public string? FoundPath(string itemId) => Found.FirstOrDefault(f => f.ItemId == itemId)?.Path;

    /// <summary>
    /// Merges one probe run (ONDEMAND-PLAN.md 3.6): for every tool the run probed, the answer replaces the earlier one (found at a path, or no longer found); tools
    /// the run did not probe keep theirs. Only tools of the catalogue can be found, each result must be for a probed tool, appear once and carry an absolute path.
    /// Item states are never touched (ED-295).
    /// </summary>
    public AddonsState MergeFound(SkillsCatalogue catalogue, IReadOnlyCollection<string> probedIds, IReadOnlyCollection<FoundOnPc> results)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(probedIds);
        ArgumentNullException.ThrowIfNull(results);
        var probed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in probedIds)
        {
            if (!probed.Add(id)) throw new ArgumentException($"'{id}' is probed twice.", nameof(probedIds));
            var item = catalogue.Items.FirstOrDefault(i => i.Id == id) ?? throw new ArgumentException($"'{id}' is not a catalogue item.", nameof(probedIds));
            if (item.Kind != ItemKind.Tool) throw new ArgumentException($"'{id}' is a {item.Kind}; only tools are found on the PC.", nameof(probedIds));
        }
        var answered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            if (!probed.Contains(result.ItemId)) throw new ArgumentException($"'{result.ItemId}' was not probed.", nameof(results));
            if (!answered.Add(result.ItemId)) throw new ArgumentException($"'{result.ItemId}' has two results.", nameof(results));
            if (!StateRules.IsAbsolutePath(result.Path)) throw new ArgumentException($"'{result.Path}' is not an absolute path.", nameof(results));
        }
        return With(found: Found.Where(f => !probed.Contains(f.ItemId)).Concat(results));
    }
}
