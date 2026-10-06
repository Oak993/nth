using System.Security.Cryptography;
using System.Text;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Skills.Import;

/// <summary>
/// A temp folder shaped like the home of a person who uses several AI tools (SK-18). Nothing here is a real home folder: the scanner is given this path explicitly.
/// Skill text is Marrow-made fixture text.
/// </summary>
public sealed class ForeignHome : IDisposable
{
    public ForeignHome()
    {
        Root = Path.Combine(TestDataDirectory.Read() ?? Path.GetTempPath(), "sk18-" + Guid.NewGuid().ToString("N"));
        Home = Path.Combine(Root, "home");
        SkillsRoot = Path.Combine(Root, "marrow-data", "skills");
        Directory.CreateDirectory(Home);
        Directory.CreateDirectory(SkillsRoot);
    }

    public string Root { get; }
    public string Home { get; }
    public string SkillsRoot { get; }

    public static string SkillText(string name, string description, string body = "BODY SENTINEL") => $"---\nname: {name}\ndescription: {description}\n---\n{body}\n";

    /// <summary>Writes <c>&lt;home&gt;/&lt;root&gt;/&lt;folder&gt;/SKILL.md</c> (and any extra files beside it) and returns the skill folder.</summary>
    public string Skill(string root, string folder, string name, string description, params (string Path, string Text)[] extra)
    {
        var dir = Path.Combine(Home, root.Replace('/', Path.DirectorySeparatorChar), folder.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SKILL.md"), SkillText(name, description));
        foreach (var (path, text) in extra)
        {
            var file = Path.Combine(dir, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        return dir;
    }

    public string Raw(string relative, string text)
    {
        var file = Path.Combine(Home, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Every file below <paramref name="folder"/>: path, size, hash and write time. Equal before and after means nothing was written, touched or created.</summary>
    public static string Fingerprint(string folder)
    {
        var lines = new List<string>();
        foreach (var path in Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(folder, path);
            if (Directory.Exists(path)) lines.Add(relative + "/ " + Directory.GetLastWriteTimeUtc(path).Ticks);
            else lines.Add(relative + " " + new FileInfo(path).Length + " " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) + " " + File.GetLastWriteTimeUtc(path).Ticks);
        }
        return string.Join("\n", lines);
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
