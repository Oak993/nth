// PORT-ONLY: first-release feature unit SK-18 (OD-0118). Marrow's own library of skills brought in from other apps, under <data root>\skills\imported:
//   index.json        {version, skills: [...]}  versioned; unreadable or newer text is never written over
//   copies\<id>\      Marrow's own copy of a skill brought in as a Copy
// A Link keeps only its record: the original is read each time skills are handed out. Nothing in another app's folders is ever written, moved or locked. The folders here are outside the managed store, the views and
// addons trees, so Reset skills and tools (which removes those) leaves the user's imports alone.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Port.Skills.Catalogue;
using Marrow.Services.Port.Skills.Generations;

namespace Marrow.Services.Port.Skills.Import;

public sealed class ImportedSkillsLibrary : IImportedSkillSource
{
    public const int Version = 1;
    public const int MaxFiles = 200;
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly UTF8Encoding Strict = new(false, true);
    private readonly object gate = new();
    private readonly string folder;
    private readonly string index;
    private readonly string copies;
    private readonly TimeProvider clock;
    private List<Entry> entries = [];
    private bool wroteSince;

    private sealed record Entry(ImportedSkill Skill, string LastDescription);

    public ImportedSkillsLibrary(string skillsRoot, TimeProvider clock)
    {
        if (string.IsNullOrWhiteSpace(skillsRoot) || !Path.IsPathFullyQualified(skillsRoot)) throw new ArgumentException("An absolute skills folder is required.", nameof(skillsRoot));
        this.clock = clock;
        folder = Path.Combine(Path.GetFullPath(skillsRoot), "imported");
        index = Path.Combine(folder, "index.json");
        copies = Path.Combine(folder, "copies");
        Load();
    }

    /// <summary>Why the stored index could not be read (it loaded empty), or null.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>The stored index is from a newer Marrow: it is shown but never changed.</summary>
    public bool ReadOnly { get; private set; }

    public IReadOnlyList<ImportedSkill> List()
    {
        lock (gate) return [.. entries.Select(Resolve).Select(r => r.Skill).OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public DuplicateQuestion? FindDuplicate(ForeignSkill skill)
    {
        lock (gate) return Duplicate(skill) is { } existing ? new DuplicateQuestion(skill, Resolve(existing).Skill) : null;
    }

    public ImportResult Import(IEnumerable<ForeignSkill> chosen, ImportMode mode, Func<DuplicateQuestion, DuplicateChoice> onDuplicate)
    {
        ArgumentNullException.ThrowIfNull(onDuplicate);
        lock (gate) CheckWritable();
        var imported = new List<ImportedSkill>();
        var replaced = new List<ImportedSkill>();
        var skipped = new List<ForeignSkill>();
        var failed = new List<ImportFailure>();
        var changed = false;
        try
        {
            foreach (var skill in chosen)
            {
                // The question is asked with the library free: the person may take a while, and the page and the chats read the library meanwhile.
                DuplicateQuestion? question;
                lock (gate) question = Duplicate(skill) is { } found ? new DuplicateQuestion(skill, Resolve(found).Skill) : null;
                var choice = DuplicateChoice.KeepBoth;
                if (question is not null)
                {
                    choice = onDuplicate(question);
                    if (choice == DuplicateChoice.Skip) { skipped.Add(skill); continue; }
                }
                lock (gate)
                {
                    CheckWritable();
                    var existing = Duplicate(skill);
                    try
                    {
                        var name = existing is null || choice == DuplicateChoice.Replace ? skill.Name : UniqueName(skill);
                        var id = Guid.NewGuid().ToString("N");
                        var record = new ImportedSkill(id, name, skill.Name, skill.Description, skill.App, skill.Source, mode, skill.Path, skill.Layout,
                            existing is not null && choice == DuplicateChoice.Replace ? existing.Skill.Enabled : true, clock.GetUtcNow(), false);
                        if (mode == ImportMode.Copy) MakeCopy(id, skill);
                        else Probe(skill);
                        if (existing is not null && choice == DuplicateChoice.Replace)
                        {
                            entries.Remove(existing);
                            DeleteCopy(existing.Skill.Id);
                            replaced.Add(existing.Skill);
                        }
                        entries.Add(new Entry(record, skill.Description));
                        imported.Add(record);
                        changed = true;
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
                    {
                        failed.Add(new ImportFailure(skill, error.Message));
                    }
                }
            }
        }
        finally
        {
            if (changed) lock (gate) Save();
        }
        return new ImportResult(imported, replaced, skipped, failed);
    }

    private void CheckWritable()
    {
        if (ReadOnly) throw new InvalidOperationException("The imported skills were written by a newer Marrow and are not changed.");
    }

    public bool Remove(string id)
    {
        lock (gate)
        {
            if (ReadOnly) throw new InvalidOperationException("The imported skills were written by a newer Marrow and are not changed.");
            var entry = entries.FirstOrDefault(e => e.Skill.Id == id);
            if (entry is null) return false;
            entries.Remove(entry);
            DeleteCopy(id);
            Save();
            return true;
        }
    }

    public void SetEnabled(string id, bool on)
    {
        lock (gate)
        {
            if (ReadOnly) throw new InvalidOperationException("The imported skills were written by a newer Marrow and are not changed.");
            var at = entries.FindIndex(e => e.Skill.Id == id);
            if (at < 0) throw new KeyNotFoundException("No imported skill " + id);
            entries[at] = entries[at] with { Skill = entries[at].Skill with { Enabled = on } };
            Save();
        }
    }

    /// <summary>The skills to hand out now: enabled, readable and, for a Link, as the original is now.</summary>
    public IReadOnlyList<ImportedGenerationSkill> Snapshot()
    {
        lock (gate)
        {
            var result = new List<ImportedGenerationSkill>();
            foreach (var entry in entries.OrderBy(e => e.Skill.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!entry.Skill.Enabled) continue;
                var (skill, files) = Resolve(entry);
                if (skill.OriginalMissing || files is null || skill.Description.Length == 0) continue;
                result.Add(new ImportedGenerationSkill(skill.Id, skill.Name, skill.Description, files));
            }
            return result;
        }
    }

    // ---- reading what an entry is now

    private (ImportedSkill Skill, IReadOnlyDictionary<string, byte[]>? Files) Resolve(Entry entry)
    {
        var skill = entry.Skill;
        try
        {
            Dictionary<string, byte[]> files;
            if (skill.Mode == ImportMode.Copy)
            {
                files = ReadTree(Path.Combine(copies, skill.Id));
            }
            else if (skill.Layout == ForeignLayout.CursorRule)
            {
                if (!File.Exists(skill.SourcePath)) return (skill with { OriginalMissing = true }, null);
                files = RuleFiles(skill.SourcePath, skill.OriginalName, out _);
            }
            else
            {
                if (!Directory.Exists(skill.SourcePath)) return (skill with { OriginalMissing = true }, null);
                files = ReadTree(skill.SourcePath);
            }
            if (!files.TryGetValue("SKILL.md", out var bytes)) return (skill with { OriginalMissing = skill.Mode == ImportMode.Link }, null);
            var parsed = ForeignFrontmatter.Parse(Strict.GetString(bytes));
            var description = string.IsNullOrWhiteSpace(parsed.Description) ? entry.LastDescription : parsed.Description!;
            files["SKILL.md"] = Encoding.UTF8.GetBytes(ForeignFrontmatter.WithName(Strict.GetString(bytes).TrimStart('﻿'), skill.Name));
            return (skill with { Description = description }, files);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        {
            return (skill with { OriginalMissing = skill.Mode == ImportMode.Link && !Exists(skill) }, null);
        }
    }

    private static bool Exists(ImportedSkill skill) => skill.Layout == ForeignLayout.CursorRule ? File.Exists(skill.SourcePath) : Directory.Exists(skill.SourcePath);

    /// <summary>Every ordinary file under <paramref name="root"/> by relative path; shortcuts and .git are skipped, size and count are bounded.</summary>
    private static Dictionary<string, byte[]> ReadTree(string root)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new IOException("A shortcut is not followed.");
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (relative.Split('/').Any(part => part == ".git") || IsLinkedPath(root, path)) continue;
            if (files.Count >= MaxFiles) throw new IOException("The skill has more than " + MaxFiles + " files.");
            var length = new FileInfo(path).Length;
            total += length;
            if (total > MaxBytes) throw new IOException("The skill is larger than " + MaxBytes / (1024 * 1024) + " MB.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            files[relative] = memory.ToArray();
        }
        return files;
    }

    private static bool IsLinkedPath(string root, string path)
    {
        for (var current = path; current is not null && current.Length > root.Length; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) return true;
        return false;
    }

    /// <summary>A Cursor rule as the files of a skill: one SKILL.md Marrow makes from the rule's description and text.</summary>
    private static Dictionary<string, byte[]> RuleFiles(string file, string name, out string description)
    {
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A shortcut is not followed.");
        var info = new FileInfo(file);
        if (info.Length > 256 * 1024) throw new IOException("The rule is larger than the limit.");
        var text = Strict.GetString(File.ReadAllBytes(file)).TrimStart('﻿');
        var parsed = ForeignFrontmatter.Parse(text);
        description = parsed.Description ?? "";
        if (string.IsNullOrWhiteSpace(description))
        {
            var first = parsed.Body.Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
            description = new string(first.Where(c => !char.IsControl(c)).ToArray()).Trim();
        }
        if (string.IsNullOrWhiteSpace(description)) throw new InvalidDataException("The rule is empty.");
        return new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["SKILL.md"] = Encoding.UTF8.GetBytes(ForeignFrontmatter.Compose(name, description, parsed.Body)) };
    }

    // ---- making a copy

    private void Probe(ForeignSkill skill)
    {
        if (skill.Layout == ForeignLayout.CursorRule) _ = RuleFiles(skill.Path, skill.Name, out _);
        else _ = ReadTree(skill.Path);
    }

    private void MakeCopy(string id, ForeignSkill skill)
    {
        var files = skill.Layout == ForeignLayout.CursorRule ? RuleFiles(skill.Path, skill.Name, out _) : ReadTree(skill.Path);
        if (!files.ContainsKey("SKILL.md")) throw new InvalidDataException("The skill has no SKILL.md.");
        Directory.CreateDirectory(copies);
        var temporary = Path.Combine(copies, ".tmp-" + id);
        var target = Path.Combine(copies, id);
        try
        {
            foreach (var (relative, bytes) in files)
            {
                var path = Path.Combine(temporary, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            Directory.Move(temporary, target);
        }
        finally
        {
            if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
        }
    }

    private void DeleteCopy(string id)
    {
        var target = Path.Combine(copies, id);
        if (id.Length == 32 && id.All(char.IsAsciiHexDigit) && Directory.Exists(target)) Directory.Delete(target, recursive: true);
    }

    // ---- duplicates and names

    private Entry? Duplicate(ForeignSkill skill) =>
        entries.FirstOrDefault(e => e.Skill.App == skill.App && string.Equals(e.Skill.SourcePath, skill.Path, StringComparison.Ordinal))
        ?? entries.FirstOrDefault(e => string.Equals(e.Skill.Name, skill.Name, StringComparison.OrdinalIgnoreCase));

    private string UniqueName(ForeignSkill skill)
    {
        var slug = skill.App switch
        {
            ForeignApp.ClaudeCode => "claude-code",
            ForeignApp.Codex => "codex",
            ForeignApp.Antigravity => "antigravity",
            ForeignApp.Cursor => "cursor",
            _ => "folder",
        };
        bool Taken(string n) => entries.Any(e => string.Equals(e.Skill.Name, n, StringComparison.OrdinalIgnoreCase));
        // ED-533 / RR-45: reserve space for the suffix before shortening
        // the base. At most entries.Count names are taken, so Count + 1 distinct candidates suffice.
        if (!SkillName.IsValid(skill.Name)) throw new InvalidDataException("The imported skill name is invalid.");
        for (long i = 1; i <= (long)entries.Count + 1; i++)
        {
            var suffix = "-" + slug + (i == 1 ? "" : "-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var length = Math.Min(skill.Name.Length, SkillName.MaxLength - suffix.Length);
            var candidate = skill.Name[..length].TrimEnd('-') + suffix;
            if (SkillName.IsValid(candidate) && !Taken(candidate)) return candidate;
        }
        throw new InvalidDataException("A unique imported skill name could not be made.");
    }

    // ---- the index

    private void Load()
    {
        if (!File.Exists(index)) return;
        try
        {
            var root = JsonNode.Parse(File.ReadAllBytes(index)) as JsonObject ?? throw new JsonException("not an object");
            var version = root["version"] is JsonValue v && v.TryGetValue<int>(out var number) ? number : 1;
            ReadOnly = version > Version;
            var list = new List<Entry>();
            foreach (var node in root["skills"] as JsonArray ?? [])
                if (Read(node as JsonObject) is { } entry && list.All(e => e.Skill.Id != entry.Skill.Id)) list.Add(entry);
            entries = list;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LoadProblem = "The imported skills could not be read: " + error.Message;
            entries = [];
        }
    }

    private static Entry? Read(JsonObject? o)
    {
        if (o is null) return null;
        string? Str(string key) => o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var id = Str("id");
        var name = Str("name");
        if (id is not { Length: 32 } || !id.All(char.IsAsciiHexDigit) || !SkillName.IsValid(name)) return null;
        if (!Enum.TryParse<ForeignApp>(Str("app"), ignoreCase: true, out var app) || !Enum.TryParse<ImportMode>(Str("mode"), true, out var mode)) return null;
        var layout = Enum.TryParse<ForeignLayout>(Str("layout"), true, out var l) ? l : ForeignLayout.SkillFolder;
        var source = Str("sourcePath") ?? "";
        var description = Str("description") ?? "";
        var at = DateTimeOffset.TryParse(Str("importedAt"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var when) ? when : DateTimeOffset.UnixEpoch;
        var enabled = o["enabled"] is JsonValue e && e.TryGetValue<bool>(out var on) ? on : true;
        return new Entry(new ImportedSkill(id, name!, Str("originalName") ?? name!, description, app, Str("sourceLabel") ?? "", mode, source, layout, enabled, at, false), description);
    }

    private void Save()
    {
        Directory.CreateDirectory(folder);
        if (LoadProblem is not null && !wroteSince && File.Exists(index) && !File.Exists(index + ".unreadable")) File.Copy(index, index + ".unreadable");
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            var s = entry.Skill;
            array.Add(new JsonObject
            {
                ["id"] = s.Id,
                ["name"] = s.Name,
                ["originalName"] = s.OriginalName,
                ["description"] = entry.LastDescription.Length > 0 ? entry.LastDescription : s.Description,
                ["app"] = s.App.ToString(),
                ["sourceLabel"] = s.SourceLabel,
                ["mode"] = s.Mode.ToString(),
                ["sourcePath"] = s.SourcePath,
                ["layout"] = s.Layout.ToString(),
                ["enabled"] = s.Enabled,
                ["importedAt"] = s.ImportedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            });
        }
        var root = new JsonObject { ["version"] = Version, ["skills"] = array };
        FileOps.WriteAtomic(index, Encoding.UTF8.GetBytes(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })));
        wroteSince = true;
    }
}
