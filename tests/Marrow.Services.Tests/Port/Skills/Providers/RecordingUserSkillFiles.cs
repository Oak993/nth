// PORT-ONLY test fixture (SK-11f, R-TEST-10). A fake file system for the user's own skills folder: every enumeration and read is recorded,
// so a test can prove that a folder was never opened (the switch is off) or that only paths inside one skill were read.
using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

public sealed class RecordingUserSkillFiles : IUserSkillFiles
{
    private readonly Dictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly List<string> accesses = [];

    public IReadOnlyList<string> Accesses => accesses;

    public void Add(string path, string text) => files[path] = text;

    public IReadOnlyList<string> SkillFolders(string root)
    {
        accesses.Add("list:" + root);
        return files.Keys.Where(f => Path.GetFileName(f) == "SKILL.md" && Path.GetDirectoryName(Path.GetDirectoryName(f)) == root)
            .Select(f => Path.GetDirectoryName(f)!).Order(StringComparer.Ordinal).ToArray();
    }

    public string? ReadText(string file)
    {
        accesses.Add("read:" + file);
        return files.GetValueOrDefault(file);
    }
}
