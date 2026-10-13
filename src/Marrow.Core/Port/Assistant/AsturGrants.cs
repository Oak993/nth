// PORT-ONLY: Dextra (DESIGN.md rev 5, 9.1 "an Astur can never hold a grant its parent lacks", 12.2; unit AS-16; ED-NEW-dextra-as16-5). No Swift twin.
// A grant is made for one assistant from a card. For an Astur it is kept only while Dextra holds a live grant that already covers it
// (GrantRules.IsWithin: the same class, a target the parent's covers, no field the parent lacks, every exclusion kept, a scope that
// ends no later). The policy engine reads the same rule at every action; this is the same rule at the moment of the grant, so a
// grant that could never be used is not kept, and the owner is told why. Pure.
namespace Marrow.Core.Port.Assistant;

public static class AsturGrants
{
    /// <summary>
    /// Why the grant cannot be kept, or null when it can. A grant for Dextra is not checked against a parent (it has none). A grant for an
    /// Astur needs, for every assistant above it, a live grant of that assistant that contains it.
    /// </summary>
    public static string? Refusal(Grant grant, IReadOnlyList<AssistantProfile> team, IEnumerable<Grant> held, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(grant);
        ArgumentNullException.ThrowIfNull(team);
        if (GrantRules.Invalid(grant) is { } invalid) return invalid;
        if (team.FirstOrDefault(p => p.Id == grant.AssistantId) is not { } owner || owner.Retired) return "That assistant is not on the team.";
        if (owner.IsRoot) return null;
        IReadOnlyList<AssistantProfile> chain;
        try { chain = AssistantChain.Of(owner.Id, team); }
        catch (Exception error) when (error is KeyNotFoundException or InvalidOperationException) { return "That assistant's parents cannot be read."; }
        var live = held.Where(g => g.IsLive(now, grant.Scope.MissionId) && GrantRules.Invalid(g) is null).ToList();
        foreach (var parent in chain.Skip(1))
        {
            if (!live.Any(p => p.AssistantId == parent.Id && GrantRules.IsWithin(grant, p)))
                return $"{parent.Name} does not hold this itself, and {owner.Name} never holds more than {parent.Name}.";
        }
        return null;
    }

    /// <summary>
    /// The grant Dextra would need to hold for the Astur's grant to be kept: the same grant, for its parent, that the owner may add on the
    /// same card ("Give Dextra this too"). It is never added without his tap.
    /// </summary>
    public static Grant ParentOffer(Grant grant, string parentId, DateTimeOffset now) => grant with
    {
        Id = Guid.NewGuid(),
        AssistantId = parentId,
        Created = now,
        UsedBy = ValueList<string>.Empty,
        RevokedAt = null,
    };
}
