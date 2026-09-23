using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/ThreadContinuation.swift (B2 1fbf81e), :1-162.
// SKELETON (cycle c4). Every member with behaviour throws MARROW_STUB until its unit ports it.
// Not declared yet, file-private in Swift and used by no other file: :37-38 truncationMarker and
// :137-162 ContinuationUnit; the implementing unit adds them here (R-FILE-1).
// Codable conformance (synthesized) is left to the implementing unit.

// :4-27
public sealed record ThreadContinuation
{
    public Guid SourceThreadID { get; init; }            // :5
    public string SourceTitle { get; init; }             // :6
    public DateTimeOffset CreatedAt { get; init; }       // :7
    public string Context { get; init; }                 // :8
    public ValueList<Attachment> Attachments { get; init; } // :9
    public bool IsTruncated { get; init; }               // :10

    // :12-26. Swift order is (sourceThreadID, sourceTitle, createdAt = .now, context, attachments = [],
    // isTruncated = false); the defaulted createdAt goes after context here, and the Swift defaults
    // `.now` and `[]` are null defaults.
    public ThreadContinuation(Guid sourceThreadID, string sourceTitle, string context,
        DateTimeOffset? createdAt = null, ValueList<Attachment>? attachments = null, bool isTruncated = false)
    {
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/ThreadContinuation.swift:12");
    }

    public const long ContextBudget = 48_000;     // :32
    public const long FirstRequestLimit = 4_000;  // :35

    public static ThreadContinuation Capture(Guid sourceID, string title, ThreadDocument document) =>
        throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/ThreadContinuation.swift:40");

    public string Prompt => throw new NotImplementedException("MARROW_STUB DroppyCode/Core/Models/ThreadContinuation.swift:118");
}
