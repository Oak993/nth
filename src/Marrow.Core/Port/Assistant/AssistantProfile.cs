// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1, 12.1; unit AS-1; ED-NEW-dextracore-3). No Swift twin.
// An assistant's profile: Dextra itself (no parent) or one of its Asturs. The fields here are the ones the policy
// engine reads (identity, parent, the owner's per-class choices, ask-first rules, the allowed MCP servers); the home
// thread, memory scope, browser profile, budget and voice are added by the units that own them (AS-12, AS-13, AS-16).
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>The six Astur node shapes of Armilla (2.5). Dextra itself is the ring and has none.</summary>
public enum AsturShape { Squircle, Hexagon, Diamond, RoundedTriangle, Clover, Star }

/// <summary>
/// An owner's "ask me first" rule: a class, a target, a field, or any mix. A part that matches every given piece asks,
/// even when a grant or the class's default would allow it ("ask-first wins over allow", 12.1).
/// </summary>
public sealed record AskFirstRule(ActionClass? Class = null, GrantTarget? Target = null, string? Field = null)
{
    public bool Matches(Classification leaf)
    {
        if (Class is null && Target is null && Field is null) return false;
        if (Class is { } c && leaf.Class != c) return false;
        if (Target is not null && (leaf.GrantTarget is null || !Target.Covers(leaf.GrantTarget))) return false;
        if (Field is not null && !string.Equals(Field, leaf.Field, StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }
}

/// <summary>An assistant's profile (9.1), as far as the policy engine reads it.</summary>
public sealed record AssistantProfile(string Id, string Name)
{
    /// <summary>Other names it answers to (for voice and "@" routing).</summary>
    public ValueList<string> Aliases { get; init; } = ValueList<string>.Empty;

    /// <summary>The parent assistant's id; null for Dextra itself.</summary>
    public string? ParentId { get; init; }

    /// <summary>The node shape of an Astur; null for Dextra.</summary>
    public AsturShape? Shape { get; init; }

    /// <summary>What it owns, in the owner's words.</summary>
    public string Area { get; init; } = "";

    /// <summary>
    /// The owner's choice for a class, where it differs from 12.1's default. A choice can only make a class
    /// stricter: one looser than the default (or than a parent's) is not applied, because a grant is the only way to
    /// let an asking class run alone.
    /// </summary>
    public ValueMap<ActionClass, ClassDefault> ClassRules { get; init; } = ValueMap<ActionClass, ClassDefault>.Empty;

    /// <summary>The owner's ask-first rules for this assistant.</summary>
    public ValueList<AskFirstRule> AskFirst { get; init; } = ValueList<AskFirstRule>.Empty;

    /// <summary>The MCP servers its missions may use.</summary>
    public ValueList<string> Connections { get; init; } = ValueList<string>.Empty;

    // ---- AS-16: the rest of 9.1 (what an Astur owns and what it may spend) ----

    /// <summary>A local Windows voice for its speech (AS-18 reads it); null: Dextra's.</summary>
    public string? Voice { get; init; }

    /// <summary>Its home thread (a chat whose AssistantID is this id); null until nth has made it.</summary>
    public Guid? HomeThread { get; init; }

    /// <summary>How it writes, in the owner's words ("short, plain, no emoji"); one clean line.</summary>
    public string Style { get; init; } = "";

    /// <summary>Its saved skills and workflows, by name.</summary>
    public ValueList<string> Skills { get; init; } = ValueList<string>.Empty;

    /// <summary>The standing duties it was created with (the kept ones become real duties in AS-10).</summary>
    public ValueList<DutyDraft> Duties { get; init; } = ValueList<DutyDraft>.Empty;

    /// <summary>The connections it asked for at creation, as the profile card listed them.</summary>
    public ValueList<AsturWant> Wants { get; init; } = ValueList<AsturWant>.Empty;

    /// <summary>The slice of the day it may use; null: no slice of its own (Dextra's, which has the owner's reserves and caps).</summary>
    public AsturBudget? Budget { get; init; }

    /// <summary>The provider, model and account its missions try first; null: the router chooses.</summary>
    public AsturModel? Model { get; init; }
    // PORT-ONLY: DESIGN.md 9.1; ED-NEW-dextra-open-4. Empty inherits the owner's general order.
    public ValueList<string> PillOrder { get; init; } = ValueList<string>.Empty;

    /// <summary>The template it was made from ("channel"), or null for one made from a sentence.</summary>
    public string? Template { get; init; }

    public DateTimeOffset? Created { get; init; }

    /// <summary>The owner retired it: it is not listed, not routed to and not delegated to; its folder and notes stay until he deletes them.</summary>
    public bool Retired { get; init; }

    /// <summary>
    /// The assistant whose browser profile this one uses, because the owner shared it explicitly (9.1 "logins never cross between
    /// assistants unless the owner shares a profile explicitly"). Null: its own profile. One step only: a profile that borrows never lends.
    /// </summary>
    public string? SharesBrowserOf { get; init; }

    /// <summary>The Astur's colour tint is its shape's (2.5); Dextra has none.</summary>
    public bool IsRoot => ParentId is null;
}

/// <summary>The rules that keep an Astur no broader than its parent (9.1, 12.1).</summary>
public static class AssistantChain
{
    /// <summary>
    /// The chain from <paramref name="assistantId"/> up to Dextra: the assistant first, its root last. Throws when the
    /// id or a parent is missing or the parents form a loop.
    /// </summary>
    public static ValueList<AssistantProfile> Of(string assistantId, IEnumerable<AssistantProfile> profiles)
    {
        var byId = profiles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var chain = new List<AssistantProfile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? id = assistantId;
        while (id is not null)
        {
            if (!seen.Add(id)) throw new InvalidOperationException($"The assistants' parents form a loop at {id}.");
            if (!byId.TryGetValue(id, out var profile)) throw new KeyNotFoundException($"No assistant has the id {id}.");
            chain.Add(profile);
            id = profile.ParentId;
        }
        return new ValueList<AssistantProfile>(chain);
    }

    /// <summary>
    /// The rule that applies to a class for the first assistant in <paramref name="chain"/>: the strictest of 12.1's
    /// default and every choice along the chain. So an Astur's rules are never looser than its parent's.
    /// </summary>
    public static ClassDefault EffectiveRule(IEnumerable<AssistantProfile> chain, ActionClass actionClass)
    {
        var rule = ActionClasses.DefaultOf(actionClass);
        foreach (var profile in chain)
            if (profile.ClassRules.TryGetValue(actionClass, out var choice)) rule = ActionClasses.Stricter(rule, choice);
        return rule;
    }

    /// <summary>Every ask-first rule along the chain: a parent's ask-first binds its Asturs.</summary>
    public static IEnumerable<AskFirstRule> AskFirstRules(IEnumerable<AssistantProfile> chain) => chain.SelectMany(p => p.AskFirst);

    /// <summary>
    /// Why an Astur's own choices are looser than its parent chain's, or null when they are not. The engine never
    /// applies a looser choice; this names it so the profile card can refuse it.
    /// </summary>
    public static string? LooserThanParent(AssistantProfile child, IEnumerable<AssistantProfile> parentChain)
    {
        var parents = parentChain.ToList();
        foreach (var (actionClass, choice) in child.ClassRules)
        {
            var parentRule = EffectiveRule(parents, actionClass);
            if (choice < parentRule) return $"{child.Name} cannot be looser than its parent for {actionClass}.";
        }
        return null;
    }
}
