// PORT-ONLY: first-release feature unit SK-18 (OD-0118). Marrow's own library of skills brought in from other apps.
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Import;

public sealed class ImportedSkillsLibrary(string skillsRoot, TimeProvider clock) : IImportedSkillSource
{
    public string? LoadProblem => throw new NotImplementedException();

    public bool ReadOnly => throw new NotImplementedException();

    public IReadOnlyList<ImportedSkill> List() => throw new NotImplementedException();

    public DuplicateQuestion? FindDuplicate(ForeignSkill skill) => throw new NotImplementedException();

    public ImportResult Import(IEnumerable<ForeignSkill> chosen, ImportMode mode, Func<DuplicateQuestion, DuplicateChoice> onDuplicate) => throw new NotImplementedException();

    public bool Remove(string id) => throw new NotImplementedException();

    public void SetEnabled(string id, bool on) => throw new NotImplementedException();

    public IReadOnlyList<ImportedGenerationSkill> Snapshot() => throw new NotImplementedException();

    internal object Unused() => (skillsRoot, clock);
}
