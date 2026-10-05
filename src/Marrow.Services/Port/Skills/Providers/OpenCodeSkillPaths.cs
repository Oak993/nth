// PORT-ONLY: SK-11d / WU-9, skills-PLAN.md 2.2 "OpenCode config merge" (opencode config.ts / paths.ts @ c42ae0d).
namespace Marrow.Services.Port.Skills.Providers;

public static class OpenCodeSkillPaths
{
    private const string Ambiguous = "Two config sources of unknown order set different skills.paths.";

    /// <summary>The user's effective skills.paths, last source wins, verbatim; or the reason the bundle is left out for this session.
    /// <paramref name="inlineContent"/> is the OPENCODE_CONFIG_CONTENT opencode will see as source 6 (null when Marrow's MCP JSON replaces it).</summary>
    public static OpenCodeSkillPathsResult Resolve(IReadOnlyDictionary<string, string> environment, OpenCodeSkillLaunch launch, string? inlineContent)
    {
        try { return OpenCodeSkillPathsResult.Of(ResolveCore(environment, launch, inlineContent)); }
        catch (OpenCodeConfigUnreadableException e) { return OpenCodeSkillPathsResult.Out(e.Message); }
        catch (LeftOut e) { return OpenCodeSkillPathsResult.Out(e.Message); }
    }

    private sealed class LeftOut(string reason) : Exception(reason);

    private static IReadOnlyList<string>? ResolveCore(IReadOnlyDictionary<string, string> env, OpenCodeSkillLaunch launch, string? inlineContent)
    {
        var configRoot = Value(env, "XDG_CONFIG_HOME");
        var dataRoot = Value(env, "XDG_DATA_HOME");
        var config = Path.Combine(configRoot ?? Path.Combine(launch.Home, ".config"), "opencode");
        var data = Path.Combine(dataRoot ?? Path.Combine(launch.Home, ".local", "share"), "opencode");

        // 1. Remote well-known config (needs the network and the user's auth): the bundle is left out.
        if (OpenCodeAuthReader.WellKnownPresent(Path.Combine(data, "auth.json"))) throw new LeftOut("A remote well-known config entry exists.");
        // 2. Legacy TOML: not parsed here.
        if (File.Exists(Path.Combine(config, "config"))) throw new LeftOut("The legacy TOML config exists.");

        IReadOnlyList<string>? effective = null;
        void Take(IReadOnlyList<string>? paths) { if (paths is not null) effective = paths; }

        foreach (var name in new[] { "config.json", "opencode.json", "opencode.jsonc" }) Take(ReadIfExists(Path.Combine(config, name)));
        // 3. The file named by OPENCODE_CONFIG.
        if (Value(env, "OPENCODE_CONFIG") is { } named) Take(ReadIfExists(Path.GetFullPath(named, launch.SessionFolder)));

        var folders = Folders(launch);
        // 4. Project files, outermost folder inward. opencode.jsonc and opencode.json of one folder have no stated order.
        if (Value(env, "OPENCODE_DISABLE_PROJECT_CONFIG") is null)
        {
            foreach (var folder in folders)
                Take(Unordered([Path.Combine(folder, "opencode.jsonc"), Path.Combine(folder, "opencode.json")]));
        }
        // 5. Config directories: .opencode folders (walking up, no stated direction), ~/.opencode, OPENCODE_CONFIG_DIR; each one's opencode.json then opencode.jsonc.
        var projectDirs = folders.Select(f => Path.Combine(f, ".opencode")).Where(Directory.Exists).ToList();
        Take(Unordered(projectDirs.SelectMany(DirectoryFiles)));
        Take(Directory.Exists(Path.Combine(launch.Home, ".opencode")) ? Ordered(DirectoryFiles(Path.Combine(launch.Home, ".opencode"))) : null);
        if (Value(env, "OPENCODE_CONFIG_DIR") is { } configDir) Take(Ordered(DirectoryFiles(Path.GetFullPath(configDir, launch.SessionFolder))));
        // 6. An OPENCODE_CONFIG_CONTENT the user already exports.
        if (inlineContent is not null) Take(OpenCodeConfigFile.ParseSkillPaths(System.Text.Encoding.UTF8.GetBytes(inlineContent)));
        return effective ?? [];
    }

    private static string? Value(IReadOnlyDictionary<string, string> env, string name) => env.TryGetValue(name, out var v) && v.Length != 0 ? v : null;
    private static IEnumerable<string> DirectoryFiles(string dir) => [Path.Combine(dir, "opencode.json"), Path.Combine(dir, "opencode.jsonc")];
    private static IReadOnlyList<string>? ReadIfExists(string file) => File.Exists(file) ? OpenCodeConfigFile.ReadSkillPaths(file) : null;

    private static IReadOnlyList<string>? Ordered(IEnumerable<string> files)
    {
        IReadOnlyList<string>? last = null;
        foreach (var file in files) if (ReadIfExists(file) is { } paths) last = paths;
        return last;
    }

    private static IReadOnlyList<string>? Unordered(IEnumerable<string> files)
    {
        IReadOnlyList<string>? first = null;
        foreach (var file in files)
        {
            if (ReadIfExists(file) is not { } paths) continue;
            if (first is not null && !first.SequenceEqual(paths, StringComparer.Ordinal)) throw new LeftOut(Ambiguous);
            first = paths;
        }
        return first;
    }

    // Folders from the outermost down to the session folder; the worktree is the nearest ancestor holding a .git entry, else the filesystem root.
    private static List<string> Folders(OpenCodeSkillLaunch launch)
    {
        var session = Path.TrimEndingDirectorySeparator(Path.GetFullPath(launch.SessionFolder));
        var stop = launch.WorktreeRoot is { } w ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(w)) : null;
        var up = new List<string>();
        for (string? dir = session; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            up.Add(dir);
            if (stop is not null ? string.Equals(dir, stop, PathComparison) : File.Exists(Path.Combine(dir, ".git")) || Directory.Exists(Path.Combine(dir, ".git"))) break;
        }
        up.Reverse();
        return up;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
