// PORT-ONLY: Ignis (OD-0134, ED-NEW-ignis-runtime). Read-only look at the Claude settings files a CLI launch will load,
// for the user's own prompt-cache lifetime choices (IgnisCacheLifetime.SettingsKeys): the user file
// (CLAUDE_CONFIG_DIR, else <home>/.claude, settings.json), the project's .claude/settings.json and settings.local.json,
// and the machine's managed-settings.json. nth never writes any of them. A missing or unreadable file is skipped.
using Marrow.Core.Port.Ignis.Engine;

namespace Marrow.Services.Port.Ignis;

public static class IgnisClaudeSettings
{
    /// <summary>SOURCE FACT (Claude Code settings docs): the Windows managed settings file.</summary>
    public static string ManagedPath { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ClaudeCode", "managed-settings.json");

    /// <summary>The settings files a launch in <paramref name="workingDirectory"/> with <paramref name="environment"/> reads.</summary>
    public static IReadOnlyList<string> Paths(IReadOnlyDictionary<string, string> environment, string? workingDirectory, string? home, string? managedPath = null)
    {
        var paths = new List<string>();
        var configDir = Value(environment, "CLAUDE_CONFIG_DIR") is { Length: > 0 } dir ? dir
            : (Value(environment, "HOME") ?? Value(environment, "USERPROFILE") ?? home) is { Length: > 0 } userHome ? Path.Combine(userHome, ".claude")
            : null;
        if (configDir is not null) paths.Add(Path.Combine(configDir, "settings.json"));
        if (workingDirectory is { Length: > 0 })
        {
            paths.Add(Path.Combine(workingDirectory, ".claude", "settings.json"));
            paths.Add(Path.Combine(workingDirectory, ".claude", "settings.local.json"));
        }
        paths.Add(managedPath ?? ManagedPath);
        return paths;
    }

    /// <summary>The user's TTL choices found in those files (setting names and env-block variable names).</summary>
    public static IReadOnlySet<string> UserTtlChoices(IReadOnlyDictionary<string, string> environment, string? workingDirectory, string? home, string? managedPath = null)
    {
        var texts = new List<string>();
        foreach (var path in Paths(environment, workingDirectory, home, managedPath))
        {
            try
            {
                if (File.Exists(path)) texts.Add(File.ReadAllText(path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return IgnisCacheLifetime.SettingsKeys(texts);
    }

    private static string? Value(IReadOnlyDictionary<string, string> environment, string name)
    {
        if (environment.TryGetValue(name, out var exact)) return exact;
        foreach (var (key, value) in environment) if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return value;
        return null;
    }
}
