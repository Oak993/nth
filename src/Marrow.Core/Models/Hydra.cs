using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Marrow.Core.Port;
using Marrow.Core.Support;
using ValueDictionary = System.Collections.Generic.Dictionary<string, Marrow.Core.Support.JSONValue>;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Hydra.swift (B3 52bc792), :1-1438.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// String constants whose text may fall under OD-0012 F1 / ED-088 (the words "Droppy Code", the agent names
// sent to the CLIs) or is built by interpolation are stub getters, left for the implementing unit to decide.
// Not declared yet, private in Swift and used by no other file: :659-718 the private rules and texts of
// HydraPrompts, :1096-1113 delegationOpener and lineStartFences, :1160-1166 delegations(fromBody:),
// :1221-1230 verificationMarkers, :1401-1437 landingLine, duration, list and quoted.
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
// WorkerPrompt) do not reach - HowToWork and WorkplaceRules, consumed only by DelegatedHeadPrompt (:1245) and
// QueuedHeadPrompt (:1275), a later part's work - are declared `internal` rather than `private` (R-NAME-6 is
// written for ordinary module-internal/private members; these are Swift file-private members with no public
// caller yet in this part) so this part's own tests can assert their exact text directly, via the
// Marrow.Core.Tests InternalsVisibleTo grant already declared in Support/HangWatchdog.cs.
// Unit Core.Models.Hydra part 3 of 4 (PR-HYDRA-053, 054, 056 to 059, 061 to 079) implemented CodexConfig,
// TeamStatus, both FallbackPolicy overloads, MergeStatus, DelegationRequest, FallbackTurnNote,
// FallbackReportNote, FallbackPreamble/FallbackReportPreamble, HeldBackMessage, UnreadableBlockMessage (its
// VerificationMarkers table too, for WithoutVerificationSteps), WithoutVerificationSteps, DelegatedHeadPrompt,
// QueuedHeadPrompt, and ReportMessage with its private helpers LandingLine, Duration, Listed and Quoted (:1401-
// 1437, not declared until now per this file's own header note). PR-HYDRA-054 (workerPrompt/scoutPrompt) was
// already implemented in part 2 (ClaudeAgents/CopilotAgents read it directly); this part only adds its test
// coverage. HydraDelegationBudget (CanDelegate, BeginTurn, ChargeRound, Refuse, NoteDropped, TakeDropNotice) and
// DelegationBlockRange/HasDelegationBlock/HasDelegationOpener/Delegations/WithoutDelegationBlock/
// HasSentDelegationBlock/HasLeavingDelegationBlock/MarkingDelegationBlockLeaving/MarkingDelegationBlockSent/
// InterruptedBlockMessage/DroppedBlockNotice are not named by any record in this part and stay MARROW_STUB for
// part 4.
// Unit Core.Models.Hydra part 4 of 4 (PR-HYDRA-080, 082, 083, 088 to 093, 097, 098, 248 to 251,
// PR-HYDRAMERGE-083, UI-HYDRAMERGE-003) implemented HydraDelegationBudget (CanDelegate, BeginTurn,
// ChargeRound, Refuse, NoteDropped, TakeDropNotice), DelegationOpener/LineStartFences (private),
// DelegationBlockRange, HasDelegationBlock, HasDelegationOpener, WithoutDelegationBlock,
// HasSentDelegationBlock/HasLeavingDelegationBlock, MarkingDelegationBlockSent/Leaving,
// InterruptedBlockMessage and DroppedBlockNotice. PR-HYDRA-097 (the fenced-code-block prohibition sentence)
// and PR-HYDRAMERGE-083 (MergeStatus's .held/.settled branches) were both already implemented by part 3's
// FallbackPolicy and MergeStatus; this part only adds their test coverage. UI-HYDRAMERGE-003's testable
// surface in this file is HydraMergeStage.Words alone (already implemented and tested, part 1); its
// geometry, typography, colors, transitions and shimmer belong to App/Views/Chat/TimelineRows.swift, a UI
// unit, and are untestable here.
// Orchestrator ruling (run4, on this unit's STOP over PR-HYDRA-084/085/098): delegation(fromEntry:)
// (DroppyCode/Core/Models/HydraDelegationStream.swift:110-118, an extension of HydraPrompts) is ported
// as the HydraPrompts partial src/Marrow.Core/Models/HydraDelegationStream.cs (R-FILE-1), containing only
// that member; Delegations(string)/DelegationsFromBody below call it directly. The rest of
// HydraDelegationStream.swift is left for unit Core.Models.HydraDelegationStream to add to that file.

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
        $"[nth] You have run {PacingTools} tools without changing a file. If the task is research, reply with your findings now. Otherwise act on what you know: make the change, or reply with what blocks you.";

    // :199. DEVIATION OD-0012 F1.
    public static string WrapUpNote =>
        "[nth] Your budget is nearly spent. Finish now: complete the smallest correct version of the task, then reply with your report.";

    // :200. DEVIATION OD-0012 F1.
    public static string FinalNote =>
        "[nth] Your budget is spent and your tools are gone. Reply now with your report: what you changed, how far it got, and what is left.";
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
            HydraMergeStage.Gathering => "Gathering the team's files",        // :521 (ASCII apostrophe U+0027)
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

    // :571. PR-HYDRA-248
    public bool CanDelegate => Rounds < HydraPrompts.MaxDelegationRounds;

    // :577-581. Only a user-authored turn resets; a turn carrying head reports or a refusal notice
    // continues the same request and resets neither. PR-HYDRA-248
    public HydraDelegationBudget BeginTurn(bool userAuthored) =>
        userAuthored ? this with { Rounds = 0, Refusals = 0 } : this;

    // :584-586. PR-HYDRA-248
    public HydraDelegationBudget ChargeRound() => this with { Rounds = Rounds + 1 };

    // :591-598. PR-HYDRA-249
    public (HydraDelegationBudget Budget, bool Result) Refuse(string message)
    {
        if (Refusals >= MaxRefusals) return (NoteDropped(message), false);
        return (this with { Refusals = Refusals + 1 }, true);
    }

    // :602-604. PR-HYDRA-249
    public HydraDelegationBudget NoteDropped(string message) =>
        this with { DropNotice = HydraPrompts.DroppedBlockNotice(message) };

    // :607-610. `defer { dropNotice = nil }; return dropNotice` (R-VAL-1: return both the changed copy and
    // the value taken). PR-HYDRA-249
    public (HydraDelegationBudget Budget, string? Result) TakeDropNotice() =>
        (this with { DropNotice = null }, DropNotice);

    public const long MaxRefusals = 3; // :616
}

// :620-1438.
public static partial class HydraPrompts
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
            $"You have your own copy of the project at {path}: a git worktree nth made for you from the project's checkout as it was when you were sent out, uncommitted work included. Work in it directly, on the files as they are; your tools already run there. When you report, nth carries your changes into that checkout itself. So never commit, branch, stash, push, check out, reset, restore or clean anything, and never make or remove worktrees, whatever the project's own guidelines say about agents and worktrees: this copy already is yours. Do not use git to check your work either.",
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
        return $"Heads may work in any project. A head works in {own} unless it is sent to another: an entry of the delegation block names it with \"project\" (a sidebar project's name or path, or the absolute path of any git repository on this Mac, which nth then adds to the sidebar), and a head spawned with your own agent tool is told the project's folder in its prompt.{named} Whatever a head changes lands in the checkout of the project it worked in, and the team's work is merged per project, one merge request each. Your own tools run in this chat's checkout, and you may read and edit files in any project all the same: what you change elsewhere is merged along with that project's work. Run your own checks in the checkout itself: never make a worktree or a copy of the project for them.";
    }

    // :708 (B3). Swift `private`. DEVIATION OD-0012 F1 (x2): "Droppy Code handles branches" -> "Marrow handles
    // branches", and "if it says Droppy Code retries by itself" -> "if it says Marrow retries by itself". B3
    // adds two sentences: the "if it says ... retries by itself" clause and "A merge never waits on another
    // chat or session ...". The rest of the paragraph is unchanged.
    internal const string AutoMergeRule =
        "nth handles branches, commits and merge requests after all heads return and you finish. Never run or delegate those operations. For merge/status questions, use only the current [Hydra] note: if merged, give its link; if files remain, say they land when your answer ends; if it says nth retries by itself, say the work lands by itself and ask nothing; if merging failed or files are outside every project, give the stated reason and required user action. A merge never waits on another chat or session: never say it does, and never ask the user to merge. If nothing is pending, promise no merge. Never claim a merge without the note confirming it.";

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
            $"The user switched on Hydra for this chat: you lead a team of helper agents, called heads, that work in parallel in your checkout. Switching Hydra on is the user asking you to use them, so it overrides any standing rule that says not to spawn agents unless asked. {howToSpawn} The agents you spawn this way are the Hydra heads themselves: nth shows each one in the team's panel under a roster name and posts its outcome in the chat. There is no other launcher and no other kind of head, so never tell the user that Hydra heads cannot be started here; when the user asks for heads, spawn them this way.",
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
            "- Tell the user in one line which heads you sent out and what each one does. nth names the heads in roster order (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on): announce each head by its task and use exactly those names in that order, never invented ones. There is no head called Ives; the roster has Ivo.",
            "- After delegating, yield until reports arrive.",
            "- Heads go out through the tools above, never through a fenced hydra block: that is the delegation format for providers without agent tools of their own. If you end a reply with one anyway, nth still sends those heads out as threads of their own, but your turn ends there and their reports come back as a later message.",
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
        "You are a Hydra head in nth: one of several helpers working in parallel for a lead agent, in the lead's own checkout, where the lead and the other heads are changing other files at the same time. Do exactly the task you were given, and only that: do not widen it, do not touch files it does not name unless the task cannot be done otherwise, and never revert, reformat or clean up work that is not yours. Changes you did not make are expected in the checkout: leave them alone, never stash, check out, reset, restore or clean anything, and never move work into a branch or worktree, whatever the project's own guidelines say about agents and worktrees. Do not use git status or git diff to check your work: they show everyone's changes. Never run a build, a test, a lint or any other check of your work: the lead runs every check itself. Make the edits your brief names and report. If something blocks you, say so instead of guessing.\n\n" +
        HowToReport;

    // :800-805. Not named by any PR-HYDRA-0xx record in this part; see WorkerPrompt above. DEVIATION
    // OD-0012 F1: "in Droppy Code:" -> "in Marrow:"; the rest is unchanged.
    public static string ScoutPrompt =>
        "You are a Hydra head in nth: a read-only researcher working in parallel for a lead agent, in the lead's own checkout, where the lead and other heads are changing files at the same time; uncommitted changes there are theirs and expected. Answer exactly the question you were given, from the code and sources you can read. Change nothing.\n\n" +
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

    // :894-904. Codex's model test is only `!model.isEmpty`: there is no "default"/"auto" sentinel here, unlike
    // Claude (ClaudeAgents, :820) and Copilot (CopilotAgents, :853).
    public static ValueMap<string, JSONValue> CodexConfig(HydraLaunch launch)
    {
        var agents = new ValueDictionary();
        if (launch.MaxHeads is { } cap) agents["max_concurrent_threads_per_session"] = cap;
        if (launch.WorkerModel is { Length: > 0 } model) agents["default_subagent_model"] = model;
        if (launch.WorkerEffort is { Length: > 0 } effort) agents["default_subagent_reasoning_effort"] = effort;
        var features = new ValueDictionary { ["multi_agent"] = true };
        if (NativeRoutableProfiles(launch).Count != 0)
        {
            features["multi_agent_v2"] = new JSONValue.Object(new ValueMap<string, JSONValue>(
                new ValueDictionary { ["expose_spawn_agent_model_overrides"] = true }));
        }
        return new ValueMap<string, JSONValue>(new ValueDictionary
        {
            ["agents"] = new JSONValue.Object(new ValueMap<string, JSONValue>(agents)),
            ["features"] = new JSONValue.Object(new ValueMap<string, JSONValue>(features)),
        });
    }

    public const long MaxDelegationRounds = 3; // :912

    // :916-936. The Swift default `now: Date = .now` is a null default.
    public static string? TeamStatus(IReadOnlyList<HydraHeadInfo> heads, DateTimeOffset? now = null)
    {
        if (heads.Count == 0) return null;
        var current = now ?? SwiftDate.Now;
        var parts = heads.Select(info =>
        {
            var name = info.Persona.Name;
            switch (info.HydraHeadInfoStatus)
            {
                case HydraHeadInfo.Status.Running:
                    var minutes = SwiftNumber.TruncatingToInt(SwiftDate.TimeIntervalSince(current, info.StartedAt) / 60);
                    return minutes > 0 ? $"{name} (working for {minutes}m)" : $"{name} (working)";
                case HydraHeadInfo.Status.Completed:
                    if (info.Landing is { } landing)
                    {
                        if (landing.IsEmpty) return $"{name} (done, changed nothing)";
                        if (landing.Landed) return $"{name} (done, landed {(landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count} files")})";
                        return $"{name} (done, patch kept)";
                    }
                    return $"{name} (done)";
                case HydraHeadInfo.Status.Failed:
                    return $"{name} (failed)";
                case HydraHeadInfo.Status.Stopped:
                    return $"{name} (stopped)";
                default:
                    throw new UnreachableException();
            }
        });
        return "Your heads so far: " + string.Join(", ", parts) + ".";
    }

    // :941-969. The Swift defaults `projects: []` and `profiles: []` are null defaults. DEVIATION OD-0012 F1
    // (x2): "Droppy Code lands its changes" -> "Marrow lands its changes" (:943), and "is from Droppy Code, not
    // the user." -> "is from Marrow, not the user." (:967); every other byte, including the nine-space run
    // before "Finish your reply" and the embedded ```hydra example, is unchanged (PR-HYDRA-056).
    public static string FallbackPolicy(long? maxHeads, bool isolated, bool autoMerges = false, bool reviewsHeads = false,
        string? heads = null, IReadOnlyList<HydraProjectRef>? projects = null,
        IReadOnlyList<HydraHeadProfile>? profiles = null)
    {
        projects ??= [];
        profiles ??= [];
        // :942-944
        var whereHeadsWork = isolated
            ? "Each head works in a copy of its project of its own and nth lands its changes in that project's checkout when it reports"
            : "The heads work in your checkout";
        // :945
        var team = maxHeads is { } cap ? $"a team of up to {cap} helper agents" : "a team of helper agents";
        // :950-952
        var whoTheHeadsAre = heads is { } label
            ? $" Your heads run on {label}, a different model from yours: it is quick, so give each one a well-bounded task with everything it needs written down, and keep the design, the judgement calls and the integration for yourself. The block is the only way to send heads out: never use an agent, task or sub-agent tool of your own, which would run heads on your own model instead of the one the user chose."
            : "";
        // :955
        const string whatHeadsCanDo = " Every head, whatever its provider, can read files, edit files and run commands in its copy, with no permission prompts to wait on; brief it to do the work, not just to describe it.";
        // :959
        var profileRule = profiles.Count == 0
            ? ""
            : $" Some heads are tuned for a purpose: {string.Join(", ", profiles.Select(p => p.BriefLabel))}. Route a task to one with \"profile\", as in `{{\"task\": \"...\", \"prompt\": \"...\", \"profile\": \"{profiles[0].Name}\"}}`; a task that fits none of them goes without a profile and runs on the heads' shared model, as does one whose profile names none of these.";
        var reviewTail = reviewsHeads ? " " + ReviewRule : "";
        var autoMergeTail = autoMerges ? " " + AutoMergeRule : "";
        // :960-968
        return
            "[Hydra is on] You lead " + team + " (\"heads\"). Delegate first, work second: anything bigger than a single obvious change to a single file is a job for heads. Audits, reviews, a feature across several files, a refactor, \"check everything\", research across many files, several tasks in one message: in your first reply, look at the code only long enough to write good briefs, a minute and a handful of files rather than ten, and then send the heads out, all of them in that one block. Never spend minutes reading before you delegate, and never do inline what heads could be doing in parallel. Only a truly single-focus request, one file and one obvious change, is yours to do alone, and size the team to the work rather than to how many heads are free: a small job whose pieces depend on each other is one head's task, or your own, not a team of heads.         Finish your reply with one fenced block\n" +
            "\n" +
            "```hydra\n" +
            "[{\"task\": \"short title\", \"prompt\": \"complete, self-contained instructions with the exact files and acceptance criteria\"}]\n" +
            "```\n" +
            "\n" +
            "and stop there: do not wait, poll or verify anything after it. When a request is yours to do alone, do it and end with no block at all: an empty block sends no heads and is not needed. An entry may also carry its head's announced name, as in `{\"task\": \"...\", \"prompt\": \"...\", \"name\": \"Otto\"}`: the announced name is authoritative and the spawned head carries exactly it, so repeating the same block spawns the same names. An entry for work in another project carries `\"project\"` with that project's name or path, as in `{\"task\": \"...\", \"prompt\": \"...\", \"project\": \"gaze-site\"}`. Name new heads with the next roster names in order after the team listed above (Hank, Walter, Ada, Otto, Nova, Remy, Iris, Milo, Juno, Ezra, Lena, Bo, Kai, Vera, Finn, Mira, Odin, Suki, Rex, Zola, Pip, Ivo, Lux, Tova, Gus, then Hank 2 and so on), and omit the name when unsure: the next heads in order go out instead. Never invent names outside the roster: there is no head called Ives (the roster has Ivo), and an unknown name falls back to the next head in order rather than renaming anyone." +
            profileRule +
            " Inside a prompt never open a fenced code block of your own (three backticks would end the hydra block early and no head would go out): describe code in words, quote identifiers with single backticks, or indent a snippet by four spaces. Each entry goes out to a head the moment its closing brace streams, before the block is finished, so write the entries in the order the heads should start and complete one entry before beginning the next. " +
            whereHeadsWork +
            "; heads never see your context, so write every prompt for a capable colleague who has read nothing yet, with the exact files, symbols and acceptance criteria, give no two heads the same file, and give no head work that rests on another head's unfinished changes: they run in parallel and never see each other's work. Keep each brief bounded: one head, one task, one report you can act on. " +
            BriefRule + " " + VerificationRule + " " + ProjectRule(projects) +
            " A head can be sent to read and report as well as to change files, so the reading goes out in parallel too. Say in one line which heads you sent out and what each one does." +
            whoTheHeadsAre + whatHeadsCanDo +
            " The reports arrive as a later message with the work already in place: build on them, do not redo them, never send out heads to verify or redo other heads, and never use git status or git diff to check on heads, since the checkout changes under you while they work. A message that opens with [Hydra] is from nth, not the user." +
            reviewTail + autoMergeTail + " " + ReportStyleRule;
    }

    // :974-976. Forwards launch.headProfiles UNFILTERED (unlike Policy's NativeRoutableProfiles(launch)).
    public static string FallbackPolicy(HydraLaunch launch) =>
        FallbackPolicy(launch.MaxHeads, launch.IsolatesHeads, launch.AutoMerges, launch.ReviewsHeads,
            launch.HeadsLabel, launch.Projects, launch.HeadProfiles);

    // :979-1022. The Swift default `now: Date = .now` is a null default.
    public static string? MergeStatus(IReadOnlyList<HydraMergeRecord> merges, long unmergedFiles, DateTimeOffset? now = null)
    {
        // :980
        static string Files(long count) => count == 1 ? "1 file" : $"{count} files";
        // :981. `date.formatted(date: .omitted, time: .shortened)`.
        static string When(DateTimeOffset date) =>
            SwiftFormatters.Formatted(date, DateFormatStyle.Of(DateFormatStyle.DateStyle.Omitted, DateFormatStyle.TimeStyle.Shortened));
        // :982-986
        string Merged(HydraMergeRecord record)
        {
            var linkParts = new List<string>();
            if (record.Label is { } label) linkParts.Add(label);
            if (record.Url?.AbsoluteString is { } url) linkParts.Add(url);
            var link = string.Join(" ", linkParts);
            var scope = record.Project is { } project ? $" in {project}" : "";
            return $"the team's work already merged as {link}{scope} at {When(record.At)} ({Files(record.Files)}).";
        }
        // :987-990
        string PendingLine(string prefix)
        {
            var one = unmergedFiles == 1;
            return $"{prefix} {(one ? "is" : "are")} not merged yet; {(one ? "it goes" : "they go")} out by {(one ? "itself" : "themselves")} once you finish this answer with every head back.";
        }
        // :991-993
        var pending = unmergedFiles > 0
            ? PendingLine($"{Files(unmergedFiles)} changed since then")
            : "Nothing has changed since, so no merge is pending and none is coming.";
        // :994-997
        if (merges.Count == 0)
        {
            if (unmergedFiles <= 0) return null;
            return $"Merge state: {PendingLine($"{Files(unmergedFiles)} changed in this chat")}";
        }
        var last = merges[merges.Count - 1];
        // :998-1021 (B3; B2 :998-1017). The failed branch now splits on Retryable (PR-HYDRA-253), and held
        // handles legacy records only (PR-HYDRAMERGE-083).
        switch (last.HydraMergeRecordOutcome)
        {
            case HydraMergeRecord.Outcome.Merged: // :999-1000
                return $"Merge state: {Merged(last)} {pending}";
            case HydraMergeRecord.Outcome.Failed: // :1001-1005. NEW at B3: the retryable branch (:1002-1004, PR-HYDRA-253).
                if (last.Retryable == true)
                {
                    // DEVIATION OD-0012 F1: "Droppy Code retries it by itself" -> "Marrow retries it by itself".
                    return $"Merge state: the last try at {When(last.At)} did not land yet ({last.Detail ?? "no reason recorded"}). nth retries it by itself within a minute; nothing is needed from the user, so do not ask for anything or describe it as a problem. Do not claim a successful merge.";
                }
                return $"Merge state: the last merge failed at {When(last.At)}: {last.Detail ?? "no reason recorded"}. The work is still in the checkout and goes out by itself once that is settled; if the reason needs the user (a sign-in, conflict markers, a real conflict with the remote), say exactly that.";
            case HydraMergeRecord.Outcome.Held: // :1006-1011. Written only by earlier versions: a merge never waits on another chat now.
                if (unmergedFiles <= 0)
                    return "Merge state: nothing from this job is left to merge. Do not claim a new merge or promise another one.";
                return $"Merge state: {PendingLine($"{Files(unmergedFiles)} from this job")}";
            case HydraMergeRecord.Outcome.Settled: // :1012-1013
                return $"Merge state: at {When(last.At)}, Hydra found no recorded files left to merge. {pending} Do not describe this as a new merge.";
            case HydraMergeRecord.Outcome.Stray: // :1014-1020
                var parts = new List<string> { "Merge state:" };
                var landed = merges.LastOrDefault(m => m.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Merged);
                if (landed is not null) parts.Add(Merged(landed));
                parts.Add($"At {When(last.At)} {last.Detail ?? "files were changed outside every project"}");
                parts.Add(pending);
                return string.Join(" ", parts);
            default:
                throw new UnreachableException();
        }
    }

    // :1027-1039. DEVIATION OD-0012 F1: "so Droppy Code runs the selected heads" -> "so Marrow runs the selected
    // heads" in the fallback route; the native route is unchanged.
    public static string DelegationRequest(HydraLaunch launch)
    {
        var route = launch.RunsNatively
            ? "Use the configured Hydra agent tools and head profiles."
            : "Delegate through the final hydra block so nth runs the selected heads; do not use native agent tools.";
        return "[Hydra setting: enabled]\n" +
            $"Use Hydra heads for this request when it involves multiple files, an audit, broad research, or independent tasks. Keep a single obvious change in one file with the lead. {route} Follow any explicit request below to work alone or limit delegation.\n" +
            "\n---\n\n";
    }

    // :1042-1046
    public static string FallbackTurnNote(string? team, string? merge = null)
    {
        var parts = new[] { team, merge }.Where(p => p is not null).Cast<string>().ToList();
        return parts.Count == 0 ? "" : "[Hydra] " + string.Join(" ", parts) + "\n\n---\n\n";
    }

    // :1051-1056. Swift order is (team, merge = nil, canDelegate); the defaulted merge goes last here. `team`/
    // `merge` are tested for presence (not emptiness), so a present-but-empty string still contributes its
    // leading space; never collapse the resulting run of spaces.
    public static string FallbackReportNote(string? team, bool canDelegate, string? merge = null)
    {
        var more = canDelegate
            ? "If the reports open up the next stage of the work, or a head failed, or a piece of the user's request is still undone, send heads out again for exactly that, with the same ```hydra block at the end of your reply; never for verifying, redoing or finishing what a head already did."
            : "Send out no more heads for this request; whatever is left, do yourself.";
        return "[Hydra] Your heads reported back below." + (team is { } t ? " " + t : "") + (merge is { } m ? " " + m : "") + " " + more + "\n\n---\n\n";
    }

    // :1060-1062. fallbackTurnNote is evaluated twice, exactly as the Swift does (an inefficiency, not a
    // behaviour to fix).
    public static string FallbackPreamble(HydraLaunch launch, string? team, string? merge = null) =>
        FallbackPolicy(launch) + "\n\n" + (FallbackTurnNote(team, merge).Length == 0 ? "---\n\n" : FallbackTurnNote(team, merge));

    // :1064-1066. Swift order is (launch, team, merge = nil, canDelegate); the defaulted merge goes last here.
    public static string FallbackReportPreamble(HydraLaunch launch, string? team, bool canDelegate, string? merge = null) =>
        FallbackPolicy(launch) + "\n\n" + FallbackReportNote(team, canDelegate, merge);

    // :1070-1075
    public static string HeldBackMessage(long count) =>
        $"Hydra held back {(count == 1 ? "a head" : $"{count} heads")}.\n" +
        $"None of the heads you asked for went out: this request has had all {MaxDelegationRounds} of its rounds of heads already. Do the rest yourself now, without git status or git diff on the heads' work, and answer the user.";

    // :1079-1084. PR-HYDRA-251
    public static string InterruptedBlockMessage(bool stopped) =>
        "No heads went out.\n" +
        $"Your reply's delegation block was dropped: the turn {(stopped ? "was stopped" : "failed")} before the heads could go out, and none of that work was delegated. Do it yourself, or send the block again.";

    // :1090-1092. PR-HYDRA-250
    public static string DroppedBlockNotice(string message) => "[Hydra] " + message + "\n\n---\n\n";

    // :1096-1098. Three dialects, case-insensitive: hydra, hydra-sent, hydra-leaving. R-RE-1. PR-HYDRA-080
    [GeneratedRegex(@"```[ \t]*hydra(?:-sent|-leaving)?[ \t]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelegationOpenerPattern();

    private static (int Lower, int Upper)? DelegationOpener(string text)
    {
        var match = DelegationOpenerPattern().Match(text);
        return match.Success ? (match.Index, match.Index + match.Length) : null;
    }

    // :1103-1113. Every "```" that opens a line (text start, or immediately preceded by a Character.isNewline
    // character), from `start` on, in order. A fence with an info string after it still counts. PR-HYDRA-082
    private static List<(int Lower, int Upper)> LineStartFences(string text, int start)
    {
        var fences = new List<(int Lower, int Upper)>();
        var cursor = start;
        while (true)
        {
            var index = text.IndexOf("```", cursor, StringComparison.Ordinal);
            if (index < 0) break;
            if (index == 0 || FoundationText.IsNewline(text[index - 1]))
                fences.Add((index, index + 3));
            cursor = index + 3;
        }
        return fences;
    }

    // :1121-1125. The end is the LAST line-start fence after the opener, never the first, or the end of the
    // text when there is none. R-STR-10: a UTF-16 offset range. PR-HYDRA-083
    public static (int LowerBound, int UpperBound)? DelegationBlockRange(string @in)
    {
        if (DelegationOpener(@in) is not { } opener) return null;
        var fences = LineStartFences(@in, opener.Upper);
        var end = fences.Count > 0 ? fences[^1].Upper : @in.Length;
        return (opener.Lower, end);
    }

    // :1128. PR-HYDRA-089
    public static bool HasDelegationBlock(string @in) => DelegationBlockRange(@in) is not null;

    // :1133-1135. The Swift parameter is a Substring (a streaming tail); the same three-dialect regex.
    // PR-HYDRA-089
    public static bool HasDelegationOpener(string @in) => DelegationOpenerPattern().IsMatch(@in);

    // :1160-1166. A body that is not valid JSON returns nil. An explicitly empty array
    // returns an empty list (PR-HYDRA-098). A bare object is treated as a one-entry array.
    // An array/object whose entries all fail Delegation(fromEntry:) returns nil, not [],
    // so the caller (Delegations) keeps walking earlier fence candidates. PR-HYDRA-085
    private static ValueList<HydraDelegation>? DelegationsFromBody(string body)
    {
        if (JSONValue.Parse(body) is not { } json) return null;
        if (json.JSONValueArray is { Count: 0 }) return new ValueList<HydraDelegation>([]);
        var entries = json.JSONValueArray ?? (json.JSONValueObject is null ? [] : [json]);
        var parsed = entries.Select(Delegation).Where(d => d is not null).Select(d => d!).ToList();
        return parsed.Count == 0 ? null : new ValueList<HydraDelegation>(parsed);
    }

    // :1144-1153. Candidate ends are every line-start fence after the opener's body start,
    // walked LAST first, then the end of the text; the first candidate body that parses
    // wins. PR-HYDRA-084
    public static ValueList<HydraDelegation>? Delegations(string @in)
    {
        if (DelegationOpener(@in) is not { } opener) return null;
        var bodyStart = opener.Upper;
        var ends = LineStartFences(@in, bodyStart).AsEnumerable().Reverse().Select(f => f.Lower)
            .Append(@in.Length);
        foreach (var end in ends)
        {
            var body = @in[bodyStart..end];
            if (DelegationsFromBody(body) is { } result) return result;
        }
        return null;
    }

    // :1169-1173. `.trimmed` is `trimmingCharacters(in: .whitespacesAndNewlines)`, ported as StringExtensions.
    // Trimmed (Support/Text.cs). PR-HYDRA-088
    public static string WithoutDelegationBlock(string text)
    {
        var kept = text;
        if (DelegationBlockRange(text) is { } range)
            kept = kept.Remove(range.LowerBound, range.UpperBound - range.LowerBound);
        return kept.Trimmed();
    }

    public static TimeSpan DelegationSentHold { get; } = SwiftTask.Seconds(3.2);          // :1177
    public static TimeSpan DelegationLeaveFade { get; } = TimeSpan.FromMilliseconds(450); // :1181

    // :1185-1187. Matches hydra-sent OR hydra-leaving; the plain hydra opener does not match. `contains`, so
    // the match may sit anywhere in the text. PR-HYDRA-092
    [GeneratedRegex(@"```[ \t]*hydra-(?:sent|leaving)[ \t]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SentOrLeavingDelegationPattern();

    public static bool HasSentDelegationBlock(string @in) => SentOrLeavingDelegationPattern().IsMatch(@in);

    // :1190-1192. PR-HYDRA-092
    [GeneratedRegex(@"```[ \t]*hydra-leaving[ \t]*\r?\n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LeavingDelegationPattern();

    public static bool HasLeavingDelegationBlock(string @in) => LeavingDelegationPattern().IsMatch(@in);

    // :1197-1200. Only rewrites a sent, not-yet-leaving block; idempotent. PR-HYDRA-091
    public static string MarkingDelegationBlockLeaving(string text)
    {
        if (!HasSentDelegationBlock(text) || HasLeavingDelegationBlock(text) || DelegationOpener(text) is not { } opener)
            return text;
        return text[..opener.Lower] + "```hydra-leaving\n" + text[opener.Upper..];
    }

    // :1204-1207. Only rewrites when an opener exists and no sent-or-leaving block is already present;
    // idempotent. Always LF, never CRLF, even when the original opener ended with CRLF. PR-HYDRA-090
    public static string MarkingDelegationBlockSent(string text)
    {
        if (DelegationOpener(text) is not { } opener || HasSentDelegationBlock(text))
            return text;
        return text[..opener.Lower] + "```hydra-sent\n" + text[opener.Upper..];
    }

    // :1211-1217. The single call site (ThreadRuntime.swift:2888) always passes nil, so the reason branch is
    // ported for structural parity only; it is unreachable in shipped code.
    public static string UnreadableBlockMessage(string? reason)
    {
        var message = "[Hydra] Your delegation block could not be read, so no heads went out.";
        if (reason?.Trimmed() is { Length: > 0 } trimmedReason)
        {
            var lastCharacter = SwiftString.Suffix(trimmedReason, 1);
            var endsWithTerminalPunctuation = lastCharacter is "." or "!" or "?";
            message += " " + trimmedReason + (endsWithTerminalPunctuation ? "" : ".");
        }
        return message + " Write the block again at the end of your reply: one fenced block whose info string is hydra, holding a JSON array of objects with a task string and a prompt string. Inside a prompt never open a fenced code block of your own (no three backticks): describe code in words, quote identifiers with single backticks, or indent snippets. This does not count as a round of heads.";
    }

    // :1221-1230. All 34 markers, in Swift's order, including the trailing space in "tsc ".
    private static readonly string[] VerificationMarkers =
    [
        "xcodebuild", "swift build", "swift test", "swiftlint", "npm test", "npm run build",
        "npm run test", "yarn build", "yarn test", "pnpm test", "cargo build", "cargo test",
        "go test", "pytest", "gradle", "eslint", "tsc ", "build succeeded", "run the build",
        "run the tests", "run the test suite", "build and prove", "prove it builds",
        "prove it compiles", "check that it builds", "check it still builds",
        "make sure it builds", "make sure it compiles", "confirm the build",
        "verify the build", "verify that it builds", "make sure nothing else broke",
        "run the suite", "build verification",
    ];

    // :1234-1242. Matching is an unanchored, case-insensitive substring test, never word-bounded.
    public static string WithoutVerificationSteps(string prompt)
    {
        var lines = SwiftString.Split(prompt, "\n", omittingEmptySubsequences: false);
        var kept = lines.Where(line =>
        {
            var lowered = SwiftString.Lowercased(line);
            return !VerificationMarkers.Any(marker => lowered.Contains(marker, StringComparison.Ordinal));
        }).ToList();
        if (kept.Count == lines.Count) return prompt;
        return string.Join("\n", kept) + "\nMake the assigned edits and report; the lead handles all validation.";
    }

    // :1245-1261. DEVIATION OD-0012 F1: "a Hydra head in Droppy Code:" -> "a Hydra head in Marrow:"; the
    // sections and headings are unchanged.
    public static string DelegatedHeadPrompt(HydraPersona persona, HydraDelegation delegation, Workplace workplace) =>
        $"You are {persona.Name}, a Hydra head in nth: one of several helpers working in parallel for a lead agent. The lead delegated this task to you.\n" +
        "\n" +
        $"## Your task: {delegation.Task}\n" +
        $"{WithoutVerificationSteps(delegation.Prompt)}\n" +
        "\n" +
        "## Where you work\n" +
        $"{WorkplaceRules(workplace)}\n" +
        "\n" +
        "## How to work\n" +
        $"{HowToWork}\n" +
        "\n" +
        "## Your report\n" +
        $"{HowToReport}";

    // :1266-1270
    public sealed record ChatContext
    {
        public string? LastUserPrompt { get; init; }                                  // :1267
        public string? LastReply { get; init; }                                       // :1268
        public ValueList<string> TouchedPaths { get; init; } = ValueList<string>.Empty; // :1269
    }

    // :1275-1309. DEVIATION OD-0012 F1: "a Hydra head in Droppy Code:" -> "a Hydra head in Marrow:". The task is
    // interpolated raw: unlike DelegatedHeadPrompt, it is NOT passed through WithoutVerificationSteps.
    public static string QueuedHeadPrompt(HydraPersona persona, string task, ChatContext context, Workplace workplace,
        bool leadIsWorking = true)
    {
        var lines = new List<string>();
        // :1277-1279
        if (context.LastUserPrompt is { Length: > 0 } prompt)
            lines.Add($"- The user last asked the lead: {Quoted(prompt, 1_200)}");
        // :1280-1282
        if (context.LastReply is { Length: > 0 } reply)
            lines.Add($"- The lead's latest reply{(leadIsWorking ? " so far" : "")}: {Quoted(reply, 800)}");
        // :1283-1286
        if (leadIsWorking && context.TouchedPaths.Count > 0)
        {
            var paths = string.Join(", ", context.TouchedPaths.Take(20));
            lines.Add($"- Files the lead is changing in its current turn: {paths}. Leave these alone; if your task cannot be done without touching one, keep the change minimal and say so in your report.");
        }
        // :1287
        var contextBlock = lines.Count == 0 ? "The lead has not said anything yet." : string.Join("\n", lines);
        // :1288-1290
        var handoff = leadIsWorking
            ? "The user queued this task for you while the lead works on something else."
            : "The user sent this task straight to you; the lead is idle and will hear your report.";
        return
            $"You are {persona.Name}, a Hydra head in nth: a helper running in parallel with the lead agent. {handoff}\n" +
            "\n" +
            "## What is going on in the main chat\n" +
            $"{contextBlock}\n" +
            "\n" +
            "## Your task\n" +
            $"{task}\n" +
            "\n" +
            "## Where you work\n" +
            $"{WorkplaceRules(workplace)}\n" +
            "\n" +
            "## How to work\n" +
            $"{HowToWork}\n" +
            "\n" +
            "## Your report\n" +
            $"{HowToReport}";
    }

    // :1314-1397. The Swift default `stillWorking: []` is a null default.
    public static string ReportMessage(IReadOnlyList<HydraReport> reports, IReadOnlyList<string>? stillWorking = null,
        bool reviewsHeads = false)
    {
        stillWorking ??= [];

        // :1315-1319
        var names = reports.Select(r => HydraRoster.Persona(r.HeadIndex).Name).ToList();
        var opening = $"Hydra reports: {Listed(names)} finished.";
        if (stillWorking.Count > 0)
        {
            opening += $" {Listed(stillWorking)} {(stillWorking.Count == 1 ? "is" : "are")} still at work; {(stillWorking.Count == 1 ? "its report comes" : "their reports come")} as a later message.";
        }

        // :1321-1339
        var sections = new List<string>();
        foreach (var report in reports)
        {
            var persona = HydraRoster.Persona(report.HeadIndex);
            var outcome = report.Status switch
            {
                HydraHeadInfo.Status.Completed => "",
                HydraHeadInfo.Status.Failed => " (failed)",
                HydraHeadInfo.Status.Stopped => " (stopped by the user before finishing)",
                HydraHeadInfo.Status.Running => "",
                _ => throw new UnreachableException(),
            };
            var effort = new List<string>();
            if (report.Elapsed is { } elapsed && elapsed >= 1) effort.Add(Duration(elapsed));
            if (report.ToolCalls > 0) effort.Add(report.ToolCalls == 1 ? "1 tool" : $"{report.ToolCalls} tools");
            var took = effort.Count == 0 ? "" : " (" + string.Join(", ", effort) + ")";
            var lines = new List<string> { $"## {persona.Name}: {report.Task}{outcome}{took}" };
            if (LandingLine(report) is { } landingLine) lines.Add(landingLine);
            var body = report.Text.Trimmed();
            lines.Add(body.Length == 0 ? "No report." : body);
            sections.Add(string.Join("\n", lines));
        }

        // :1341-1388
        var closing = new List<string>();
        if (reports.Any(r => r.Landing?.Landed == true)) // :1342-1344
            closing.Add("The changes listed above are in your checkout already; nothing needs applying.");
        if (reports.Any(r => r.CopyPath is null && r.Status == HydraHeadInfo.Status.Completed)) // :1345-1347
            closing.Add("The heads worked in your checkout, so their changes are there already.");
        if (reports.Any(r => r.Landing is { } l && l.Conflicts.Count > 0)) // :1348-1350
            closing.Add("A file listed with conflicts was merged three-way and keeps conflict markers: resolve those first, and tell the user in one line that you settled them, so the landing card above does not read as broken.");

        // :1353-1361
        var owners = new Dictionary<string, List<string>>();
        foreach (var report in reports)
        foreach (var file in report.Landing?.Files ?? ValueList<HydraLanding.File>.Empty)
        {
            if (!owners.TryGetValue(file.Path, out var owned)) owners[file.Path] = owned = [];
            owned.Add(HydraRoster.Persona(report.HeadIndex).Name);
        }
        foreach (var pair in owners.OrderBy(kv => kv.Key, SwiftStringComparer.Instance))
            if (pair.Value.Count > 1)
                closing.Add($"{Listed(pair.Value)} both changed {pair.Key}: read it as it is now before you build on it.");

        // :1362-1365
        foreach (var report in reports)
        {
            if (report.Landing?.PatchPath is not { } path) continue;
            closing.Add($"{HydraRoster.Persona(report.HeadIndex).Name}'s changes would not apply on their own; the patch is at {path}. Apply it with `git apply --3way {path}` and settle what conflicts.");
        }

        if (reports.Any(r => r.Status == HydraHeadInfo.Status.Failed)) // :1366-1368
            closing.Add("A head that failed leaves its part to you: do it yourself or send it out again.");
        if (reports.Any(r => r.Status == HydraHeadInfo.Status.Stopped)) // :1369-1371
            closing.Add("A head the user stopped leaves its part alone unless the user asks for it.");
        if (reports.Any(r => r.Origin == HydraHeadInfo.Origin.Queued)) // :1372-1374
            closing.Add("The user queued that work for the heads while you were busy; take it into account and carry on with the main job.");
        if (reports.Any(r => r.Origin == HydraHeadInfo.Origin.Sent)) // :1375-1377
            closing.Add("The user sent that work straight to the heads instead of to you; take it into account, and reply to the user on it as if they had asked you.");

        // :1380-1384
        closing.Add(reviewsHeads
            ? "Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile or revert anything. " + ReviewRule
            : "Do not check any of this with git status or git diff: the checkout changes under you while heads work, and the user may be editing too. Do not reconcile, revert or redo anything. Build on the reports, read the files they name if something matters, run one verification if it matters, and finish the job.");
        closing.Add(ReportStyleRule); // :1385
        if (stillWorking.Count > 0) // :1386-1388
            closing.Add($"Do not wait for {Listed(stillWorking)} and do not take over {(stillWorking.Count == 1 ? "its" : "their")} tasks; tell the user {(stillWorking.Count == 1 ? "it is" : "they are")} still at work and that you will hear from {(stillWorking.Count == 1 ? "it" : "them")}.");

        // :1390-1396
        return opening + "\n\n" + string.Join("\n\n", sections) + "\n\n" + string.Join(" ", closing);
    }

    // :1401-1418. Nothing for a head that worked in the checkout itself: its changes are simply there.
    private static string? LandingLine(HydraReport report)
    {
        if (report.Landing is not { } landing)
        {
            if (report.CopyPath is not { } path) return null;
            return report.Status == HydraHeadInfo.Status.Completed ? "Changed nothing." : $"Nothing landed; its unfinished work is in its copy at {path}.";
        }
        if (landing.IsEmpty) return "Changed nothing.";
        var files = landing.Files.Take(25).Select(file =>
        {
            var line = $"{file.Path} (+{file.Additions} −{file.Deletions})"; // U+2212 MINUS SIGN
            if (landing.Conflicts.Contains(file.Path)) line += " with conflicts";
            return line;
        }).ToList();
        var listed = string.Join(", ", files) + (landing.Files.Count > 25 ? $", and {landing.Files.Count - 25} more" : "");
        var omitted = landing.DroppedBuildOutputFiles == 0
            ? ""
            : $"; {landing.DroppedBuildOutputFiles} build-output file{(landing.DroppedBuildOutputFiles == 1 ? " was" : "s were")} left out";
        // patchPath is tested BEFORE error, so a landing carrying both reports the patch form.
        if (landing.PatchPath is not null) return $"Did not land: {listed}{omitted}.";
        if (landing.Error is { } error) return $"Did not land ({error}): {listed}{omitted}.";
        return $"Landed in your checkout: {listed}{omitted}.";
    }

    // :1420-1423
    // NEW at B3 (PR-HYDRA-254; ED-197, F.ICUFormatters/SwiftFormatters.DurationUnits): B2's hand-built "Ns" /
    // "Mm Ss" is replaced by Duration.UnitsFormatStyle(allowed: [.seconds, .minutes], width: .narrow,
    // maximumUnitCount: 2, fractionalPart: .hide(rounded: .down)). No locale is set, so Locale.autoupdatingCurrent
    // applies (SwiftFormatters.CurrentLocale.Current, ED-197).
    private static readonly SwiftFormatters.DurationUnit[] DurationSecondsAndMinutes =
        [SwiftFormatters.DurationUnit.Seconds, SwiftFormatters.DurationUnit.Minutes];

    private static string Duration(double seconds)
    {
        var total = SwiftNumber.TruncatingToInt(seconds);
        return SwiftFormatters.DurationUnits(total, DurationSecondsAndMinutes, SwiftFormatters.CurrentLocale.Current);
    }

    // :1425-1431. No Oxford comma: two names are "A and B", three or more are "A, B and C".
    private static string Listed(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    // :1433-1437. U+201C LEFT DOUBLE QUOTATION MARK, U+201D RIGHT DOUBLE QUOTATION MARK, U+2026 HORIZONTAL
    // ELLIPSIS. Counts Characters (grapheme clusters), not UTF-16 units.
    private static string Quoted(string text, int limit)
    {
        var trimmed = text.Trimmed();
        var cut = SwiftString.Count(trimmed) > limit ? SwiftString.Prefix(trimmed, limit) + "…" : trimmed;
        return "“" + cut + "”";
    }
}
