// PORT-ONLY: first-release feature unit SK-18 (OD-0118). The places other AI tools keep skills, under an EXPLICIT home (never the ambient one), read-only: nothing here creates, writes, locks or touches a file
// of another app. Shortcuts (symbolic links, junctions) are not followed. A file that cannot be understood is reported and left out; it never stops the scan.
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Port.Skills.Catalogue;

namespace Marrow.Services.Port.Skills.Import;

public sealed partial class ForeignSkillScanner
{
    private static readonly UTF8Encoding Strict = new(false, true);
    private readonly string home;
    private readonly string[] picked;
    private readonly ScanLimits limits;

    public ForeignSkillScanner(string home, IEnumerable<string>? pickedFolders = null, ScanLimits? limits = null)
    {
        if (string.IsNullOrWhiteSpace(home) || !Path.IsPathFullyQualified(home)) throw new ArgumentException("An explicit absolute home folder is required.", nameof(home));
        this.home = Path.GetFullPath(home);
        picked = [.. pickedFolders ?? []];
        this.limits = limits ?? new ScanLimits();
    }

    [GeneratedRegex(@"\A(?:v?[0-9]+(?:\.[0-9A-Za-z]+)*(?:[-+][0-9A-Za-z.\-+]+)?|[0-9a-f]{7,40})\z", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLike();

    public ForeignScan Scan(CancellationToken cancellation = default)
    {
        var found = new Dictionary<string, ForeignSkill>(StringComparer.Ordinal);
        var problems = new List<ScanProblem>();
        var full = false;

        void Add(ForeignSkill? skill)
        {
            if (skill is null || found.ContainsKey(skill.Path)) return;
            if (found.Count >= limits.MaxSkills)
            {
                if (!full) problems.Add(new ScanProblem(skill.Path, "The scan stopped at its limit of " + limits.MaxSkills + " skills."));
                full = true;
                return;
            }
            found.Add(skill.Path, skill);
        }

        FolderRoot(ForeignApp.ClaudeCode, "Skills", Combine(".claude", "skills"));
        Plugins();
        FolderRoot(ForeignApp.Codex, "Skills", Combine(".codex", "skills"));
        FolderRoot(ForeignApp.Codex, "Skills", Combine(".agents", "skills"));
        foreach (var relative in new[] { Combine(".gemini", "antigravity-cli", "skills"), Combine(".gemini", "config", "skills"), Combine(".gemini", "skills") })
            FolderRoot(ForeignApp.Antigravity, "Skills", relative);
        FolderRoot(ForeignApp.Cursor, "Skills", Combine(".cursor", "skills"));
        Rules(Path.Combine(home, ".cursor", "rules"), 0);
        foreach (var folder in picked) Picked(folder);

        var skills = found.Values
            .OrderBy(s => s.App).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Path, StringComparer.Ordinal)
            .ToArray();
        return new ForeignScan(skills, problems);

        string Combine(params string[] parts) => Path.Combine([home, .. parts]);

        void FolderRoot(ForeignApp app, string source, string root)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var dir in Children(root, problems)) Add(ReadSkill(app, source, dir, problems));
        }

        void Plugins()
        {
            var root = Combine(".claude", "plugins");
            if (!Directory.Exists(root)) return;
            var winners = new Dictionary<string, (ForeignSkill Skill, DateTime Written)>(StringComparer.Ordinal);
            foreach (var skillsDir in FindNamed(root, "skills", 0, problems))
            {
                var owner = skillsDir;
                for (var parent = Path.GetDirectoryName(skillsDir); parent is not null && parent.Length > root.Length; parent = Path.GetDirectoryName(parent))
                {
                    owner = parent;
                    if (!VersionLike().IsMatch(Path.GetFileName(parent))) break;
                }
                var label = Path.GetFileName(owner);
                foreach (var dir in Children(skillsDir, problems))
                {
                    if (ReadSkill(ForeignApp.ClaudeCode, label, dir, problems) is not { } skill) continue;
                    var written = File.GetLastWriteTimeUtc(Path.Combine(dir, "SKILL.md"));
                    var key = label + "|" + skill.Name;
                    // Old versions of a plugin stay in its cache; the newest one is the one that is in use.
                    if (!winners.TryGetValue(key, out var existing) || written > existing.Written || written == existing.Written && string.CompareOrdinal(skill.Path, existing.Skill.Path) > 0)
                        winners[key] = (skill, written);
                }
            }
            foreach (var (skill, _) in winners.Values.OrderBy(w => w.Skill.Path, StringComparer.Ordinal)) Add(skill);
        }

        void Rules(string root, int depth)
        {
            if (!Directory.Exists(root) || depth > 2 || IsLink(root)) return;
            cancellation.ThrowIfCancellationRequested();
            foreach (var entry in Safe(() => Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal).ToArray(), root, problems))
            {
                if (IsLink(entry)) { problems.Add(new ScanProblem(entry, "A shortcut is not followed.")); continue; }
                if (Directory.Exists(entry)) { Rules(entry, depth + 1); continue; }
                var extension = Path.GetExtension(entry);
                if (extension is not (".mdc" or ".md")) continue;
                Add(ReadRule(entry, problems));
            }
        }

        void Picked(string folder)
        {
            cancellation.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) { problems.Add(new ScanProblem(folder, "Choose a folder by its full path.")); return; }
            var path = Path.GetFullPath(folder);
            if (!Directory.Exists(path)) { problems.Add(new ScanProblem(path, "This folder was not found.")); return; }
            if (IsLink(path)) { problems.Add(new ScanProblem(path, "A shortcut is not followed.")); return; }
            var label = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
            if (label.Length == 0) label = path;
            if (File.Exists(Path.Combine(path, "SKILL.md"))) { AddUnknown(ReadSkill(ForeignApp.Folder, label, path, problems)); return; }
            foreach (var dir in FindWithSkill(path, 0, problems)) AddUnknown(ReadSkill(ForeignApp.Folder, label, dir, problems));
        }

        // A skill an app's own place already holds stays under that app.
        void AddUnknown(ForeignSkill? skill)
        {
            if (skill is not null && found.Values.Any(s => s.Path == skill.Path || skill.Path.StartsWith(s.Path + Path.DirectorySeparatorChar, StringComparison.Ordinal))) return;
            Add(skill);
        }
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    private static T[] Safe<T>(Func<T[]> read, string path, List<ScanProblem> problems)
    {
        try { return read(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            problems.Add(new ScanProblem(path, "This folder could not be read."));
            return [];
        }
    }

    /// <summary>The ordinary, visible sub-folders of a skills folder; a shortcut is reported and skipped.</summary>
    private static IEnumerable<string> Children(string root, List<ScanProblem> problems)
    {
        if (!Directory.Exists(root)) yield break;
        if (IsLink(root)) { problems.Add(new ScanProblem(root, "A shortcut is not followed.")); yield break; }
        foreach (var entry in Safe(() => Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal).ToArray(), root, problems))
        {
            var name = Path.GetFileName(entry);
            if (name.StartsWith('.')) continue;
            if (IsLink(entry)) { problems.Add(new ScanProblem(entry, "A shortcut is not followed.")); continue; }
            if (Directory.Exists(entry)) yield return entry;
        }
    }

    private IEnumerable<string> FindNamed(string root, string name, int depth, List<ScanProblem> problems)
    {
        if (depth >= limits.MaxDepth || IsLink(root)) yield break;
        foreach (var entry in Safe(() => Directory.GetDirectories(root).Order(StringComparer.Ordinal).ToArray(), root, problems))
        {
            var leaf = Path.GetFileName(entry);
            if (leaf is ".git" or "node_modules" || IsLink(entry)) continue;
            if (leaf == name) yield return entry;
            else foreach (var inner in FindNamed(entry, name, depth + 1, problems)) yield return inner;
        }
    }

    private IEnumerable<string> FindWithSkill(string root, int depth, List<ScanProblem> problems)
    {
        if (depth >= 3) yield break;
        foreach (var dir in Children(root, problems))
        {
            if (File.Exists(Path.Combine(dir, "SKILL.md"))) yield return dir;
            else foreach (var inner in FindWithSkill(dir, depth + 1, problems)) yield return inner;
        }
    }

    private ForeignSkill? ReadSkill(ForeignApp app, string source, string dir, List<ScanProblem> problems)
    {
        var file = Path.Combine(dir, "SKILL.md");
        if (!File.Exists(file)) return null;
        if (IsLink(file)) { problems.Add(new ScanProblem(file, "A shortcut is not followed.")); return null; }
        var text = ReadText(file, problems);
        if (text is null) return null;
        var parsed = ForeignFrontmatter.Parse(text);
        if (!parsed.HasFrontmatter) { problems.Add(new ScanProblem(file, "There is no readable front matter.")); return null; }
        if (string.IsNullOrWhiteSpace(parsed.Description)) { problems.Add(new ScanProblem(file, "There is no description.")); return null; }
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        var name = SkillName.IsValid(parsed.Name) ? parsed.Name! : SkillName.IsValid(folder) ? folder : null;
        if (name is null) { problems.Add(new ScanProblem(file, "There is no usable name.")); return null; }
        return new ForeignSkill(app, source, name, parsed.Description, dir, ForeignLayout.SkillFolder);
    }

    private ForeignSkill? ReadRule(string file, List<ScanProblem> problems)
    {
        var text = ReadText(file, problems);
        if (text is null) return null;
        var parsed = ForeignFrontmatter.Parse(text);
        var description = parsed.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            var first = parsed.Body.Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).FirstOrDefault(l => l.Length > 0);
            description = first is null ? null : new string(first.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }
        if (string.IsNullOrWhiteSpace(description)) { problems.Add(new ScanProblem(file, "The rule is empty.")); return null; }
        var stem = Slug(Path.GetFileNameWithoutExtension(file));
        if (!SkillName.IsValid(stem)) { problems.Add(new ScanProblem(file, "There is no usable name.")); return null; }
        return new ForeignSkill(ForeignApp.Cursor, "Rules", stem, description, file, ForeignLayout.CursorRule);
    }

    internal static string Slug(string value)
    {
        var builder = new StringBuilder();
        foreach (var c in value.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') builder.Append(c);
            else if (builder.Length > 0 && builder[^1] != '-') builder.Append('-');
        }
        return builder.ToString().Trim('-');
    }

    private string? ReadText(string file, List<ScanProblem> problems)
    {
        try
        {
            var info = new FileInfo(file);
            if (info.Length > limits.MaxSkillFileBytes) { problems.Add(new ScanProblem(file, "The file is larger than the limit of " + limits.MaxSkillFileBytes / 1024 + " KB.")); return null; }
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var bytes = memory.ToArray();
            if (Array.IndexOf(bytes, (byte)0) >= 0) { problems.Add(new ScanProblem(file, "The file is not text.")); return null; }
            return Strict.GetString(bytes);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            problems.Add(new ScanProblem(file, error is DecoderFallbackException ? "The file is not text." : "The file could not be read."));
            return null;
        }
    }
}
