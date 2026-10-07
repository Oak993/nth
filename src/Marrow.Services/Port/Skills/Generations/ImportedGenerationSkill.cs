// PORT-ONLY: first-release feature unit SK-18 (OD-0118). A skill the user brought in from another app, as the generations take it: bytes read now, never a path into the other app.
namespace Marrow.Services.Port.Skills.Generations;

/// <summary><see cref="Files"/> are relative paths below the skill's folder, SKILL.md included, whose front matter already carries <see cref="Name"/>.</summary>
public sealed record ImportedGenerationSkill(string Id, string Name, string Description, IReadOnlyDictionary<string, byte[]> Files, string? PluginId = null, string? PluginPath = null);

public interface IImportedSkillSource
{
    /// <summary>The skills to hand out now: enabled, readable, one entry per name.</summary>
    IReadOnlyList<ImportedGenerationSkill> Snapshot();
}
