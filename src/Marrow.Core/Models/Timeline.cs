using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Timeline.swift (B2 1fbf81e), :1-366.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// :202-207 FileEdit is already ported in Models/FileEdit.cs (pre-convention file, kept as is).
// Codable conformance (synthesized coding, :312-328 and :356-365 init(from:)) is left to the implementing unit.

// :4-33
public sealed record TimelineItem
{
    public string Id { get; init; }                           // :5
    public Guid? TurnID { get; init; }                        // :6
    public DateTimeOffset Date { get; init; }                 // :7
    public Content TimelineItemContent { get; init; } // :8 Swift: content (R-NAME-3)

    // :10-15. Swift order is (id, turnID, date, content); the defaulted id and date go last here.
    public TimelineItem(Guid? turnID, Content content, string? id = null, DateTimeOffset? date = null)
    {
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:10");
    }

    // :17-26
    public abstract record Content
    {
        private Content() { }

        public sealed record User(UserMessage Value) : Content;                          // :18
        public sealed record Assistant(AssistantMessage Value) : Content;                // :19
        public sealed record Reasoning(ReasoningBlock Value) : Content;                  // :20
        public sealed record Tool(ToolCall Value) : Content;                             // :21
        public sealed record Plan(ProposedPlan Value) : Content;                         // :22
        public sealed record Todos(ValueList<TodoStep> Value) : Content;                 // :23
        public sealed record Notice(global::Marrow.Core.Models.Notice Value) : Content;  // :24
        public sealed record TurnEnd(TurnSummary Value) : Content;                       // :25
    }

    public bool IsEmptyReasoning => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:29");

    public TimelineItem CleanedOfEmDashes => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:39");
}

// :58-70
public sealed record UserMessage
{
    public required string Text { get; init; }                                  // :59
    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty; // :60
    public ValueList<long>? HydraHeads { get; init; }                           // :63
    public bool? HydraBrief { get; init; }                                      // :66

    public bool IsFromHydra => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:68");
    public bool IsHydraBrief => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:69");
}

// :75-87
public sealed record FollowUpPrompt
{
    public Guid Id { get; init; } = Guid.NewGuid();                             // :76
    public required string Text { get; init; }                                  // :77
    public ValueList<Attachment> Attachments { get; init; } = ValueList<Attachment>.Empty; // :78
    public DateTimeOffset CreatedAt { get; init; } = SwiftDate.Now;             // :79
    public Guid? BundleID { get; init; } = null;                                // :82

    public bool IsEmpty => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:84");
}

// :89-138
public sealed record Attachment
{
    public Guid Id { get; init; } = Guid.NewGuid(); // :90
    public required string Name { get; init; }      // :91
    public required string Path { get; init; }      // :92
    public required string MimeType { get; init; }  // :93

    public SwiftURL Url => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:95");

    public bool IsImage => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:96");

    // :102-111 @concurrent static func base64Images(_:)
    public static Task<ValueList<(Attachment Image, string Base64)>> Base64Images(IReadOnlyList<Attachment> attachments) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:103");
}

// :140-143
public sealed record AssistantMessage
{
    public required string Text { get; init; }       // :141
    public bool IsStreaming { get; init; } = false;  // :142
}

// :145-148
public sealed record ReasoningBlock
{
    public required string Text { get; init; }       // :146
    public bool IsStreaming { get; init; } = false;  // :147
}

// :150-200
public sealed record ToolCall
{
    // :151-160
    public enum Kind
    {
        [RawValue("command")] Command,
        [RawValue("read")] Read,
        [RawValue("edit")] Edit,
        [RawValue("search")] Search,
        [RawValue("web")] Web,
        [RawValue("mcp")] Mcp,
        [RawValue("agent")] Agent,
        [RawValue("other")] Other,
    }

    // :162-167
    public enum Status
    {
        [RawValue("running")] Running,
        [RawValue("completed")] Completed,
        [RawValue("failed")] Failed,
        [RawValue("declined")] Declined,
    }

    public const long OutputLimit = 60_000; // :169

    public required Kind ToolCallKind { get; init; }                           // :171 Swift: kind (R-NAME-3)
    public required string Title { get; init; }                                // :172
    public string? Detail { get; init; }                                       // :173
    public string Output { get; init; } = "";                                  // :174
    public Status ToolCallStatus { get; init; } = Status.Running;              // :175 Swift: status (R-NAME-3)
    public long? ExitCode { get; init; }                                       // :176
    public ValueList<FileEdit> Edits { get; init; } = ValueList<FileEdit>.Empty; // :177
    public DateTimeOffset StartedAt { get; init; } = SwiftDate.Now;            // :178
    public DateTimeOffset? FinishedAt { get; init; }                           // :179

    // The mutating methods return the changed copy (R-VAL-1).
    public ToolCall AppendOutput(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:181");

    public ToolCall SetOutput(string text) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:186");

    public ToolCall Finish(Status status) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:191");
}

// :209-219
public sealed record ProposedPlan
{
    // :210-215
    public enum State
    {
        [RawValue("drafting")] Drafting,
        [RawValue("proposed")] Proposed,
        [RawValue("accepted")] Accepted,
        [RawValue("dismissed")] Dismissed,
    }

    public required string Markdown { get; init; }            // :217
    public required State ProposedPlanState { get; init; }    // :218 Swift: state (R-NAME-3)
}

// :221-230
public sealed record TodoStep
{
    // :222-226
    public enum Status
    {
        [RawValue("pending")] Pending,
        [RawValue("active")] Active,
        [RawValue("done")] Done,
    }

    public required string Text { get; init; }             // :228
    public required Status TodoStepStatus { get; init; }    // :229 Swift: status (R-NAME-3)
}

// :232-244
public sealed record Notice
{
    // :233-237
    public enum Level
    {
        [RawValue("info")] Info,
        [RawValue("warning")] Warning,
        [RawValue("error")] Error,
    }

    public required Level NoticeLevel { get; init; }   // :239 Swift: level (R-NAME-3)
    public required string Message { get; init; }      // :240
    public LimitNotice? Limit { get; init; } = null;   // :243
}

// :248-250
public sealed record LimitNotice
{
    public required Guid ThreadID { get; init; } // :249
}

// :252-257
public enum TurnStatus
{
    [RawValue("running")] Running,
    [RawValue("completed")] Completed,
    [RawValue("interrupted")] Interrupted,
    [RawValue("failed")] Failed,
}

// :259-267.
public sealed record TurnSummary
{
    public required Guid TurnID { get; init; }         // :260
    public required TurnStatus Status { get; init; }   // :261
    public required double Duration { get; init; }     // :262
    public required long FilesChanged { get; init; }   // :263
    public required long Additions { get; init; }      // :264
    public required long Deletions { get; init; }      // :265
    public FileChangeSummary? Changes { get; init; }    // :266
}

// :270-281.
public sealed record HydraMergeRecord
{
    // :273
    public enum Outcome
    {
        [RawValue("merged")] Merged,
        [RawValue("failed")] Failed,
        [RawValue("stray")] Stray,
        [RawValue("held")] Held,
        [RawValue("settled")] Settled,
    }

    public required DateTimeOffset At { get; init; }                  // :274
    public required Outcome HydraMergeRecordOutcome { get; init; }    // :275 Swift: outcome (R-NAME-3)
    public string? Project { get; init; }                             // :276
    public string? Label { get; init; }                               // :277
    public SwiftURL? Url { get; init; }                                // :278
    public required long Files { get; init; }                         // :279
    public string? Detail { get; init; }                              // :280
}

// :283-307
public sealed record TurnRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();                     // :284
    public required long Index { get; init; }                           // :285
    public string? ProviderTurnID { get; init; }                        // :286
    public DateTimeOffset StartedAt { get; init; } = SwiftDate.Now;     // :287
    public DateTimeOffset? CompletedAt { get; init; }                   // :288
    public TurnStatus Status { get; init; } = TurnStatus.Running;       // :289
    public string? BaseCheckpoint { get; init; }                        // :290
    public string? EndCheckpoint { get; init; }                         // :291
    public string? UserItemID { get; init; }                            // :292
    public string? ProviderDiff { get; init; }                          // :294
    public string? ProviderAnchor { get; init; }                        // :296
    public ValueList<string>? TouchedPaths { get; init; }               // :299
    public ValueList<TokenSpend>? TokenSpends { get; init; }            // :303
    public bool HydraMerged { get; init; } = false;                     // :306
}

// :331-339
public sealed record ContextUsage
{
    public required long UsedTokens { get; init; }  // :332
    public long? WindowTokens { get; init; }        // :333

    public double? Fraction => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/Timeline.swift:335");
}

// :341-366
public sealed record ThreadDocument
{
    public Guid ThreadID { get; init; }                                                        // :342
    public ValueList<TimelineItem> Items { get; init; } = ValueList<TimelineItem>.Empty;       // :343
    public ValueList<TurnRecord> Turns { get; init; } = ValueList<TurnRecord>.Empty;           // :344
    public ContextUsage? Usage { get; init; }                                                  // :345
    public ValueList<FollowUpPrompt> FollowUps { get; init; } = ValueList<FollowUpPrompt>.Empty; // :346
    public ValueList<HydraMergeRecord> HydraMerges { get; init; } = ValueList<HydraMergeRecord>.Empty; // :347
    public ThreadContinuation? Continuation { get; init; } = null;                             // :350

    // :352-354
    public ThreadDocument(Guid threadID)
    {
        ThreadID = threadID;
    }
}
