// PORT-ONLY: Dextra (DESIGN.md rev 5, 4.1 "answered for him when the delegate responder is on for that chat", 4.3, 12.3 point 2; unit AS-22; ED-NEW-dextra-as22-4,
// -10). No Swift twin.
// The delegate's way with a provider approval of an owner's chat. The engine decides it exactly as it decides a mission's (classifier, the owner's rules,
// grants, taint, the outward privacy check). An Allow by the class's own rule or by a grant is answered yes: logged first, labelled in the chat, kept
// where the owner can Override it. An Ask becomes a card in the queue beside the chat's own (an irreversible class keeps the hold-to-act buttons); a Deny
// is nobody's decision to take and stays the chat's own card. A mission chat's approvals are the mission runner's and never come here.
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public sealed partial class DelegateResponder
{
    /// <summary>An approval of a chat. True when the delegate has it (answered, or a card raised); false when the chat keeps it as it always did.</summary>
    public bool OnApproval(Guid thread, ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Engaged(thread) is not { IsMission: false } engaged) return false;
        try
        {
            ApprovalVerdict verdict;
            try { verdict = o.DecideApproval(thread, request); }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                verdict = new ApprovalVerdict(new Classification(ActionClass.Unknown, "nth could not read its rules"),
                    new PolicyDecision(Verdict.Ask, PolicyRule.Unknown, "Asks: nth could not check its rules for this"));
            }
            var decided = new ApprovalDecision(verdict.Classification, verdict.Decision, verdict.Outward);
            var outcome = DelegatePolicy.ForApproval(decided, o.MayAnswer(thread));
            switch (outcome.Kind)
            {
                case DelegateApprovalKind.Answer:
                    return Allowed(engaged, request, verdict) || Ask(engaged, request, verdict, DelegateReason.NotLogged);
                case DelegateApprovalKind.Ask:
                    return Ask(engaged, request, verdict, outcome.Reason ?? DelegateReason.Rules);
                default:
                    Log(engaged, "delegate", verdict.Classification.Class, "left to the chat: " + verdict.Decision.Rule, [Plain("request", request.Title)], null, engaged.Title);
                    return false;
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Fault("Dextra could not check an approval for you, so it is left to you: " + error.Message);
            return false;
        }
    }

    // The engine allowed it by its own rule or a grant: logged first (refused when it cannot be), then answered yes and labelled.
    private bool Allowed(DelegateEngagement e, ApprovalRequest request, ApprovalVerdict verdict)
    {
        var decision = verdict.Decision;
        var id = Guid.NewGuid();
        var by = decision.GrantsUsed.Count > 0 ? string.Join(",", decision.GrantsUsed.Select(g => g.ToString("D"))) : "rule:" + decision.Rule;
        if (!Log(e, "delegate", verdict.Classification.Class, "allowed: " + decision.Rule, [Plain("request", request.Title), Plain("reasoning", decision.Reason)], by, request.Title))
            return false;
        o.Agents.Resolve(e.Thread, request, approve: true);
        if (decision.GrantsUsed.Count > 0) o.GrantsUsed?.Invoke(decision.GrantsUsed, "approval:" + request.Id);
        verdict.OnAllowed?.Invoke();
        o.Book.Add(new DelegateRecord(id, o.Time.GetUtcNow(), e.Thread, e.Title, DelegateKind.Approval, request.Headline + " " + request.Title, "Allowed", e.Who, decision.Reason)
        {
            Project = e.Chat.ProjectID,
            Confidence = 1,
        });
        o.Agents.Note(e.Thread, DelegateWords.ApprovalNote(e.Who, request.Title, decision.Reason));
        Told(e.Thread, Masked("allowed " + request.Title, DelegateWords.ShownChars), approval: true);
        return true;
    }

    // The engine asks (or the delegate may not answer here): a card in the queue, bound to the request, beside the chat's own.
    private bool Ask(DelegateEngagement e, ApprovalRequest request, ApprovalVerdict verdict, DelegateReason reason)
    {
        var c = verdict.Classification;
        var decision = verdict.Decision;
        Log(e, "delegate", c.Class, "left to the owner: " + reason + (reason == DelegateReason.Rules ? " (" + decision.Rule + ")" : ""), [Plain("request", request.Title)], null, e.Title);
        o.Agents.Note(e.Thread, DelegateWords.LeftNote(e.Who, reason, decision.Reason));
        lock (gate)
        {
            if (cards.Count >= MaxCards && !cards.ContainsKey((e.Thread, request.Id))) return false;   // the chat keeps its own card
            cards[(e.Thread, request.Id)] = request.Id;
        }
        var outward = verdict.Outward;
        var card = new DecisionItem(Guid.NewGuid(), DecisionKind.Approval, DecisionRules.Weigh(c.Class), request.Headline)
        {
            Inputs = new ValueList<DecisionInput>(MissionApprovals.Inputs(request)),
            Why = DelegateWords.Reason(reason, decision.Reason),
            Rule = reason == DelegateReason.Rules && decision.Reason.Length > 0 ? decision.Reason : DelegateWords.RuleLine(reason),
            AssistantId = o.AssistantId(),
            ThreadId = e.Thread,
            Agent = e.Title,
            Class = c.Class,
            RequestId = request.Id,
            Highlights = outward.Spans,
            Note = outward.HasPersonalData ? OutwardPrivacy.Summary(outward) + (outward.Refusal is { } refusal ? ". " + refusal : "") : null,
        };
        o.Queue.Raise(card, (item, answer) => o.Agents.Post(() => Approved(e, item, answer, request, verdict)),
            verdict.StillBlocked is { } blocked ? (_, answer) => answer.Approves ? blocked() : null : null);
        return true;
    }

    // The owner answered the approval's card in the queue: yes goes to the chat's agent once, logged first, unless the chat settled it before.
    private void Approved(DelegateEngagement e, DecisionItem card, DecisionAnswer answer, ApprovalRequest request, ApprovalVerdict verdict)
    {
        bool open;
        lock (gate) open = cards.Remove((e.Thread, request.Id));
        if (!open) return;
        var actionClass = card.Class ?? ActionClass.Unknown;
        if (!answer.Approves)
        {
            Log(e, "delegate", actionClass, "denied by the owner: " + answer.Kind, [Plain("request", request.Title)], "card:" + card.Id.ToString("D"), e.Title);
            o.Agents.Resolve(e.Thread, request, approve: false);
            return;
        }
        if (!Log(e, "delegate", actionClass, "allowed by the owner: " + answer.Kind, [Plain("request", request.Title)], "card:" + card.Id.ToString("D"), e.Title))
        {
            o.Agents.Resolve(e.Thread, request, approve: false);
            return;
        }
        o.Agents.Resolve(e.Thread, request, approve: true);
        verdict.OnAllowed?.Invoke();
    }
}
