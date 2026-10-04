// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>Read-only snapshot of folder scans / provider reports; the caller never supplies Marrow's entries as user entries.</summary>
public sealed record ExistingSkill(string Name, string Description);
