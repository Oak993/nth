// PORT-ONLY: SK-11f / WU-12 (skills-PLAN.md 3.3 native row, Q17, finding 6). The user's own skills (~/.agents/skills) for the API providers, behind the switch that is off by default.
// While it is off nothing here touches the folder: no listing, no read, and every answer is empty.
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Generations;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Providers;

public sealed class NativeUserSkills
{
    private readonly string root;
    private readonly bool enabled;
    private readonly IUserSkillFiles files;
    private readonly object gate = new();
    private IReadOnlyList<OwnSkill>? listed;

    public NativeUserSkills(string root, bool enabled, IUserSkillFiles files)
    {
        if (!Path.IsPathFullyQualified(root)) throw new ArgumentException("An absolute skills folder is required.", nameof(root));
        this.root = Path.TrimEndingDirectorySeparator(root);
        this.enabled = enabled;
        this.files = files ?? throw new ArgumentNullException(nameof(files));
    }

    /// <summary>The user's skills, listed once and kept for the session; empty (and the folder untouched) while the switch is off.</summary>
    public IReadOnlyList<OwnSkill> List()
    {
        if (!enabled) return [];
        lock (gate) return listed ??= Scan();
    }

    /// <summary>The same entries as the clash and budget snapshot SK-9's generation takes (their names and whole descriptions).</summary>
    public IReadOnlyList<ExistingSkill> Existing() => Array.AsReadOnly(List().Select(s => new ExistingSkill(s.Name, s.Description)).ToArray());

    /// <summary>
    /// A file of one of the user's skills as text, or null when <paramref name="name"/> is not one of them (always null while the switch is off).
    /// Throws <see cref="ArgumentException"/> for a path that is not a safe relative path and <see cref="FileNotFoundException"/> for a file that is not a readable text file in the skill.
    /// </summary>
    public string? Read(string name, string relative)
    {
        var skill = List().FirstOrDefault(s => s.Name == name);
        if (skill is null) return null;
        StoreManifest.RequireRelative(relative);
        var path = Path.Combine(skill.Folder, relative.Replace('/', Path.DirectorySeparatorChar));
        return files.ReadText(root, path) ?? throw new FileNotFoundException("The file is not a readable text file in the skill.");
    }

    private List<OwnSkill> Scan()
    {
        var result = new List<OwnSkill>();
        IReadOnlyList<string> folders;
        try { folders = files.SkillFolders(root); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return result; } // a folder that cannot be read lists nothing; it never fails the session
        foreach (var folder in folders)
        {
            string? text;
            try { text = files.ReadText(root, Path.Combine(folder, "SKILL.md")); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            if (text is null) continue;
            ExistingSkill disclosure;
            try { disclosure = SkillFrontmatter.Disclosure(text); }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException) { continue; } // unusable metadata: not listed, the rest still load
            if (!SkillName.IsValid(disclosure.Name) || result.Any(s => s.Name == disclosure.Name)) continue;
            result.Add(new OwnSkill(disclosure.Name, disclosure.Description, folder));
        }
        return result;
    }
}
