using Marrow.Core.Port;

namespace Marrow.Core.Models;

// Source: DroppyCode/Core/Models/Requests.swift (B2 1fbf81e), :1-53.
/// <summary>A provider asking for permission before it acts.</summary>
public sealed record ApprovalRequest(string Id, ApprovalRequest.Kind RequestKind, string Title,
    ValueList<ApprovalRequest.Option> Options, string? Detail = null, string? Reason = null,
    string? ToolItemID = null)
{
    // :5-11
    public enum Kind { Command, FileChange, Tool, Permissions, Plan }

    // :13-24
    public sealed record Option(string Id, string Title, Option.Role OptionRole)
    {
        // :14-19
        public enum Role { Approve, ApproveAlways, Decline, Cancel }
    }

    // :34-42
    public string Headline => RequestKind switch
    {
        Kind.Command => "Run this command?",
        Kind.FileChange => "Apply these changes?",
        Kind.Tool => "Allow this action?",
        Kind.Permissions => "Grant extra access?",
        Kind.Plan => "Ready to build this plan?",
    };

    // :44-52
    // DEVIATION OD-0010: SF Symbol names cannot ship on Windows. The five-way mapping is kept verbatim;
    // porting/icons/sf-symbols-to-lucide.json pairs each identifier with its shipped Lucide glyph.
    public string Symbol => RequestKind switch
    {
        Kind.Command => "terminal",
        Kind.FileChange => "doc.badge.plus",
        Kind.Tool => "wrench.and.screwdriver",
        Kind.Permissions => "lock.shield",
        Kind.Plan => "checklist",
    };
}

// Source: DroppyCode/Core/Models/Requests.swift (B2 1fbf81e), :55-74.
/// <summary>A provider asking the user to choose or type an answer.</summary>
public sealed record QuestionRequest(string Id, ValueList<QuestionRequest.Question> Questions)
{
    // :56-65
    public sealed record Question(string Id, string Header, string Prompt, ValueList<Choice> Choices,
        bool AllowsMultiple, bool AllowsOther, bool IsSecret);

    // :67-70
    public sealed record Choice(string Label, string? Detail = null);
}
