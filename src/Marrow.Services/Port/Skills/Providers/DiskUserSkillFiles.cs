// PORT-ONLY: SK-11f / WU-12. The user's own skills folder on disk, read-only. A link (symbolic link or junction) is never followed: not as a skill folder, not as a folder on
// the way to a file, not as the file.
using System.Text;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class DiskUserSkillFiles : IUserSkillFiles
{
    private static readonly UTF8Encoding Strict = new(false, true);

    public IReadOnlyList<string> SkillFolders(string root)
    {
        if (!Directory.Exists(root)) return [];
        var folders = new List<string>();
        foreach (var folder in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            if (IsLink(folder)) continue;
            var skill = Path.Combine(folder, "SKILL.md");
            if (File.Exists(skill) && !IsLink(skill)) folders.Add(folder);
        }
        return folders;
    }

    public string? ReadText(string root, string file)
    {
        var rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var path = Path.GetFullPath(file);
        if (!path.StartsWith(rootPath + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return null;
        for (var part = path; part != rootPath; part = Path.GetDirectoryName(part)!)
            if (IsLink(part)) return null;
        if (!File.Exists(path)) return null;
        try { return Strict.GetString(File.ReadAllBytes(path)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException) { return null; }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return true; } // unreadable: treated as not safe
    }
}
