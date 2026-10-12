// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1, 12.2, 12.4, 12.7; unit AS-1; ED-NEW-dextracore-6). No Swift twin.
// PolicyEngine.Decide(action, context): pure C#, no model. Allow, Ask or Deny, always with the rule that applied. A
// command of several parts is decided part by part and the strictest verdict wins (Deny, then Ask, then Allow). Until
// AS-7 turns auto-answering on, the caller sends every mission approval to the queue whatever this returns.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

public enum Verdict { Allow, Ask, Deny }

/// <summary>Which rule decided a part, for the card's rule line and the audit log.</summary>
public enum PolicyRule
{
    /// <summary>The class runs alone.</summary>
    Alone,
    /// <summary>A live grant covered it.</summary>
    Grant,
    /// <summary>Hard deny: never allowed.</summary>
    HardDeny,
    /// <summary>Subscriptions, transfers and money to people are never allowed (12.7).</summary>
    MoneyNeverAllowed,
    /// <summary>Purchases are switched off (12.7, off by default).</summary>
    PurchasesOff,
    /// <summary>The owner chose Never for this class.</summary>
    OwnerNever,
    /// <summary>The classifier could not place it.</summary>
    Unknown,
    /// <summary>An irreversible class: always confirmed.</summary>
    AlwaysConfirmed,
    /// <summary>Permanent configuration from a tainted context: "needs an untainted request".</summary>
    NeedsUntaintedRequest,
    /// <summary>Permanent configuration: always a card.</summary>
    ConfigurationCard,
    /// <summary>An ask-first rule matched.</summary>
    AskFirst,
    /// <summary>The owner chose to be asked for this class.</summary>
    OwnerAsks,
    /// <summary>The context is tainted, so this lost its auto-allow (12.4).</summary>
    Tainted,
    /// <summary>A grant-possible class with no live grant that covers it.</summary>
    NeedsGrant,
    /// <summary>The duty running it does not cover it.</summary>
    OutsideDuty,
}

/// <summary>What a duty run may do: it can only narrow its assistant's grants and classes (12.1).</summary>
public sealed record DutyScope
{
    /// <summary>When set, only these classes may run without asking during the duty.</summary>
    public ValueList<ActionClass>? AllowedClasses { get; init; }

    /// <summary>When set, only these of the assistant's grants count during the duty. A duty never adds a grant.</summary>
    public ValueList<Guid>? GrantIds { get; init; }
}

/// <summary>Everything the engine reads about where an action runs. The caller passes the time.</summary>
public sealed record PolicyContext(ValueList<AssistantProfile> Chain, DateTimeOffset Now)
{
    /// <summary>The live grants of every assistant in the chain (revoked and spent ones are skipped by the engine).</summary>
    public ValueList<Grant> Grants { get; init; } = ValueList<Grant>.Empty;

    public Guid? MissionId { get; init; }

    /// <summary>The taint of the context the action comes from (12.4).</summary>
    public TaintContext TaintContext { get; init; } = TaintContext.Clean;

    public DutyScope? Duty { get; init; }

    /// <summary>"Let Dextra buy things" (12.7). Off by default.</summary>
    public bool PurchasesOn { get; init; }

    /// <summary>The owner's global ask-first rules, on top of each profile's.</summary>
    public ValueList<AskFirstRule> AskFirst { get; init; } = ValueList<AskFirstRule>.Empty;

    public bool Tainted => TaintContext.IsTainted;
}

/// <summary>A verdict with its reason. Parts holds each part's own decision for a command of several parts.</summary>
public sealed record PolicyDecision(Verdict Verdict, PolicyRule Rule, string Reason)
{
    /// <summary>The part this decided (the strictest part for a decision of several).</summary>
    public Classification? Leaf { get; init; }

    /// <summary>The grants that allowed it; a Once grant among them is spent once the action runs.</summary>
    public ValueList<Guid> GrantsUsed { get; init; } = ValueList<Guid>.Empty;

    /// <summary>A memory note written from a tainted context: kept, but quarantined and never used until the owner keeps it.</summary>
    public bool Quarantine { get; init; }

    /// <summary>A delegation from a tainted context: it passes the material as data only.</summary>
    public bool DataOnly { get; init; }

    /// <summary>A delegation from a tainted context: the child mission starts tainted.</summary>
    public bool ChildStartsTainted { get; init; }

    public ValueList<PolicyDecision> Parts { get; init; } = ValueList<PolicyDecision>.Empty;
}

/// <summary>The policy engine of 12.1.</summary>
public static class PolicyEngine
{
    public const string NeedsUntaintedRequest = "needs an untainted request";

    /// <summary>Decides a classified action: each part on its own, then the strictest verdict.</summary>
    public static PolicyDecision Decide(Classification action, PolicyContext context)
    {
        if (context.Chain.Count == 0) throw new ArgumentException("A policy context names the acting assistant.", nameof(context));
        var parts = action.Leaves.Select(leaf => DecideLeaf(leaf, context)).ToList();
        if (parts.Count == 1) return parts[0];
        var strictest = parts.MaxBy(p => (int)p.Verdict)!;
        return strictest with
        {
            GrantsUsed = strictest.Verdict == Verdict.Allow
                ? new ValueList<Guid>(parts.SelectMany(p => p.GrantsUsed).Distinct())
                : ValueList<Guid>.Empty,
            Quarantine = parts.Any(p => p.Quarantine),
            DataOnly = parts.Any(p => p.DataOnly),
            ChildStartsTainted = parts.Any(p => p.ChildStartsTainted),
            Parts = new ValueList<PolicyDecision>(parts),
        };
    }

    /// <summary>Decides one part. The order is the rule order of ED-NEW-dextracore-6.</summary>
    public static PolicyDecision DecideLeaf(Classification leaf, PolicyContext context)
    {
        PolicyDecision Make(Verdict verdict, PolicyRule rule, string reason) => new(verdict, rule, reason) { Leaf = leaf };
        var actionClass = leaf.Class;
        var rule = AssistantChain.EffectiveRule(context.Chain, actionClass);

        // 1. Never: hard deny, money that is not a purchase, purchases off, the owner's Never.
        if (actionClass == ActionClass.HardDeny) return Make(Verdict.Deny, PolicyRule.HardDeny, $"Never allowed: {leaf.Reason}");
        if (actionClass == ActionClass.Money)
        {
            if (leaf.Money != MoneyKind.Purchase)
                return Make(Verdict.Deny, PolicyRule.MoneyNeverAllowed, "Never allowed: subscriptions, transfers and money to people stay with you");
            if (!context.PurchasesOn) return Make(Verdict.Deny, PolicyRule.PurchasesOff, "Purchases are off");
        }
        if (rule == ClassDefault.Never) return Make(Verdict.Deny, PolicyRule.OwnerNever, $"You chose never for this: {leaf.Reason}");

        // 2. Permanent configuration and suggestions from a tainted context are refused, whatever the grants.
        if (leaf.Guard is AssistantToolGuard.PermanentConfiguration or AssistantToolGuard.Suggestion && context.Tainted)
            return Make(Verdict.Deny, PolicyRule.NeedsUntaintedRequest, NeedsUntaintedRequest);

        // What a tainted context may still do, with its limits attached to the verdict whatever it is.
        var quarantine = leaf.Guard == AssistantToolGuard.MemoryWrite && context.Tainted;
        var dataOnly = leaf.Guard == AssistantToolGuard.Delegation && context.Tainted;
        PolicyDecision With(PolicyDecision d) => d with { Quarantine = quarantine, DataOnly = dataOnly, ChildStartsTainted = dataOnly };

        // 3. Unknown asks, whatever the grants; irreversible classes are always confirmed.
        if (actionClass == ActionClass.Unknown) return With(Make(Verdict.Ask, PolicyRule.Unknown, $"Asks: not recognised: {leaf.Reason}"));
        if (ActionClasses.IsIrreversible(actionClass)) return With(Make(Verdict.Ask, PolicyRule.AlwaysConfirmed, ActionClasses.RuleText(actionClass)));
        if (leaf.Guard == AssistantToolGuard.PermanentConfiguration)
            return With(Make(Verdict.Ask, PolicyRule.ConfigurationCard, "Changes standing configuration: always a card"));

        // 4. Ask-first wins over allow.
        if (context.AskFirst.Concat(AssistantChain.AskFirstRules(context.Chain)).Any(r => r.Matches(leaf)))
            return With(Make(Verdict.Ask, PolicyRule.AskFirst, "You asked to be asked first"));

        // 5. The owner chose to be asked for a class that would run alone, or every time for a grant class.
        var defaultRule = ActionClasses.DefaultOf(actionClass);
        if (rule > defaultRule)
            return With(Make(Verdict.Ask, PolicyRule.OwnerAsks, $"You chose to be asked: {leaf.Reason}"));

        // 6. Taint takes away auto-allow for Run, Write elsewhere, Connector write, signing in and data-carrying calls.
        if (context.Tainted && (actionClass is ActionClass.Run or ActionClass.WriteElsewhere or ActionClass.ConnectorWrite or ActionClass.Login
                                || (actionClass == ActionClass.ReadWeb && leaf.CarriesData)))
            return With(Make(Verdict.Ask, PolicyRule.Tainted, "Asks: this agent read untrusted content"));

        // 7. Grant classes, and reads outside the access folders, run alone only under a live grant.
        var needsGrant = ActionClasses.IsGrantable(actionClass) || (actionClass == ActionClass.ReadLocal && leaf.OutsideAccess);
        if (needsGrant)
        {
            var grant = CoveringGrant(leaf, context);
            if (grant is null) return With(Make(Verdict.Ask, PolicyRule.NeedsGrant, ActionClasses.IsGrantable(actionClass)
                ? ActionClasses.RuleText(actionClass) : "Asks: reading outside access folders"));
            if (!InDuty(actionClass, context)) return With(Make(Verdict.Ask, PolicyRule.OutsideDuty, "Asks: outside this duty"));
            return With(Make(Verdict.Allow, PolicyRule.Grant, $"Allowed by a grant: {leaf.Reason}") with { GrantsUsed = new ValueList<Guid>([grant.Id]) });
        }

        // 8. A duty can only narrow.
        if (!InDuty(actionClass, context)) return With(Make(Verdict.Ask, PolicyRule.OutsideDuty, "Asks: outside this duty"));
        return With(Make(Verdict.Allow, PolicyRule.Alone, ActionClasses.RuleText(actionClass)));
    }

    /// <summary>Whether a tainted or untainted context may answer as the delegate responder (4.3): never when tainted.</summary>
    public static Verdict DelegateAnswer(PolicyContext context) => context.Tainted ? Verdict.Deny : Verdict.Allow;

    private static bool InDuty(ActionClass actionClass, PolicyContext context) =>
        context.Duty?.AllowedClasses is not { } allowed || allowed.Contains(actionClass);

    /// <summary>
    /// A live grant of the acting assistant that covers the part, inside the duty's grants, and within a live grant
    /// of every assistant above it (an Astur never holds a grant its parent lacks, 9.1).
    /// </summary>
    private static Grant? CoveringGrant(Classification leaf, PolicyContext context)
    {
        var acting = context.Chain[0].Id;
        var live = context.Grants.Where(g => g.IsLive(context.Now, context.MissionId) && GrantRules.Invalid(g) is null).ToList();
        foreach (var grant in live.Where(g => g.AssistantId == acting))
        {
            if (!grant.Covers(leaf.Class, leaf.GrantTarget, leaf.Field)) continue;
            if (context.Duty?.GrantIds is { } duty && !duty.Contains(grant.Id)) continue;
            var withinParents = context.Chain.Skip(1).All(parent =>
                live.Any(p => p.AssistantId == parent.Id && GrantRules.IsWithin(grant, p)));
            if (withinParents) return grant;
        }
        return null;
    }
}
