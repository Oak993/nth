// PORT-ONLY: SK-9 / PLAN WU-5 and research-loading section 1. Explicit roots only; no ambient user home and no credentials or processes.
using System.Text.Json;
using Marrow.Services.Port.Skills.Store;

namespace Marrow.Services.Port.Skills.Generations;

/// <summary>Read-only scanner. SK-11 supplies effective extra paths/override homes from provider config resolution; uncertain resolution must refuse a generation.</summary>
public sealed class SkillClashScanner
{
    private readonly string home;
    private readonly string project;
    private readonly string repo;
    public SkillClashScanner(string home, string project, string repositoryRoot)
    {
        if (!Path.IsPathFullyQualified(home) || !Path.IsPathFullyQualified(project) || !Path.IsPathFullyQualified(repositoryRoot))
            throw new ArgumentException("Explicit absolute fixture/user and project roots are required.");
        this.home = Path.GetFullPath(home); this.project = Path.GetFullPath(project); repo = Path.GetFullPath(repositoryRoot);
        if (this.project != repo && !StorePaths.Under(this.project, repo)) throw new ArgumentException("The project is outside the repository root.");
    }

    public string ResolveExtra(string path)
    {
        if (path.Contains("{env:", StringComparison.Ordinal) || path.Contains("{file:", StringComparison.Ordinal))
            throw new InvalidDataException("A substituted skills path cannot be resolved by this scan.");
        return Path.GetFullPath(path.StartsWith("~/", StringComparison.Ordinal) ? Path.Combine(home, path[2..]) : Path.IsPathFullyQualified(path) ? path : Path.Combine(project, path));
    }

    public IReadOnlyList<ExistingSkill> Scan(string provider, IEnumerable<string>? extraPaths = null, IEnumerable<ExistingSkill>? reported = null,
        bool ownSkillsForApi = false, string? providerHome = null)
    {
        GenerationFilters.RequireProvider(provider);
        if (provider == "native" && !ownSkillsForApi) return []; // Q17: not even enumerate private skills while off.
        var userFolders = provider switch
        {
            "claude" => new[] { ".claude/skills" },
            "codex" => [".agents/skills", ".codex/skills"],
            "copilot" => [".copilot/skills", ".agents/skills"],
            "opencode" or "opencode-go" => [".config/opencode/skills", ".opencode/skills", ".claude/skills", ".agents/skills"],
            "pi" => [".pi/agent/skills", ".agents/skills"],
            "commandcode" => [".commandcode/skills", ".agents/skills"],
            "grok" => [".grok/skills", ".agents/skills", ".claude/skills"],
            "agy" => [".gemini/antigravity-cli/skills", ".gemini/config/skills", ".gemini/skills"],
            "cursor" => [".cursor/skills", ".agents/skills", ".claude/skills", ".codex/skills"],
            "devin" => [".agents/skills", ".config/devin/skills"],
            "native" => [".agents/skills"],
            _ => throw new ArgumentException("Unknown provider."),
        };
        var projectFolders = provider switch
        {
            "claude" => Array.Empty<string>(), // PLAN 2.4: namespaces; no project filtering.
            "codex" or "native" => [".agents/skills"],
            "copilot" => [".github/skills", ".claude/skills", ".agents/skills"],
            "opencode" or "opencode-go" => [".opencode/skills", ".claude/skills", ".agents/skills"],
            "pi" or "commandcode" => [], // PLAN 2.4: user's project copy wins without filtering.
            "grok" => [".grok/skills", ".claude/skills", ".agents/skills"],
            "agy" => [".agents/skills", ".agent/skills"],
            "cursor" => [".cursor/skills", ".agents/skills", ".claude/skills", ".codex/skills"],
            "devin" => [".devin/skills", ".agents/skills", ".windsurf/skills"],
            _ => throw new ArgumentException("Unknown provider."),
        };
        var roots = userFolders.Select(p => Path.Combine(home, p.Replace('/', Path.DirectorySeparatorChar))).ToList();
        if (providerHome is not null)
        {
            if (!Path.IsPathFullyQualified(providerHome)) throw new ArgumentException("A provider home override must be absolute.");
            var suffix = provider switch { "codex" => ".codex/skills", "grok" => ".grok/skills", "copilot" => ".copilot/skills", "pi" => ".pi/agent/skills", "commandcode" => ".commandcode/skills", _ => throw new ArgumentException("No home override is defined for this provider.") };
            roots.Remove(Path.Combine(home, suffix.Replace('/', Path.DirectorySeparatorChar)));
            roots.Add(Path.Combine(providerHome, "skills"));
        }
        for (var folder = project; folder is not null; folder = folder == repo ? null : Path.GetDirectoryName(folder))
            roots.AddRange(projectFolders.Select(p => Path.Combine(folder, p.Replace('/', Path.DirectorySeparatorChar))));
        roots.AddRange((extraPaths ?? []).Select(ResolveExtra));
        if (provider == "grok")
        {
            var grokHome = providerHome ?? Path.Combine(home, ".grok");
            var config = Path.Combine(grokHome, "config.toml");
            roots.AddRange(GrokPaths(config));
        }
        var result = new List<ExistingSkill>();
        foreach (var root in roots.Distinct(StorePaths.Comparer)) ReadFolder(root, result);
        result.AddRange((reported ?? []).Where(s => !s.Name.StartsWith("marrow:", StringComparison.OrdinalIgnoreCase)));
        return Array.AsReadOnly(result.ToArray());
    }

    private static void ReadFolder(string root, List<ExistingSkill> result)
    {
        StorePaths.EnsureAncestors(root);
        if (!Directory.Exists(root)) return;
        foreach (var file in StoreTrees.Entries(root))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new IOException("A clash scan refuses reparse points.");
            if (Path.GetFileName(file).Equals("SKILL.md", StringComparison.OrdinalIgnoreCase)) result.Add(SkillFrontmatter.Disclosure(File.ReadAllText(file)));
        }
    }

    private IEnumerable<string> GrokPaths(string config)
    {
        StorePaths.EnsureAncestors(config);
        if (!File.Exists(config)) yield break;
        var section = "";
        var lines = File.ReadAllLines(config);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = SkillFrontmatter.StripComment(lines[i], whitespaceRequired: false).Trim();
            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']')) throw new InvalidDataException("An unsupported Grok section header refuses the scan.");
                section = line; continue;
            }
            if (section is not ("[skills]" or "[plugins]") || !line.StartsWith("paths", StringComparison.Ordinal)) continue;
            var equal = line.IndexOf('=', StringComparison.Ordinal);
            if (equal < 0 || line[..equal].Trim() != "paths") throw new InvalidDataException("An unresolved Grok paths declaration refuses the scan.");
            var value = line[(equal + 1)..].Trim();
            while (!value.EndsWith(']') && i + 1 < lines.Length) value += SkillFrontmatter.StripComment(lines[++i], whitespaceRequired: false).Trim();
            // Only the documented string-array form is read. Unsupported TOML syntax is a closed seam, never an empty scan.
            string[] paths;
            try { paths = JsonSerializer.Deserialize<string[]>(value) ?? throw new InvalidDataException("Empty Grok paths declaration."); }
            catch (JsonException) { throw new InvalidDataException("Unsupported Grok paths syntax."); }
            foreach (var path in paths)
            {
                var resolved = ResolveExtra(path);
                yield return section == "[plugins]" ? Path.Combine(resolved, "skills") : resolved;
            }
        }
    }
}
