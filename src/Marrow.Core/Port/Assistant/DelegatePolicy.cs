// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.3 "The delegate responder", 12.4 "What a tainted context can never do", 16 row AS-22; unit AS-22;
// ED-NEW-dextra-as22-4, -5). No Swift twin.
// The rules of what a delegate answers, pure: no I/O, no clock, no model. Three questions are put to the same table of words.
//   BeforeModel: may this question be asked of the delegate's model at all? Not when the engine says a tainted delegate answers nothing,
//     not when it asks for a secret, carries instructions, holds too much, or touches money, credentials, anything sent out, deleting,
//     installing or a promise (the owner always confirms those).
//   AfterModel: may the model's reading be used? Only when it flagged none of those topics, is sure enough (0.8), does not rest on taste
//     (a design pick is the owner's unless a recorded decision settles it, quoted and found in what nth gave the model), and every answer is
//     one of the choices (or short, secret-free words where the question takes its own), none of them, nor the detail of a choice picked, touching those topics again.
//   ForApproval: an approval is answered only when the engine allowed it by its own rule or a grant; a rule that asks goes to the queue.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>What the delegate does with a question: the answers it picked, or the reason it leaves the question to the owner.</summary>
public sealed record DelegateOutcome(ValueMap<string, ValueList<string>>? Answers, DelegateReason? Leaves, string Summary)
{
    public bool Answered => Answers is not null;

    public static DelegateOutcome Leave(DelegateReason reason) => new(null, reason, "");
}

/// <summary>What the delegate does with an approval.</summary>
public enum DelegateApprovalKind
{
    /// <summary>Answer yes: the engine allowed it alone.</summary>
    Answer,
    /// <summary>Leave it to the owner as a card in the queue.</summary>
    Ask,
    /// <summary>Do nothing: the engine denied it, so it is not a decision for the queue either; the chat keeps its own card.</summary>
    Skip,
}

public sealed record DelegateApprovalOutcome(DelegateApprovalKind Kind, DelegateReason? Reason, string Line);

public static class DelegatePolicy
{
    /// <summary>A model sure below this leaves the question to the owner (UNVERIFIED first guess, ED-NEW-dextra-as22-5).</summary>
    public const double MinConfidence = 0.8;

    /// <summary>The most questions one request may hold for the delegate to answer all of them.</summary>
    public const int MaxQuestions = 4;

    /// <summary>The most choices one question may offer.</summary>
    public const int MaxChoices = 12;

    /// <summary>The longest header plus prompt of one question.</summary>
    public const int MaxPromptChars = 2_000;

    /// <summary>The longest words the delegate gives a question that takes its own.</summary>
    public const int MaxAnswerChars = 300;

    /// <summary>The shortest quote that counts as evidence of a recorded decision.</summary>
    public const int MinEvidenceChars = 12;

    /// <summary>Why a question may not be asked of the model, or null when it may. <paramref name="mayAnswer"/> is the engine's verdict (PolicyEngine.DelegateAnswer).</summary>
    public static DelegateReason? BeforeModel(QuestionRequest request, Verdict mayAnswer)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (mayAnswer != Verdict.Allow) return DelegateReason.Tainted;
        if (request.Questions.Count is 0 or > MaxQuestions) return DelegateReason.TooMuch;
        if (request.Questions.Any(q => q.IsSecret)) return DelegateReason.Credentials;
        foreach (var question in request.Questions)
        {
            var text = question.Header + "\n" + question.Prompt;
            if (text.Length > MaxPromptChars || question.Choices.Count > MaxChoices) return DelegateReason.TooMuch;
            if (DelegateTopics.Find(text) is { } topic) return topic;
            // Words in the question that give orders are not a question: nothing follows them (12.4 "Instructions found in content").
            // A question about sending something reads like an order to send it, so the topics are read first and say what it is.
            var everything = text + "\n" + string.Join("\n", question.Choices.Select(c => c.Label + " " + c.Detail));
            if (InstructionScanner.Find(everything) is not null) return DelegateReason.Tainted;
        }
        return null;
    }

    /// <summary>
    /// What to do with the model's reading of <paramref name="request"/>. <paramref name="evidence"/> is the text nth gave the model that
    /// holds the owner's recorded decisions (the decisions, the past answers, the owner's own words in the chat), never the agent's words.
    /// </summary>
    public static DelegateOutcome AfterModel(QuestionRequest request, DelegateReply? reply, string evidence, Verdict mayAnswer)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (mayAnswer != Verdict.Allow) return DelegateOutcome.Leave(DelegateReason.Tainted);
        if (reply is null) return DelegateOutcome.Leave(DelegateReason.Unclear);
        if (reply.Flag is { } flagged) return DelegateOutcome.Leave(flagged);
        if (reply.Basis == DelegateBasis.Taste) return DelegateOutcome.Leave(DelegateReason.Taste);
        if (reply.Basis == DelegateBasis.Unsure || !(reply.Confidence >= MinConfidence)) return DelegateOutcome.Leave(DelegateReason.LowConfidence);
        var decided = reply.Basis == DelegateBasis.Decided && Verified(reply.Evidence, evidence);
        if (!decided && request.Questions.Any(q => DelegateTopics.LooksLikeTaste(q.Header + " " + q.Prompt)))
            return DelegateOutcome.Leave(DelegateReason.Taste);

        var picked = new Dictionary<string, ValueList<string>>(StringComparer.Ordinal);
        var said = new List<string>();
        foreach (var question in request.Questions)
        {
            if (!reply.Answers.TryGetValue(question.Id, out var given) || given.Count == 0) return DelegateOutcome.Leave(DelegateReason.Unclear);
            var values = new List<string>();
            foreach (var value in given)
            {
                var match = question.Choices.FirstOrDefault(c => Same(c.Label, value));
                if (match is not null)
                {
                    if (!values.Contains(match.Label, StringComparer.Ordinal)) values.Add(match.Label);
                    // A label is often all of "Option A"; what the choice will do is in its detail, so the detail of what it picked is read for the topics too.
                    if (DelegateTopics.Find(match.Detail) is { } detailTopic) return DelegateOutcome.Leave(detailTopic);
                    continue;
                }
                // Not a choice: only where the question takes its own words, and then they are short, hold no secret and touch no topic.
                if (question.Choices.Count > 0 && !question.AllowsOther) return DelegateOutcome.Leave(DelegateReason.Unclear);
                var words = value.Trim();
                if (words.Length == 0 || words.Length > MaxAnswerChars) return DelegateOutcome.Leave(DelegateReason.Unclear);
                if (FeedShield.Mask(words) != words) return DelegateOutcome.Leave(DelegateReason.Credentials);
                values.Add(words);
            }
            if (values.Count > 1 && !question.AllowsMultiple) return DelegateOutcome.Leave(DelegateReason.Unclear);
            // What it picked must not touch the topics either: a choice that says "delete it" is not made for the owner.
            foreach (var value in values)
                if (DelegateTopics.Find(value) is { } topic) return DelegateOutcome.Leave(topic);
            picked[question.Id] = new ValueList<string>(values);
            said.Add(string.Join(", ", values));
        }
        return new DelegateOutcome(new ValueMap<string, ValueList<string>>(picked), null, string.Join("; ", said));
    }

    /// <summary>The delegate's way with an approval of a chat: only what the engine allowed by its own rule or a grant is answered.</summary>
    public static DelegateApprovalOutcome ForApproval(ApprovalDecision decided, Verdict mayAnswer)
    {
        ArgumentNullException.ThrowIfNull(decided);
        var decision = decided.Decision;
        if (decision.Verdict == Verdict.Deny) return new DelegateApprovalOutcome(DelegateApprovalKind.Skip, DelegateReason.Rules, decision.Reason);
        if (mayAnswer != Verdict.Allow) return new DelegateApprovalOutcome(DelegateApprovalKind.Ask, DelegateReason.Tainted, decision.Reason);
        if (decision.Verdict == Verdict.Allow && decision.Rule is PolicyRule.Alone or PolicyRule.Grant)
            return new DelegateApprovalOutcome(DelegateApprovalKind.Answer, null, decision.Reason);
        return new DelegateApprovalOutcome(DelegateApprovalKind.Ask, DelegateReason.Rules, decision.Reason);
    }

    /// <summary>Whether <paramref name="quote"/> is a real stretch of <paramref name="evidence"/> (what the owner decided), read the same way: hidden characters out, look-alikes folded, case and spacing ignored.</summary>
    public static bool Verified(string? quote, string? evidence)
    {
        if (string.IsNullOrWhiteSpace(quote) || string.IsNullOrWhiteSpace(evidence)) return false;
        var wanted = InstructionScanner.Normalize(quote);
        if (wanted.Length < MinEvidenceChars) return false;
        return InstructionScanner.Normalize(evidence).Contains(wanted, StringComparison.Ordinal);
    }

    private static bool Same(string label, string value) =>
        string.Equals(label.Trim(), value.Trim(), StringComparison.OrdinalIgnoreCase);
}
