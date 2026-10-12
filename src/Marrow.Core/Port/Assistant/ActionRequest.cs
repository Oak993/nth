// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.1a; unit AS-1; ED-NEW-dextracore-2). No Swift twin.
// What the classifier reads (a shell command, an MCP tool call, a native file or web tool, one of nth-assistant's own
// tools), what it knows about the mission (worktree, access folders, the allowlist), and what it says back.
using Marrow.Core.Port;

namespace Marrow.Core.Port.Assistant;

/// <summary>One action an agent or the assistant asks to take.</summary>
public abstract record ActionRequest
{
    private ActionRequest() { }

    /// <summary>A shell command line. Auto reads it as PowerShell, cmd and POSIX and keeps the strictest reading.</summary>
    public sealed record ShellCommand(string Command, ShellKind Shell = ShellKind.Auto, string? WorkingDirectory = null) : ActionRequest;

    /// <summary>An MCP tool call, with the server's declared annotations (which can only make the class stricter).</summary>
    public sealed record McpTool(string Server, string Tool, string? Description = null, McpToolAnnotations? Annotations = null) : ActionRequest;

    /// <summary>A provider's native file read.</summary>
    public sealed record FileRead(string Path) : ActionRequest;

    /// <summary>A provider's native file write or edit.</summary>
    public sealed record FileWrite(string Path) : ActionRequest;

    /// <summary>A provider's native web fetch.</summary>
    public sealed record WebFetch(string Url) : ActionRequest;

    /// <summary>A provider's native web search.</summary>
    public sealed record WebSearch(string Query) : ActionRequest;

    /// <summary>One of the nth-assistant tools of 15.5, by name.</summary>
    public sealed record AssistantTool(string Name) : ActionRequest;
}

/// <summary>An MCP server's declared tool annotations. A server can lie, so they only ever make a class stricter.</summary>
public sealed record McpToolAnnotations(bool? ReadOnlyHint = null, bool? DestructiveHint = null, bool? OpenWorldHint = null, bool? IdempotentHint = null);

/// <summary>What the classifier knows about where an action runs. Paths are full Windows paths.</summary>
public sealed record ClassifierContext
{
    /// <summary>The mission's own worktree (12.6a): the only place a removal is plain work.</summary>
    public string? MissionWorktree { get; init; }

    /// <summary>The project's access folders: readable alone, and writable alone when there is no mission worktree.</summary>
    public ValueList<string> AccessFolders { get; init; } = ValueList<string>.Empty;

    /// <summary>The assistant's own home folder.</summary>
    public string? AssistantFolder { get; init; }

    /// <summary>Where a command starts when the request names no folder. Defaults to the mission worktree.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>"Commands the Assistant may run alone" (12.1a), as command lines: "dotnet test", "npm run build".</summary>
    public ValueList<string> Allowlist { get; init; } = ValueList<string>.Empty;

    /// <summary>Full paths of the scripts that are unchanged since the mission started (the caller hashes them).</summary>
    public ValueList<string> UnchangedScripts { get; init; } = ValueList<string>.Empty;

    /// <summary>nth's own folders (settings, grants, audit): never written by an agent (hard deny).</summary>
    public ValueList<string> ProtectedRoots { get; init; } = ValueList<string>.Empty;

    /// <summary>Environment values used to read paths such as $env:USERPROFILE, %TEMP% or ~ (HOME, USERPROFILE, TEMP...).</summary>
    public ValueMap<string, string> Environment { get; init; } = ValueMap<string, string>.Empty;

    /// <summary>
    /// cmd runs a file from the working folder before one on PATH, so "git" there may be a git.cmd an agent wrote.
    /// Given the working folder and a bare command name, says whether such a file shadows it. Null: not checked, and
    /// a bare command read with cmd's grammar in a folder an agent can write is Unknown.
    /// </summary>
    public Func<string, string, bool>? CommandShadowed { get; init; }
}

/// <summary>What one of nth-assistant's tools needs from the engine beyond its class (12.4, 15.5).</summary>
public enum AssistantToolGuard
{
    None,
    /// <summary>Creates or changes a duty, watcher, Astur, grant, reserve rule or capability: always a card, refused when tainted.</summary>
    PermanentConfiguration,
    /// <summary>Adds a line to the Suggestions lane: refused when tainted.</summary>
    Suggestion,
    /// <summary>Writes a memory note: quarantined when tainted.</summary>
    MemoryWrite,
    /// <summary>Deletes a memory note: asks when tainted (a deletion cannot be quarantined and undone, 8.2).</summary>
    MemoryDelete,
    /// <summary>Hands work to an Astur: data only, and the child starts tainted, when tainted.</summary>
    Delegation,
}

/// <summary>
/// The class of an action, with what the engine needs to decide it: the target (for grants), the field, whether a
/// network call carries data, whether it reads untrusted content, whether a read is outside the access folders. A
/// command of several parts lists each part in Parts; the engine decides each part and keeps the strictest verdict.
/// </summary>
public sealed record Classification(ActionClass Class, string Reason)
{
    /// <summary>What the action touches, as text for the card (a path, an origin, a server).</summary>
    public string? Target { get; init; }

    /// <summary>What the action touches, as a grant target.</summary>
    public GrantTarget? GrantTarget { get; init; }

    /// <summary>The field within the class ("read", "send", "sign in"), when known.</summary>
    public string? Field { get; init; }

    /// <summary>A network call that carries data (a body, a query, credentials): asks once the agent is tainted.</summary>
    public bool CarriesData { get; init; }

    /// <summary>The action reads untrusted content (web, email, messages, PDFs, files outside the access folders).</summary>
    public bool ReadsUntrusted { get; init; }

    /// <summary>A read outside the access folders: asks unless a folder grant covers it.</summary>
    public bool OutsideAccess { get; init; }

    /// <summary>For Money: what kind of money movement it is.</summary>
    public MoneyKind Money { get; init; }

    /// <summary>For nth-assistant tools: the extra rule that applies.</summary>
    public AssistantToolGuard Guard { get; init; }

    /// <summary>The parts of a command made of several, each a leaf. Empty for a leaf.</summary>
    public ValueList<Classification> Parts { get; init; } = ValueList<Classification>.Empty;

    /// <summary>Every leaf: this one, or each of its parts.</summary>
    public IEnumerable<Classification> Leaves => Parts.Count == 0 ? [this] : Parts.SelectMany(p => p.Leaves);

    /// <summary>
    /// One classification for several leaves: named after the strictest leaf, with every leaf kept in Parts and the
    /// flags of all of them. One leaf is returned as it is.
    /// </summary>
    public static Classification Of(IEnumerable<Classification> leaves)
    {
        var all = leaves.SelectMany(l => l.Leaves).ToList();
        if (all.Count == 0) return new Classification(ActionClass.Work, "runs nothing");
        if (all.Count == 1) return all[0];
        var strictest = all.MaxBy(l => ActionClasses.Strictness(l.Class) + (l.OutsideAccess ? 46 : 0) + (l.CarriesData ? 1 : 0))!;
        return strictest with
        {
            CarriesData = all.Any(l => l.CarriesData),
            ReadsUntrusted = all.Any(l => l.ReadsUntrusted),
            OutsideAccess = all.Any(l => l.OutsideAccess),
            Parts = new ValueList<Classification>(all),
        };
    }
}
