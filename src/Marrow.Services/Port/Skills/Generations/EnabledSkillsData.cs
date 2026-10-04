// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

public sealed record EnabledSkillsData(string Version, IReadOnlyDictionary<string, bool> Skills, IReadOnlyDictionary<string, bool> Plugins);
