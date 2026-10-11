// PORT-ONLY: UE-3 (ED-NEW-providers-git-1). What keeps Git for Windows in use when its update cannot run, said plainly on the failed row: "Git Bash is running (used by Claude Code).
// Close it and try again." The names come from the running processes under Git's install folder (WindowsGitLocks) or, after the installer refused, from the installer's own list in its log.
using System.Text.RegularExpressions;

namespace Marrow.Services.Port.Updates;

/// <summary>One running process that keeps Git for Windows' files in use: its executable name (bash.exe) and, when known, the app that started it (Claude Code).</summary>
public sealed record GitLockingProcess(string Name, string? Host = null);

public static class GitInUse
{
    /// <summary>The words when nothing names what holds Git.</summary>
    public const string Fallback = "Git is in use by another app. Close it and try again.";

    private static readonly Regex Listed = new(@"^\s*(?<name>[^\s()]+\.exe)\s+\(PID\s+[0-9]+\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    // Git's own commands and the agents it ships keep their names; everything else under Git's folder (bash, sh, mintty and the usr\bin tools a Bash session runs) is Git Bash.
    private static readonly string[] Agents = ["ssh-agent", "ssh-pageant", "gpg-agent", "keyboxd", "scdaemon", "dirmngr"];

    /// <summary>The executable names the Git installer listed as using Git for Windows (its "The following process(es) use Git for Windows" message), in order, each once.</summary>
    public static IReadOnlyList<string> NamesFromLog(string log)
    {
        var start = log.IndexOf("use Git for Windows", StringComparison.Ordinal);
        if (start < 0) return [];
        var end = log.IndexOf("Please terminate", start, StringComparison.Ordinal);
        var list = end < 0 ? log[start..] : log[start..end];
        return Listed.Matches(list).Select(m => m.Groups["name"].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The row's sentence for the processes that hold Git: what is running, the apps that started it, and what to do.</summary>
    public static string Message(IReadOnlyList<GitLockingProcess> processes)
    {
        if (processes.Count == 0) return Fallback;
        var labels = processes.Select(p => Label(p.Name)).Distinct(StringComparer.Ordinal).OrderBy(l => l == "Git Bash" ? 0 : l == "a Git command" ? 1 : 2).ThenBy(l => l, StringComparer.Ordinal).ToList();
        var hosts = processes.Select(p => p.Host).OfType<string>().Where(h => h.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var what = Join(labels);
        what = char.ToUpperInvariant(what[0]) + what[1..];
        var one = labels.Count == 1;
        return $"{what} {(one ? "is" : "are")} running{(hosts.Count > 0 ? $" (used by {Join(hosts)})" : "")}. Close {(one ? "it" : "them")} and try again.";
    }

    internal static string Label(string executable)
    {
        var name = Path.GetFileNameWithoutExtension(executable).ToLowerInvariant();
        if (name == "git" || name.StartsWith("git-", StringComparison.Ordinal)) return "a Git command";
        return Agents.Contains(name) ? name : "Git Bash";
    }

    private static string Join(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => names[0] + " and " + names[1],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}
