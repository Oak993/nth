using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Custom;

internal sealed class CombinedOwnSkillSource(IImportedSkillSource imported, IImportedSkillSource own) : IImportedSkillSource
{
    public IReadOnlyList<ImportedGenerationSkill> Snapshot() => imported.Snapshot().Concat(own.Snapshot()).ToArray();
}
