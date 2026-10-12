using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Library.swift (B2 1fbf81e), :1-278.

// :3-55. Associated-value enum -> abstract record + nested sealed records (R-ENUM-2).
[JsonConverter(typeof(ProjectIconConverter))]
public abstract record ProjectIcon
{
    private ProjectIcon() { }

    // :7-8. The synthesized Hashable compares the associated String canonically (R-STR-2), where a record's own
    // equality is ordinal: "e" + U+0301 and U+00E9 are one icon in Swift.
    public sealed record Emoji(string Value) : ProjectIcon  // :7
    {
        public bool Equals(Emoji? other) => other is not null && SwiftString.Equal(Value, other.Value);
        public override int GetHashCode() => SwiftStringComparer.Instance.GetHashCode(Value);
    }

    public sealed record Symbol(string Name) : ProjectIcon  // :8
    {
        public bool Equals(Symbol? other) => other is not null && SwiftString.Equal(Name, other.Name);
        public override int GetHashCode() => SwiftStringComparer.Instance.GetHashCode(Name);
    }

    // :11-14. Swift: emoji (R-NAME-3: collides with the nested Emoji case type).
    public string? ProjectIconEmoji => this is Emoji emoji ? emoji.Value : null;

    // :17-20
    public string? SymbolName => this is Symbol symbol ? symbol.Name : null;

    // :23-28
    public string Storage => this switch
    {
        Emoji emoji => "emoji:" + emoji.Value,
        Symbol symbol => "symbol:" + symbol.Name,
        _ => throw new UnreachableException(),
    };

    // :32-41. Failable init -> static Create (R-NAME-5).
    public static ProjectIcon? Create(string storage)
    {
        if (storage.Length == 0) return null; // :33
        if (SwiftString.HasPrefix(storage, "emoji:")) return new Emoji(SwiftString.DropFirst(storage, "emoji:".Length));   // :34-35
        if (SwiftString.HasPrefix(storage, "symbol:")) return new Symbol(SwiftString.DropFirst(storage, "symbol:".Length)); // :36-37
        return new Symbol(storage); // :38-39
    }
}

// PORT-ONLY: System.Text.Json converter standing in for ProjectIcon's custom Codable (R-CODE-1, R-CODE-3).
internal sealed class ProjectIconConverter : JsonConverter<ProjectIcon>
{
    public override ProjectIcon Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    // :43-49
    internal static ProjectIcon Decode(JsonElement element)
    {
        var raw = SwiftDecoding.ReadString(element); // :44 singleValueContainer().decode(String.self)
        return ProjectIcon.Create(raw) ?? throw new DecodingError("dataCorrupted", "An empty project icon"); // :45-47
    }

    public override void Write(Utf8JsonWriter writer, ProjectIcon value, JsonSerializerOptions options) => WriteValue(writer, value);

    // :51-54
    internal static void WriteValue(Utf8JsonWriter writer, ProjectIcon value) => writer.WriteStringValue(value.Storage);
}

// :57-94
[JsonConverter(typeof(ProjectConverter))]
public sealed record Project
{
    public Guid Id { get; init; }                            // :58
    public string Name { get; init; }                         // :59
    public string Path { get; init; }                          // :60
    public DateTimeOffset AddedAt { get; init; }                 // :61
    public bool IsExpanded { get; init; }                         // :62
    public ValueList<ProjectScript> Scripts { get; init; }        // :63
    public Guid? HydraPairID { get; init; }                        // :67
    public ProjectIcon? Icon { get; init; }                         // :70

    // :72-79
    public Project(string name, string path)
    {
        Id = Guid.NewGuid();
        Name = name;
        Path = path;
        AddedAt = SwiftDate.Now;
        IsExpanded = true;
        Scripts = ValueList<ProjectScript>.Empty;
    }

    // Used by the decoder (:81-91) to set every stored field directly.
    private Project(Guid id, string name, string path, DateTimeOffset addedAt, bool isExpanded,
        ValueList<ProjectScript> scripts, Guid? hydraPairID, ProjectIcon? icon)
    {
        Id = id;
        Name = name;
        Path = path;
        AddedAt = addedAt;
        IsExpanded = isExpanded;
        Scripts = scripts;
        HydraPairID = hydraPairID;
        Icon = icon;
    }

    internal static Project FromDecoded(Guid id, string name, string path, DateTimeOffset addedAt, bool isExpanded,
        ValueList<ProjectScript> scripts, Guid? hydraPairID, ProjectIcon? icon) =>
        new(id, name, path, addedAt, isExpanded, scripts, hydraPairID, icon);

    // :93 URL(fileURLWithPath: path). ENGINEERING DECISION, as Attachment.Url (Models/Timeline.cs): F.URL
    // (SwiftURL) only builds URL(string:); there is no ported fileURLWithPath initializer. Built as an
    // absolute file URI so any other caller of Url still gets a working SwiftURL.
    public SwiftURL Url =>
        SwiftURL.Create("file://" + PercentEncodeFileUriPath(Path)) ?? throw SwiftTraps.Fatal("Invalid project path.");

    // Manual, minimal RFC3986 percent-encoding for the Url property above (not a Swift twin; see the
    // DEVIATION note on Url, mirroring Attachment's PercentEncodeFileUriPath in Models/Timeline.cs).
    private static string PercentEncodeFileUriPath(string path)
    {
        var builder = new System.Text.StringBuilder();
        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0 || normalized[0] != '/') builder.Append('/');
        foreach (var b in System.Text.Encoding.UTF8.GetBytes(normalized))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || "-._~/:".IndexOf(c, StringComparison.Ordinal) >= 0)) builder.Append(c);
            else builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

// PORT-ONLY: System.Text.Json converter standing in for Project's hand-written init(from:) and synthesized
// encode(to:) (R-CODE-1, R-CODE-3).
internal sealed class ProjectConverter : JsonConverter<Project>
{
    public override Project Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    // :81-91. id and path are required; everything else has a default (PR-STORE-082).
    internal static Project Decode(JsonElement element)
    {
        var id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadUUID);       // :83
        var path = SwiftDecoding.Decode(element, "path", SwiftDecoding.ReadString); // :84
        var name = Coding.Value(element, "name", SwiftDecoding.ReadString, () => SwiftPath.LastPathComponent(path)); // :85
        var addedAt = Coding.Value(element, "addedAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now);          // :86
        var isExpanded = Coding.Value(element, "isExpanded", SwiftDecoding.ReadBool, () => true);                   // :87
        // A malformed element inside "scripts" fails the whole array decode, caught by container.value's try?,
        // so scripts falls back to empty; it does NOT fail the surrounding project (Coding.swift:5-7). This is
        // the exact three-line behaviour the pinned Coding.swift and Library.swift give; it degrades one field,
        // never the project itself, because "scripts" here is a plain container.value(default:) read, not a
        // Lenient<T> wrapper around Project as a whole.
        var scripts = Coding.Value(element, "scripts", field => SwiftDecoding.ReadArray(field, ProjectScriptConverter.Decode),
            () => ValueList<ProjectScript>.Empty); // :88
        var hydraPairID = Coding.Value<Guid?>(element, "hydraPairID", field => SwiftDecoding.ReadUUID(field), () => null); // :89
        var icon = Coding.Value<ProjectIcon?>(element, "icon", ProjectIconConverter.Decode, () => null);                  // :90
        return Project.FromDecoded(id, name, path, addedAt, isExpanded, scripts, hydraPairID, icon);
    }

    public override void Write(Utf8JsonWriter writer, Project value, JsonSerializerOptions options) => WriteMembers(writer, value);

    // Synthesized encode(to:): declaration order, nil optionals omitted (R-CODE-3).
    internal static void WriteMembers(Utf8JsonWriter writer, Project value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("name", value.Name);
        writer.WriteString("path", value.Path);
        writer.WriteString("addedAt", FoundationISO8601.Format(value.AddedAt));
        writer.WriteBoolean("isExpanded", value.IsExpanded);
        writer.WritePropertyName("scripts");
        writer.WriteStartArray();
        foreach (var script in value.Scripts) ProjectScriptConverter.WriteMembers(writer, script);
        writer.WriteEndArray();
        if (value.HydraPairID is { } hydraPairID) writer.WriteString("hydraPairID", SwiftUUID.UuidString(hydraPairID));
        if (value.Icon is { } icon)
        {
            writer.WritePropertyName("icon");
            ProjectIconConverter.WriteValue(writer, icon);
        }
        writer.WriteEndObject();
    }
}

// :96-119
[JsonConverter(typeof(ProjectScriptConverter))]
public sealed record ProjectScript
{
    public Guid Id { get; init; }                    // :97
    public string Name { get; init; }                 // :98
    public string Command { get; init; }                // :99
    public string Symbol { get; init; }                   // :100
    public bool RunOnWorktreeCreate { get; init; }          // :101

    // :103-109
    public ProjectScript(string name, string command, string symbol = "play", bool runOnWorktreeCreate = false)
    {
        Id = Guid.NewGuid();
        Name = name;
        Command = command;
        Symbol = symbol;
        RunOnWorktreeCreate = runOnWorktreeCreate;
    }

    // Used by the decoder (:111-118) to set every stored field directly.
    private ProjectScript(Guid id, string name, string command, string symbol, bool runOnWorktreeCreate)
    {
        Id = id;
        Name = name;
        Command = command;
        Symbol = symbol;
        RunOnWorktreeCreate = runOnWorktreeCreate;
    }

    internal static ProjectScript FromDecoded(Guid id, string name, string command, string symbol, bool runOnWorktreeCreate) =>
        new(id, name, command, symbol, runOnWorktreeCreate);
}

// PORT-ONLY: System.Text.Json converter standing in for ProjectScript's hand-written init(from:) and
// synthesized encode(to:) (R-CODE-1, R-CODE-3).
internal sealed class ProjectScriptConverter : JsonConverter<ProjectScript>
{
    public override ProjectScript Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    // :111-118. name and command are required; id, symbol and runOnWorktreeCreate have defaults.
    internal static ProjectScript Decode(JsonElement element) => ProjectScript.FromDecoded(
        Coding.Value(element, "id", SwiftDecoding.ReadUUID, () => Guid.NewGuid()),      // :113
        SwiftDecoding.Decode(element, "name", SwiftDecoding.ReadString),                 // :114
        SwiftDecoding.Decode(element, "command", SwiftDecoding.ReadString),               // :115
        Coding.Value(element, "symbol", SwiftDecoding.ReadString, () => "play"),           // :116
        Coding.Value(element, "runOnWorktreeCreate", SwiftDecoding.ReadBool, () => false)); // :117

    public override void Write(Utf8JsonWriter writer, ProjectScript value, JsonSerializerOptions options) => WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, ProjectScript value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("name", value.Name);
        writer.WriteString("command", value.Command);
        writer.WriteString("symbol", value.Symbol);
        writer.WriteBoolean("runOnWorktreeCreate", value.RunOnWorktreeCreate);
        writer.WriteEndObject();
    }
}

// :121-265
[JsonConverter(typeof(ChatThreadConverter))]
public sealed record ChatThread
{
    public const string Untitled = "New thread"; // :122

    public Guid Id { get; init; }                             // :124
    public Guid ProjectID { get; init; }                        // :125
    public string Title { get; init; }                           // :126
    public DateTimeOffset CreatedAt { get; init; }                 // :127
    public DateTimeOffset UpdatedAt { get; init; }                  // :128
    public ProviderKind Provider { get; init; }                      // :129
    public string? Model { get; init; }                                // :130
    public string? Effort { get; init; }                                // :131
    public bool FastMode { get; init; }                                   // :132
    public RuntimeMode RuntimeMode { get; init; }                          // :133
    public InteractionMode InteractionMode { get; init; }                   // :134
    public string? WorktreePath { get; init; }                               // :135
    public string? Branch { get; init; }                                      // :136
    public string? ProviderSessionID { get; init; }                             // :137
    // PORT-ONLY: OD-0114 / ED-404. Null binds pre-MA-1 native sessions to legacy Account 1 in place.
    public Guid? ProviderAccountID { get; init; }
    // PORT-ONLY: OD-0114. An account pin is independent of sidebar IsPinned; null follows the order.
    public Guid? ProviderAccountPinID { get; init; }
    public string? ProviderResumeAt { get; init; }                                // :138
    public bool IsPinned { get; init; }                                            // :139
    public bool IsArchived { get; init; }                                           // :140
    public bool IsSettled { get; init; } = false;                                     // :143
    public DateTimeOffset? SettledAt { get; init; }                                     // :145
    public bool HasUnread { get; init; }                                                  // :146
    public bool HasCustomTitle { get; init; }                                               // :147
    public double? SortOrder { get; init; }                                                   // :149
    public double? ActivityOrder { get; init; }                                                 // :151
    public DateTimeOffset? ActivityOrderDay { get; init; }                                        // :153
    public TurnStatus? LastStatus { get; init; }                                                    // :154
    public Guid? ParentThreadID { get; init; }                                                        // :158
    public bool IsInPanel { get; init; } = false;                                                        // :161
    public bool ExpandsHelpers { get; init; } = false;                                                      // :166

    // :167-170. A settable computed property whose setter mutates the struct's own stored field (mutating
    // returns the new value, R-VAL-1): the getter ports directly, the setter becomes a With-method.
    public bool FoldsHelpers => !ExpandsHelpers;
    public ChatThread WithFoldsHelpers(bool value) => this with { ExpandsHelpers = !value };

    public bool HydraEnabled { get; init; } = true;              // :174
    public bool HydraEnabledIsExplicit { get; init; } = true;      // :179
    public Guid? HydraPairID { get; init; }                          // :182
    public long HydraSpawnCount { get; init; } = 0;                    // :184
    public HydraHeadInfo? Hydra { get; init; }                          // :186

    // PORT-ONLY (OD-0114, MA-7): the pill this thread relays through, and where it is in it, so a restart resumes the relay. All three are null for a thread with no pill.
    public Guid? PillID { get; init; }
    public int? PillStepIndex { get; init; }
    public string? PillStepAccountID { get; init; }

    // PORT-ONLY (web surfaces W6, ED-NEW-web-w6-1; DESIGN 3.9): this thread is the side chat of its parent, a second light
    // conversation beside the main run. It is a panel child (ParentThreadID, IsInPanel) that the sidebar, the palette and the
    // subagent panel never list. Written only when set, so every other thread's file is byte for byte what it was.
    public bool IsSideChat { get; init; }

    public Marrow.Core.Accounts.Pills.PillThreadState? PillRelay =>
        PillID is { } pill && PillStepIndex is { } index && PillStepAccountID is { } account ? new(pill, index, account) : null;

    public ChatThread WithPillRelay(Marrow.Core.Accounts.Pills.PillThreadState? state) =>
        this with { PillID = state?.PillId, PillStepIndex = state?.StepIndex, PillStepAccountID = state?.AccountId };

    public bool IsHelper => ParentThreadID is not null; // :189
    public bool IsHydraHead => Hydra is not null;         // :192

    /// <summary>Defaults key holding the per-chat Hydra default; AppSettings reads it from here.</summary>
    public const string HydraDefaultKey = "hydraDefaultEnabled"; // :195

    // :199-202. CaptureRun.defaults is a capture-mode-only redirection, proven user-unreachable
    // (porting/dead_code.md entry 44: "Library.swift:200 ... use CaptureRun.defaults"); it always resolves to
    // UserDefaults.standard on this build, so the port reads UserDefaults.Standard directly.
    public static bool HydraDefaultEnabled() =>
        Plist.AsBool(UserDefaults.Standard.Current.Object(HydraDefaultKey)) ?? true;

    // :204-221
    public ChatThread(Guid projectID, ProviderKind provider, string? model, string? effort, RuntimeMode runtimeMode, bool fastMode = false)
    {
        Id = Guid.NewGuid();
        ProjectID = projectID;
        Title = Untitled;
        CreatedAt = SwiftDate.Now;
        UpdatedAt = SwiftDate.Now;
        Provider = provider;
        Model = model;
        Effort = effort;
        FastMode = fastMode;
        RuntimeMode = runtimeMode;
        InteractionMode = InteractionMode.Build;
        IsPinned = false;
        IsArchived = false;
        HasUnread = false;
        HasCustomTitle = false;
        HydraEnabled = HydraDefaultEnabled();
        // Every other field takes its declaration default above: IsSettled false, IsInPanel false,
        // ExpandsHelpers false, HydraEnabledIsExplicit true, HydraSpawnCount 0.
    }

    // Used by the decoder (:223-264) to set every stored field directly.
    private ChatThread(Guid id, Guid projectID, string title, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        ProviderKind provider, string? model, string? effort, bool fastMode, RuntimeMode runtimeMode,
        InteractionMode interactionMode, string? worktreePath, string? branch, string? providerSessionID,
        string? providerResumeAt, bool isPinned, bool isArchived, bool isSettled, DateTimeOffset? settledAt,
        bool hasUnread, bool hasCustomTitle, double? sortOrder, double? activityOrder, DateTimeOffset? activityOrderDay,
        TurnStatus? lastStatus, Guid? parentThreadID, bool isInPanel, bool expandsHelpers, bool hydraEnabled,
        bool hydraEnabledIsExplicit, Guid? hydraPairID, long hydraSpawnCount, HydraHeadInfo? hydra)
    {
        Id = id;
        ProjectID = projectID;
        Title = title;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Provider = provider;
        Model = model;
        Effort = effort;
        FastMode = fastMode;
        RuntimeMode = runtimeMode;
        InteractionMode = interactionMode;
        WorktreePath = worktreePath;
        Branch = branch;
        ProviderSessionID = providerSessionID;
        ProviderResumeAt = providerResumeAt;
        IsPinned = isPinned;
        IsArchived = isArchived;
        IsSettled = isSettled;
        SettledAt = settledAt;
        HasUnread = hasUnread;
        HasCustomTitle = hasCustomTitle;
        SortOrder = sortOrder;
        ActivityOrder = activityOrder;
        ActivityOrderDay = activityOrderDay;
        LastStatus = lastStatus;
        ParentThreadID = parentThreadID;
        IsInPanel = isInPanel;
        ExpandsHelpers = expandsHelpers;
        HydraEnabled = hydraEnabled;
        HydraEnabledIsExplicit = hydraEnabledIsExplicit;
        HydraPairID = hydraPairID;
        HydraSpawnCount = hydraSpawnCount;
        Hydra = hydra;
    }

    internal static ChatThread FromDecoded(Guid id, Guid projectID, string title, DateTimeOffset createdAt, DateTimeOffset updatedAt,
        ProviderKind provider, string? model, string? effort, bool fastMode, RuntimeMode runtimeMode,
        InteractionMode interactionMode, string? worktreePath, string? branch, string? providerSessionID,
        string? providerResumeAt, bool isPinned, bool isArchived, bool isSettled, DateTimeOffset? settledAt,
        bool hasUnread, bool hasCustomTitle, double? sortOrder, double? activityOrder, DateTimeOffset? activityOrderDay,
        TurnStatus? lastStatus, Guid? parentThreadID, bool isInPanel, bool expandsHelpers, bool hydraEnabled,
        bool hydraEnabledIsExplicit, Guid? hydraPairID, long hydraSpawnCount, HydraHeadInfo? hydra) =>
        new(id, projectID, title, createdAt, updatedAt, provider, model, effort, fastMode, runtimeMode, interactionMode,
            worktreePath, branch, providerSessionID, providerResumeAt, isPinned, isArchived, isSettled, settledAt,
            hasUnread, hasCustomTitle, sortOrder, activityOrder, activityOrderDay, lastStatus, parentThreadID,
            isInPanel, expandsHelpers, hydraEnabled, hydraEnabledIsExplicit, hydraPairID, hydraSpawnCount, hydra);
}

// PORT-ONLY: System.Text.Json converter standing in for ChatThread's hand-written init(from:) and synthesized
// encode(to:) (R-CODE-1, R-CODE-3).
internal sealed class ChatThreadConverter : JsonConverter<ChatThread>
{
    public override ChatThread Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    // :223-264. id and projectID are required; everything else has a default.
    internal static ChatThread Decode(JsonElement element)
    {
        var id = SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadUUID);               // :225
        var projectID = SwiftDecoding.Decode(element, "projectID", SwiftDecoding.ReadUUID); // :226
        var title = Coding.Value(element, "title", SwiftDecoding.ReadString, () => ChatThread.Untitled); // :227
        var createdAt = Coding.Value(element, "createdAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now); // :228
        var updatedAt = Coding.Value(element, "updatedAt", SwiftDecoding.ReadStorageDate, () => createdAt);     // :229
        var provider = Coding.Value(element, "provider", SwiftDecoding.ReadRawValue<ProviderKind>, () => ProviderKind.Codex); // :230
        var model = Coding.Value<string?>(element, "model", SwiftDecoding.ReadString, () => null);   // :231
        var effort = Coding.Value<string?>(element, "effort", SwiftDecoding.ReadString, () => null); // :232
        var fastMode = Coding.Value(element, "fastMode", SwiftDecoding.ReadBool, () => false);       // :233
        // A mode that cannot be read falls back to supervised, never to full machine access (:234-236).
        var runtimeMode = Coding.Value(element, "runtimeMode", SwiftDecoding.ReadRawValue<RuntimeMode>, () => RuntimeMode.Supervised); // :237
        var interactionMode = Coding.Value(element, "interactionMode", SwiftDecoding.ReadRawValue<InteractionMode>, () => InteractionMode.Build); // :238
        var worktreePath = Coding.Value<string?>(element, "worktreePath", SwiftDecoding.ReadString, () => null); // :239
        var branch = Coding.Value<string?>(element, "branch", SwiftDecoding.ReadString, () => null);             // :240
        var providerSessionID = Coding.Value<string?>(element, "providerSessionID", SwiftDecoding.ReadString, () => null); // :241
        var providerResumeAt = Coding.Value<string?>(element, "providerResumeAt", SwiftDecoding.ReadString, () => null);   // :242
        var isPinned = Coding.Value(element, "isPinned", SwiftDecoding.ReadBool, () => false);     // :243
        var isArchived = Coding.Value(element, "isArchived", SwiftDecoding.ReadBool, () => false); // :244
        var isSettled = Coding.Value(element, "isSettled", SwiftDecoding.ReadBool, () => false);   // :245
        var settledAt = Coding.Value<DateTimeOffset?>(element, "settledAt", field => SwiftDecoding.ReadStorageDate(field), () => null); // :246
        var hasUnread = Coding.Value(element, "hasUnread", SwiftDecoding.ReadBool, () => false);         // :247
        // DEVIATION OD-0008 A11 (BLK-0011 FIX): upstream (:248) defaults an unreadable hasCustomTitle to
        // false; the port defaults it to true so a typed name is never overwritten by the auto-titler. A
        // stored true or false is still read as stored; only the absent/null/wrong-type fallback differs.
        var hasCustomTitle = Coding.Value(element, "hasCustomTitle", SwiftDecoding.ReadBool, () => true); // :248, DEVIATION OD-0008 A11
        var sortOrder = Coding.Value<double?>(element, "sortOrder", field => SwiftDecoding.ReadDouble(field), () => null); // :249
        var activityOrder = Coding.Value<double?>(element, "activityOrder", field => SwiftDecoding.ReadDouble(field), () => null); // :250
        var activityOrderDay = Coding.Value<DateTimeOffset?>(element, "activityOrderDay", field => SwiftDecoding.ReadStorageDate(field), () => null); // :251
        var lastStatus = Coding.Value<TurnStatus?>(element, "lastStatus", field => SwiftDecoding.ReadRawValue<TurnStatus>(field), () => null); // :252
        var parentThreadID = Coding.Value<Guid?>(element, "parentThreadID", field => SwiftDecoding.ReadUUID(field), () => null); // :253
        var isInPanel = Coding.Value(element, "isInPanel", SwiftDecoding.ReadBool, () => false);           // :254
        var expandsHelpers = Coding.Value(element, "expandsHelpers", SwiftDecoding.ReadBool, () => false); // :255
        // Threads saved while Hydra was app-wide only carry an ignored false and no explicit flag: they come
        // back on. A save that wrote the flag holds a real choice. hydraEnabledIsExplicit is then set true
        // unconditionally, so every save from here on writes a real choice (:256-260).
        var explicitFlag = Coding.Value(element, "hydraEnabledIsExplicit", SwiftDecoding.ReadBool, () => false); // :258
        var hydraEnabled = explicitFlag ? Coding.Value(element, "hydraEnabled", SwiftDecoding.ReadBool, () => true) : true; // :259
        const bool hydraEnabledIsExplicit = true; // :260
        var hydraPairID = Coding.Value<Guid?>(element, "hydraPairID", field => SwiftDecoding.ReadUUID(field), () => null); // :261
        var hydraSpawnCount = Coding.Value(element, "hydraSpawnCount", SwiftDecoding.ReadInt, () => 0L);                   // :262
        // HydraHeadInfo.init(from:) opens `decoder.container(keyedBy:)` first (Hydra.swift:399); a "hydra"
        // value that is present, non-null, but not a JSON object cannot supply one, so that Swift init
        // throws typeMismatch immediately, which container.value(.hydra, default: nil) (:263) swallows to
        // nil (PR-STORE-166). HydraHeadInfoConverter.Decode (Hydra.cs) is itself fully lenient field by
        // field and performs no such top-level check, so that guard is reproduced here.
        var hydra = Coding.Value<HydraHeadInfo?>(element, "hydra", field =>
            field.ValueKind == JsonValueKind.Object
                ? field.Deserialize<HydraHeadInfo>(Coding.StorageOptions)
                : throw new DecodingError("typeMismatch", "Expected to decode Dictionary<String, Any>."),
            () => null); // :263

        var thread = ChatThread.FromDecoded(id, projectID, title, createdAt, updatedAt, provider, model, effort, fastMode,
            runtimeMode, interactionMode, worktreePath, branch, providerSessionID, providerResumeAt, isPinned,
            isArchived, isSettled, settledAt, hasUnread, hasCustomTitle, sortOrder, activityOrder, activityOrderDay,
            lastStatus, parentThreadID, isInPanel, expandsHelpers, hydraEnabled, hydraEnabledIsExplicit, hydraPairID,
            hydraSpawnCount, hydra);
        // PORT-ONLY (MA-7): absent or unreadable pill fields mean no pill.
        return thread with
        {
            ProviderAccountID = element.TryGetProperty("providerAccountID", out var accountBinding) && accountBinding.ValueKind != JsonValueKind.Null
                ? Coding.Value<Guid?>(element, "providerAccountID", field => SwiftDecoding.ReadUUID(field), () => Guid.Empty) : null,
            ProviderAccountPinID = element.TryGetProperty("providerAccountPinID", out var accountPin) && accountPin.ValueKind != JsonValueKind.Null
                ? Coding.Value<Guid?>(element, "providerAccountPinID", field => SwiftDecoding.ReadUUID(field), () => Guid.Empty) : null,
            PillID = Coding.Value<Guid?>(element, "pillID", field => SwiftDecoding.ReadUUID(field), () => null),
            PillStepIndex = Coding.Value<int?>(element, "pillStepIndex", field => (int)SwiftDecoding.ReadInt(field), () => null),
            PillStepAccountID = Coding.Value<string?>(element, "pillStepAccountID", SwiftDecoding.ReadString, () => null),
            IsSideChat = Coding.Value(element, "isSideChat", SwiftDecoding.ReadBool, () => false), // PORT-ONLY (web surfaces W6): absent means an ordinary thread
        };
    }

    public override void Write(Utf8JsonWriter writer, ChatThread value, JsonSerializerOptions options) => WriteMembers(writer, value);

    // Synthesized encode(to:): declaration order, nil optionals omitted (R-CODE-3).
    internal static void WriteMembers(Utf8JsonWriter writer, ChatThread value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("projectID", SwiftUUID.UuidString(value.ProjectID));
        writer.WriteString("title", value.Title);
        writer.WriteString("createdAt", FoundationISO8601.Format(value.CreatedAt));
        writer.WriteString("updatedAt", FoundationISO8601.Format(value.UpdatedAt));
        writer.WriteString("provider", SwiftEnum<ProviderKind>.RawValue(value.Provider));
        if (value.Model is { } model) writer.WriteString("model", model);
        if (value.Effort is { } effort) writer.WriteString("effort", effort);
        writer.WriteBoolean("fastMode", value.FastMode);
        writer.WriteString("runtimeMode", SwiftEnum<RuntimeMode>.RawValue(value.RuntimeMode));
        writer.WriteString("interactionMode", SwiftEnum<InteractionMode>.RawValue(value.InteractionMode));
        if (value.WorktreePath is { } worktreePath) writer.WriteString("worktreePath", worktreePath);
        if (value.Branch is { } branch) writer.WriteString("branch", branch);
        if (value.ProviderSessionID is { } providerSessionID) writer.WriteString("providerSessionID", providerSessionID);
        if (value.ProviderResumeAt is { } providerResumeAt) writer.WriteString("providerResumeAt", providerResumeAt);
        if (value.ProviderAccountID is { } accountID) writer.WriteString("providerAccountID", SwiftUUID.UuidString(accountID));
        if (value.ProviderAccountPinID is { } accountPinID) writer.WriteString("providerAccountPinID", SwiftUUID.UuidString(accountPinID));
        writer.WriteBoolean("isPinned", value.IsPinned);
        writer.WriteBoolean("isArchived", value.IsArchived);
        writer.WriteBoolean("isSettled", value.IsSettled);
        if (value.SettledAt is { } settledAt) writer.WriteString("settledAt", FoundationISO8601.Format(settledAt));
        writer.WriteBoolean("hasUnread", value.HasUnread);
        writer.WriteBoolean("hasCustomTitle", value.HasCustomTitle);
        if (value.SortOrder is { } sortOrder) SwiftJson.WriteDouble(writer, "sortOrder", sortOrder);
        if (value.ActivityOrder is { } activityOrder) SwiftJson.WriteDouble(writer, "activityOrder", activityOrder);
        if (value.ActivityOrderDay is { } activityOrderDay) writer.WriteString("activityOrderDay", FoundationISO8601.Format(activityOrderDay));
        if (value.LastStatus is { } lastStatus) writer.WriteString("lastStatus", SwiftEnum<TurnStatus>.RawValue(lastStatus));
        if (value.ParentThreadID is { } parentThreadID) writer.WriteString("parentThreadID", SwiftUUID.UuidString(parentThreadID));
        writer.WriteBoolean("isInPanel", value.IsInPanel);
        writer.WriteBoolean("expandsHelpers", value.ExpandsHelpers);
        writer.WriteBoolean("hydraEnabled", value.HydraEnabled);
        writer.WriteBoolean("hydraEnabledIsExplicit", value.HydraEnabledIsExplicit);
        if (value.HydraPairID is { } hydraPairID) writer.WriteString("hydraPairID", SwiftUUID.UuidString(hydraPairID));
        writer.WriteNumber("hydraSpawnCount", value.HydraSpawnCount);
        if (value.Hydra is { } hydra)
        {
            writer.WritePropertyName("hydra");
            JsonSerializer.Serialize(writer, hydra, Coding.StorageOptions);
        }
        if (value.PillID is { } pillID) writer.WriteString("pillID", SwiftUUID.UuidString(pillID)); // PORT-ONLY (MA-7)
        if (value.PillStepIndex is { } pillStepIndex) writer.WriteNumber("pillStepIndex", pillStepIndex);
        if (value.PillStepAccountID is { } pillStepAccountID) writer.WriteString("pillStepAccountID", pillStepAccountID);
        if (value.IsSideChat) writer.WriteBoolean("isSideChat", true); // PORT-ONLY (web surfaces W6): omitted when false
        writer.WriteEndObject();
    }
}

// :267-278
[JsonConverter(typeof(LibraryConverter))]
public sealed record Library
{
    public ValueList<Project> Projects { get; init; } = ValueList<Project>.Empty; // :268
    public ValueList<ChatThread> Threads { get; init; } = ValueList<ChatThread>.Empty; // :269

    public Library() { } // :271

    // Used by the decoder (:273-277) to set every stored field directly.
    private Library(ValueList<Project> projects, ValueList<ChatThread> threads)
    {
        Projects = projects;
        Threads = threads;
    }

    internal static Library FromDecoded(ValueList<Project> projects, ValueList<ChatThread> threads) => new(projects, threads);
}

// PORT-ONLY: System.Text.Json converter standing in for Library's hand-written init(from:) and synthesized
// encode(to:) (R-CODE-1, R-CODE-2, R-CODE-3).
internal sealed class LibraryConverter : JsonConverter<Library>
{
    public override Library Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    // :273-277. Every project and thread that fails to decode is dropped, in place, without failing the rest
    // of the library (the Lenient<T> + compactMap pattern, R-CODE-2).
    internal static Library Decode(JsonElement element) => Library.FromDecoded(
        Coding.LenientList(element, "projects", ProjectConverter.Decode, () => ValueList<Project>.Empty),
        Coding.LenientList(element, "threads", ChatThreadConverter.Decode, () => ValueList<ChatThread>.Empty));

    public override void Write(Utf8JsonWriter writer, Library value, JsonSerializerOptions options) => WriteMembers(writer, value);

    // Synthesized encode(to:): declaration order (R-CODE-3).
    internal static void WriteMembers(Utf8JsonWriter writer, Library value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("projects");
        writer.WriteStartArray();
        foreach (var project in value.Projects) ProjectConverter.WriteMembers(writer, project);
        writer.WriteEndArray();
        writer.WritePropertyName("threads");
        writer.WriteStartArray();
        foreach (var thread in value.Threads) ChatThreadConverter.WriteMembers(writer, thread);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
