// PORT-ONLY: SK-9 / DL-9; OD-0099 progressive disclosure and skills-PLAN.md 2.1-2.4, 2.7.

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>Reviewed pin metadata supplied by the catalogue builder. No disk manifest is trusted.</summary>
public sealed record GenerationSkill(string ItemId, string Name, string SourcePath, string Description,
    SkillGroup Group, int Priority, bool RequiresKeyLoginOrInternet, bool? ExplicitOnlyOverride = null)
{
    public string Id => ItemId + "/" + SourcePath;
    public bool DefaultOn => SkillDefaults.Enabled(ItemId, Group, RequiresKeyLoginOrInternet);
    public bool ExplicitOnly => ExplicitOnlyOverride ?? Name is "screenshot" or "yeet" or "review-agent";
}
