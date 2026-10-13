// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a "From real actions to classes", 12.3 point 2; unit AS-5;
// ED-NEW-dextra-as5-12). No Swift twin. The classifier's view of one mission chat: its worktree (the only place a removal
// is plain work), the folder its commands start in, nth's own folders (never written by an agent: a hard deny), the
// environment that places paths such as %TEMP% or ~, and the probe that tells whether a bare command name is shadowed by
// a file in the working folder (cmd runs git.cmd from there before git on PATH, and an agent can write that folder).
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Port.Assistant;
using Marrow.Services.Assistant;

namespace Marrow.App.Assistant;

public static class MissionClassifier
{
    /// <summary>The environment values the classifier reads paths with (12.1a).</summary>
    public static readonly IReadOnlyList<string> EnvironmentKeys =
    [
        "USERPROFILE", "HOME", "HOMEDRIVE", "HOMEPATH", "TEMP", "TMP", "APPDATA", "LOCALAPPDATA", "PROGRAMDATA", "ProgramFiles",
        "ProgramFiles(x86)", "ProgramW6432", "SystemRoot", "SystemDrive", "windir", "PUBLIC", "OneDrive",
    ];

    /// <summary>cmd's default PATHEXT, plus .PS1 and .CPL, which Windows also opens by name: always checked, whatever PATHEXT says.</summary>
    public static readonly IReadOnlyList<string> RunnableExtensions =
        [".COM", ".EXE", ".BAT", ".CMD", ".VBS", ".VBE", ".JS", ".JSE", ".WSF", ".WSH", ".MSC", ".PS1", ".CPL"];

    /// <summary>
    /// The context for a provider approval of the mission's chat. A mission with no workspace yet gets no worktree and no
    /// folder, so every write and every command placed by folder asks.
    /// </summary>
    public static ClassifierContext For(MissionRecord record, IReadOnlyList<string> protectedRoots, ValueMap<string, string>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(protectedRoots);
        var workspace = record.Workspace;
        var working = workspace is null ? null : workspace.WorkingFolder.Length > 0 ? workspace.WorkingFolder : workspace.Path;
        return new ClassifierContext
        {
            MissionWorktree = workspace?.Path,
            WorkingDirectory = working,
            AccessFolders = workspace is null ? ValueList<string>.Empty : new ValueList<string>([workspace.Path]),
            ProtectedRoots = new ValueList<string>(protectedRoots.Where(r => !string.IsNullOrWhiteSpace(r))),
            Environment = environment ?? CurrentEnvironment(),
            CommandShadowed = Shadowed,
        };
    }

    /// <summary>
    /// The context for an approval of one chat of the mission, classified in that chat's own folder (its session's working
    /// folder; ED-NEW-dextra-as5-14): a relative path is placed where the chat really works. A chat working outside the
    /// worktree keeps the worktree as the only place a removal is plain work, so its own folder reads as outside; a chat
    /// nth cannot place (no folder) gets no worktree, no folder and no access folders, so every write and every command
    /// placed by folder asks.
    /// </summary>
    public static ClassifierContext ForAgent(MissionRecord record, IReadOnlyList<string> protectedRoots, string? agentFolder,
        ValueMap<string, string>? environment = null)
    {
        var context = For(record, protectedRoots, environment);
        if (record.Workspace is null) return context;
        if (string.IsNullOrWhiteSpace(agentFolder))
            return context with { MissionWorktree = null, WorkingDirectory = null, AccessFolders = ValueList<string>.Empty };
        return context with { WorkingDirectory = agentFolder };
    }

    /// <summary>The listed environment values of nth's own process.</summary>
    public static ValueMap<string, string> CurrentEnvironment()
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in EnvironmentKeys)
            if (System.Environment.GetEnvironmentVariable(key) is { Length: > 0 } value) values[key] = value;
        return new ValueMap<string, string>(values);
    }

    /// <summary>
    /// Whether a file in <paramref name="folder"/> would run instead of the bare command <paramref name="name"/> found on
    /// PATH: a file named exactly so when the name carries a runnable extension, or the name with any runnable extension
    /// (PATHEXT's and the defaults). A name that is not bare, or a folder nth cannot read, counts as shadowed (fail
    /// closed); a folder that does not exist shadows nothing.
    /// </summary>
    public static bool Shadowed(string folder, string name)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(name)) return true;
        if (name.IndexOfAny([.. Path.GetInvalidFileNameChars(), '/', '\\', ':', '*', '?']) >= 0 || name is "." or "..") return true;
        try
        {
            if (!Directory.Exists(folder)) return false;
            var extensions = Extensions();
            var own = Path.GetExtension(name);
            if (own.Length > 0 && extensions.Contains(own) && File.Exists(Path.Combine(folder, name))) return true;
            foreach (var path in Directory.EnumerateFileSystemEntries(folder, name + ".*"))
            {
                var file = Path.GetFileName(path);
                if (file.Length > name.Length && string.Equals(file[..name.Length], name, StringComparison.OrdinalIgnoreCase)
                    && extensions.Contains(file[name.Length..]))
                    return true;
            }
            return false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static HashSet<string> Extensions()
    {
        var set = new HashSet<string>(RunnableExtensions, StringComparer.OrdinalIgnoreCase);
        if (System.Environment.GetEnvironmentVariable("PATHEXT") is { Length: > 0 } pathext)
            foreach (var ext in pathext.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (ext.StartsWith('.')) set.Add(ext);
        return set;
    }
}
