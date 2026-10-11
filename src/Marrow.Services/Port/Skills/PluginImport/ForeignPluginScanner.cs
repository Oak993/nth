// PORT-ONLY: owner request 2026-10-11 (ED-NEW-mcpimport-2). The plugins other AI apps installed on this PC, read-only and under an EXPLICIT home (the pattern of SK-18's ForeignSkillScanner):
// Claude Code's ~/.claude/plugins (installed_plugins.json, each entry's installPath; known_marketplaces.json's installLocation when an entry has none; the plugin cache when there is no list), Codex's
// ~/.codex/plugins/cache (the place ProviderSkillReports knows), and folders the user picks. A plugin is a folder with .claude-plugin/plugin.json (or .codex-plugin/plugin.json) naming it. Shortcuts are
// not followed; a file that cannot be understood is reported and left out, never thrown.
using System.Text.Json;
using Marrow.Services.Port.Skills.Import;

namespace Marrow.Services.Port.Skills.PluginImport;

public enum ForeignPluginApp
{
    ClaudeCode,
    Codex,
    Folder,
}

/// <summary>One plugin another app installed. <see cref="Source"/> is its marketplace or the folder chosen; <see cref="Path"/> is the plugin's folder (where its manifest folder is).</summary>
public sealed record ForeignPlugin(ForeignPluginApp App, string Name, string Description, string Source, string Path)
{
    public string Key => App + "|" + Path;
}

public sealed record ForeignPluginScan(IReadOnlyList<ForeignPlugin> Plugins, IReadOnlyList<ScanProblem> Problems);

public sealed class ForeignPluginScanner
{
    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private const int MaxDepth = 5;
    private const long MaxManifestBytes = 1024 * 1024;
    private readonly string home;
    private readonly string[] picked;

    public ForeignPluginScanner(string home, IEnumerable<string>? pickedFolders = null)
    {
        if (string.IsNullOrWhiteSpace(home) || !System.IO.Path.IsPathFullyQualified(home)) throw new ArgumentException("An explicit absolute home folder is required.", nameof(home));
        this.home = System.IO.Path.GetFullPath(home);
        picked = [.. pickedFolders ?? []];
    }

    public ForeignPluginScan Scan()
    {
        var found = new Dictionary<string, ForeignPlugin>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<ScanProblem>();
        void Add(ForeignPlugin? plugin) { if (plugin is not null) found.TryAdd(System.IO.Path.GetFullPath(plugin.Path), plugin); }

        ClaudeCode(Add, problems);
        foreach (var dir in Find(System.IO.Path.Combine(home, ".codex", "plugins", "cache"), 0, problems))
            Add(Read(ForeignPluginApp.Codex, SourceUnder(System.IO.Path.Combine(home, ".codex", "plugins", "cache"), dir), dir, problems));
        foreach (var folder in picked)
        {
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Path.IsPathFullyQualified(folder)) { problems.Add(new ScanProblem(folder, "Choose a folder by its full path.")); continue; }
            var path = System.IO.Path.GetFullPath(folder);
            if (!Directory.Exists(path)) { problems.Add(new ScanProblem(path, "This folder was not found.")); continue; }
            var label = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(path));
            string[] dirs = Manifest(path) is not null ? [path] : Find(path, 0, problems).ToArray();
            foreach (var dir in dirs) if (!found.ContainsKey(System.IO.Path.GetFullPath(dir))) Add(Read(ForeignPluginApp.Folder, label.Length == 0 ? path : label, dir, problems));
        }
        var plugins = found.Values.OrderBy(p => p.App).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Path, StringComparer.Ordinal).ToArray();
        return new ForeignPluginScan(plugins, problems);
    }

    private void ClaudeCode(Action<ForeignPlugin?> add, List<ScanProblem> problems)
    {
        var root = System.IO.Path.Combine(home, ".claude", "plugins");
        if (!Directory.Exists(root)) return;
        var listFile = System.IO.Path.Combine(root, "installed_plugins.json");
        if (!File.Exists(listFile))
        {
            // no list: what the plugin cache holds (newest version of each)
            var cache = System.IO.Path.Combine(root, "cache");
            foreach (var group in Find(cache, 0, problems).GroupBy(d => SourceUnder(cache, d) + "|" + Parent(cache, d), StringComparer.OrdinalIgnoreCase))
            {
                var newest = group.OrderByDescending(d => Directory.GetLastWriteTimeUtc(d)).ThenByDescending(d => d, StringComparer.Ordinal).First();
                add(Read(ForeignPluginApp.ClaudeCode, SourceUnder(cache, newest), newest, problems));
            }
            return;
        }
        var marketplaces = Marketplaces(System.IO.Path.Combine(root, "known_marketplaces.json"), problems);
        try
        {
            using var document = JsonDocument.Parse(ReadText(listFile), Lenient);
            if (!document.RootElement.TryGetProperty("plugins", out var plugins) || plugins.ValueKind != JsonValueKind.Object) return;
            foreach (var plugin in plugins.EnumerateObject())
            {
                var at = plugin.Name.LastIndexOf('@');
                var name = at > 0 ? plugin.Name[..at] : plugin.Name;
                var marketplace = at > 0 ? plugin.Name[(at + 1)..] : "";
                // version 2 keeps a list of installs per plugin, version 1 one object
                var installs = plugin.Value.ValueKind == JsonValueKind.Array ? plugin.Value.EnumerateArray().ToArray() : [plugin.Value];
                var paths = installs.Where(i => i.ValueKind == JsonValueKind.Object && i.TryGetProperty("installPath", out var p) && p.ValueKind == JsonValueKind.String)
                    .Select(i => i.GetProperty("installPath").GetString()!).Where(p => p.Length > 0 && System.IO.Path.IsPathFullyQualified(p)).ToList();
                if (paths.Count == 0 && marketplaces.TryGetValue(marketplace, out var location))
                    paths.AddRange(new[] { System.IO.Path.Combine(location, "plugins", name), System.IO.Path.Combine(location, name), location }.Where(p => Manifest(p) is not null));
                var dir = paths.FirstOrDefault(p => Directory.Exists(p) && Manifest(p) is not null);
                if (dir is null) { problems.Add(new ScanProblem(listFile, "“" + name + "” was not found where Claude Code installed it.")); continue; }
                add(Read(ForeignPluginApp.ClaudeCode, marketplace, dir, problems));
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            problems.Add(new ScanProblem(listFile, "This file could not be read."));
        }
    }

    private static Dictionary<string, string> Marketplaces(string file, List<ScanProblem> problems)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!File.Exists(file)) return result;
        try
        {
            using var document = JsonDocument.Parse(ReadText(file), Lenient);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var market in document.RootElement.EnumerateObject())
                if (market.Value.ValueKind == JsonValueKind.Object && market.Value.TryGetProperty("installLocation", out var location) && location.ValueKind == JsonValueKind.String && System.IO.Path.IsPathFullyQualified(location.GetString()!))
                    result[market.Name] = location.GetString()!;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            problems.Add(new ScanProblem(file, "This file could not be read."));
        }
        return result;
    }

    private static string SourceUnder(string root, string dir)
    {
        var relative = System.IO.Path.GetRelativePath(root, dir).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return relative.Length > 1 ? relative[0] : "";
    }

    private static string Parent(string root, string dir)
    {
        var relative = System.IO.Path.GetRelativePath(root, dir).Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        return relative.Length > 1 ? relative[1] : relative[0];
    }

    /// <summary>The plugin manifest of a folder, or null.</summary>
    private static string? Manifest(string dir)
    {
        foreach (var marker in new[] { ".claude-plugin", ".codex-plugin" })
        {
            var file = System.IO.Path.Combine(dir, marker, "plugin.json");
            if (File.Exists(file)) return file;
        }
        return null;
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return false; }
    }

    /// <summary>Plugin folders below <paramref name="root"/>: a folder with a manifest is one, and is not looked into further.</summary>
    private static IEnumerable<string> Find(string root, int depth, List<ScanProblem> problems)
    {
        if (depth > MaxDepth || !Directory.Exists(root) || IsLink(root)) yield break;
        string[] dirs;
        try { dirs = [.. Directory.GetDirectories(root).Order(StringComparer.Ordinal)]; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { problems.Add(new ScanProblem(root, "This folder could not be read.")); yield break; }
        foreach (var dir in dirs)
        {
            var leaf = System.IO.Path.GetFileName(dir);
            if (leaf is ".git" or "node_modules" || leaf.StartsWith('.') || IsLink(dir)) continue;
            if (Manifest(dir) is not null) yield return dir;
            else foreach (var inner in Find(dir, depth + 1, problems)) yield return inner;
        }
    }

    private static ForeignPlugin? Read(ForeignPluginApp app, string source, string dir, List<ScanProblem> problems)
    {
        var file = Manifest(dir);
        if (file is null) return null;
        try
        {
            using var document = JsonDocument.Parse(ReadText(file), Lenient);
            var root = document.RootElement;
            var name = root.ValueKind == JsonValueKind.Object && root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString()!.Trim() : "";
            if (name.Length == 0) { problems.Add(new ScanProblem(file, "The manifest has no name.")); return null; }
            var description = root.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()!.Trim() : "";
            return new ForeignPlugin(app, name, description, source, dir);
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            problems.Add(new ScanProblem(file, "The manifest could not be read."));
            return null;
        }
    }

    private static string ReadText(string file)
    {
        var info = new FileInfo(file);
        if (info.Length > MaxManifestBytes) throw new IOException("The file is too big.");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
