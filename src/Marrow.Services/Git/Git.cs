using System.Text;
using Marrow.Core.Port;
using Marrow.Core.Support;

// Source: DroppyCode/Services/Git/Git.swift (B2 1fbf81e), :1-722. Ported in part 1 of this unit: :4-119, :131-152,
// :233-242, :259-265, :293-379, :517-524 (the fast path; the rest of indexPath is part 2, PR-GIT-027) and :719-722.
// Not yet ported, and why: :121-129, :154-231, :244-257, :267-291, :381-516, :525-715 belong to parts 2 to 4 of the
// Services.Git.Git unit (their records are PR-GIT-027 onward), and are added there.

namespace Marrow.Services.Git;

/// <summary>Git.swift:4-10.</summary>
public sealed record GitStatus
{
    public string? Branch { get; init; }   // :5
    public string? Upstream { get; init; } // :6
    public long Ahead { get; init; }       // :7 = 0
    public long Behind { get; init; }      // :8 = 0
    public long ChangedFiles { get; init; } // :9 = 0
}

/// <summary>Git.swift:12-18.</summary>
public sealed record GitBranch(string Name, bool IsCurrent, string? Upstream)
{
    /// <summary>:17</summary>
    public string Id => Name;
}

/// <summary>Git.swift:20-717. A thin wrapper around the git command line. Every call runs off the main actor.</summary>
public sealed record Git
{
    /// <summary>
    /// :22 <c>let directory: URL</c>, held as the file URL's path. ENGINEERING DECISION (as Timeline.swift:95's port):
    /// F.URL builds only URL(string:), and every use of <c>directory</c> in this file is a path (the working directory
    /// of each git process, the component walk of repositoryRoot, the .git join of indexPath).
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// :24-26 <c>URL(fileURLWithPath: path)</c>: a path that is not absolute is taken against the current directory;
    /// an absolute one is kept in the spelling the caller gave (no symlink or case resolution, upstream defect 86).
    /// </summary>
    public Git(string path) =>
        Directory = SwiftPath.IsAbsolute(path) ? path : SwiftPath.AppendingPathComponent(Environment.CurrentDirectory, path);

    // :28
    private static string Executable => LoginEnvironment.Git;

    /// <summary>:30-49. <paramref name="environment"/> null is Swift's default <c>[:]</c>.</summary>
    public async Task<ShellResult> Run(
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null,
        byte[]? input = null,
        double timeout = 120)
    {
        // :37-40. LoginEnvironment.current is case-insensitive and Mutating keeps its comparer (ED-128), so an extra in
        // another case keeps the first spelling and takes the extra's value; extras are merged last and win.
        var merged = LoginEnvironment.Current.Mutating(values =>
        {
            values["GIT_TERMINAL_PROMPT"] = "0";
            values["GIT_OPTIONAL_LOCKS"] = "0";
            if (environment is { } extra)
                foreach (var (key, value) in extra) values[key] = value;
        });
        // :41-48. An argv array to CreateProcessW, never a shell (R-PROC-1), so %(...), ^{tree} reach git as written.
        return await Shell.Run(
            Executable,
            ["-c", "core.quotepath=false", "-c", "color.ui=false", .. arguments],
            @in: Directory,
            environment: merged,
            input: input,
            timeout: timeout);
    }

    /// <summary>:51-55</summary>
    public async Task<string> Output(IReadOnlyList<string> arguments)
    {
        var result = await Run(arguments);
        Check(result);
        return result.Output;
    }

    /// <summary>:57-59</summary>
    public static void Check(ShellResult result)
    {
        if (!result.Succeeded) throw new ShellError(result.FailureMessage);
    }

    // MARK: - Repository

    /// <summary>:63-65</summary>
    public async Task<bool> IsRepository()
    {
        var result = await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["rev-parse", "--is-inside-work-tree"]));
        return result is { } answered && SwiftString.Equal(answered.TrimmedOutput, "true");
    }

    /// <summary>:67-74. The URL's path after one deleteLastPathComponent per prefix segment.</summary>
    public async Task<string> RepositoryRoot()
    {
        // :68
        var prefix = await Output(["rev-parse", "--show-prefix"]);
        // :69-72
        var root = Directory;
        foreach (var _ in SwiftString.Split(prefix.Trimmed(), "/"))
            root = SwiftPath.FileURLDeletingLastPathComponentPath(root);
        // :73. UPSTREAM DEFECT 86, reproduced: the root is counted off the app's own path, never asked of git.
        return root;
    }

    /// <summary>:76-78</summary>
    public async Task<bool> HasCommits() =>
        (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["rev-parse", "--verify", "--quiet", "HEAD"])))?.Succeeded ?? false;

    /// <summary>:80-98</summary>
    public async Task<GitStatus?> Status()
    {
        // :81
        if (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["status", "--porcelain=v2", "--branch"])) is not { Succeeded: true } result)
            return null;
        // :82
        var status = new GitStatus();
        // :83-96
        foreach (var line in SwiftString.Split(result.Output, "\n"))
        {
            if (Value(line, after: "# branch.head ") is { } head)
            {
                // :85
                status = status with { Branch = SwiftString.Equal(head, "(detached)") ? null : head };
            }
            else if (Value(line, after: "# branch.upstream ") is { } upstream)
            {
                // :87
                status = status with { Upstream = upstream };
            }
            else if (Value(line, after: "# branch.ab ") is { } counts)
            {
                // :89-92
                foreach (var token in SwiftString.Split(counts, " "))
                {
                    if (SwiftString.HasPrefix(token, "+")) status = status with { Ahead = SwiftNumber.ParseInt(SwiftString.DropFirst(token)) ?? 0 };
                    if (SwiftString.HasPrefix(token, "-")) status = status with { Behind = SwiftNumber.ParseInt(SwiftString.DropFirst(token)) ?? 0 };
                }
            }
            else if (!SwiftString.HasPrefix(line, "#"))
            {
                // :94
                status = status with { ChangedFiles = checked(status.ChangedFiles + 1) };
            }
        }
        // :97
        return status;
    }

    /// <summary>:100-111</summary>
    public async Task<IReadOnlyList<GitBranch>> Branches()
    {
        // :101
        const string format = "%(HEAD)%09%(refname:short)%09%(upstream:short)";
        // :102-104
        if (await SwiftTry.OptionalAsync<string>(async () =>
                await Output(["for-each-ref", "--sort=-committerdate", "--format=" + format, "refs/heads"])) is not { } text)
            return [];
        // :105-110
        var branches = new List<GitBranch>();
        foreach (var line in SwiftString.Split(text, "\n"))
        {
            var parts = SwiftString.Split(line, "\t", omittingEmptySubsequences: false);
            if (parts.Count < 2) continue;
            var upstream = parts.Count > 2 && parts[2].Length != 0 ? parts[2] : null;
            branches.Add(new GitBranch(parts[1], SwiftString.Equal(parts[0], "*"), upstream));
        }
        return branches;
    }

    /// <summary>:113-115</summary>
    public async Task SwitchBranch(string name) => Check(await Run(["switch", name]));

    /// <summary>:117-119</summary>
    public async Task CreateBranch(string name) => Check(await Run(["switch", "-c", name]));

    /// <summary>:131-134</summary>
    public async Task<string?> RemoteURL()
    {
        if (await SwiftTry.OptionalAsync<string>(async () => await Output(["remote", "get-url", "origin"])) is not { } text)
            return null;
        return text.Trimmed().NilIfEmpty();
    }

    /// <summary>:136-147 The https page for the origin remote, when it is a hosted repository.</summary>
    public async Task<SwiftURL?> RemoteWebURL()
    {
        // :138
        if (await RemoteURL() is not { } remote) return null;
        // :139 removeLast(4) drops four Characters.
        if (SwiftString.HasSuffix(remote, ".git")) remote = SwiftString.DropLast(remote, 4);
        // :140-145
        if (SwiftString.HasPrefix(remote, "git@") && FirstIndex(remote, of: ":") is { } colon)
        {
            // :141 "git@" is four ASCII Characters, so offset 4 is the Character after it.
            var host = remote[4..colon];
            // :142 remote.index(after: colon) is one Character, ":", past it.
            remote = "https://" + host + "/" + remote[(colon + 1)..];
        }
        else if (SwiftString.HasPrefix(remote, "ssh://git@"))
        {
            // :144
            remote = "https://" + SwiftString.DropFirst(remote, SwiftString.Count("ssh://git@"));
        }
        // :146
        return SwiftString.HasPrefix(remote, "http") ? SwiftURL.Create(remote) : null;
    }

    /// <summary>:149-152</summary>
    public async Task<IReadOnlyList<string>> ListFiles()
    {
        if (await SwiftTry.OptionalAsync<string>(async () =>
                await Output(["ls-files", "--cached", "--others", "--exclude-standard"])) is not { } text)
            return [];
        return SwiftString.Split(text, "\n");
    }

    // MARK: - Landing a team's work

    /// <summary>:231-242 The branch the remote treats as its default: what origin's HEAD points at, else main or master
    /// where one exists.</summary>
    public async Task<string> DefaultBranch()
    {
        // :234-237
        if (await SwiftTry.OptionalAsync<string>(async () =>
                (await Output(["symbolic-ref", "--short", "refs/remotes/origin/HEAD"])).Trimmed()) is { } head
            && head.Length != 0)
        {
            return SwiftString.HasPrefix(head, "origin/") ? SwiftString.DropFirst(head, SwiftString.Count("origin/")) : head;
        }
        // :238-240
        foreach (var candidate in new[] { "main", "master" })
        {
            var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["rev-parse", "--verify", "--quiet", "refs/remotes/origin/" + candidate]));
            if (result?.Succeeded == true) return candidate;
        }
        // :241
        return "main";
    }

    /// <summary>:259-261</summary>
    public async Task<string> TreeHash(string of) => (await Output(["rev-parse", of + "^{tree}"])).Trimmed();

    /// <summary>:263-265</summary>
    public async Task<string> CommitHash(string of = "HEAD") => (await Output(["rev-parse", "--verify", of])).Trimmed();

    /// <summary>:293-295</summary>
    public async Task<bool> IsAncestor(string commit, string of) =>
        (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["merge-base", "--is-ancestor", commit, of])))?.Succeeded ?? false;

    /// <summary>:297-301 Paths that differ between two commits or trees, as they are on disk: NUL-separated, so a name
    /// with a quote or a tab in it comes back unquoted and usable as a path.</summary>
    public async Task<IReadOnlyList<string>> ChangedPaths(string from, string to) =>
        SwiftString.Split(await Output(["diff", "--name-only", "-z", from, to]), "\0");

    /// <summary>:303-319 Paths with uncommitted changes, staged or not, untracked ones included.</summary>
    public async Task<IReadOnlyList<string>> DirtyPaths()
    {
        // :305
        if (await SwiftTry.OptionalAsync<string>(async () =>
                await Output(["status", "--porcelain=v1", "-z", "--untracked-files=all"])) is not { } text)
            return [];
        // :306-307
        var records = new Queue<string>(SwiftString.Split(text, "\0", omittingEmptySubsequences: false));
        var paths = new List<string>();
        // :308-317
        while (records.TryDequeue(out var entry))
        {
            if (SwiftString.Count(entry) <= 3) continue;
            var code = SwiftString.Prefix(entry, 2);
            paths.Add(SwiftString.DropFirst(entry, 3));
            // A rename or a copy is two records: the new name, then the name it came from.
            // That second record is not an entry of its own, and both names are dirty.
            if ((Contains(code, "R") || Contains(code, "C")) && records.TryDequeue(out var origin) && origin.Length != 0)
                paths.Add(origin);
        }
        // :318
        return paths;
    }

    /// <summary>:321-325 Paths from <paramref name="among"/> that the checkout ignores.</summary>
    public async Task<IReadOnlySet<string>> IgnoredPaths(IReadOnlyList<string> among)
    {
        if (among.Count == 0) return new HashSet<string>(SwiftStringComparer.Instance);
        if (await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["check-ignore", "-z", "--stdin"], input: NulSeparated(among))) is not { } result
            || !(result.Succeeded || result.Status == 1))
            return new HashSet<string>(SwiftStringComparer.Instance);
        // :324
        return new HashSet<string>(SwiftString.Split(result.Output, "\0", omittingEmptySubsequences: true), SwiftStringComparer.Instance);
    }

    /// <summary>:327-335 Paths from <paramref name="among"/> that HEAD holds.</summary>
    public Task<IReadOnlySet<string>> TrackedPaths(IReadOnlyList<string> among) => PresentPaths(among, @in: "HEAD");

    /// <summary>:337-354 Paths from <paramref name="among"/> that <paramref name="in"/> holds, answered by one
    /// <c>cat-file --batch-check</c> over the lot.</summary>
    public async Task<IReadOnlySet<string>> PresentPaths(IReadOnlyList<string> among, string @in)
    {
        // :342
        if (among.Count == 0) return new HashSet<string>(SwiftStringComparer.Instance);
        // :343-346
        var input = new List<byte>();
        foreach (var path in among)
        {
            input.AddRange(Encoding.UTF8.GetBytes(@in + ":" + path));
            input.Add(0);
        }
        // :347
        if (await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["cat-file", "--batch-check=%(objecttype)", "-Z"], input: [.. input])) is not { Succeeded: true } result)
            return new HashSet<string>(SwiftStringComparer.Instance);
        // :348
        var answers = SwiftString.Split(result.Output, "\0", omittingEmptySubsequences: false);
        var tracked = new HashSet<string>(SwiftStringComparer.Instance);
        // :350-352. UPSTREAM DEFECT 80, reproduced: zip stops at the shorter side, so an unanswered tail reads as absent.
        foreach (var (path, answer) in among.Zip(answers))
            if (!SwiftString.HasSuffix(answer, " missing")) tracked.Add(path);
        // :353
        return tracked;
    }

    /// <summary>:356-361</summary>
    private static byte[] NulSeparated(IReadOnlyList<string> paths)
    {
        var data = new List<byte>();
        foreach (var path in paths)
        {
            data.AddRange(Encoding.UTF8.GetBytes(path));
            data.Add(0);
        }
        return [.. data];
    }

    /// <summary>:363-379 Whether a rebase, merge or cherry-pick is underway: nothing may move then. A directory git
    /// cannot be asked about counts as busy.</summary>
    public async Task<bool> HasOperationInProgress()
    {
        // :373-374
        if (await SwiftTry.OptionalAsync<string>(async () =>
                (await Output(["rev-parse", "--path-format=absolute", "--git-dir"])).Trimmed()) is not { } gitDirectory
            || gitDirectory.Length == 0)
            return true;
        // :375-377. Joined with a literal "/" as the source does; Windows accepts it.
        foreach (var marker in new[] { "rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG" })
        {
            var path = gitDirectory + "/" + marker;
            if (File.Exists(path) || System.IO.Directory.Exists(path)) return true;
        }
        // :378
        return false;
    }

    /// <summary>
    /// :511-538 The repository's index file. A checkout with its own <c>.git</c> directory keeps it right there, which
    /// saves the git process that asking would cost on every snapshot. Private in Swift; internal so the tests reach it
    /// (R-NAME-6).
    /// </summary>
    internal async Task<string?> IndexPath()
    {
        // :518-521
        var dotGit = SwiftPath.FileURLAppendingPathComponentPath(Directory, ".git");
        var environment = LoginEnvironment.Current;
        var exists = File.Exists(dotGit) || System.IO.Directory.Exists(dotGit);
        var isDirectory = System.IO.Directory.Exists(dotGit);
        // :522-524
        if (exists && isDirectory && !environment.ContainsKey("GIT_DIR") && !environment.ContainsKey("GIT_INDEX_FILE"))
            return dotGit + "/index";
        await Task.CompletedTask;
        throw new NotImplementedException("MARROW_STUB DroppyCode/Services/Git/Git.swift:525-537");
    }

    /// <summary>:719-722 <c>Substring.value(after:)</c>, a private extension of the file.</summary>
    private static string? Value(string substring, string after) =>
        SwiftString.HasPrefix(substring, after) ? SwiftString.DropFirst(substring, SwiftString.Count(after)) : null;

    /// <summary><c>firstIndex(of:)</c> for a one-Character needle, as a UTF-16 offset (R-STR-10).</summary>
    private static int? FirstIndex(string text, string of)
    {
        var offset = 0;
        foreach (var character in SwiftString.Characters(text))
        {
            if (SwiftString.Equal(character, of)) return offset;
            offset += character.Length;
        }
        return null;
    }

    /// <summary><c>contains(_:)</c> of a one-Character needle over Characters (:314).</summary>
    private static bool Contains(string text, string character) =>
        SwiftString.Characters(text).Any(each => SwiftString.Equal(each, character));
}
