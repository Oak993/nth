using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Hydra.swift (B2 1fbf81e), :1-1434.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// String constants whose text may fall under OD-0012 F1 / ED-088 (the words "Droppy Code", the agent names
// sent to the CLIs) or is built by interpolation are stub getters, left for the implementing unit to decide.
// Not declared yet, private in Swift and used by no other file: :659-718 the private rules and texts of
// HydraPrompts, :1092-1109 delegationOpener and lineStartFences, :1156-1162 delegations(fromBody:),
// :1217-1226 verificationMarkers, :1397-1433 landingLine, duration, list and quoted.
// Codable conformance (synthesized coding and the init(from:) at :82-94, :398-433, :477-485) is left to the
// implementing unit.
// Unit Core.Models.Hydra part 1 of 4 (PR-HYDRA-001 to 024) implemented HydraHeadProfile (ctor, BriefLabel,
// AgentSlug, Matches, and its Codable round trip), HydraPair (ctor, HeadsProvider, SendsHeadsElsewhere,
// CustomName, ClampedCap, ProjectPair, MaxHeadsRange, and its custom init(from:)/encode(to:)), HydraLaunch's
// HasRoom and Profile(named:), HydraBudget's three notes (OD-0012 F1: "[Marrow]"), HydraPersona.Color,
// HydraRoster's Persona(at:)/Index(named:)/Persona(named:), and HydraHeadInfo (its custom init(from:)/
// encode(to:), Persona, DisplayName, IsFinished, HasOwnCopy and Status.IsFinished). It also gave HydraLanding
// and HydraLanding.File Codable support (a converter each) so HydraHeadInfo's `landing` field can decode and
// encode; HydraLanding.IsEmpty and Landed are not named by any record in this part and stay MARROW_STUB for a
// later part. PR-HYDRA-016 (the in-turn budget check order and the out-of-turn backstop allowance) describes
// AppModel+Hydra.swift / ThreadRuntime.swift runtime behaviour, not anything in this file, and is untestable
// here; it is left for the unit that owns the turn loop.

// :17-51. Codable is synthesized (no CodingKeys, no custom init(from:)/encode(to:)): id and name are strictly
// required in declaration order, provider/model/effort are decodeIfPresent/encodeIfPresent (R-CODE-1).
[JsonConverter(typeof(HydraHeadProfileConverter))]
public sealed record HydraHeadProfile
{
    public Guid Id { get; init; }                // :18
    public string Name { get; init; }            // :21
    public ProviderKind? Provider { get; init; } // :22
    public string? Model { get; init; }          // :23
    public string? Effort { get; init; }         // :24

    // :26-29
    public HydraHeadProfile(string name)
    {
        Id = Guid.NewGuid();
        Name = name;
    }

    // Used by the decoder (:17-24) to set every stored field directly.
    private HydraHeadProfile(Guid id, string name, ProviderKind? provider, string? model, string? effort)
    {
        Id = id;
        Name = name;
        Provider = provider;
        Model = model;
        Effort = effort;
    }

    // :32-35
    public string BriefLabel
    {
        get
        {
            var parts = new List<string>(2);
            if (Model is { } model) parts.Add(model);
            if (Provider is { } provider) parts.Add(provider.DisplayName);
            var on = string.Join(" on ", parts);
            return on.Length == 0 ? Name : $"{Name} ({on})";
        }
    }

    // :39-41
    public string AgentSlug =>
        string.Concat(SwiftString.Characters(SwiftString.Lowercased(Name))
            .Select(c => SwiftCharacter.IsLetter(c) || SwiftCharacter.IsNumber(c) ? c : "-"));

    // :46-50
    public bool Matches(string name)
    {
        var wanted = name.Trimmed();
        return SwiftString.CaseInsensitiveCompare(Name.Trimmed(), wanted) == 0 || AgentSlug == wanted;
    }

    internal static HydraHeadProfile FromDecoded(Guid id, string name, ProviderKind? provider, string? model, string? effort) =>
        new(id, name, provider, model, effort);
}

// PORT-ONLY: System.Text.Json converter standing in for HydraHeadProfile's synthesized Codable (R-CODE-1, R-CODE-3).
internal sealed class HydraHeadProfileConverter : JsonConverter<HydraHeadProfile>
{
    public override HydraHeadProfile Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraHeadProfile Decode(JsonElement element) => HydraHeadProfile.FromDecoded(
        SwiftDecoding.Decode(element, "id", SwiftDecoding.ReadUUID),
        SwiftDecoding.Decode(element, "name", SwiftDecoding.ReadString),
        SwiftDecoding.DecodeIfPresentValue<ProviderKind>(element, "provider", SwiftDecoding.ReadRawValue<ProviderKind>),
        SwiftDecoding.DecodeIfPresent(element, "model", SwiftDecoding.ReadString),
        SwiftDecoding.DecodeIfPresent(element, "effort", SwiftDecoding.ReadString));

    public override void Write(Utf8JsonWriter writer, HydraHeadProfile value, JsonSerializerOptions options) =>
        WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, HydraHeadProfile value)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("name", value.Name);
        if (value.Provider is { } provider) writer.WriteString("provider", SwiftEnum<ProviderKind>.RawValue(provider));
        if (value.Model is { } model) writer.WriteString("model", model);
        if (value.Effort is { } effort) writer.WriteString("effort", effort);
        writer.WriteEndObject();
    }
}

// :58-121. Custom init(from:) at :82-94; encode(to:) is synthesized (declaration order, nil optionals omitted).
[JsonConverter(typeof(HydraPairConverter))]
public sealed record HydraPair
{
    // :74. A ClosedRange<Int> is its own Sequence (1, 2, ..., 8); ClampedCap only reads the two bounds.
    public static IReadOnlyList<long> MaxHeadsRange { get; } = [1, 2, 3, 4, 5, 6, 7, 8];

    public Guid Id { get; init; }                                  // :59
    public ProviderKind Provider { get; init; }                    // :60
    public string? OrchestratorModel { get; init; }                // :61
    public string? OrchestratorEffort { get; init; }               // :62
    public ProviderKind? WorkerProvider { get; init; }             // :63
    public string? WorkerModel { get; init; }                      // :64
    public string? WorkerEffort { get; init; }                     // :65
    public long? MaxHeads { get; init; }                           // :67
    public string? Name { get; init; }                             // :69
    public ValueList<HydraHeadProfile> HeadProfiles { get; init; } = ValueList<HydraHeadProfile>.Empty; // :72

    // :76-80
    public HydraPair(ProviderKind provider)
    {
        Id = Guid.NewGuid();
        Provider = provider;
    }

    // Used by the decoder (:82-94) to set every stored field directly, maxHeads already clamped.
    private HydraPair(Guid id, ProviderKind provider, string? orchestratorModel, string? orchestratorEffort,
        ProviderKind? workerProvider, string? workerModel, string? workerEffort, long? maxHeads, string? name,
        ValueList<HydraHeadProfile> headProfiles)
    {
        Id = id;
        Provider = provider;
        OrchestratorModel = orchestratorModel;
        OrchestratorEffort = orchestratorEffort;
        WorkerProvider = workerProvider;
        WorkerModel = workerModel;
        WorkerEffort = workerEffort;
        MaxHeads = maxHeads;
        Name = name;
        HeadProfiles = headProfiles;
    }

    // :97
    public ProviderKind HeadsProvider => WorkerProvider ?? Provider;

    // :100
    public bool SendsHeadsElsewhere => WorkerProvider is { } workerProvider && workerProvider != Provider;

    // :103-106
    public string? CustomName
    {
        get
        {
            var trimmed = (Name ?? "").Trimmed();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }

    // :109-111
    public static long? ClampedCap(long? cap) =>
        cap is { } value ? Math.Min(Math.Max(value, MaxHeadsRange[0]), MaxHeadsRange[^1]) : null;

    // :117-120
    public static HydraPair? ProjectPair(Guid? id, bool hydraOn, IReadOnlyList<HydraPair> @in)
    {
        if (!hydraOn || id is not { } wanted) return null;
        foreach (var pair in @in)
            if (pair.Id == wanted) return pair;
        return null;
    }

    internal static HydraPair FromDecoded(Guid id, ProviderKind provider, string? orchestratorModel, string? orchestratorEffort,
        ProviderKind? workerProvider, string? workerModel, string? workerEffort, long? maxHeads, string? name,
        ValueList<HydraHeadProfile> headProfiles) =>
        new(id, provider, orchestratorModel, orchestratorEffort, workerProvider, workerModel, workerEffort, maxHeads, name, headProfiles);
}

// PORT-ONLY: System.Text.Json converter standing in for HydraPair's custom init(from:) (:82-94) and its
// synthesized encode(to:) (R-CODE-1, R-CODE-2, R-CODE-3).
internal sealed class HydraPairConverter : JsonConverter<HydraPair>
{
    public override HydraPair Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraPair Decode(JsonElement element) => HydraPair.FromDecoded(
        Coding.Value(element, "id", SwiftDecoding.ReadUUID, Guid.NewGuid),
        SwiftDecoding.Decode(element, "provider", SwiftDecoding.ReadRawValue<ProviderKind>),
        Coding.Value(element, "orchestratorModel", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "orchestratorEffort", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "workerProvider", field => (ProviderKind?)SwiftDecoding.ReadRawValue<ProviderKind>(field), () => (ProviderKind?)null),
        Coding.Value(element, "workerModel", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "workerEffort", SwiftDecoding.ReadString, () => (string?)null),
        HydraPair.ClampedCap(Coding.Value(element, "maxHeads", field => (long?)SwiftDecoding.ReadInt(field), () => (long?)null)),
        Coding.Value(element, "name", SwiftDecoding.ReadString, () => (string?)null),
        Coding.LenientList(element, "headProfiles", HydraHeadProfileConverter.Decode, () => ValueList<HydraHeadProfile>.Empty));

    public override void Write(Utf8JsonWriter writer, HydraPair value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", SwiftUUID.UuidString(value.Id));
        writer.WriteString("provider", SwiftEnum<ProviderKind>.RawValue(value.Provider));
        if (value.OrchestratorModel is { } orchestratorModel) writer.WriteString("orchestratorModel", orchestratorModel);
        if (value.OrchestratorEffort is { } orchestratorEffort) writer.WriteString("orchestratorEffort", orchestratorEffort);
        if (value.WorkerProvider is { } workerProvider) writer.WriteString("workerProvider", SwiftEnum<ProviderKind>.RawValue(workerProvider));
        if (value.WorkerModel is { } workerModel) writer.WriteString("workerModel", workerModel);
        if (value.WorkerEffort is { } workerEffort) writer.WriteString("workerEffort", workerEffort);
        if (value.MaxHeads is { } maxHeads) writer.WriteNumber("maxHeads", maxHeads);
        if (value.Name is { } name) writer.WriteString("name", name);
        writer.WritePropertyName("headProfiles");
        writer.WriteStartArray();
        foreach (var profile in value.HeadProfiles) HydraHeadProfileConverter.WriteMembers(writer, profile);
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}

// :125-180
public sealed record HydraLaunch
{
    public required ProviderKind HeadsProvider { get; init; }                                    // :128
    public required bool RunsNatively { get; init; }                                             // :133
    public string? HeadsLabel { get; init; }                                                     // :136
    public string? WorkerModel { get; init; }                                                    // :139
    public string? WorkerEffort { get; init; }                                                   // :140
    public long? MaxHeads { get; init; }                                                         // :143
    public ValueList<HydraHeadProfile> HeadProfiles { get; init; } = ValueList<HydraHeadProfile>.Empty; // :146
    public bool IsolatesHeads { get; init; } = true;                                             // :148
    public bool AutoMerges { get; init; } = false;                                               // :151
    public bool ReviewsHeads { get; init; } = false;                                             // :155
    public ValueList<HydraProjectRef> Projects { get; init; } = ValueList<HydraProjectRef>.Empty; // :158
    public bool AntiSlopEnabled { get; init; } = true;                                           // :160

    public const long UncappedConcurrency = 8; // :168

    // :171-173
    public bool HasRoom(long running) => running < (MaxHeads ?? UncappedConcurrency);

    // :177-179
    public HydraHeadProfile? Profile(string named)
    {
        foreach (var profile in HeadProfiles)
            if (profile.Matches(named)) return profile;
        return null;
    }
}

// :186-201
public static class HydraBudget
{
    public const long PacingTools = 24;              // :188
    public const long WrapUpTools = 120;             // :191
    public const long MaxTools = 160;                // :192
    public const double WrapUpSeconds = 25 * 60;     // :193
    public const double MaxSeconds = 35 * 60;        // :194
    public const double ReportSeconds = 3 * 60;      // :196

    // :198. DEVIATION OD-0012 F1: "Droppy Code" -> "Marrow" in the app's own text.
    public static string PacingNote =>
        $"[Marrow] You have run {PacingTools} tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you.";

    // :199. DEVIATION OD-0012 F1.
    public static string WrapUpNote =>
        "[Marrow] Your budget is nearly spent. Finish now: complete the smallest correct version of the task, then reply with your report.";

    // :200. DEVIATION OD-0012 F1.
    public static string FinalNote =>
        "[Marrow] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.";
}

// :204-226. Color is a plain RGBA value at the Core boundary (IMPLEMENTING 2.1).
public sealed record HydraPersona
{
    public string Name { get; }   // :205
    public string Asset { get; }  // :208
    public uint Hex { get; }      // :210

    // :218-224. Color(red:green:blue:) defaults alpha to 1; the colour does not change between light and dark.
    public (double Red, double Green, double Blue, double Alpha) Color =>
        (((Hex >> 16) & 0xFF) / 255.0, ((Hex >> 8) & 0xFF) / 255.0, (Hex & 0xFF) / 255.0, 1.0);

    // :212-216
    public HydraPersona(string name, string asset, uint hex)
    {
        Name = name;
        Asset = asset;
        Hex = hex;
    }
}

// :230-293
public static class HydraRoster
{
    // :231-257
    public static ValueList<HydraPersona> Personas { get; } = new HydraPersona[]
    {
        new("Hank", "hydra-head-01", 0xFF8A3D),
        new("Walter", "hydra-head-02", 0x3D8BFF),
        new("Ada", "hydra-head-03", 0x34C46A),
        new("Otto", "hydra-head-04", 0xA35BE0),
        new("Nova", "hydra-head-05", 0xF25C9A),
        new("Remy", "hydra-head-06", 0x2BB5B0),
        new("Iris", "hydra-head-07", 0xE8B430),
        new("Milo", "hydra-head-08", 0xE84F4F),
        new("Juno", "hydra-head-09", 0x6A5CFF),
        new("Ezra", "hydra-head-10", 0x4FD1A1),
        new("Lena", "hydra-head-11", 0x2FB8E6),
        new("Bo", "hydra-head-12", 0xB0784A),
        new("Kai", "hydra-head-13", 0x8FD14F),
        new("Vera", "hydra-head-14", 0xD946EF),
        new("Finn", "hydra-head-15", 0xF97316),
        new("Mira", "hydra-head-16", 0xC084FC),
        new("Odin", "hydra-head-17", 0xB45309),
        new("Suki", "hydra-head-18", 0xF472B6),
        new("Rex", "hydra-head-19", 0x2563EB),
        new("Zola", "hydra-head-20", 0x65A30D),
        new("Pip", "hydra-head-21", 0x94A3B8),
        new("Ivo", "hydra-head-22", 0x0D9488),
        new("Lux", "hydra-head-23", 0xFACC15),
        new("Tova", "hydra-head-24", 0x059669),
        new("Gus", "hydra-head-25", 0x7DD3FC),
    };

    // :259-265
    public static HydraPersona Persona(long at)
    {
        var count = Personas.Count;
        var baseCase = Personas[(int)(((at % count) + count) % count)];
        var round = Math.Max(0, at) / count;
        return round <= 0 ? baseCase : new HydraPersona($"{baseCase.Name} {round + 1}", baseCase.Asset, baseCase.Hex);
    }

    // :273-280
    public static long? Index(string named)
    {
        var bare = named.Trimmed();
        if (TrailingIntegerSpace(bare) is { } space) bare = bare[..space].Trimmed();
        for (var i = 0; i < Personas.Count; i++)
            if (SwiftString.CaseInsensitiveCompare(Personas[i].Name, bare) == 0) return i;
        return null;
    }

    // :284-292
    public static HydraPersona? Persona(string named)
    {
        if (Index(named) is not { } index) return null;
        var bare = named.Trimmed();
        if (TrailingIntegerSpace(bare) is { } space &&
            SwiftNumber.ParseInt(bare[(space + 1)..]) is { } round && round > 1)
            return Persona(index + (round - 1) * Personas.Count);
        return Persona(index);
    }

    // The index of the last " " in `text` when the token after it parses as an Int (a trailing round
    // number, "Otto 2"). Space is a single UTF-16 unit and always its own Character, so an ordinal
    // LastIndexOf finds the same boundary as Swift's Character-walked lastIndex(of:).
    private static int? TrailingIntegerSpace(string text)
    {
        var space = text.LastIndexOf(' ');
        return space >= 0 && SwiftNumber.ParseInt(text[(space + 1)..]) is not null ? space : null;
    }
}

// :297-434. Custom init(from:) at :398-433; encode(to:) is synthesized (declaration order, nil optionals omitted).
[JsonConverter(typeof(HydraHeadInfoConverter))]
public sealed record HydraHeadInfo
{
    // :300-303
    public enum Kind
    {
        [RawValue("native")] Native,
        [RawValue("droppy")] Droppy,
    }

    // :307-311
    public enum Origin
    {
        [RawValue("delegated")] Delegated,
        [RawValue("queued")] Queued,
        [RawValue("sent")] Sent,
    }

    // :313-320
    public enum Status
    {
        [RawValue("running")] Running,
        [RawValue("completed")] Completed,
        [RawValue("failed")] Failed,
        [RawValue("stopped")] Stopped,
    }

    public long Index { get; init; }                                   // :323
    public string Task { get; init; }                                  // :324
    public Kind HydraHeadInfoKind { get; init; }                       // :325 Swift: kind (R-NAME-3)
    public Origin HydraHeadInfoOrigin { get; init; }                   // :326 Swift: origin (R-NAME-3)
    public Status HydraHeadInfoStatus { get; init; } = Status.Running; // :327 Swift: status (R-NAME-3)
    public string? Summary { get; init; }                              // :330
    public string? Activity { get; init; }                             // :332
    public long ToolCalls { get; init; } = 0;                          // :333
    public long Tokens { get; init; } = 0;                             // :334
    public DateTimeOffset StartedAt { get; init; } = SwiftDate.Now;    // :335
    public DateTimeOffset? FinishedAt { get; init; }                   // :336
    public DateTimeOffset? QueuedAt { get; init; }                     // :339
    public DateTimeOffset? ExecutionStartedAt { get; init; }           // :344
    public DateTimeOffset? ExecutionFinishedAt { get; init; }          // :347
    public DateTimeOffset? LandingStartedAt { get; init; }             // :351
    public DateTimeOffset? LandingFinishedAt { get; init; }            // :354
    public string? NativeID { get; init; }                             // :357
    public string? NativeTaskID { get; init; }                         // :359
    public string? ToolUseID { get; init; }                            // :361
    public Guid? BatchID { get; init; }                                // :363
    public string? Profile { get; init; }                              // :366
    public bool CanStop { get; init; } = true;                         // :368
    public bool IsBackground { get; init; } = true;                    // :371
    public string? BaseTree { get; init; }                             // :375
    public HydraLanding? Landing { get; init; }                        // :377
    public DateTimeOffset? MergedAt { get; init; }                     // :382

    public HydraPersona Persona => HydraRoster.Persona(Index);              // :384
    public string DisplayName => Persona.Name;                             // :386
    public bool IsFinished => HydraHeadInfoStatus.IsFinished;              // :387
    public bool HasOwnCopy => BaseTree is not null;                        // :389

    // :391-396
    public HydraHeadInfo(long index, string task, Kind kind, Origin origin)
    {
        Index = index;
        Task = task;
        HydraHeadInfoKind = kind;
        HydraHeadInfoOrigin = origin;
    }

    // Used by the decoder (:398-433) to set every stored field directly.
    private HydraHeadInfo(
        long index, string task, Kind kind, Origin origin, Status status, string? summary, string? activity,
        long toolCalls, long tokens, DateTimeOffset startedAt, DateTimeOffset? finishedAt, DateTimeOffset? queuedAt,
        DateTimeOffset? executionStartedAt, DateTimeOffset? executionFinishedAt, DateTimeOffset? landingStartedAt,
        DateTimeOffset? landingFinishedAt, string? nativeID, string? nativeTaskID, string? toolUseID, Guid? batchID,
        string? profile, bool canStop, bool isBackground, string? baseTree, HydraLanding? landing, DateTimeOffset? mergedAt)
    {
        Index = index;
        Task = task;
        HydraHeadInfoKind = kind;
        HydraHeadInfoOrigin = origin;
        HydraHeadInfoStatus = status;
        Summary = summary;
        Activity = activity;
        ToolCalls = toolCalls;
        Tokens = tokens;
        StartedAt = startedAt;
        FinishedAt = finishedAt;
        QueuedAt = queuedAt;
        ExecutionStartedAt = executionStartedAt;
        ExecutionFinishedAt = executionFinishedAt;
        LandingStartedAt = landingStartedAt;
        LandingFinishedAt = landingFinishedAt;
        NativeID = nativeID;
        NativeTaskID = nativeTaskID;
        ToolUseID = toolUseID;
        BatchID = batchID;
        Profile = profile;
        CanStop = canStop;
        IsBackground = isBackground;
        BaseTree = baseTree;
        Landing = landing;
        MergedAt = mergedAt;
    }

    internal static HydraHeadInfo FromDecoded(
        long index, string task, Kind kind, Origin origin, Status status, string? summary, string? activity,
        long toolCalls, long tokens, DateTimeOffset startedAt, DateTimeOffset? finishedAt, DateTimeOffset? queuedAt,
        DateTimeOffset? executionStartedAt, DateTimeOffset? executionFinishedAt, DateTimeOffset? landingStartedAt,
        DateTimeOffset? landingFinishedAt, string? nativeID, string? nativeTaskID, string? toolUseID, Guid? batchID,
        string? profile, bool canStop, bool isBackground, string? baseTree, HydraLanding? landing, DateTimeOffset? mergedAt) =>
        new(index, task, kind, origin, status, summary, activity, toolCalls, tokens, startedAt, finishedAt, queuedAt,
            executionStartedAt, executionFinishedAt, landingStartedAt, landingFinishedAt, nativeID, nativeTaskID,
            toolUseID, batchID, profile, canStop, isBackground, baseTree, landing, mergedAt);
}

public static class HydraHeadInfoStatusExtensions
{
    extension(HydraHeadInfo.Status status)
    {
        // :319
        public bool IsFinished => status != HydraHeadInfo.Status.Running;
    }
}

// PORT-ONLY: System.Text.Json converter standing in for HydraHeadInfo's custom init(from:) (:398-433) and its
// synthesized encode(to:) (R-CODE-1, R-CODE-2, R-CODE-3).
internal sealed class HydraHeadInfoConverter : JsonConverter<HydraHeadInfo>
{
    public override HydraHeadInfo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraHeadInfo Decode(JsonElement element) => HydraHeadInfo.FromDecoded(
        Coding.Value(element, "index", SwiftDecoding.ReadInt, () => 0L),
        Coding.Value(element, "task", SwiftDecoding.ReadString, () => ""),
        Coding.Value(element, "kind", SwiftDecoding.ReadRawValue<HydraHeadInfo.Kind>, () => HydraHeadInfo.Kind.Droppy),
        Coding.Value(element, "origin", SwiftDecoding.ReadRawValue<HydraHeadInfo.Origin>, () => HydraHeadInfo.Origin.Delegated),
        // A status that cannot be read (a record from a newer build) reads as stopped, never as completed (:404-407).
        Coding.Value(element, "status", SwiftDecoding.ReadRawValue<HydraHeadInfo.Status>, () => HydraHeadInfo.Status.Stopped),
        Coding.Value(element, "summary", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "activity", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "toolCalls", SwiftDecoding.ReadInt, () => 0L),
        Coding.Value(element, "tokens", SwiftDecoding.ReadInt, () => 0L),
        Coding.Value(element, "startedAt", SwiftDecoding.ReadStorageDate, () => SwiftDate.Now),
        Coding.Value(element, "finishedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "queuedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "executionStartedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "executionFinishedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "landingStartedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "landingFinishedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null),
        Coding.Value(element, "nativeID", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "nativeTaskID", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "toolUseID", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "batchID", field => (Guid?)SwiftDecoding.ReadUUID(field), () => (Guid?)null),
        Coding.Value(element, "profile", SwiftDecoding.ReadString, () => (string?)null),
        // Droppy Code owns a Droppy-run head's turn, so it can always stop one; a native head depends on the
        // provider having said so (:426-428). The default reads the already-decoded kind, per the Swift order.
        DecodeCanStop(element),
        Coding.Value(element, "isBackground", SwiftDecoding.ReadBool, () => true),
        Coding.Value(element, "baseTree", SwiftDecoding.ReadString, () => (string?)null),
        Coding.Value(element, "landing", field => (HydraLanding?)HydraLandingConverter.Decode(field), () => (HydraLanding?)null),
        Coding.Value(element, "mergedAt", field => (DateTimeOffset?)SwiftDecoding.ReadStorageDate(field), () => (DateTimeOffset?)null));

    private static bool DecodeCanStop(JsonElement element)
    {
        var kind = Coding.Value(element, "kind", SwiftDecoding.ReadRawValue<HydraHeadInfo.Kind>, () => HydraHeadInfo.Kind.Droppy);
        return Coding.Value(element, "canStop", SwiftDecoding.ReadBool, () => kind == HydraHeadInfo.Kind.Droppy);
    }

    public override void Write(Utf8JsonWriter writer, HydraHeadInfo value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("index", value.Index);
        writer.WriteString("task", value.Task);
        writer.WriteString("kind", SwiftEnum<HydraHeadInfo.Kind>.RawValue(value.HydraHeadInfoKind));
        writer.WriteString("origin", SwiftEnum<HydraHeadInfo.Origin>.RawValue(value.HydraHeadInfoOrigin));
        writer.WriteString("status", SwiftEnum<HydraHeadInfo.Status>.RawValue(value.HydraHeadInfoStatus));
        if (value.Summary is { } summary) writer.WriteString("summary", summary);
        if (value.Activity is { } activity) writer.WriteString("activity", activity);
        writer.WriteNumber("toolCalls", value.ToolCalls);
        writer.WriteNumber("tokens", value.Tokens);
        writer.WriteString("startedAt", FoundationISO8601.Format(value.StartedAt));
        if (value.FinishedAt is { } finishedAt) writer.WriteString("finishedAt", FoundationISO8601.Format(finishedAt));
        if (value.QueuedAt is { } queuedAt) writer.WriteString("queuedAt", FoundationISO8601.Format(queuedAt));
        if (value.ExecutionStartedAt is { } executionStartedAt) writer.WriteString("executionStartedAt", FoundationISO8601.Format(executionStartedAt));
        if (value.ExecutionFinishedAt is { } executionFinishedAt) writer.WriteString("executionFinishedAt", FoundationISO8601.Format(executionFinishedAt));
        if (value.LandingStartedAt is { } landingStartedAt) writer.WriteString("landingStartedAt", FoundationISO8601.Format(landingStartedAt));
        if (value.LandingFinishedAt is { } landingFinishedAt) writer.WriteString("landingFinishedAt", FoundationISO8601.Format(landingFinishedAt));
        if (value.NativeID is { } nativeID) writer.WriteString("nativeID", nativeID);
        if (value.NativeTaskID is { } nativeTaskID) writer.WriteString("nativeTaskID", nativeTaskID);
        if (value.ToolUseID is { } toolUseID) writer.WriteString("toolUseID", toolUseID);
        if (value.BatchID is { } batchID) writer.WriteString("batchID", SwiftUUID.UuidString(batchID));
        if (value.Profile is { } profile) writer.WriteString("profile", profile);
        writer.WriteBoolean("canStop", value.CanStop);
        writer.WriteBoolean("isBackground", value.IsBackground);
        if (value.BaseTree is { } baseTree) writer.WriteString("baseTree", baseTree);
        if (value.Landing is { } landing)
        {
            writer.WritePropertyName("landing");
            HydraLandingConverter.WriteMembers(writer, landing);
        }
        if (value.MergedAt is { } mergedAt) writer.WriteString("mergedAt", FoundationISO8601.Format(mergedAt));
        writer.WriteEndObject();
    }
}

// :439-451.
public sealed record HydraMentionTarget
{
    public required Guid ThreadID { get; init; }                 // :440
    public required string Task { get; init; }                   // :441
    public required HydraHeadInfo.Status Status { get; init; }   // :442

    public const string Scheme = "droppycode-head"; // :444

    public SwiftURL Url => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:445");

    public static Guid? ThreadIDIn(SwiftURL url) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:447");
}

// :455-487. Custom init(from:) at :477-485 (every field lenient with the in-source default); encode(to:) is
// synthesized (declaration order, nil optionals omitted). Only the decoder/encoder needed to read and write the
// `landing` field of HydraHeadInfo (:377) is ported here; IsEmpty and Landed are this file's own later records.
[JsonConverter(typeof(HydraLandingConverter))]
public sealed record HydraLanding
{
    // :456-460
    [JsonConverter(typeof(HydraLandingFileConverter))]
    public sealed record File
    {
        public required string Path { get; init; }    // :457
        public required long Additions { get; init; } // :458
        public required long Deletions { get; init; } // :459
    }

    public ValueList<File> Files { get; init; } = ValueList<File>.Empty;          // :463
    public ValueList<string> Conflicts { get; init; } = ValueList<string>.Empty;  // :465
    public long DroppedBuildOutputFiles { get; init; } = 0;                       // :467
    public string? PatchPath { get; init; }                                       // :469
    public string? Error { get; init; }                                           // :470

    public bool IsEmpty => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:473");
    public bool Landed => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:475");

    // :486
    public HydraLanding() { }
}

// PORT-ONLY: System.Text.Json converter standing in for HydraLanding's custom init(from:) (:477-485) and its
// synthesized encode(to:).
internal sealed class HydraLandingConverter : JsonConverter<HydraLanding>
{
    public override HydraLanding Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraLanding Decode(JsonElement element) => new()
    {
        Files = Coding.LenientList(element, "files", HydraLandingFileConverter.Decode, () => ValueList<HydraLanding.File>.Empty),
        Conflicts = Coding.LenientList(element, "conflicts", SwiftDecoding.ReadString, () => ValueList<string>.Empty),
        DroppedBuildOutputFiles = Coding.Value(element, "droppedBuildOutputFiles", SwiftDecoding.ReadInt, () => 0L),
        PatchPath = Coding.Value(element, "patchPath", SwiftDecoding.ReadString, () => (string?)null),
        Error = Coding.Value(element, "error", SwiftDecoding.ReadString, () => (string?)null),
    };

    public override void Write(Utf8JsonWriter writer, HydraLanding value, JsonSerializerOptions options) =>
        WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, HydraLanding value)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("files");
        writer.WriteStartArray();
        foreach (var file in value.Files) HydraLandingFileConverter.WriteMembers(writer, file);
        writer.WriteEndArray();
        writer.WritePropertyName("conflicts");
        writer.WriteStartArray();
        foreach (var conflict in value.Conflicts) writer.WriteStringValue(conflict);
        writer.WriteEndArray();
        writer.WriteNumber("droppedBuildOutputFiles", value.DroppedBuildOutputFiles);
        if (value.PatchPath is { } patchPath) writer.WriteString("patchPath", patchPath);
        if (value.Error is { } error) writer.WriteString("error", error);
        writer.WriteEndObject();
    }
}

// PORT-ONLY: System.Text.Json converter for HydraLanding.File (synthesized Codable, declaration order path,
// additions, deletions, all required).
internal sealed class HydraLandingFileConverter : JsonConverter<HydraLanding.File>
{
    public override HydraLanding.File Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Decode(document.RootElement);
    }

    internal static HydraLanding.File Decode(JsonElement element) => new()
    {
        Path = SwiftDecoding.Decode(element, "path", SwiftDecoding.ReadString),
        Additions = SwiftDecoding.Decode(element, "additions", SwiftDecoding.ReadInt),
        Deletions = SwiftDecoding.Decode(element, "deletions", SwiftDecoding.ReadInt),
    };

    public override void Write(Utf8JsonWriter writer, HydraLanding.File value, JsonSerializerOptions options) =>
        WriteMembers(writer, value);

    internal static void WriteMembers(Utf8JsonWriter writer, HydraLanding.File value)
    {
        writer.WriteStartObject();
        writer.WriteString("path", value.Path);
        writer.WriteNumber("additions", value.Additions);
        writer.WriteNumber("deletions", value.Deletions);
        writer.WriteEndObject();
    }
}

// :493-504
public sealed record HydraDelegation
{
    public required string Task { get; init; }   // :494
    public required string Prompt { get; init; } // :495
    public string? Name { get; init; }           // :496
    public string? Project { get; init; }        // :500
    public string? Profile { get; init; }        // :503
}

// :509-517. Swift `enum HydraMergeStage: Int` keeps the implicit values 0 to 6, its raw values (R-ENUM-1).
public enum HydraMergeStage
{
    Gathering,
    Committing,
    Describing,
    Pushing,
    Opening,
    Merging,
    Syncing,
}

public static class HydraMergeStageExtensions
{
    public static IReadOnlyList<HydraMergeStage> AllCases =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:509");

    extension(HydraMergeStage stage)
    {
        public string Words => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:519");
    }
}

// :533-536
public sealed record HydraProjectRef
{
    public required string Name { get; init; } // :534
    public required string Path { get; init; } // :535
}

// :539-552
public sealed record HydraReport
{
    public required long HeadIndex { get; init; }                  // :540
    public required string Task { get; init; }                     // :541
    public required HydraHeadInfo.Origin Origin { get; init; }     // :542
    public required HydraHeadInfo.Status Status { get; init; }     // :543
    public required string Text { get; init; }                     // :544
    public HydraLanding? Landing { get; init; }                    // :547
    public string? CopyPath { get; init; }                         // :549
    public double? Elapsed { get; init; }                          // :550
    public long ToolCalls { get; init; } = 0;                      // :551
}

// :559-617. The mutating methods return the changed copy (R-VAL-1); one with a Swift result returns both.
public sealed record HydraDelegationBudget
{
    public long Rounds { get; private init; } = 0;          // :561
    public long Refusals { get; private init; } = 0;        // :563
    public string? DropNotice { get; private init; }        // :568

    public bool CanDelegate => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:571");

    public HydraDelegationBudget BeginTurn(bool userAuthored) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:577");

    public HydraDelegationBudget ChargeRound() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:584");

    public (HydraDelegationBudget Budget, bool Result) Refuse(string message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:591");

    public HydraDelegationBudget NoteDropped(string message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:602");

    public (HydraDelegationBudget Budget, string? Result) TakeDropNotice() =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:607");

    public const long MaxRefusals = 3; // :616
}

// :620-1434.
public static class HydraPrompts
{
    public static string WorkerAgentName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:621");
    public static string ScoutAgentName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:622");

    // :627 static func workerAgentName(for:). C# cannot give a method the name of the property
    // WorkerAgentName, so it is WorkerAgentNameFor.
    public static string WorkerAgentNameFor(HydraHeadProfile @for) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:627");

    public static string? ProfileName(string? forAgentName) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:633");

    public static ValueList<HydraHeadProfile> NativeRoutableProfiles(HydraLaunch of) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:642");

    // :652-655
    public abstract record Workplace
    {
        private Workplace() { }

        public sealed record OwnCopy(string Path) : Workplace; // :653
        public sealed record Shared(string Path) : Workplace;  // :654
    }

    public static string Policy(ProviderKind @for, HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:722");

    public static string WorkerPrompt => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:793");

    public static string ScoutPrompt => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:800");

    public static JSONValue ClaudeAgents(HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:807");

    public static ValueList<JSONValue> CopilotAgents(HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:842");

    public static ValueMap<string, JSONValue> CodexConfig(HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:894");

    public const long MaxDelegationRounds = 3; // :912

    // :916. The Swift default `now: Date = .now` is a null default.
    public static string? TeamStatus(IReadOnlyList<HydraHeadInfo> heads, DateTimeOffset? now = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:916");

    // :941. The Swift defaults `projects: []` and `profiles: []` are null defaults.
    public static string FallbackPolicy(long? maxHeads, bool isolated, bool autoMerges = false, bool reviewsHeads = false,
        string? heads = null, IReadOnlyList<HydraProjectRef>? projects = null,
        IReadOnlyList<HydraHeadProfile>? profiles = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:941");

    public static string FallbackPolicy(HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:974");

    // :979. The Swift default `now: Date = .now` is a null default.
    public static string? MergeStatus(IReadOnlyList<HydraMergeRecord> merges, long unmergedFiles, DateTimeOffset? now = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:979");

    public static string DelegationRequest(HydraLaunch launch) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1023");

    public static string FallbackTurnNote(string? team, string? merge = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1038");

    // :1047. Swift order is (team, merge = nil, canDelegate); the defaulted merge goes last here.
    public static string FallbackReportNote(string? team, bool canDelegate, string? merge = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1047");

    public static string FallbackPreamble(HydraLaunch launch, string? team, string? merge = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1056");

    // :1060. Swift order is (launch, team, merge = nil, canDelegate); the defaulted merge goes last here.
    public static string FallbackReportPreamble(HydraLaunch launch, string? team, bool canDelegate, string? merge = null) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1060");

    public static string HeldBackMessage(long count) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1066");

    public static string InterruptedBlockMessage(bool stopped) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1075");

    public static string DroppedBlockNotice(string message) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1086");

    // :1117. Range<String.Index> is a UTF-16 offset range (R-STR-10).
    public static (int LowerBound, int UpperBound)? DelegationBlockRange(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1117");

    public static bool HasDelegationBlock(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1124");

    // :1129. The Swift parameter is a Substring.
    public static bool HasDelegationOpener(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1129");

    public static ValueList<HydraDelegation>? Delegations(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1140");

    public static string WithoutDelegationBlock(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1165");

    public static TimeSpan DelegationSentHold { get; } = SwiftTask.Seconds(3.2);          // :1173
    public static TimeSpan DelegationLeaveFade { get; } = TimeSpan.FromMilliseconds(450); // :1177

    public static bool HasSentDelegationBlock(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1181");

    public static bool HasLeavingDelegationBlock(string @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1186");

    public static string MarkingDelegationBlockLeaving(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1193");

    public static string MarkingDelegationBlockSent(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1200");

    public static string UnreadableBlockMessage(string? reason) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1207");

    public static string WithoutVerificationSteps(string prompt) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1230");

    public static string DelegatedHeadPrompt(HydraPersona persona, HydraDelegation delegation, Workplace workplace) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1241");

    // :1262-1266
    public sealed record ChatContext
    {
        public string? LastUserPrompt { get; init; }                                  // :1263
        public string? LastReply { get; init; }                                       // :1264
        public ValueList<string> TouchedPaths { get; init; } = ValueList<string>.Empty; // :1265
    }

    public static string QueuedHeadPrompt(HydraPersona persona, string task, ChatContext context, Workplace workplace,
        bool leadIsWorking = true) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1271");

    // :1310. The Swift default `stillWorking: []` is a null default.
    public static string ReportMessage(IReadOnlyList<HydraReport> reports, IReadOnlyList<string>? stillWorking = null,
        bool reviewsHeads = false) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:1310");
}
