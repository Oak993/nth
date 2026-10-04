// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

public static class SkillDefaults
{
    public static bool Enabled(string itemId, SkillGroup group, bool requiresKeyLoginOrInternet) =>
        itemId is not ("A3" or "A4" or "O5") && !requiresKeyLoginOrInternet && group is not SkillGroup.Other;
}
