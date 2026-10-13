// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.3, 15.3; unit AS-16; ED-NEW-dextra-as16-12). No Swift twin.
// Which pieces of work Dextra handed to Asturs, and which chat is waiting for them. A chat's pieces form one group; when none is still at
// work, the group is due: its reports go back to that chat in one message, each under its Astur's name (DelegationMerge). A piece the owner
// stopped, or one that blocked, may go on later; its group is delivered without it, and if it later finishes, that one report is due on
// its own ("late"). A group is due once: Observe marks it delivered before it returns it, so a crash between the mark and the message loses
// the message and never repeats it. Kept in delegations.json (a version, unknown fields written back, a newer file left as it is) so a
// restart does not forget who is waiting. Plain data and arithmetic; the controller reads the missions and sends the messages.
using System.Globalization;
using System.Text.Json.Nodes;

namespace Marrow.Services.Assistant;

/// <summary>Where a delegated piece stands. Stopped and Blocked may go on again; Finished and Failed are the end.</summary>
public enum PieceState { Waiting, Finished, Failed, Stopped, Blocked }

public sealed record DelegatedPiece(Guid Mission, string AsturId, PieceState State)
{
    public bool Final => State is PieceState.Finished or PieceState.Failed;
}

/// <summary>A group of pieces handed out by one chat, and whether its message was sent.</summary>
public sealed record DelegationGroup(Guid Id, Guid Chat, string FromAssistant, IReadOnlyList<DelegatedPiece> Pieces, bool Delivered);

/// <summary>A message the book says is due: a whole group, or one late piece of a group already delivered.</summary>
public sealed record DelegationDue(Guid Group, Guid Chat, string FromAssistant, IReadOnlyList<DelegatedPiece> Pieces, bool Late);

public sealed class DelegationBook
{
    public const int FormatVersion = 1;

    /// <summary>The most pieces one chat's group holds; one more is refused.</summary>
    public const int MaxPieces = 16;

    /// <summary>The most groups kept; a new one is refused when this many are still waiting.</summary>
    public const int MaxGroups = 64;

    private sealed class Group(Guid id, Guid chat, string from)
    {
        public Guid Id { get; } = id;
        public Guid Chat { get; } = chat;
        public string From { get; } = from;
        public List<DelegatedPiece> Pieces { get; } = [];
        public bool Delivered { get; set; }
    }

    private readonly object gate = new();
    private readonly List<Group> groups = [];
    private JsonObject? root;

    /// <summary>The file's version was newer than this build knows: nothing was read, and nothing is written over it.</summary>
    public bool ReadOnly { get; private set; }

    public event Action? Changed;

    /// <summary>The groups, oldest first.</summary>
    public IReadOnlyList<DelegationGroup> Groups
    {
        get { lock (gate) return [.. groups.Select(g => new DelegationGroup(g.Id, g.Chat, g.From, [.. g.Pieces], g.Delivered))]; }
    }

    /// <summary>
    /// Records that <paramref name="chat"/> handed a piece to an Astur. It joins the chat's group that is still waiting, else starts one.
    /// Null when the group is full, or when this many groups are already waiting.
    /// </summary>
    public Guid? Add(Guid chat, string fromAssistant, Guid mission, string asturId)
    {
        Guid id;
        lock (gate)
        {
            var group = groups.LastOrDefault(g => g.Chat == chat && !g.Delivered);
            if (group is null)
            {
                Prune();
                // Full: the oldest group already delivered (one kept only for a piece that may go on) makes room; one still waiting never does.
                if (groups.Count >= MaxGroups)
                {
                    var oldest = groups.FindIndex(g => g.Delivered);
                    if (oldest < 0) return null;
                    groups.RemoveAt(oldest);
                }
                group = new Group(Guid.NewGuid(), chat, fromAssistant);
                groups.Add(group);
            }
            else if (group.Pieces.Count >= MaxPieces) return null;
            if (group.Pieces.All(p => p.Mission != mission)) group.Pieces.Add(new DelegatedPiece(mission, asturId, PieceState.Waiting));
            id = group.Id;
        }
        Changed?.Invoke();
        return id;
    }

    /// <summary>The group holding a mission, if any.</summary>
    public DelegationGroup? Of(Guid mission) => Groups.FirstOrDefault(g => g.Pieces.Any(p => p.Mission == mission));

    /// <summary>
    /// A mission's piece moved to <paramref name="state"/>. Returns the message that is due because of it, once: the group, when this was
    /// the last piece at work, or the late piece, when a piece left out of a delivered group has now ended. Null otherwise.
    /// </summary>
    public DelegationDue? Observe(Guid mission, PieceState state)
    {
        DelegationDue? due = null;
        var changed = false;
        lock (gate)
        {
            var group = groups.FirstOrDefault(g => g.Pieces.Any(p => p.Mission == mission));
            if (group is null) return null;
            var index = group.Pieces.FindIndex(p => p.Mission == mission);
            var before = group.Pieces[index];
            if (before.State == state) return null;
            var after = before with { State = state };
            group.Pieces[index] = after;
            changed = true;
            if (!group.Delivered)
            {
                if (group.Pieces.All(p => p.State != PieceState.Waiting))
                {
                    group.Delivered = true;
                    due = new DelegationDue(group.Id, group.Chat, group.From, [.. group.Pieces], Late: false);
                }
            }
            else if (!before.Final && after.Final)
            {
                due = new DelegationDue(group.Id, group.Chat, group.From, [after], Late: true);
            }
            Prune();
        }
        if (changed) Changed?.Invoke();
        return due;
    }

    /// <summary>Forgets the groups of a chat that is gone (the chat was deleted): nobody is waiting for them.</summary>
    public void Forget(Guid chat)
    {
        bool removed;
        lock (gate) removed = groups.RemoveAll(g => g.Chat == chat) > 0;
        if (removed) Changed?.Invoke();
    }

    // Under the lock: delivered groups with nothing that may still go on are dropped.
    private void Prune() => groups.RemoveAll(g => g.Delivered && g.Pieces.All(p => p.Final));

    // ---- the stored document ----

    /// <summary>The document to store, or null when the file read was newer than this build knows.</summary>
    public JsonObject? ToJson()
    {
        lock (gate)
        {
            if (ReadOnly) return null;
            var document = root is null ? new JsonObject() : (JsonObject)root.DeepClone();
            document["version"] = FormatVersion;
            document["groups"] = new JsonArray([.. groups.Select(g => (JsonNode)new JsonObject
            {
                ["id"] = g.Id.ToString("D"),
                ["chat"] = g.Chat.ToString("D"),
                ["from"] = g.From,
                ["delivered"] = g.Delivered,
                ["pieces"] = new JsonArray([.. g.Pieces.Select(p => (JsonNode)new JsonObject
                {
                    ["mission"] = p.Mission.ToString("D"),
                    ["astur"] = p.AsturId,
                    ["state"] = p.State.ToString(),
                })]),
            })]);
            return document;
        }
    }

    /// <summary>Loads a stored document. A newer version makes the book read-only and loads nothing; a line that is not a group is skipped.</summary>
    public void Load(JsonObject? document)
    {
        lock (gate)
        {
            groups.Clear();
            root = null;
            ReadOnly = false;
            if (document is null) return;
            if (AssistantJson.Number(document["version"]) is { } version && version > FormatVersion)
            {
                ReadOnly = true;
                return;
            }
            root = (JsonObject)document.DeepClone();
            root.Remove("version");
            root.Remove("groups");
            if (document["groups"] is not JsonArray lines) return;
            foreach (var line in lines.OfType<JsonObject>())
            {
                if (groups.Count >= MaxGroups) break;
                if (ReadGuid(line["id"]) is not { } id || ReadGuid(line["chat"]) is not { } chat || line["from"] is not JsonValue from || !from.TryGetValue<string>(out var by)
                    || !AssistantStore.IsId(by) || groups.Any(g => g.Id == id)) continue;
                var group = new Group(id, chat, by) { Delivered = line["delivered"] is JsonValue d && d.TryGetValue<bool>(out var flag) && flag };
                if (line["pieces"] is JsonArray pieces)
                {
                    foreach (var piece in pieces.OfType<JsonObject>())
                    {
                        if (group.Pieces.Count >= MaxPieces) break;
                        if (ReadGuid(piece["mission"]) is not { } mission || piece["astur"] is not JsonValue astur || !astur.TryGetValue<string>(out var asturId) || !AssistantStore.IsId(asturId)
                            || piece["state"] is not JsonValue state || !state.TryGetValue<string>(out var stateText) || !Enum.TryParse<PieceState>(stateText, out var parsed)
                            || !Enum.IsDefined(parsed) || group.Pieces.Any(p => p.Mission == mission)) continue;
                        group.Pieces.Add(new DelegatedPiece(mission, asturId, parsed));
                    }
                }
                if (group.Pieces.Count > 0) groups.Add(group);
            }
        }
    }

    private static Guid? ReadGuid(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && Guid.TryParse(text, CultureInfo.InvariantCulture, out var id) ? id : null;
}
