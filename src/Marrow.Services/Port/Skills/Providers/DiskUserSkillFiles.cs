// PORT-ONLY: SK-11f / WU-12. Not implemented yet.
namespace Marrow.Services.Port.Skills.Providers;

public sealed class DiskUserSkillFiles : IUserSkillFiles
{
    public IReadOnlyList<string> SkillFolders(string root) => throw new NotImplementedException();
    public string? ReadText(string file) => throw new NotImplementedException();
}
