using System.Text.Json;
using Marrow.Core.Port;

// PORT-ONLY: web surfaces W3c, a thread's pane workspace as it is kept on disk (E:/Droppy/work/design/web-surfaces/DESIGN.md
// 3.11 "Persistence ... Migration and bad data"; ED-NEW-web-w3c-1). No Swift counterpart. PaneWorkspaceState is what the
// solver reads; StoredWorkspace is that plus what a build must not lose on its way through: the data each pane unit keeps
// for its pane (browser tabs, artifact selection), the schema version found, and whatever a newer build wrote that this
// one does not understand (unknown fields, unknown pane kinds). Reading never throws and never guesses: a saved workspace
// is read in full or it is reported damaged, and a damaged one is replaced by the default layout while its text is kept.

namespace Marrow.Core.Models;

/// <summary>Pane ids a newer build wrote that this one does not know: kept on disk, never drawn.</summary>
/// <param name="Anchor">
/// The id of a known pane the unknown ones are tied to: the pane they shared a tab group with (<paramref name="Within"/>), or the first
/// pane of the nearest known slot before them in the saved order. Null when no known slot came before.
/// </param>
/// <param name="Within">The ids are tabs of the slot that holds <paramref name="Anchor"/>; otherwise they are a slot of their own after it.</param>
/// <param name="Index">Where the slot stood among all the slots saved; used when the anchor is gone or there was none.</param>
/// <param name="Ids">The unknown ids, in their order.</param>
public sealed record UnknownSlot(string? Anchor, bool Within, int Index, ValueList<string> Ids);

/// <summary>An unknown pane id in the most-recent list, and the place it held among all the ids saved there.</summary>
public sealed record UnknownRecent(string Id, int Index);

/// <summary>A thread's saved pane workspace.</summary>
public sealed record StoredWorkspace
{
    /// <summary>The schema this build writes.</summary>
    public const int CurrentVersion = 1;

    /// <summary>The panes, order, groups, ratios, collapse memory, expanded pane and narrow selection.</summary>
    public PaneWorkspaceState State { get; init; } = PaneWorkspaceState.Default;

    /// <summary>What each pane unit keeps for its pane, as JSON text by persisted pane id (3.11: tabs, scroll, zoom, selection, version).</summary>
    public ValueMap<string, string> PaneData { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>The schema version of the file. A file from a newer build keeps its number when this build saves it again.</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>Slots with panes this build does not know.</summary>
    public ValueList<UnknownSlot> UnknownSlots { get; init; } = ValueList<UnknownSlot>.Empty;

    /// <summary>Unknown pane ids in the most-recent list.</summary>
    public ValueList<UnknownRecent> UnknownRecents { get; init; } = ValueList<UnknownRecent>.Empty;

    /// <summary>Top-level fields this build does not know, as JSON text by name.</summary>
    public ValueMap<string, string> UnknownFields { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>Nothing worth a file: the default layout and nothing kept.</summary>
    public bool IsEmpty =>
        State.Equals(PaneWorkspaceState.Default) && PaneData.Count == 0 && UnknownSlots.Count == 0 && UnknownRecents.Count == 0
        && UnknownFields.Count == 0 && Version <= CurrentVersion;

    /// <summary>The same record with a new layout.</summary>
    public StoredWorkspace WithState(PaneWorkspaceState state) => this with { State = state };

    /// <summary>The same record with a pane's data set (JSON text) or removed (null or invalid text).</summary>
    public StoredWorkspace WithPaneData(string paneId, string? json)
    {
        if (string.IsNullOrEmpty(paneId)) return this;
        var compact = json is null ? null : PaneWorkspaceJson.Compact(json);
        if (compact is null) return PaneData.ContainsKey(paneId) ? this with { PaneData = PaneData.Mutating(map => map.Remove(paneId)) } : this;
        return PaneData.TryGetValue(paneId, out var current) && current == compact ? this : this with { PaneData = PaneData.Mutating(map => map[paneId] = compact) };
    }
}

/// <summary>How a saved workspace read.</summary>
public enum WorkspaceReadOutcome
{
    /// <summary>A file of this build's schema (or older), read in full.</summary>
    Ok,

    /// <summary>A file of a newer schema, read as far as this build knows it; the rest is kept.</summary>
    Newer,

    /// <summary>Not a workspace: the thread opens with the default layout and the text is kept apart.</summary>
    Corrupted,
}

/// <summary>The result of reading a saved workspace.</summary>
/// <param name="Stored">The workspace, or null when it is corrupted.</param>
/// <param name="Reason">Why it is corrupted, in a line for the log; empty otherwise.</param>
public sealed record WorkspaceRead(WorkspaceReadOutcome Outcome, StoredWorkspace? Stored, string Reason = "");

/// <summary>The JSON shape of a saved workspace (schema 1) and its reader and writer.</summary>
/// <remarks>
/// Fields: <c>v</c> (schema), <c>order</c> (slots, each a list of pane ids), <c>side</c>, <c>drawer</c>, <c>termGrid</c>,
/// <c>ratios</c>, <c>collapsed</c>, <c>pre</c>, <c>expanded</c>, <c>recent</c>, <c>narrow</c>, <c>panes</c> (data by pane id).
/// A field at its default is not written. Pane ids are <see cref="PaneKinds.Id"/>.
/// </remarks>
public static class PaneWorkspaceJson
{
    /// <summary>Reads JSON text. Text that is not JSON is corrupted.</summary>
    public static WorkspaceRead Read(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Bad("empty");
        try
        {
            using var document = JsonDocument.Parse(text);
            return Read(document.RootElement);
        }
        catch (JsonException error)
        {
            return Bad("not JSON: " + error.Message);
        }
    }

    /// <summary>Reads a workspace object.</summary>
    public static WorkspaceRead Read(JsonElement element)
    {
        try
        {
            return ReadObject(element);
        }
        catch (FormatException error)
        {
            return Bad(error.Message);
        }
        catch (InvalidOperationException error)
        {
            return Bad(error.Message);
        }
    }

    private static WorkspaceRead Bad(string reason) => new(WorkspaceReadOutcome.Corrupted, null, reason);

    private static WorkspaceRead ReadObject(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("not an object");
        var version = StoredWorkspace.CurrentVersion;
        var slots = new List<(List<PaneKind> Known, List<string> Unknown)>();
        var side = false;
        var drawer = false;
        var termGrid = false;
        var ratios = new Dictionary<string, double>(StringComparer.Ordinal);
        var collapsed = new HashSet<string>(StringComparer.Ordinal);
        var pre = new Dictionary<string, double>(StringComparer.Ordinal);
        PaneKind? expanded = null;
        var recent = new List<PaneKind>();
        var unknownRecent = new List<UnknownRecent>();
        var recentSeen = 0;
        var narrow = PaneKind.Chat;
        var paneData = new Dictionary<string, string>(StringComparer.Ordinal);
        var unknownFields = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in root.EnumerateObject())
        {
            var value = property.Value;
            switch (property.Name)
            {
                case "v":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out version) || version < 1) throw new FormatException("v is not a schema number");
                    break;
                case "order":
                    if (value.ValueKind != JsonValueKind.Array) throw new FormatException("order is not a list");
                    foreach (var slot in value.EnumerateArray()) slots.Add(ReadSlot(slot));
                    break;
                case "side":
                    side = ReadBool(value, "side");
                    break;
                case "drawer":
                    drawer = ReadBool(value, "drawer");
                    break;
                case "termGrid":
                    termGrid = ReadBool(value, "termGrid");
                    break;
                case "ratios":
                    ReadNumbers(value, "ratios", ratios);
                    break;
                case "pre":
                    ReadNumbers(value, "pre", pre);
                    break;
                case "collapsed":
                    if (value.ValueKind != JsonValueKind.Array) throw new FormatException("collapsed is not a list");
                    foreach (var key in value.EnumerateArray())
                    {
                        if (key.ValueKind != JsonValueKind.String) throw new FormatException("collapsed holds a non-string");
                        collapsed.Add(key.GetString()!);
                    }
                    break;
                case "expanded":
                    if (value.ValueKind == JsonValueKind.Null) break;
                    if (value.ValueKind != JsonValueKind.String) throw new FormatException("expanded is not a pane id");
                    expanded = PaneKinds.FromId(value.GetString());
                    break;
                case "recent":
                    if (value.ValueKind != JsonValueKind.Array) throw new FormatException("recent is not a list");
                    foreach (var id in value.EnumerateArray())
                    {
                        if (id.ValueKind != JsonValueKind.String) throw new FormatException("recent holds a non-string");
                        var text = id.GetString()!;
                        if (PaneKinds.FromId(text) is { } kind) recent.Add(kind);
                        else unknownRecent.Add(new UnknownRecent(text, recentSeen));
                        recentSeen++;
                    }
                    break;
                case "narrow":
                    if (value.ValueKind == JsonValueKind.Null) break;
                    if (value.ValueKind != JsonValueKind.String) throw new FormatException("narrow is not a pane id");
                    narrow = PaneKinds.FromId(value.GetString()) ?? PaneKind.Chat;
                    break;
                case "panes":
                    if (value.ValueKind != JsonValueKind.Object) throw new FormatException("panes is not an object");
                    foreach (var pane in value.EnumerateObject()) paneData[pane.Name] = CompactText(pane.Value);
                    break;
                default:
                    unknownFields[property.Name] = CompactText(value);
                    break;
            }
        }

        var order = new List<PaneSlot>();
        var unknownSlots = new List<UnknownSlot>();
        string? before = null; // the first pane of the last known slot seen
        for (var i = 0; i < slots.Count; i++)
        {
            var (known, unknown) = slots[i];
            if (known.Count > 0)
            {
                order.Add(new PaneSlot(new ValueList<PaneKind>(known)));
                before = PaneKinds.Id(known[0]);
                if (unknown.Count > 0) unknownSlots.Add(new UnknownSlot(before, true, i, new ValueList<string>(unknown)));
            }
            else if (unknown.Count > 0) unknownSlots.Add(new UnknownSlot(before, false, i, new ValueList<string>(unknown)));
        }

        var state = new PaneWorkspaceState
        {
            Order = new ValueList<PaneSlot>(order),
            IsSideChatOpen = side,
            IsTerminalInDrawer = drawer,
            TerminalPrefersGrid = termGrid,
            Ratios = new ValueMap<string, double>(ratios),
            Collapsed = new ValueSet<string>(collapsed),
            PreCollapse = new ValueMap<string, double>(pre),
            Expanded = expanded,
            RecentTabs = new ValueList<PaneKind>(recent),
            NarrowSelection = narrow,
        }.Normalized();
        var stored = new StoredWorkspace
        {
            State = state,
            PaneData = new ValueMap<string, string>(paneData),
            Version = version,
            UnknownSlots = new ValueList<UnknownSlot>(unknownSlots),
            UnknownRecents = new ValueList<UnknownRecent>(unknownRecent),
            UnknownFields = new ValueMap<string, string>(unknownFields),
        };
        return new WorkspaceRead(version > StoredWorkspace.CurrentVersion ? WorkspaceReadOutcome.Newer : WorkspaceReadOutcome.Ok, stored);
    }

    private static (List<PaneKind> Known, List<string> Unknown) ReadSlot(JsonElement slot)
    {
        var ids = new List<string>();
        switch (slot.ValueKind)
        {
            case JsonValueKind.String:
                ids.Add(slot.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var id in slot.EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.String) throw new FormatException("a slot holds a non-string");
                    ids.Add(id.GetString()!);
                }
                break;
            default:
                throw new FormatException("a slot is not a list of pane ids");
        }
        var known = new List<PaneKind>();
        var unknown = new List<string>();
        foreach (var id in ids)
        {
            if (PaneKinds.FromId(id) is { } kind)
            {
                // The chat and Side chat are not in the grid's order: the state would drop them, so they are not kept as panes.
                if (PaneKinds.IsGridKind(kind) && !known.Contains(kind)) known.Add(kind);
            }
            else unknown.Add(id);
        }
        return (known, unknown);
    }

    private static bool ReadBool(JsonElement value, string name) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new FormatException(name + " is not true or false"),
    };

    private static void ReadNumbers(JsonElement value, string name, Dictionary<string, double> into)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException(name + " is not an object");
        foreach (var pair in value.EnumerateObject())
        {
            if (pair.Value.ValueKind != JsonValueKind.Number || !pair.Value.TryGetDouble(out var number) || !double.IsFinite(number))
                throw new FormatException(name + "." + pair.Name + " is not a number");
            into[pair.Name] = number;
        }
    }

    /// <summary>JSON text in the library's form (keys sorted, no white space); null when the text is not JSON.</summary>
    public static string? Compact(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            return CompactText(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The form the library file writes a value in (keys sorted, no white space, numbers as Swift renders them), so text kept here
    // is the same text after a trip through library.json and a unit that sets what it already holds changes nothing.
    private static string CompactText(JsonElement element) => SwiftJson.WriteString(element, SwiftJsonStyle.Storage);

    /// <summary>The workspace as JSON text (compact).</summary>
    public static string Write(StoredWorkspace stored)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, stored);
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes the workspace object. Fields at their default are left out; keys are in a fixed order.</summary>
    public static void Write(Utf8JsonWriter writer, StoredWorkspace stored)
    {
        var state = stored.State.Normalized();
        writer.WriteStartObject();
        writer.WriteNumber("v", Math.Max(StoredWorkspace.CurrentVersion, stored.Version));

        var slots = SlotsToWrite(state, stored);
        if (slots.Count > 0)
        {
            writer.WritePropertyName("order");
            writer.WriteStartArray();
            foreach (var slot in slots)
            {
                writer.WriteStartArray();
                foreach (var id in slot) writer.WriteStringValue(id);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        if (state.IsSideChatOpen) writer.WriteBoolean("side", true);
        if (state.IsTerminalInDrawer) writer.WriteBoolean("drawer", true);
        if (state.TerminalPrefersGrid) writer.WriteBoolean("termGrid", true);
        WriteNumbers(writer, "ratios", state.Ratios);
        if (state.Collapsed.Count > 0)
        {
            writer.WritePropertyName("collapsed");
            writer.WriteStartArray();
            foreach (var key in state.Collapsed.OrderBy(key => key, StringComparer.Ordinal)) writer.WriteStringValue(key);
            writer.WriteEndArray();
        }
        WriteNumbers(writer, "pre", state.PreCollapse);
        if (state.Expanded is { } expanded) writer.WriteString("expanded", PaneKinds.Id(expanded));
        var recent = state.RecentTabs.Select(PaneKinds.Id).ToList();
        foreach (var unknown in stored.UnknownRecents.OrderBy(unknown => unknown.Index)) recent.Insert(Math.Min(unknown.Index, recent.Count), unknown.Id);
        if (recent.Count > 0)
        {
            writer.WritePropertyName("recent");
            writer.WriteStartArray();
            foreach (var id in recent) writer.WriteStringValue(id);
            writer.WriteEndArray();
        }
        if (state.NarrowSelection != PaneKind.Chat) writer.WriteString("narrow", PaneKinds.Id(state.NarrowSelection));
        if (stored.PaneData.Count > 0)
        {
            writer.WritePropertyName("panes");
            writer.WriteStartObject();
            foreach (var pair in stored.PaneData.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteRawValue(pair.Value);
            }
            writer.WriteEndObject();
        }
        foreach (var pair in stored.UnknownFields.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            writer.WritePropertyName(pair.Key);
            writer.WriteRawValue(pair.Value);
        }
        writer.WriteEndObject();
    }

    private static void WriteNumbers(Utf8JsonWriter writer, string name, ValueMap<string, double> numbers)
    {
        var finite = numbers.Where(pair => double.IsFinite(pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        if (finite.Count == 0) return;
        writer.WritePropertyName(name);
        writer.WriteStartObject();
        foreach (var pair in finite) writer.WriteNumber(pair.Key, pair.Value);
        writer.WriteEndObject();
    }

    // The slots as ids: the state's slots (a tab group's unknown panes follow its known ones), then each slot of only unknown panes
    // right after the slot of the pane it followed (or back at its saved place when that pane is gone or none came before it).
    private static List<List<string>> SlotsToWrite(PaneWorkspaceState state, StoredWorkspace stored)
    {
        var slots = new List<List<string>>();
        foreach (var slot in state.Order) slots.Add(slot.Panes.Select(PaneKinds.Id).ToList());
        int SlotOf(string? anchor) => anchor is null ? -1 : slots.FindIndex(ids => ids.Contains(anchor, StringComparer.Ordinal));
        var alone = new List<UnknownSlot>();
        foreach (var extra in stored.UnknownSlots)
        {
            if (extra.Within && SlotOf(extra.Anchor) is var at and >= 0) slots[at].AddRange(extra.Ids);
            else alone.Add(extra);
        }
        var after = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var extra in alone.OrderBy(extra => extra.Index))
        {
            var anchor = SlotOf(extra.Anchor);
            if (!extra.Within && anchor >= 0)
            {
                var placed = after.GetValueOrDefault(extra.Anchor!);
                after[extra.Anchor!] = placed + 1;
                slots.Insert(Math.Min(anchor + 1 + placed, slots.Count), [.. extra.Ids]);
            }
            else slots.Insert(Math.Min(extra.Index, slots.Count), [.. extra.Ids]);
        }
        return slots;
    }
}

/// <summary>How a thread's workspace starts (DESIGN 3.11 "Migration and bad data").</summary>
public static class PaneWorkspaceMigration
{
    /// <summary>
    /// The workspace a thread opens with. A saved one is used as it is (bad parts normalised away, nothing re-clamped: the
    /// layout from a larger monitor is kept whole, and the solver fits it to the window it is drawn in). A thread with none
    /// maps the old ThreadRuntime flags in once (the terminal drawer, Changes) and starts from the ratios the user set last.
    /// </summary>
    public static StoredWorkspace Initial(StoredWorkspace? saved, bool legacyTerminalVisible, bool legacyDiffVisible, ValueMap<string, double>? lastRatios)
    {
        if (saved is not null) return saved with { State = saved.State.Normalized() };
        var state = PaneWorkspaceState.FromLegacyFlags(legacyTerminalVisible, legacyDiffVisible);
        if (lastRatios is { Count: > 0 }) state = state with { Ratios = new ValueMap<string, double>(lastRatios.Where(pair => double.IsFinite(pair.Value))) };
        return new StoredWorkspace { State = state };
    }
}
