// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>Read-only snapshot of folder scans / provider reports; the caller never supplies Marrow's entries as user entries.</summary>
public enum SkillOrigin { Unknown, AccountSynced, ProviderSystem, ProviderPlugin, User, ProviderReported }

public sealed record ExistingSkill(string Name, string Description)
{
    public SkillOrigin Origin { get; init; }
    public string? SourcePath { get; init; }
    public bool AccountProvided => Origin == SkillOrigin.AccountSynced;
    public IReadOnlyList<string> EquivalentNames => GenerationFilters.Equivalents(Name);
}
