using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Marrow.Core.Port;
using Marrow.Core.Support;
using ValueDictionary = System.Collections.Generic.Dictionary<string, Marrow.Core.Support.JSONValue>;

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
// Unit Core.Models.Hydra part 2 of 4 (PR-HYDRA-025 to 052, excluding 029/030/041/042 which are not named by
// this part's records) implemented HydraMentionTarget.Url/ThreadIDIn, HydraLanding.IsEmpty/Landed,
// HydraMergeStage.Words, HydraPrompts' private word-for-word texts (HowToWork, HowToReport, BriefRule,
// VerificationRule, ProjectRule, AutoMergeRule, ReviewRule, ReportStyleRule, WorkplaceRules), the native
// Policy(for:launch:) (howToSpawn/howToWait/profileRule per provider, the conditional review and auto-merge
// insertions), NativeRoutableProfiles, WorkerAgentName/ScoutAgentName/WorkerAgentNameFor/ProfileName, and
// ClaudeAgents/CopilotAgents. WorkerPrompt and ScoutPrompt (:793-805) are not named by any PR-HYDRA-0xx record
// in this part, but ClaudeAgents (051) and CopilotAgents (052) both read them directly, so they are implemented
// here too, word for word with OD-0012 F1's "Droppy Code" -> "Marrow" applied (they are sent to the head's own
// CLI, so they are app-sent text). The private HydraPrompts members that this part's own callers (Policy,
// WorkerPrompt) do not reach - HowToWork and WorkplaceRules, consumed only by DelegatedHeadPrompt (:1241) and
// QueuedHeadPrompt (:1271), a later part's work - are declared `internal` rather than `private` (R-NAME-6 is
// written for ordinary module-internal/private members; these are Swift file-private members with no public
// caller yet in this part) so this part's own tests can assert their exact text directly, via the
// Marrow.Core.Tests InternalsVisibleTo grant already declared in Support/HangWatchdog.cs.

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

    // :445. `URL(string:)` never fails for this exact scheme://uppercase-UUID form, so the
    // `URL(fileURLWithPath: "/")` fallback is unreachable in practice; SwiftURL.Create("file:///")
    // is its equivalent (there is no FileURLWithPath helper) and is never actually returned.
    public SwiftURL Url => SwiftURL.Create($"{Scheme}://{SwiftUUID.UuidString(ThreadID)}") ?? SwiftURL.Create("file:///")!;

    // :447-450
    public static Guid? ThreadIDIn(SwiftURL url)
    {
        if (url.Scheme is not { } scheme || !SwiftString.Equal(scheme, Scheme)) return null;
        if (url.HostComponent() is not { } host) return null;
        return SwiftUUID.Parse(host);
    }
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

    public bool IsEmpty => Files.Count == 0 && PatchPath is null && Error is null; // :473
    public bool Landed => Files.Count != 0 && PatchPath is null && Error is null;  // :475

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
    // :509-517. Plain Int enum, no [RawValue]: CaseIterable synthesizes declaration order.
    public static IReadOnlyList<HydraMergeStage> AllCases { get; } =
    [
        HydraMergeStage.Gathering,
        HydraMergeStage.Committing,
        HydraMergeStage.Describing,
        HydraMergeStage.Pushing,
        HydraMergeStage.Opening,
        HydraMergeStage.Merging,
        HydraMergeStage.Syncing,
    ];

    extension(HydraMergeStage stage)
    {
        // :519-528
        public string Words => stage switch
        {
            HydraMergeStage.Gathering => "Gathering the team\U00002019s files", // :521, U+2019 RIGHT SINGLE QUOTATION MARK
            HydraMergeStage.Committing => "Writing the commit",                 // :522
            HydraMergeStage.Describing => "Writing the commit message",         // :523
            HydraMergeStage.Pushing => "Pushing the branch",                    // :524
            HydraMergeStage.Opening => "Opening the merge request",             // :525
            HydraMergeStage.Merging => "Merging",                               // :526
            HydraMergeStage.Syncing => "Bringing the checkout up to date",      // :527
            _ => throw new UnreachableException(),
        };
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
    public const string WorkerAgentName = "droppy-worker"; // :621
    public const string ScoutAgentName = "droppy-scout";    // :622

    // :627-629. C# cannot give a method the name of the const WorkerAgentName, so it is WorkerAgentNameFor.
    public static string WorkerAgentNameFor(HydraHeadProfile @for) => $"{WorkerAgentName}-{@for.AgentSlug}";

    // :633-636
    public static string? ProfileName(string? forAgentName)
    {
        if (forAgentName is not { } agentName || !SwiftString.HasPrefix(agentName, $"{WorkerAgentName}-")) return null;
        return SwiftString.DropFirst(agentName, WorkerAgentName.Length + 1);
    }

    // :642-647
    public static ValueList<HydraHeadProfile> NativeRoutableProfiles(HydraLaunch of) =>
        new(of.HeadProfiles.Where(profile =>
            (profile.Provider is null || profile.Provider == of.HeadsProvider) &&
            (profile.Model is not null || profile.Effort is not null)));

    // :652-655
    public abstract record Workplace
    {
        private Workplace() { }

        public sealed record OwnCopy(string Path) : Workplace; // :653
        public sealed record Shared(string Path) : Workplace;  // :654
    }

    // :659-670. Swift `private`; kept internal for this part's own tests (see the file header note).
    // DEVIATION OD-0012 F1 (x2): "Droppy Code made for you" -> "Marrow made for you", and "Droppy Code
    // carries your changes" -> "Marrow carries your changes"; the rest of both cases is unchanged.
    internal static string WorkplaceRules(Workplace workplace) => workplace switch
    {
        Workplace.OwnCopy(var path) => string.Join("\n",
            $"You have your own copy of the project at {path}: a git worktree Marrow made for you from the project's checkout as it was when you were sent out, uncommitted work included. Work in it directly, on the files as they are; your tools already run there. When you report, Marrow carries your changes into that checkout itself. So never commit, branch, stash, push, check out, reset, restore or clean anything, and never make or remove worktrees, whatever the project's own guidelines say about agents and worktrees: this copy already is yours. Do not use git to check your work either.",
            "Never build, test or verify anything in your copy: the lead runs every check itself, in its own checkout. Anything under the project folder that git does not ignore lands in the lead's checkout, so leave no build output there."),
        Workplace.Shared(var path) =>
            $"You work in the lead's checkout at {path}, alongside the lead and the other heads, who are changing other files at the same time. Changes you did not make are expected there: leave every one of them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes, and the lead does the verifying.",
        _ => throw new UnreachableException(),
    };

    // :674-680. Swift `private`; kept internal for this part's own tests (see the file header note).
    internal const string HowToWork =
        "- Start on the task right away: read what you need and no more, then make the change.\n" +
        "- A brief that names the file, the line and the change needs no second reading: make that edit first, then the next. Think briefly between tool calls; if you find yourself weighing designs for minutes, take the simplest reading of the brief and do it, and mention the choice in your report.\n" +
        "- Do exactly this task and only this. Do not widen it, do not touch files it does not name unless it cannot be done otherwise, and never revert, reformat or clean up work that is not yours.\n" +
        "- Never run a build, a test, a lint, a type check or any other check of your work, and never wait on one: the lead runs every check itself, in its own checkout. Make the edits your brief names and report.\n" +
        "- If something blocks you, stop and say so instead of guessing or working around it.";

    // :682. Swift `private`.
    internal const string HowToReport =
        "Report the result, the files you changed and material blockers once, in a few lines the lead can act on: what changed, where, and anything still open or uncertain. Name the caveats and the unfinished work plainly rather than leaving them out. No preamble, no restating the brief, no logs. The lead handles validation.";

    // :688. Swift `private`.
    internal const string BriefRule =
        "Write each brief as numbered, executable steps naming the file, symbol or line anchor, and exact change. Resolve design choices first; ask no questions and offer no alternatives. Give enough context to begin editing within a few tool calls.";

    // :692. Swift `private`.
    internal const string VerificationRule =
        "Heads edit or research and report; they never build, test, lint or verify. Do not assign checks or request pass reports. Run required validation yourself in the checkout after the heads return.";

    // :698-703. Swift `private`. DEVIATION OD-0012 F1: "which Droppy Code then adds to the sidebar" ->
    // "which Marrow then adds to the sidebar"; "on this Mac" is not one of the OD-0008 B4/OD-0012 G1 strings
    // and stays verbatim (see PR-HYDRA-035 windows_path).
    internal static string ProjectRule(IReadOnlyList<HydraProjectRef> projects)
    {
        var own = projects.Count > 0 ? $"this chat's project, {projects[0].Name} (`{projects[0].Path}`)" : "this chat's project";
        var others = projects.Skip(1).ToList();
        var named = others.Count == 0
            ? ""
            : " The other projects in the sidebar: " + string.Join(", ", others.Select(p => $"{p.Name} (`{p.Path}`)")) + ".";
        return $"Heads may work in any project. A head works in {own} unless it is sent to another: an entry of the delegation block names it with \"project\" (a sidebar project's name or path, or the absolute path of any git repository on this Mac, which Marrow then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt.{named} Whatever a head changes lands in the checkout of the project it worked in, and the team's work is merged per project, one merge request each. Your own tools run in this chat's checkout, and you may read and edit files in any project all the same: what you change elsewhere is merged along with that project's work. Run your own checks in the checkout itself: never make a worktree or a copy of the project for them.";
    }

    // :708. Swift `private`. DEVIATION OD-0012 F1: "Droppy Code handles branches" -> "Marrow handles
    // branches"; the rest of the paragraph is unchanged.
    internal const string AutoMergeRule =
        "Marrow handles branches, commits and merge requests after all heads return and you finish. Never run or delegate those operations. For merge/status questions, use only the current [Hydra] note: if merged, give its link; if files remain, say they land when your answer ends; if merging failed or files are outside every project, give the stated reason and required user action. If nothing is pending, promise no merge. Never claim a merge without the note confirming it.";

    // :713. Swift `private`.
    internal const string ReviewRule =
        "Read every changed file named in the reports and its affected callers. Correct integration issues yourself, run the narrowest required build in the checkout, and disclose your corrections. Never delegate this review.";

    // :718. Swift `private`.
    internal const string ReportStyleRule =
        "For each head, give one plain sentence starting with its name and describing the user-visible result; add the file details only when useful, and keep the whole account bounded to a line or two per head. Omit repeated summaries, audit jargon and decorative symbols. Say plainly what you still need from the user and what you settled or fixed yourself.";

    // :722-790. DEVIATION OD-0012 F1 (x2): "Droppy Code shows each one" -> "Marrow shows each one", and
    // "Droppy Code names the heads" -> "Marrow names the heads", and "Droppy Code still sends those heads
    // out" -> "Marrow still sends those heads out"; every other byte, including the contradiction at :782
    // between "never through a fenced hydra block" and "still sends those heads out as threads of their own"
    // (PR-HYDRA-047, reproduced upstream defect), is unchanged.
    public static string Policy(ProviderKind @for, HydraLaunch launch)
    {
        var maxHeads = launch.MaxHeads;
        var autoMerges = launch.AutoMerges;
        var reviewsHeads = launch.ReviewsHeads;
        var projects = launch.Projects;
        var profiles = NativeRoutableProfiles(launch);
        string profileRule;
        if (profiles.Count == 0)
        {
            profileRule = "";
        }
        else
        {
            switch (@for)
            {
                case ProviderKind.Claude:
                case ProviderKind.Copilot:
                    var variants = string.Join(", ", profiles.Select(profile => $"`{WorkerAgentNameFor(profile)}` for {profile.Name}"));
                    profileRule = $" Some heads are tuned for a purpose: route the task to {variants} instead of the plain `{WorkerAgentName}` when the work belongs to that purpose.";
                    break;
                case ProviderKind.Codex:
                    var routes = string.Join(", ", profiles.Select(profile =>
                    {
                        var parts = new List<string>();
                        if (profile.Model is { } model) parts.Add($"`model` = {model}");
                        if (profile.Effort is { } effort) parts.Add($"`reasoning_effort` = {effort}");
                        return $"{profile.Name} (pass {string.Join(" and ", parts)})";
                    }));
                    // :745. Plain ASCII apostrophe in "profile's"; U+2014 EM DASH, twice, each surrounded by
                    // a single space.
                    profileRule = $" Some heads are tuned for a purpose: route the task by passing the profile's own knobs in `spawn_agent` \U00002014 {routes} \U00002014 when the work belongs to that purpose.";
                    break;
                default:
                    profileRule = "";
                    break;
            }
        }
        string howToSpawn;
        string howToWait;
        switch (@for)
        {
            case ProviderKind.Claude:
                howToSpawn = $"Two agent types are yours: `{WorkerAgentName}` (edits files, runs commands) and `{ScoutAgentName}` (read-only research). Spawn them with the Agent tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.{profileRule}";
                howToWait = "Launch every head for a job in one message so they run in parallel, in the foreground" +
                    (maxHeads is { } cap1 ? $", and run at most {cap1} at once." : ".");
                break;
            case ProviderKind.Codex:
                howToSpawn = $"Spawn heads with `spawn_agent`: the `worker` agent for anything that edits files or runs commands, the `explorer` agent for read-only research.{profileRule}";
                howToWait = "Spawn every head for a job before waiting, so they run in parallel, and collect them with `wait_agent`; never leave a head running when you answer." +
                    (maxHeads is { } cap2 ? $" Run at most {cap2} at once." : "");
                break;
            case ProviderKind.Copilot:
                howToSpawn = $"Two agents are yours: `{WorkerAgentName}` (edits files, runs commands) and `{ScoutAgentName}` (read-only research). Start them with the task tool and prefer them over other agents while Hydra is on: they run on the model and effort the user chose for heads.{profileRule}";
                howToWait = "Start every head for a job at once so they run in parallel" +
                    (maxHeads is { } cap3 ? $", and run at most {cap3} at a time." : ".");
                break;
            default:
                howToSpawn = "";
                howToWait = maxHeads is { } cap4 ? $"Run at most {cap4} heads at once." : "Send every head for a job out at once so they run in parallel.";
                break;
        }
        var reviewBullet = reviewsHeads ? "- " + ReviewRule + "\n" : "";
        var autoMergeBlock = autoMerges ? "\n" + AutoMergeRule + "\n" : "";
        var lines = new[]
        {
            "# Hydra",
            "",
            $"The user switched on Hydra for this chat: you lead a team of helper agents, called heads, that work in parallel in your checkout. Switching Hydra on is the user asking you to use them, so it overrides any standing rule that says not to spawn agents unless asked. {howToSpawn} The agents you spawn this way are the Hydra heads themselves: Marrow shows each one in the team's panel under a roster name and posts its outcome in the chat. There is no other launcher and no other kind of head, so never tell the user that Hydra heads cannot be started here; when the user asks for heads, spawn them this way.",
            "",
            "Delegate first, work second. Anything bigger than a single obvious change to a single file is a job for heads: audits, reviews, a feature that spans files, a refactor, \"check everything\", research across many files or sources, several tasks in one message. In your first reply, look at the code only long enough to write good briefs, a minute and a handful of files rather than ten, then send out scouts for the reading and workers for the changes, all in that same message. Never spend minutes reading before you delegate, and never do inline what heads could be doing in parallel. Only a truly single-focus request, one file and one obvious change, is yours to do alone, and size the team to the work rather than to how many heads are free: a handful of independent pieces is a team, where three tiny steps that depend on each other are one head's task, or your own.",
            "",
            "When you delegate:",
            $"- {howToWait}",
            "- Give each head one self-contained task with the exact files, symbols and acceptance criteria it needs. Heads share the checkout but not your context, so write the task as if to a capable colleague who has read nothing yet.",
            $"- {BriefRule}",
            $"- {VerificationRule}",
            $"- {ProjectRule(projects)}",
            "- Split the work so no two heads edit the same file and no head's task rests on another head's unfinished changes: they run in parallel and never see each other's work. Keep integration, verification and the final answer for yourself: never send out a head to verify, redo or finish another head's work.",
            "- Tell the user in one line which heads you sent out and what each one does. Marrow names the heads in roster order (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on): announce each head by its task and use exactly those names in that order, never invented ones. There is no head called Ives; the roster has Ivo.",
            "- After delegating, yield until reports arrive.",
            "- Heads go out through the tools above, never through a fenced hydra block: that is the delegation format for providers without agent tools of their own. If you end a reply with one anyway, Marrow still sends those heads out as threads of their own, but your turn ends there and their reports come back as a later message.",
            "",
            "When they report back:",
            "- The checkout changes under you while heads work, and the user may be editing too. Never use git status or git diff to check on a head, and never reconcile, revert, stash or move changes you did not make.",
            "- Take a report as done work: read the files it names if something matters, but do not redo the task and do not start it over because the tree looks different from what you expected. A head that failed leaves its part to you: do it or send it out again. A head the user stopped leaves its part alone unless the user asks.",
            $"{reviewBullet}- Then finish the job: integrate, run one verification if it matters, and answer the user. {ReportStyleRule}",
            autoMergeBlock,
        };
        return string.Join("\n", lines);
    }

    // :793-798. Not named by any PR-HYDRA-0xx record in this part, but ClaudeAgents/CopilotAgents (051, 052)
    // both read it directly (see the file header note). DEVIATION OD-0012 F1: "in Droppy Code:" -> "in
    // Marrow:"; the rest is unchanged.
    public static string WorkerPrompt =>
        "You are a Hydra head in Marrow: one of several helpers working in parallel for a lead agent, in the lead's own checkout, where the lead and the other heads are changing other files at the same time. Do exactly the task you were given, and only that: do not widen it, do not touch files it does not name unless the task cannot be done otherwise, and never revert, reformat or clean up work that is not yours. Changes you did not make are expected in the checkout: leave them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes. Never run a build, a test, a lint or any other check of your work: the lead runs every check itself. Make the edits your brief names and report. If something blocks you, say so instead of guessing.\n\n" +
        HowToReport;

    // :800-805. Not named by any PR-HYDRA-0xx record in this part; see WorkerPrompt above. DEVIATION
    // OD-0012 F1: "in Droppy Code:" -> "in Marrow:"; the rest is unchanged.
    public static string ScoutPrompt =>
        "You are a Hydra head in Marrow: a read-only researcher working in parallel for a lead agent, in the lead's own checkout, where the lead and other heads are changing files at the same time; uncommitted changes there are theirs and expected. Answer exactly the question you were given, from the code and sources you can read. Change nothing.\n\n" +
        "Reply with a short report the lead can act on: the findings, with file paths and line references, and anything that contradicts what the lead assumed. No preamble.";

    // :807-839
    public static JSONValue ClaudeAgents(HydraLaunch launch)
    {
        var antiSlop = AntiSlopPolicy.Instructions(launch.AntiSlopEnabled);
        var worker = new ValueDictionary
        {
            ["description"] = "Hydra head that implements one delegated task: edits files, runs commands. Use proactively when a request splits into independent pieces or touches several parts of the codebase.",
            ["prompt"] = WorkerPrompt + "\n\n" + antiSlop,
            ["background"] = false,
        };
        var scout = new ValueDictionary
        {
            ["description"] = "Hydra head for read-only research: finds files, reads code, gathers facts and reports back. Use proactively when a job needs many files or sources read.",
            ["prompt"] = ScoutPrompt + "\n\n" + antiSlop,
            ["tools"] = new JSONValue.Array(new ValueList<JSONValue>(new JSONValue[]
                { "Read", "Grep", "Glob", "WebFetch", "WebSearch" })),
            ["background"] = false,
        };
        if (launch.WorkerModel is { } model && model != "default")
        {
            worker["model"] = model;
            scout["model"] = model;
        }
        if (launch.WorkerEffort is { } effort && effort.Length != 0)
        {
            worker["effort"] = effort;
            scout["effort"] = effort;
        }
        var agents = new ValueDictionary
        {
            [WorkerAgentName] = new JSONValue.Object(new ValueMap<string, JSONValue>(worker)),
            [ScoutAgentName] = new JSONValue.Object(new ValueMap<string, JSONValue>(scout)),
        };
        foreach (var profile in NativeRoutableProfiles(launch))
        {
            var variant = new ValueDictionary(worker)
            {
                ["description"] = $"Hydra head that implements one delegated task, tuned as the '{profile.Name}' profile. Prefer it for work that belongs to '{profile.Name}' over the plain {WorkerAgentName}.",
            };
            if (profile.Model is { } profileModel && profileModel != "default") variant["model"] = profileModel;
            if (profile.Effort is { } profileEffort && profileEffort.Length != 0) variant["effort"] = profileEffort;
            agents[WorkerAgentNameFor(profile)] = new JSONValue.Object(new ValueMap<string, JSONValue>(variant));
        }
        return new JSONValue.Object(new ValueMap<string, JSONValue>(agents));
    }

    // :842-888. dead_code.md item 17: the Swift `tools` parameter of the local `agent` helper is dead (both
    // call sites pass `tools: nil`), so the port omits the parameter and the tools concept entirely.
    public static ValueList<JSONValue> CopilotAgents(HydraLaunch launch)
    {
        var antiSlop = AntiSlopPolicy.Instructions(launch.AntiSlopEnabled);

        JSONValue Agent(string name, string display, string description, string prompt)
        {
            var obj = new ValueDictionary
            {
                ["name"] = name,
                ["displayName"] = display,
                ["description"] = description,
                ["prompt"] = prompt,
                ["infer"] = true,
            };
            if (launch.WorkerModel is { } model && model != "auto") obj["model"] = model;
            if (launch.WorkerEffort is { } effort && effort.Length != 0) obj["reasoningEffort"] = effort;
            return new JSONValue.Object(new ValueMap<string, JSONValue>(obj));
        }

        var worker = Agent(
            WorkerAgentName,
            "Hydra worker",
            "Hydra head that implements one delegated task: edits files, runs commands. Use when a request splits into independent pieces.",
            WorkerPrompt + "\n\n" + antiSlop);

        var agents = new List<JSONValue>
        {
            worker,
            Agent(
                ScoutAgentName,
                "Hydra scout",
                "Hydra head for read-only research: finds files, reads code and reports back. Use when a job needs many files or sources read.",
                ScoutPrompt + "\n\n" + antiSlop),
        };

        foreach (var profile in NativeRoutableProfiles(launch))
        {
            if (worker is not JSONValue.Object workerObject) continue;
            var variant = new ValueDictionary(workerObject.Value);
            variant["name"] = WorkerAgentNameFor(profile);
            variant["displayName"] = $"Hydra worker \U000000B7 {profile.Name}"; // :881, U+00B7 MIDDLE DOT
            variant["description"] = $"Hydra head that implements one delegated task, tuned as the '{profile.Name}' profile. Prefer it for work that belongs to '{profile.Name}' over the plain {WorkerAgentName}.";
            if (profile.Model is { } profileModel && profileModel != "auto") variant["model"] = profileModel;
            if (profile.Effort is { } profileEffort && profileEffort.Length != 0) variant["reasoningEffort"] = profileEffort;
            agents.Add(new JSONValue.Object(new ValueMap<string, JSONValue>(variant)));
        }

        return new ValueList<JSONValue>(agents);
    }

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
