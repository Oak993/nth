// PORT-ONLY: first-release feature unit SK-15 (DL-17, ONDEMAND-PLAN.md 4.3, skills-PLAN.md Q8). What the providers already bring, read-only.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Pages;

/// <summary>One provider's name (as the page prints it) and the skills it reports itself: synced skills, its own system skills, the user's own.</summary>
public sealed record ProviderSkillGroup(string Provider, IReadOnlyList<ExistingSkill> Skills);

/// <summary>
/// "Already available through your providers": the section lists what the providers report, copies nothing and downloads nothing. The reports come from SK-11's provider wiring; unbound the
/// section reports nothing and does not show (<see cref="NoProviderSkills"/>). A test scripts it.
/// </summary>
public interface IProviderSkills
{
    IReadOnlyList<ProviderSkillGroup> Report();
}

public sealed class NoProviderSkills : IProviderSkills
{
    public IReadOnlyList<ProviderSkillGroup> Report() => [];
}
