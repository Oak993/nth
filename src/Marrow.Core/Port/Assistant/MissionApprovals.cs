// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a, 12.3 point 2, 12.4; unit AS-7; ED-NEW-dextra-as7-6). No Swift twin.
// One decision for one provider approval of a Dextra agent, the same in the controller and in the injection self-check:
// the classifier places the request in the agent's own folder, the engine decides it with the agent's taint and role,
// and the outward privacy check turns an Allow that would send personal data to an unlisted recipient, or reach a third
// party the owner has not confirmed before, into Ask. Pure.
using Marrow.Core.Models;
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>A decided approval: its class, the decision, and the outward check of its inputs (None when it sends nothing).</summary>
public sealed record ApprovalDecision(Classification Classification, PolicyDecision Decision, OutwardCheck Outward);

public static class MissionApprovals
{
    /// <summary>
    /// Decides one approval. <paramref name="decide"/> is the engine (PolicyEngine.Decide unless a test swaps it); a
    /// policy that throws is the caller's to handle (it asks). <paramref name="confirmedRecipients"/> are the third
    /// parties the owner already confirmed a step to (none: every recipient off his list is new).
    /// </summary>
    public static ApprovalDecision Decide(ApprovalRequest request, ClassifierContext classifier, PolicyContext policy, ProviderKind? provider,
        IEnumerable<string> personalDataRecipients, Func<Classification, PolicyContext, PolicyDecision>? decide = null,
        IEnumerable<string>? confirmedRecipients = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var classification = ActionClassifier.ClassifyApproval(request, classifier, provider: provider);
        var decision = (decide ?? PolicyEngine.Decide)(classification, policy);
        var outward = classification.Leaves.Any(l => OutwardPrivacy.Applies(l.Class))
            ? OutwardPrivacy.Check(Outgoing(request, provider), personalDataRecipients)
            : OutwardCheck.None;
        return new ApprovalDecision(classification, OutwardPrivacy.Apply(decision, outward, confirmedRecipients), outward);
    }

    /// <summary>
    /// What of an approval leaves the PC: its command or request, and its detail (a tool's input, a command's own folder).
    /// Not the agent's reason, and not Claude's description of a command, which are the agent's words to the owner.
    /// </summary>
    public static IReadOnlyList<DecisionInput> Outgoing(ApprovalRequest request, ProviderKind? provider) =>
        [.. Inputs(request).Where(i => i.Name != Reason && !(i.Name == "Detail" && provider == ProviderKind.Claude && request.RequestKind == ApprovalRequest.Kind.Command))];

    public const string Reason = "The agent's reason";

    /// <summary>The exact inputs of an approval as its card shows them (4.2): the command or request, its detail, the agent's reason.</summary>
    public static IReadOnlyList<DecisionInput> Inputs(ApprovalRequest request)
    {
        var inputs = new List<DecisionInput> { new(request.RequestKind == ApprovalRequest.Kind.Command ? "Command" : "Request", request.Title) };
        if (!string.IsNullOrWhiteSpace(request.Detail)) inputs.Add(new("Detail", request.Detail!));
        if (!string.IsNullOrWhiteSpace(request.Reason)) inputs.Add(new(Reason, request.Reason!));
        return inputs;
    }
}
