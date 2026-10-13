// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.1, 4.2, 12.6; unit AS-5; ED-NEW-dextra-as5-1). No Swift twin.
// A decision card: what lands in the queue, what it states (the exact inputs, why, who asks, which rule sent it), which
// answers it takes, and the binding that withdraws a card whose inputs changed. Pure: DecisionQueue (Marrow.App) holds
// the cards and acts on the answers.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>What a card is about (4.1).</summary>
public enum DecisionKind
{
    /// <summary>A provider approval from one of Dextra's agents (<c>ProviderEvent.Approval</c>).</summary>
    Approval,
    /// <summary>An nth-dextra tool call the policy asks about.</summary>
    ToolCall,
    /// <summary>A question from an agent (<c>ProviderEvent.Question</c> or <c>ask_owner</c>).</summary>
    Question,
    /// <summary>Merging a mission's branch into the owner's, or copying a snapshot's changes back (12.6a).</summary>
    Merge,
    /// <summary>Helpers over the depth limit (13.2).</summary>
    Limit,
    /// <summary>A task paused before it would touch a usage reserve (11.5): four options, the owner picks one. AS-13.</summary>
    Reserve,
    /// <summary>A task paused at its cap (13.2): the owner picks one of its options. AS-15.</summary>
    Cap,
    /// <summary>A duty paused after three failures in a row (13.2): try it again, or keep it paused. AS-15.</summary>
    Duty,
}

/// <summary>How a card is answered (4.2's table).</summary>
public enum DecisionWeight
{
    /// <summary>Allow once, For this mission, Always..., Deny.</summary>
    Reversible,
    /// <summary>Do it or Don't: an irreversible step, always confirmed, never covered by a grant.</summary>
    Irreversible,
    /// <summary>The proposed options, plus Discuss.</summary>
    Question,
}

public enum DecisionState { Open, Held, Answered, Withdrawn }

/// <summary>
/// How much a card needs the owner (13.1): NeedsYou is a decision only he can make, and it is the only kind that makes a toast; Offered is
/// a card he may raise himself (12.4: a chat that read untrusted content cannot ask for it), which waits in the queue and makes no sound.
/// </summary>
public enum DecisionPriority { NeedsYou, Offered }

/// <summary>What holds a card (4.2): Stop holds every card of its scope; Pause holds only irreversible ones.</summary>
public enum DecisionHold { Stop, Pause }

public enum DecisionAnswerKind { AllowOnce, ForThisMission, Always, Deny, DoIt, Dont, Pick, Discuss, Merge, RebaseFirst, Discard }

/// <summary>The owner's answer to one card.</summary>
public sealed record DecisionAnswer(DecisionAnswerKind Kind)
{
    /// <summary>Pick: the option chosen. Discuss: what the owner said. Deny or Don't: his reason, when he gave one.</summary>
    public string? Text { get; init; }

    /// <summary>Always: the structured grant he filled in from the card (12.2), never a one-tap "Allow always".</summary>
    public Grant? Grant { get; init; }

    public static DecisionAnswer AllowOnce { get; } = new(DecisionAnswerKind.AllowOnce);

    public static DecisionAnswer ForThisMission { get; } = new(DecisionAnswerKind.ForThisMission);

    public static DecisionAnswer DoIt { get; } = new(DecisionAnswerKind.DoIt);

    public static DecisionAnswer Merge { get; } = new(DecisionAnswerKind.Merge);

    public static DecisionAnswer RebaseFirst { get; } = new(DecisionAnswerKind.RebaseFirst);

    public static DecisionAnswer Discard { get; } = new(DecisionAnswerKind.Discard);

    public static DecisionAnswer Deny(string? reason = null) => new(DecisionAnswerKind.Deny) { Text = reason };

    public static DecisionAnswer Dont(string? reason = null) => new(DecisionAnswerKind.Dont) { Text = reason };

    public static DecisionAnswer Pick(string option) => new(DecisionAnswerKind.Pick) { Text = option };

    public static DecisionAnswer Discuss(string text) => new(DecisionAnswerKind.Discuss) { Text = text };

    public static DecisionAnswer Always(Grant grant) => new(DecisionAnswerKind.Always) { Grant = grant };

    /// <summary>Whether the answer lets the step go ahead (AllowOnce, ForThisMission, Always, DoIt, Pick, Merge).</summary>
    public bool Approves => Kind is DecisionAnswerKind.AllowOnce or DecisionAnswerKind.ForThisMission or DecisionAnswerKind.Always
        or DecisionAnswerKind.DoIt or DecisionAnswerKind.Merge;
}

/// <summary>One exact input of a card: the full command, a recipient, the message text, an amount (4.2).</summary>
public sealed record DecisionInput(string Name, string Value);

/// <summary>A decision card (4.2).</summary>
public sealed record DecisionItem(Guid Id, DecisionKind Kind, DecisionWeight Weight, string Title)
{
    /// <summary>What will happen, with its exact inputs.</summary>
    public ValueList<DecisionInput> Inputs { get; init; } = ValueList<DecisionInput>.Empty;

    /// <summary>Why it is being asked.</summary>
    public string Why { get; init; } = "";

    /// <summary>Which rule sent it to the owner ("Always confirmed: sending").</summary>
    public string Rule { get; init; } = "";

    /// <summary>Who asks: the assistant, its mission and the agent (by name, for the card).</summary>
    public string AssistantId { get; init; } = "";

    public Guid? MissionId { get; init; }

    public Guid? ThreadId { get; init; }

    public string? Agent { get; init; }

    /// <summary>The action's class, when it is an action.</summary>
    public ActionClass? Class { get; init; }

    /// <summary>What a grant would have to cover; null when no grant can cover it (Unknown, irreversible, work alone).</summary>
    public GrantTarget? GrantTarget { get; init; }

    /// <summary>The provider's request id, for an approval or a question.</summary>
    public string? RequestId { get; init; }

    /// <summary>A question's proposed answers.</summary>
    public ValueList<string> Options { get; init; } = ValueList<string>.Empty;

    public DecisionState State { get; init; } = DecisionState.Open;

    /// <summary>Whether the card needs the owner now (AS-9): only a card that does makes a toast.</summary>
    public DecisionPriority Priority { get; init; } = DecisionPriority.NeedsYou;

    public DecisionHold? HeldBy { get; init; }

    public DecisionAnswer? Answer { get; init; }

    /// <summary>
    /// The answer the owner gave while the task was paused (12.6b "Cards while paused"): kept, shown as "your answer runs
    /// on resume", and acted on at Resume. A card holding one is still open; answering again replaces it.
    /// </summary>
    public DecisionAnswer? Kept { get; init; }

    public DateTimeOffset Raised { get; init; }

    public DateTimeOffset? Settled { get; init; }

    /// <summary>A line the card adds below its rule: the provider cancelled the request, the inputs changed.</summary>
    public string? Note { get; init; }

    /// <summary>
    /// The personal data and secrets in the card's inputs, which the card highlights in place (12.4 "Privacy shield on
    /// the way out", AS-7): each names its input, where it starts and how long it is.
    /// </summary>
    public ValueList<PrivacySpan> Highlights { get; init; } = ValueList<PrivacySpan>.Empty;

    /// <summary>The card's binding to its inputs: an irreversible card whose inputs change is withdrawn and raised anew.</summary>
    public string Binding => DecisionRules.Bind(Kind, Title, Inputs);

    /// <summary>The answers the card offers, in the order they show.</summary>
    public ValueList<DecisionAnswerKind> Answers => DecisionRules.Answers(this);
}

/// <summary>The rules of 4.2: which answers a card takes, when one is refused, and what a hold covers.</summary>
public static class DecisionRules
{
    /// <summary>The weight of an action of this class: irreversible classes are always confirmed with Do it or Don't.</summary>
    public static DecisionWeight Weigh(ActionClass actionClass) =>
        ActionClasses.IsIrreversible(actionClass) || actionClass is ActionClass.Money or ActionClass.HardDeny
            ? DecisionWeight.Irreversible
            : DecisionWeight.Reversible;

    /// <summary>The answers of a card (4.2's table). Always... and For this mission need a class a grant can cover.</summary>
    public static ValueList<DecisionAnswerKind> Answers(DecisionItem item)
    {
        // A reserve's card is its four options and nothing else (11.5): a free-text answer would be an option nobody checked.
        // A cap's card and a paused duty's are the same (13.2): their options and nothing to type.
        if (item.Kind is DecisionKind.Reserve or DecisionKind.Cap or DecisionKind.Duty) return new([DecisionAnswerKind.Pick]);
        if (item.Kind == DecisionKind.Merge)
            return new([DecisionAnswerKind.Merge, DecisionAnswerKind.RebaseFirst, DecisionAnswerKind.Discard]);
        if (item.Kind == DecisionKind.Question || item.Weight == DecisionWeight.Question)
            return item.Options.Count > 0
                ? new([DecisionAnswerKind.Pick, DecisionAnswerKind.Discuss])
                : new([DecisionAnswerKind.Discuss]);
        if (item.Weight == DecisionWeight.Irreversible) return new([DecisionAnswerKind.DoIt, DecisionAnswerKind.Dont]);
        var answers = new List<DecisionAnswerKind> { DecisionAnswerKind.AllowOnce };
        if (Grantable(item))
        {
            if (item.MissionId is not null) answers.Add(DecisionAnswerKind.ForThisMission);
            answers.Add(DecisionAnswerKind.Always);
        }
        answers.Add(DecisionAnswerKind.Deny);
        return new(answers);
    }

    /// <summary>A grant can cover the card: a reversible action of a grant class with a target to cover.</summary>
    public static bool Grantable(DecisionItem item) =>
        item.Weight == DecisionWeight.Reversible && item.Class is { } c && item.GrantTarget is { } target && GrantRules.Takes(c, target.Kind);

    /// <summary>The buttons that keep the irreversible-button rule (no focus, no Enter, hold Space): Do it, Merge, Discard.</summary>
    public static bool IsIrreversibleButton(DecisionAnswerKind kind) =>
        kind is DecisionAnswerKind.DoIt or DecisionAnswerKind.Merge or DecisionAnswerKind.Discard;

    /// <summary>Whether a Pause holds the card (4.2, 12.6b: only irreversible cards; reversible ones stay answerable).</summary>
    public static bool HeldByPause(DecisionItem item) => item.Weight == DecisionWeight.Irreversible || item.Kind == DecisionKind.Merge;

    /// <summary>Null when the card takes the answer, else why not. A held, answered or withdrawn card takes none.</summary>
    public static string? Refusal(DecisionItem item, DecisionAnswer answer)
    {
        switch (item.State)
        {
            case DecisionState.Held: return item.HeldBy == DecisionHold.Pause ? "Held by Pause." : "Held by Stop.";
            case DecisionState.Answered: return "It is already answered.";
            case DecisionState.Withdrawn: return "It was withdrawn.";
        }
        if (!item.Answers.Contains(answer.Kind)) return "This card does not take that answer.";
        switch (answer.Kind)
        {
            case DecisionAnswerKind.Pick when answer.Text is not { } option || !item.Options.Contains(option):
                return "Pick one of the card's own answers.";
            case DecisionAnswerKind.Discuss when string.IsNullOrWhiteSpace(answer.Text):
                return "Say what to discuss.";
            case DecisionAnswerKind.ForThisMission when item.MissionId is null:
                return "There is no mission to grant it for.";
            case DecisionAnswerKind.Always:
                if (answer.Grant is not { } grant) return "Always\U00002026 needs a grant filled in from the card.";
                if (GrantRules.Invalid(grant) is { } invalid) return invalid;
                if (item.Class is not { } c || grant.Class != c) return "The grant is for another kind of action.";
                if (!string.Equals(grant.AssistantId, item.AssistantId, StringComparison.Ordinal)) return "The grant is for another assistant.";
                if (grant.Scope.Kind == GrantScopeKind.Once) return "A grant for once is Allow once.";
                if (item.GrantTarget is not { } target || !grant.Target.Covers(target)) return "The grant does not cover what the card asks.";
                break;
        }
        return null;
    }

    /// <summary>
    /// The binding of a card to its inputs: SHA-256 over the kind, the title and every input, each length-prefixed so no two
    /// different input lists read alike. Lowercase hex.
    /// </summary>
    public static string Bind(DecisionKind kind, string title, IEnumerable<DecisionInput> inputs)
    {
        var text = new StringBuilder();
        void Part(string value) => text.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append(';');
        Part(kind.ToString());
        Part(title);
        foreach (var input in inputs)
        {
            Part(input.Name);
            Part(input.Value);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
