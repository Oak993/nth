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

// :17-51
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
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:26");
    }

    public string BriefLabel => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:32");

    public string AgentSlug => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:39");

    public bool Matches(string name) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:46");
}

// :58-121.
public sealed record HydraPair
{
    public static IReadOnlyList<long> MaxHeadsRange =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:74");
    public Guid Id { get; init; }                                  // :59
    public ProviderKind Provider { get; init; }                    // :60
    public string? OrchestratorModel { get; init; }                // :61
    public string? OrchestratorEffort { get; init; }               // :62
    public ProviderKind? WorkerProvider { get; init; }             // :63
    public string? WorkerModel { get; init; }                      // :64
    public string? WorkerEffort { get; init; }                     // :65
    public long? MaxHeads { get; init; }                           // :67
    public string? Name { get; init; }                             // :69
    public ValueList<HydraHeadProfile> HeadProfiles { get; init; } // :72

    // :76-80
    public HydraPair(ProviderKind provider)
    {
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:76");
    }

    public ProviderKind HeadsProvider => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:97");

    public bool SendsHeadsElsewhere => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:100");

    public string? CustomName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:103");

    public static long? ClampedCap(long? cap) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:109");

    public static HydraPair? ProjectPair(Guid? id, bool hydraOn, IReadOnlyList<HydraPair> @in) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:117");
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

    public bool HasRoom(long running) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:171");

    public HydraHeadProfile? Profile(string named) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:177");
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

    public static string PacingNote => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:198");
    public static string WrapUpNote => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:199");
    public static string FinalNote => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:200");
}

// :204-226. Color is a plain RGBA value at the Core boundary (IMPLEMENTING 2.1).
public sealed record HydraPersona
{
    public string Name { get; }   // :205
    public string Asset { get; }  // :208
    public uint Hex { get; }      // :210

    public (double Red, double Green, double Blue, double Alpha) Color =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:218");

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

    public static HydraPersona Persona(long at) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:259");

    public static long? Index(string named) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:273");

    public static HydraPersona? Persona(string named) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:284");
}

// :297-434
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

    public HydraPersona Persona => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:384");
    public string DisplayName => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:386");
    public bool IsFinished => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:387");
    public bool HasOwnCopy => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:389");

    // :391-396
    public HydraHeadInfo(long index, string task, Kind kind, Origin origin)
    {
        Index = index;
        Task = task;
        HydraHeadInfoKind = kind;
        HydraHeadInfoOrigin = origin;
    }
}

public static class HydraHeadInfoStatusExtensions
{
    extension(HydraHeadInfo.Status status)
    {
        public bool IsFinished => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Hydra.swift:319");
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

// :455-487
public sealed record HydraLanding
{
    // :456-460
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
