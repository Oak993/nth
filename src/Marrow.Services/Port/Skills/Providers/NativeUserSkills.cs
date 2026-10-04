// PORT-ONLY: SK-11f / WU-12. Not implemented yet.
#pragma warning disable CS9113 // stub, replaced by the implementation
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class NativeUserSkills(string root, bool enabled, IUserSkillFiles files)
{
    public IReadOnlyList<OwnSkill> List() => throw new NotImplementedException();
    public IReadOnlyList<ExistingSkill> Existing() => throw new NotImplementedException();
    public string? Read(string name, string relative) => throw new NotImplementedException();
}
