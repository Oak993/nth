// PORT-ONLY: first-release feature unit SK-1. The counts of the catalogue, in one place (the SK-1 brief: "they are set again by SK-2's re-read later").
namespace Marrow.Core.Port.Skills.Catalogue;

/// <summary>
/// The counts skills-ONDEMAND-PLAN.md prints, as read on 2026-10-04: section 0 and 2.1 "10 vendor skill sets holding 615 skills (Anthropic 388, OpenAI 227)",
/// section 2.2 "11 tools plus 6 package sets". The two sets the OD-0099 amendment added (claude-plugins-official and the Figma folders) are outside those
/// totals until SK-2's re-read. SK-2 changes these numbers here and nowhere else; the tests hold the catalogue file to them.
/// </summary>
public static class SkillsCatalogueCounts
{
    public const int SchemaVersion = 1;

    public const int VendorSets = 10;

    public const int Skills = 615;

    public const int AnthropicSkills = 388;

    public const int OpenAiSkills = 227;

    public const int Tools = 11;

    public const int PackageSets = 6;

    public const int AddedSets = 2;
}
