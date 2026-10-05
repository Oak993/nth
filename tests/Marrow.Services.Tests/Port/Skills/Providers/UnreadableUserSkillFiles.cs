// PORT-ONLY test fixture (SK-11f, R-TEST-10). A skills folder the user's account cannot read.
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class UnreadableUserSkillFiles : IUserSkillFiles
{
    public IReadOnlyList<string> SkillFolders(string root) => throw new UnauthorizedAccessException("Access to the path is denied.");
    public string? ReadText(string root, string file) => throw new IOException("The file is in use.");
}
