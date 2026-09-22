namespace Marrow.Core.Models;

// DroppyCode/Core/Models/Requests.swift:4-53.
public sealed class ApprovalRequest
{
    public enum Kind { Command, FileChange, Tool, Permissions, Plan }

    public sealed record Option(string Id, string Title, Option.Role OptionRole)
    {
        public enum Role { Approve, ApproveAlways, Decline, Cancel }
    }

    public ApprovalRequest(string id, Kind kind, string title, string? detail,
        string? reason, IEnumerable<Option> options, string? toolItemId)
    {
        Id = id; RequestKind = kind; Title = title; Detail = detail;
        Reason = reason; Options = Array.AsReadOnly(options.ToArray()); ToolItemId = toolItemId;
    }

    public string Id { get; }
    public Kind RequestKind { get; }
    public string Title { get; }
    public string? Detail { get; }
    public string? Reason { get; }
    public IReadOnlyList<Option> Options { get; }
    public string? ToolItemId { get; }

    public string Headline => RequestKind switch
    {
        Kind.Command => "Run this command?",
        Kind.FileChange => "Apply these changes?",
        Kind.Tool => "Allow this action?",
        Kind.Permissions => "Grant extra access?",
        Kind.Plan => "Ready to build this plan?",
        _ => throw new ArgumentOutOfRangeException(nameof(RequestKind))
    };

    // Semantic identifiers only; licensed Windows glyphs await UNK-0007.
    public string Symbol => RequestKind switch
    {
        Kind.Command => "terminal",
        Kind.FileChange => "doc.badge.plus",
        Kind.Tool => "wrench.and.screwdriver",
        Kind.Permissions => "lock.shield",
        Kind.Plan => "checklist",
        _ => throw new ArgumentOutOfRangeException(nameof(RequestKind))
    };
}

// DroppyCode/Core/Models/Requests.swift:56-74.
public sealed class QuestionRequest
{
    public sealed record Choice(string Label, string? Detail);

    public sealed class Question : IEquatable<Question>
    {
        public Question(string id, string header, string prompt, IEnumerable<Choice> choices,
            bool allowsMultiple, bool allowsOther, bool isSecret)
        {
            Id = id; Header = header; Prompt = prompt;
            Choices = Array.AsReadOnly(choices.ToArray());
            AllowsMultiple = allowsMultiple; AllowsOther = allowsOther; IsSecret = isSecret;
        }

        public string Id { get; }
        public string Header { get; }
        public string Prompt { get; }
        public IReadOnlyList<Choice> Choices { get; }
        public bool AllowsMultiple { get; }
        public bool AllowsOther { get; }
        public bool IsSecret { get; }

        public bool Equals(Question? other) => other is not null
            && Id == other.Id && Header == other.Header && Prompt == other.Prompt
            && AllowsMultiple == other.AllowsMultiple && AllowsOther == other.AllowsOther
            && IsSecret == other.IsSecret && Choices.SequenceEqual(other.Choices);

        public override bool Equals(object? obj) => obj is Question question && Equals(question);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Id); hash.Add(Header); hash.Add(Prompt);
            foreach (var choice in Choices) hash.Add(choice);
            hash.Add(AllowsMultiple); hash.Add(AllowsOther); hash.Add(IsSecret);
            return hash.ToHashCode();
        }
    }

    public QuestionRequest(string id, IEnumerable<Question> questions)
    {
        Id = id; Questions = Array.AsReadOnly(questions.ToArray());
    }

    public string Id { get; }
    public IReadOnlyList<Question> Questions { get; }
}
