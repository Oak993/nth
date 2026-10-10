using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Port;

// Source: DroppyCode/Services/Git/Git.swift (B3 52bc7926, was B2 1fbf81e), :1-822. Re-pinned from B2 to B3 for lane
// L2d (B3 plan A9); the B2 -> B3 diff touches only createPullRequest's fallback (:172-200), the new static
// existingRequestURL (:202-218), the new captureTree(paths:onto:from:)/blobData/fileMode (:280-359) and pushBranch's
// force parameter (:390-392) - every other citation below is the same B2 content re-pointed by the diff's own line
// shift (+3, +21, +102 or +105 depending on where in the file it falls; AM-0003). Ported in part 1 of this unit:
// :4-119, :131-152, :254-263, :361-367, :398-484, :622-629 and :824-827. Ported in part 2: :154-248, :369-374,
// :380-396, :536-643 and :763-779. Ported in part 3: :121-129, :265-278, :644-762 and :781-821; part 3 also verifies
// :536-593 (PR-GIT-065, PR-GIT-067, PR-GIT-068, PR-GIT-070), where the scratch capture now carries the OD-0008 A3
// proof, and previewRestore carries the OD-0008 A4 derivation. This lane (L2d) added: the existingRequestURL fallback
// and helper (:192-218, PR-GIT-161/162), captureTree(paths:onto:from:), BlobData and FileMode (:280-359,
// PR-GIT-163/164/165/166), pushBranch's force parameter (:387-392, PR-GIT-031), and the two-argument UpdateRef
// (:376-378, PR-GIT-036) - no longer dead code at B3 (AppModel+HydraMerge.swift:339 now calls it for a Hydra job's own
// branch); porting/dead_code.md entry 48 and PR-GIT-036's NOT_APPLICABLE_WITH_EVIDENCE status are stale and need
// retiring (ED-061 reclassified).

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
public sealed partial record Git
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

    /// <summary>
    /// :121-125 A worktree at <paramref name="at"/> on a new branch, started at <paramref name="base"/> when one is
    /// given, else at HEAD. The 120 s default budget, not 300 (porting/extracts/git-command-vectors.md section 0.1).
    /// </summary>
    public async Task AddWorktree(string at, string branch, string? @base)
    {
        // :122-123
        List<string> arguments = ["worktree", "add", "-b", branch, at];
        if (@base is { } commit) arguments.Add(commit);
        // :124
        Check(await Run(arguments));
    }

    /// <summary>:127-129 --force: uncommitted changes in that worktree do not stop the removal.</summary>
    public async Task RemoveWorktree(string at) => Check(await Run(["worktree", "remove", "--force", at]));

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

    // MARK: - Commit and publish

    /// <summary>:156-161</summary>
    public async Task<(string Summary, string Patch)> WorkingChanges()
    {
        // :157-158
        var summary = await SwiftTry.OptionalAsync<string>(async () => await Output(["status", "--short"])) ?? "";
        var patch = await SwiftTry.OptionalAsync<string>(async () => await Output(["diff", "HEAD", "--no-ext-diff"])) ?? "";
        // :159. UPSTREAM DEFECT 75, reproduced: the test counts UTF-8 bytes, the cut keeps 40,000 Characters.
        if (SwiftString.Utf8Count(patch) > 40_000) patch = SwiftString.Prefix(patch, 40_000);
        // :160
        return (summary, patch);
    }

    /// <summary>:163-166. Both steps at run's 120 s default; no identity is passed, so the user's own signs it.</summary>
    public async Task CommitAll(string message)
    {
        Check(await Run(["add", "-A"]));
        Check(await Run(["commit", "-F", "-"], input: Encoding.UTF8.GetBytes(message)));
    }

    /// <summary>:168-170</summary>
    public async Task Push() => Check(await Run(["push", "-u", "origin", "HEAD"], timeout: 300));

    /// <summary>
    /// :172-200 (B3, was :172-197 B2) Opens a pull request (GitHub) or merge request (GitLab) for the current branch,
    /// or for <paramref name="source"/> into <paramref name="target"/> when those are given.
    /// </summary>
    public async Task<SwiftURL?> CreatePullRequest(string title, string body, string? source = null, string? target = null)
    {
        // :175-192. Shell.run(tool:) resolves the name on the login PATH per call, adds no -c prefix and no git
        // variables, and leaves stdin at the null device (PR-GIT-050).
        // A switch expression over the three cases, with no discard arm (ED-075, R-ENUM-1); each case is a local
        // function holding that case's Swift lines.
        var result = await (await Forge() switch
        {
            MergeRequestLink.Forge.Gitlab => Gitlab(),
            MergeRequestLink.Forge.Github => Github(),
            MergeRequestLink.Forge.Gitea => Gitea(),
        });

        // :177-181
        Task<ShellResult> Gitlab()
        {
            List<string> arguments = ["mr", "create", "--title", title, "--description", body, "--yes"];
            if (source is { } sourceBranch) arguments.AddRange(["--source-branch", sourceBranch]);
            if (target is { } targetBranch) arguments.AddRange(["--target-branch", targetBranch]);
            return Shell.RunTool("glab", arguments, @in: Directory, timeout: 300);
        }

        // :182-186
        Task<ShellResult> Github()
        {
            List<string> arguments = ["pr", "create", "--title", title, "--body", body];
            if (source is { } sourceBranch) arguments.AddRange(["--head", sourceBranch]);
            if (target is { } targetBranch) arguments.AddRange(["--base", targetBranch]);
            return Shell.RunTool("gh", arguments, @in: Directory, timeout: 300);
        }

        // :187-191
        Task<ShellResult> Gitea()
        {
            List<string> arguments = ["pulls", "create", "--title", title, "--description", body];
            if (source is { } sourceBranch) arguments.AddRange(["--head", sourceBranch]);
            if (target is { } targetBranch) arguments.AddRange(["--base", targetBranch]);
            return Shell.RunTool("tea", arguments, @in: Directory, timeout: 300);
        }

        // :193-195 (B3, PR-GIT-161). A non-zero exit is checked against the forge's own "already exists" refusal
        // before Check(result) throws, so a retried request that the forge already opened is returned as if it had
        // just been created, for every forge.
        if (!result.Succeeded && ExistingRequestURL(result, await RemoteWebURL()) is { } existing) return existing;
        // :196
        Check(result);
        // :197-199. stdout, one "\n", then stderr; the first https run of non-whitespace, parsed; no match or a parse
        // failure is nil.
        var text = result.Output + "\n" + result.ErrorOutput;
        return FirstAddress(text) is { } match ? SwiftURL.Create(match) : null;
    }

    /// <summary>
    /// :198 <c>text.firstMatch(of: #/https://\S+/#)</c>. Swift's regex walks Characters; this walks scalars, so a match
    /// whose edge would split a Character is no match (R-RE-2). .NET's \s is Swift's White_Space set.
    /// </summary>
    private static string? FirstAddress(string text)
    {
        for (var match = Address().Match(text); match.Success; match = match.NextMatch())
        {
            if (SwiftString.IsCharacterBoundary(text, match.Index) && SwiftString.IsCharacterBoundary(text, match.Index + match.Length))
                return match.Value;
        }
        return null;
    }

    [GeneratedRegex(@"https://\S+", RegexOptions.CultureInvariant)]
    private static partial Regex Address();

    /// <summary>
    /// :202-218 (B3; new at B3, PR-GIT-161/162). The open request a forge names when creating another for the same
    /// branch fails: GitHub and Gitea print its address, GitLab names it as <c>!&lt;number&gt;</c>. A retried merge
    /// then carries on with that request rather than failing on it.
    /// </summary>
    public static SwiftURL? ExistingRequestURL(ShellResult result, SwiftURL? web)
    {
        // :206
        var text = result.Output + "\n" + result.ErrorOutput;
        // :207. ASCII needle, ordinal search, as Forge()'s does.
        if (!SwiftString.Lowercased(text).Contains("already exist", StringComparison.Ordinal)) return null;
        // :208-210. Reuses FirstAddress (GitHub's and Gitea's shape: the existing request's own address).
        if (FirstAddress(text) is { } address && SwiftURL.Create(address) is { } url) return url;
        // :211. GitLab's shape: "!<n>".
        if (RequestNumber(text) is not { } number || web is null) return null;
        // :212-216. Credentials stripped via URLComponents when the web URL carries a user; the original web URL
        // stands when that reconstruction fails ("if let stripped"). URLComponents.User, not a SwiftURL member: this
        // port exposes the parsed user component only there (ED-169).
        if (URLComponents.Create(web, resolvingAgainstBaseURL: false) is { User: not null } components)
        {
            if ((components with { User = null, Password = null }).Url is { } stripped) web = stripped;
        }
        // :217. Appending keeps the "-/" segment of "-/merge_requests/<n>" intact (SwiftURL.Appending).
        return web.Appending("-/merge_requests/" + number);
    }

    /// <summary>
    /// :211 <c>text.firstMatch(of: #/!(\d+)/#)</c>, boundary-aware as <see cref="FirstAddress"/> (R-RE-2). Returns the
    /// captured digits alone, with no leading "!".
    /// </summary>
    private static string? RequestNumber(string text)
    {
        for (var match = RequestNumberPattern().Match(text); match.Success; match = match.NextMatch())
        {
            if (SwiftString.IsCharacterBoundary(text, match.Index) && SwiftString.IsCharacterBoundary(text, match.Index + match.Length))
                return match.Groups[1].Value;
        }
        return null;
    }

    [GeneratedRegex(@"!(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex RequestNumberPattern();

    /// <summary>
    /// :220-227 Which forge the origin remote is on, by its host: GitLab and GitHub by name, and Gitea's family (tea
    /// serves them all) for anything else.
    /// </summary>
    public async Task<MergeRequestLink.Forge> Forge()
    {
        var remote = SwiftString.Lowercased(await RemoteURL() ?? "");
        // :224-226. ASCII needles: an ordinal search finds what String.contains finds.
        if (remote.Contains("gitlab", StringComparison.Ordinal)) return MergeRequestLink.Forge.Gitlab;
        if (remote.Contains("github", StringComparison.Ordinal)) return MergeRequestLink.Forge.Github;
        return MergeRequestLink.Forge.Gitea;
    }

    /// <summary>
    /// :229-248 Lands a request with the forge's own CLI. GitLab answers 405 while it is still checking a fresh request,
    /// so that is tried again a few times.
    /// </summary>
    public async Task MergePullRequest(MergeRequestLink link)
    {
        // :232
        var number = link.Number.ToString(CultureInfo.InvariantCulture);
        // :233's initial "The request could not be merged." is never read (porting/dead_code.md entry 49, PR-GIT-049
        // NOT_APPLICABLE_WITH_EVIDENCE, ED-061): every way to :247 assigns lastFailure at :242 first.
        string? lastFailure = null;
        // :234
        for (var attempt = 0; attempt < 6; attempt++)
        {
            // :235-240. :237 passes "--remove-source-branch" last, so GitLab removes the request's source branch on
            // the remote once it merges; the agent-facing prompt (Core/Models/MergeRequestLink.swift:50) keeps
            // `glab mr merge <number> --yes` without it at B2, and the port leaves that difference as it is.
            var result = link.LinkForge switch
            {
                MergeRequestLink.Forge.Gitlab => await Shell.RunTool("glab",
                    ["mr", "merge", number, "--yes", "--squash", "--remove-source-branch"], @in: Directory, timeout: 300),
                MergeRequestLink.Forge.Github => await Shell.RunTool("gh", ["pr", "merge", number, "--merge"], @in: Directory, timeout: 300),
                MergeRequestLink.Forge.Gitea => await Shell.RunTool("tea", ["pulls", "merge", number], @in: Directory, timeout: 300),
            };
            // :241-243. No separator between the two streams.
            if (result.Succeeded) return;
            lastFailure = result.FailureMessage;
            var text = SwiftString.Lowercased(result.Output + result.ErrorOutput);
            // :244. UPSTREAM DEFECT 77, reproduced: "405" matches those digits anywhere. ASCII needles, ordinal search.
            if (!(link.LinkForge == MergeRequestLink.Forge.Gitlab && (text.Contains("405", StringComparison.Ordinal) ||
                    text.Contains("not allowed", StringComparison.Ordinal) || text.Contains("checking", StringComparison.Ordinal))))
                break;
            // :245. UPSTREAM DEFECT 76, reproduced: the sixth failure still sleeps 13 s. A cancelled sleep is swallowed.
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(checked(3 + attempt * 2)));
        }
        // :247
        throw new ShellError(SwiftTraps.Unwrap(lastFailure));
    }

    // MARK: - Landing a team's work

    /// <summary>:252-263 (B3, was :231-242 B2) The branch the remote treats as its default: what origin's HEAD points
    /// at, else main or master where one exists.</summary>
    public async Task<string> DefaultBranch()
    {
        // :255-258
        if (await SwiftTry.OptionalAsync<string>(async () =>
                (await Output(["symbolic-ref", "--short", "refs/remotes/origin/HEAD"])).Trimmed()) is { } head
            && head.Length != 0)
        {
            return SwiftString.HasPrefix(head, "origin/") ? SwiftString.DropFirst(head, SwiftString.Count("origin/")) : head;
        }
        // :259-261
        foreach (var candidate in new[] { "main", "master" })
        {
            var result = await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["rev-parse", "--verify", "--quiet", "refs/remotes/origin/" + candidate]));
            if (result?.Succeeded == true) return candidate;
        }
        // :262
        return "main";
    }

    /// <summary>
    /// :265-278 (B3, was :244-257 B2) A tree of HEAD with only <paramref name="paths"/> taken from the working tree,
    /// additions and deletions included: what one thread's work amounts to, with nothing else that happens to be
    /// uncommitted alongside it.
    /// </summary>
    public async Task<string> CaptureTree(IReadOnlyList<string> paths)
    {
        // :269-270. The temporary folder and the name shape of captureTree() (PR-GIT-070).
        var index = TemporaryItem("droppy-code-index-");
        try
        {
            // :272
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
            // :273 Seeded from HEAD when there is one, never from the real index.
            if (await HasCommits()) Check(await Run(["read-tree", "HEAD"], environment: environment));
            // :274. UPSTREAM DEFECT 79, reproduced: --ignore-errors here, and not at the incremental add of :553.
            Check(await Run(["add", "-A", "--ignore-errors", "--pathspec-from-file=-", "--pathspec-file-nul"],
                environment: environment, input: NulSeparated(paths), timeout: 300));
            // :275-277
            var tree = await Run(["write-tree"], environment: environment);
            Check(tree);
            return tree.TrimmedOutput;
        }
        finally
        {
            // :271
            SwiftTry.Ignore(() => FileOps.RemoveItem(index));
        }
    }

    /// <summary>
    /// :280-345 (new at B3, PR-GIT-163/164/165). The team's <paramref name="paths"/> on top of <paramref name="onto"/>
    /// (a commit the checkout's <paramref name="from"/> is an ancestor of): what the work amounts to on the remote as
    /// it is now. A checkout that has fallen behind otherwise sends a merge request built on its old HEAD, and every
    /// file the remote changed since then conflicts on the forge. A file <paramref name="onto"/> changed since
    /// <paramref name="from"/> too is merged three ways with git's own <c>merge-file</c> (<paramref name="from"/>'s
    /// version, <paramref name="onto"/>'s, the working tree's). Null when any such file conflicts, is changed on one
    /// side and deleted on the other, or was added on both sides differently; <c>Merged</c> holds every three-way
    /// result, for the checkout to take once the work has landed.
    /// </summary>
    public async Task<(string Tree, IReadOnlyDictionary<string, byte[]> Merged)?> CaptureTree(IReadOnlyList<string> paths, string onto, string from)
    {
        // :291-293. A directory (not a file), removed whole in the deferred cleanup (PR-GIT-070).
        var scratch = TemporaryItem("droppy-code-rebase-");
        System.IO.Directory.CreateDirectory(scratch);
        try
        {
            // :294-295
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = SwiftPath.AppendingPathComponent(scratch, "index") };
            Check(await Run(["read-tree", onto], environment: environment));

            // :297-300. Renames as a delete and an add, so a file the remote moved away counts as changed.
            var upstream = new HashSet<string>(
                SwiftString.Split(await Output(["diff", "--name-only", "--no-renames", "-z", from, onto]), "\0"),
                SwiftStringComparer.Instance);
            var clean = paths.Where(path => !upstream.Contains(path)).ToList();
            var contested = paths.Where(path => upstream.Contains(path)).ToList();
            // :301-303
            if (clean.Count != 0)
            {
                Check(await Run(["add", "-A", "--ignore-errors", "--pathspec-from-file=-", "--pathspec-file-nul"],
                    environment: environment, input: NulSeparated(clean), timeout: 300));
            }

            // :305
            var merged = new Dictionary<string, byte[]>(SwiftStringComparer.Instance);
            // :306-341
            foreach (var path in contested)
            {
                var ancestor = await BlobData(from, path);
                var remote = await BlobData(onto, path);
                // :309. try? Data(contentsOf:): a missing file, or any other read failure, is nil.
                var working = SwiftTry.Optional(() => File.ReadAllBytes(Joined(Directory, path)));
                if (remote is null && working is null)
                {
                    // :311-313. Gone on both sides: the base already has it the way the team left it.
                    continue;
                }
                if (remote is not null && working is null)
                {
                    // :314-317. The team deleted it: fine only while the remote left it as `from` had it.
                    if (!BytesEqual(remote, ancestor)) return null;
                    Check(await Run(["rm", "--cached", "--quiet", "--ignore-unmatch", "--", path], environment: environment));
                    continue;
                }
                if (remote is null)
                {
                    // :318-320. The remote deleted a file the team kept or changed.
                    return null;
                }
                // :321-339. remote and working both present.
                if (BytesEqual(remote, working)) continue;
                // :323-324. Added on both sides with different contents: nothing to merge from.
                if (ancestor is null) return null;
                var current = SwiftPath.AppendingPathComponent(scratch, "current");
                var common = SwiftPath.AppendingPathComponent(scratch, "common");
                var other = SwiftPath.AppendingPathComponent(scratch, "other");
                FileOps.WriteAtomic(current, working!);
                FileOps.WriteAtomic(common, ancestor);
                FileOps.WriteAtomic(other, remote);
                // :331-333. Exit 0 is a clean merge written into `current`; above 0 counts conflicts. No GIT_INDEX_FILE
                // override for this call (per source): merge-file works on the scratch files alone.
                var mergeFile = await Run(["merge-file", "--quiet", "--", current, common, other]);
                if (!mergeFile.Succeeded) return null;
                var content = File.ReadAllBytes(current);
                var blob = await Run(["hash-object", "-w", "--stdin"], input: content);
                Check(blob);
                var mode = await FileMode(onto, path) ?? "100644";
                Check(await Run(["update-index", "--add", "--cacheinfo", mode + "," + blob.TrimmedOutput + "," + path],
                    environment: environment));
                merged[path] = content;
            }
            // :342-344
            var tree = await Run(["write-tree"], environment: environment);
            Check(tree);
            return (tree.TrimmedOutput, merged);
        }
        finally
        {
            // :293
            SwiftTry.Ignore(() => FileOps.RemoveItem(scratch));
        }
    }

    /// <summary>:314-321 <c>remote == ancestor</c> / <c>remote != working</c>: both operands optional there, so nil
    /// equals nil and nothing else.</summary>
    private static bool BytesEqual(byte[]? left, byte[]? right) =>
        left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    /// <summary>
    /// :347-351 (new at B3, PR-GIT-166). A file's bytes at a commit, or nil where the commit has no such file.
    /// Private in Swift; internal so the tests reach it (R-NAME-6), as <see cref="CaptureTree(string,IReadOnlyList{string}?,WorkingTreeWatch)"/> already is.
    /// </summary>
    internal async Task<byte[]?> BlobData(string commit, string path)
    {
        if (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["cat-file", "blob", commit + ":" + path]))
            is not { Succeeded: true } result) return null;
        return result.Stdout;
    }

    /// <summary>
    /// :353-359 (new at B3, PR-GIT-166). A file's mode at a commit ("100644", "100755"), or nil where it has none.
    /// Private in Swift; internal so the tests reach it (R-NAME-6).
    /// </summary>
    internal async Task<string?> FileMode(string commit, string path)
    {
        if (await SwiftTry.OptionalAsync<string>(async () => await Output(["ls-tree", "-z", commit, "--", path]))
            is not { } listing) return null;
        if (SwiftString.Split(listing, "\0").FirstOrDefault() is not { } entry) return null;
        return SwiftString.Split(entry, " ").FirstOrDefault();
    }

    /// <summary>:361-363</summary>
    public async Task<string> TreeHash(string of) => (await Output(["rev-parse", of + "^{tree}"])).Trimmed();

    /// <summary>:365-367</summary>
    public async Task<string> CommitHash(string of = "HEAD") => (await Output(["rev-parse", "--verify", of])).Trimmed();

    /// <summary>
    /// :369-374 (B3, was :267-272 B2) A commit of <paramref name="tree"/> on <paramref name="parent"/> in the user's
    /// own name, for a branch to carry. No environment is passed: the user's configured identity signs it
    /// (porting/port_impacts.md entry 232).
    /// </summary>
    public async Task<string> CommitWork(string tree, string parent, string message)
    {
        var result = await Run(["commit-tree", tree, "-p", parent, "-F", "-"], input: Encoding.UTF8.GetBytes(message));
        Check(result);
        return result.TrimmedOutput;
    }

    /// <summary>
    /// :376-378 (PR-GIT-036). Moves <paramref name="ref"/> unconditionally. No longer dead code at B3: a Hydra job's
    /// own branch (ownBranch true) is rebuilt fresh with this overload on every retry, so there is nothing to protect
    /// it against, unlike the compare-and-swap overload below (PR-GIT-037) a checkout's shared branch still uses.
    /// </summary>
    public async Task UpdateRef(string @ref, string to) => Check(await Run(["update-ref", @ref, to]));

    /// <summary>
    /// :380-385 Moves <paramref name="ref"/> only while it still points at <paramref name="expecting"/>, so a branch
    /// someone else moved in the meantime is left where it is rather than run over. <paramref name="expecting"/> nil
    /// means the ref must not exist yet: it is passed as an empty fourth argument.
    /// </summary>
    public async Task UpdateRef(string @ref, string to, string? expecting) =>
        Check(await Run(["update-ref", @ref, to, expecting ?? ""]));

    /// <summary>
    /// :390-392 (B3, was :285-287 B2). The refspec is one argument. <paramref name="force"/> is for a branch Marrow
    /// owns outright (a Hydra job's), which a retry rebuilds on a newer base, so its open merge request follows along
    /// instead of a second one being opened; "--force" is inserted after "-u" and before "origin" (PR-GIT-031).
    /// </summary>
    public async Task PushBranch(string name, bool force = false)
    {
        List<string> arguments = ["push", "-u"];
        if (force) arguments.Add("--force");
        arguments.AddRange(["origin", "refs/heads/" + name + ":refs/heads/" + name]);
        Check(await Run(arguments, timeout: 300));
    }

    /// <summary>:394-396</summary>
    public async Task Fetch() => Check(await Run(["fetch", "--quiet", "origin"], timeout: 300));

    /// <summary>:398-400</summary>
    public async Task<bool> IsAncestor(string commit, string of) =>
        (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["merge-base", "--is-ancestor", commit, of])))?.Succeeded ?? false;

    /// <summary>:402-406 Paths that differ between two commits or trees, as they are on disk: NUL-separated, so a name
    /// with a quote or a tab in it comes back unquoted and usable as a path.</summary>
    public async Task<IReadOnlyList<string>> ChangedPaths(string from, string to) =>
        SwiftString.Split(await Output(["diff", "--name-only", "-z", from, to]), "\0");

    /// <summary>:408-424 Paths with uncommitted changes, staged or not, untracked ones included.</summary>
    public async Task<IReadOnlyList<string>> DirtyPaths()
    {
        // :410
        if (await SwiftTry.OptionalAsync<string>(async () =>
                await Output(["status", "--porcelain=v1", "-z", "--untracked-files=all"])) is not { } text)
            return [];
        // :411-412
        var records = new Queue<string>(SwiftString.Split(text, "\0", omittingEmptySubsequences: false));
        var paths = new List<string>();
        // :413-422
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
        // :423
        return paths;
    }

    /// <summary>:426-430 Paths from <paramref name="among"/> that the checkout ignores.</summary>
    public async Task<IReadOnlySet<string>> IgnoredPaths(IReadOnlyList<string> among)
    {
        if (among.Count == 0) return new HashSet<string>(SwiftStringComparer.Instance);
        if (await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["check-ignore", "-z", "--stdin"], input: NulSeparated(among))) is not { } result
            || !(result.Succeeded || result.Status == 1))
            return new HashSet<string>(SwiftStringComparer.Instance);
        // :429
        return new HashSet<string>(SwiftString.Split(result.Output, "\0", omittingEmptySubsequences: true), SwiftStringComparer.Instance);
    }

    /// <summary>:432-440 Paths from <paramref name="among"/> that HEAD holds.</summary>
    public Task<IReadOnlySet<string>> TrackedPaths(IReadOnlyList<string> among) => PresentPaths(among, @in: "HEAD");

    /// <summary>:442-459 Paths from <paramref name="among"/> that <paramref name="in"/> holds, answered by one
    /// <c>cat-file --batch-check</c> over the lot.</summary>
    public async Task<IReadOnlySet<string>> PresentPaths(IReadOnlyList<string> among, string @in)
    {
        // :447
        if (among.Count == 0) return new HashSet<string>(SwiftStringComparer.Instance);
        // :448-451
        var input = new List<byte>();
        foreach (var path in among)
        {
            input.AddRange(Encoding.UTF8.GetBytes(@in + ":" + path));
            input.Add(0);
        }
        // :452
        if (await SwiftTry.OptionalAsync<ShellResult>(async () =>
                await Run(["cat-file", "--batch-check=%(objecttype)", "-Z"], input: [.. input])) is not { Succeeded: true } result)
            return new HashSet<string>(SwiftStringComparer.Instance);
        // :453
        var output = GitBatchOutput.RetainPrefix(@in, among, result.Output);
        var answers = SwiftString.Split(output, "\0", omittingEmptySubsequences: false);
        var tracked = new HashSet<string>(SwiftStringComparer.Instance);
        // :455-457. UPSTREAM DEFECT 80, reproduced: zip stops at the shorter side, so an unanswered tail reads as absent.
        foreach (var (path, answer) in among.Zip(answers))
            if (!SwiftString.HasSuffix(answer, " missing")) tracked.Add(path);
        // :458
        return tracked;
    }

    /// <summary>:461-466</summary>
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

    /// <summary>:468-484 Whether a rebase, merge or cherry-pick is underway: nothing may move then. A directory git
    /// cannot be asked about counts as busy.</summary>
    public async Task<bool> HasOperationInProgress()
    {
        // :478-479
        if (await SwiftTry.OptionalAsync<string>(async () =>
                (await Output(["rev-parse", "--path-format=absolute", "--git-dir"])).Trimmed()) is not { } gitDirectory
            || gitDirectory.Length == 0)
            return true;
        // :480-482. Joined with a literal "/" as the source does; Windows accepts it.
        foreach (var marker in new[] { "rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "BISECT_LOG" })
        {
            var path = gitDirectory + "/" + marker;
            if (File.Exists(path) || System.IO.Directory.Exists(path)) return true;
        }
        // :483
        return false;
    }

    /// <summary>:486-489 Moves the current branch to <paramref name="to"/>; index and working tree stay as they are.</summary>
    public async Task ResetSoft(string to) => Check(await Run(["reset", "--soft", "--quiet", to]));

    /// <summary>:491-495 The index takes HEAD's version of <paramref name="paths"/>; the working tree stays as it is.</summary>
    public async Task ResetIndex(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Check(await Run(["reset", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"], input: NulSeparated(paths)));
    }

    /// <summary>
    /// :497-501 Index and working tree take <paramref name="from"/>'s version of <paramref name="paths"/>. UPSTREAM
    /// DEFECT 78, reproduced: no --literal-pathspecs, so the paths are patterns.
    /// </summary>
    public async Task CheckoutPaths(string from, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Check(await Run(["checkout", "--quiet", from, "--", .. paths]));
    }

    /// <summary>:503-506. UPSTREAM DEFECT 78, reproduced: no --literal-pathspecs, so the paths are patterns.</summary>
    public async Task RemovePaths(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Check(await Run(["rm", "--quiet", "--", .. paths]));
    }

    // PORT-ONLY DEVIATION OD-0020: reset --soft stages remote deletions, so sync must force their removal.
    public async Task RemovePathsAfterSoftReset(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0) return;
        Check(await Run(["rm", "--quiet", "-f", "--", .. paths]));
    }

    /// <summary>:508-510</summary>
    public async Task PullFastForward() => Check(await Run(["pull", "--ff-only", "--quiet"], timeout: 300));

    // MARK: - Checkpoints

    /// <summary>
    /// :514-516. The namespace keeps the upstream product name (porting/port_impacts.md entry 220): renaming it would
    /// orphan every existing checkpoint. uuidString is uppercase (R-UUID-1), lowercased as the Swift writes.
    /// </summary>
    public static string CheckpointRef(Guid thread, long turn, string phase) =>
        "refs/droppy-code/checkpoints/" + SwiftString.Lowercased(SwiftUUID.UuidString(thread)) + "/" +
        turn.ToString(CultureInfo.InvariantCulture) + "-" + phase;

    /// <summary>
    /// :518-534 Snapshots the working tree into a hidden ref without touching the user's index or branch. A tree
    /// already checkpointed since anything changed reuses its commit, so a turn starting on an untouched tree costs the
    /// update-ref alone.
    /// </summary>
    public async Task CaptureCheckpoint(string @ref)
    {
        // :522-523
        var tree = await CurrentTree();
        var watch = await Watch();
        // :524-532
        string commit;
        if (watch?.CommitOf(tree) is { } known)
        {
            commit = known;
        }
        else
        {
            // DEVIATION OD-0012 F1: the message is "Marrow checkpoint" (upstream "Droppy Code checkpoint"), in the
            // Marrow identity. No -p anywhere: the commit is parentless (porting/port_impacts.md entry 233).
            var result = await Run(["commit-tree", tree, "-m", "Nth checkpoint"], environment: Identity);
            Check(result);
            commit = result.TrimmedOutput;
            watch?.Remember(commit, tree);
        }
        // :533
        Check(await Run(["update-ref", @ref, commit]));
    }

    /// <summary>
    /// :536-544 The working tree as a tree object, from the directory's watch when it can vouch that nothing changed
    /// since the last capture, else captured now.
    /// </summary>
    public async Task<string> CurrentTree()
    {
        if (await IndexPath() is not { } indexPath || await Watch() is not { } watch) return await CaptureTree();
        // :540-543. The watch is handed on so the scratch capture can prove it (OD-0008 A3).
        return await watch.CurrentTree(indexPath, async (scratch, paths) =>
        {
            if (scratch is null) return await CaptureTree();
            return await CaptureTree(scratch, paths, watch);
        });
    }

    /// <summary>
    /// :546-568 captureTree over a scratch index kept between captures: with <paramref name="paths"/>, the directories
    /// the watch saw change since the last capture, only they are walked and the rest of the index stands. A walk that
    /// fails, as when a path vanished with nothing of it in the index, is redone whole. Private in Swift; internal so the
    /// tests reach it (R-NAME-6).
    /// <para>
    /// DEVIATION OD-0008 A3 (BLK-0003, PR-GIT-067, PR-GIT-077): a tree is handed back only from a capture that proves it
    /// covered the whole tree; any other capture throws CocoaError(.fileReadUnknown), the error the watch itself throws
    /// for a capture with no tree (WorkingTreeWatch.swift:333), so captureCheckpoint records no checkpoint. ENGINEERING
    /// DECISION, how the proof is made. Route 1 of BLK-0003, the scratch deleted while git writes through it: only the
    /// watch's teardown deletes the scratch (WorkingTreeWatch.swift:159-170), and it drops the stream before it queues
    /// the deletion, while a stream never comes back (PR-GIT-078); so a watch that still has its stream after the last
    /// git process of the capture has deleted nothing under it. The check runs on the fast path and on the rebuild.
    /// Route 2, a changed-path list released early by a cookie some other writer created (WorkingTreeWatch.swift:191-192):
    /// cookie names are shared by every writer, so nothing the watch records can rule it out. The fast path's tree is
    /// therefore compared with a whole-tree add (`add -A -- .`, 300 s, the add of :564) over the same scratch index,
    /// written with write-tree: equal trees prove that no change lay outside the walked directories; unequal ones throw.
    /// The scratch keeps git's stat cache, so the extra walk re-hashes only what changed. The rebuild walks the whole
    /// tree by construction and needs no second walk.
    /// </para>
    /// </summary>
    internal async Task<string> CaptureTree(string scratch, IReadOnlyList<string>? paths, WorkingTreeWatch watch)
    {
        // :551
        var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = scratch };
        // :552-557. UPSTREAM DEFECT 79, reproduced: this add carries no --ignore-errors, unlike :274.
        if (paths is { Count: > 0 } changed && (File.Exists(scratch) || System.IO.Directory.Exists(scratch)) &&
            (await SwiftTry.OptionalAsync<ShellResult>(async () => await Run(["add", "-A", "--pathspec-from-file=-", "--pathspec-file-nul"],
                environment: environment, input: NulSeparated(changed), timeout: 300)))?.Succeeded == true)
        {
            var incremental = await Run(["write-tree"], environment: environment);
            Check(incremental);
            // DEVIATION OD-0008 A3: the whole-tree proof of route 2, then the stream check of route 1.
            Check(await Run(["add", "-A", "--", "."], environment: environment, timeout: 300));
            var whole = await Run(["write-tree"], environment: environment);
            Check(whole);
            if (!string.Equals(whole.TrimmedOutput, incremental.TrimmedOutput, StringComparison.Ordinal) || !watch.HasStream)
                throw new CocoaError(CocoaError.ErrorCode.FileReadUnknown);
            return incremental.TrimmedOutput;
        }
        // :558
        SwiftTry.Ignore(() => FileOps.RemoveItem(scratch));
        // :559-563
        if (await IndexPath() is { } indexPath && (File.Exists(indexPath) || System.IO.Directory.Exists(indexPath)))
            SwiftTry.Ignore(() => File.Copy(indexPath, scratch));
        else if (await HasCommits())
            Check(await Run(["read-tree", "HEAD"], environment: environment));
        // :564-567
        Check(await Run(["add", "-A", "--", "."], environment: environment, timeout: 300));
        var tree = await Run(["write-tree"], environment: environment);
        Check(tree);
        // DEVIATION OD-0008 A3: the stream check of route 1.
        if (!watch.HasStream) throw new CocoaError(CocoaError.ErrorCode.FileReadUnknown);
        return tree.TrimmedOutput;
    }

    /// <summary>:570-573</summary>
    private async Task<WorkingTreeWatch?> Watch()
    {
        if (await IndexPath() is not { } indexPath) return null;
        return WorkingTreeWatch.Shared(Directory, SwiftPath.DeletingLastPathComponent(indexPath));
    }

    /// <summary>
    /// :575-593 The working tree as a tree object: a cheap before/after marker for a diff, with no commit and nothing
    /// referenced. Respects .gitignore.
    /// </summary>
    public async Task<string> CaptureTree()
    {
        // :579
        var index = TemporaryItem("droppy-code-index-");
        try
        {
            // :581
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
            // :583-588 Starting from the real index keeps git's stat cache, so unchanged files are not re-hashed.
            if (await IndexPath() is { } indexPath && (File.Exists(indexPath) || System.IO.Directory.Exists(indexPath)))
                SwiftTry.Ignore(() => File.Copy(indexPath, index));
            else if (await HasCommits())
                Check(await Run(["read-tree", "HEAD"], environment: environment));
            // :589-592
            Check(await Run(["add", "-A", "--", "."], environment: environment, timeout: 300));
            var tree = await Run(["write-tree"], environment: environment);
            Check(tree);
            return tree.TrimmedOutput;
        }
        finally
        {
            // :580
            SwiftTry.Ignore(() => FileOps.RemoveItem(index));
        }
    }

    /// <summary>
    /// :595-610 What a worktree's index path was resolved from: its <c>.git</c> file, which names the git directory,
    /// and the environment overrides git would honour over it.
    /// </summary>
    internal sealed record GitFileStamp
    {
        /// <summary>
        /// :598 <c>systemFileNumber</c>. ED-044: the (volume serial, NTFS file index) pair from GetFileInformationByHandle,
        /// nil when the file cannot be asked or its index is 0.
        /// </summary>
        public (uint VolumeSerial, ulong FileIndex)? Identifier { get; }

        public DateTimeOffset? Modified { get; } // :599
        public long? Size { get; } // :600
        public string Overrides { get; } // :601

        /// <summary>:603-609</summary>
        public GitFileStamp(string gitFile, IReadOnlyDictionary<string, string> environment)
        {
            var attributes = FileInformation.Read(gitFile);
            Identifier = attributes is { FileIndex: not 0 } known ? (known.VolumeSerial, known.FileIndex) : null;
            Modified = attributes?.Modified;
            Size = attributes?.Size;
            Overrides = (environment.TryGetValue("GIT_DIR", out var gitDirectory) ? gitDirectory : "") + "\0" +
                (environment.TryGetValue("GIT_INDEX_FILE", out var indexFile) ? indexFile : "");
        }

        /// <summary>:597 Equatable, member by member; the String member by Swift's ==.</summary>
        public bool Equals(GitFileStamp? other) =>
            other is not null && Identifier == other.Identifier && Modified == other.Modified && Size == other.Size &&
            SwiftString.Equal(Overrides, other.Overrides);

        public override int GetHashCode() =>
            HashCode.Combine(Identifier, Modified, Size, SwiftStringComparer.Instance.GetHashCode(Overrides));
    }

    /// <summary>
    /// :612-614 Resolved index paths of worktrees by working directory. A session works in a handful of directories;
    /// past the bound the cache is simply dropped.
    /// </summary>
    internal static readonly Mutex<Dictionary<string, (GitFileStamp Stamp, string? Path)>> WorktreeIndexPaths =
        new(new Dictionary<string, (GitFileStamp Stamp, string? Path)>(SwiftStringComparer.Instance));

    /// <summary>
    /// :616-643 The repository's index file. A checkout with its own <c>.git</c> directory keeps it right there, which
    /// saves the git process that asking would cost on every snapshot. A worktree (<c>.git</c> is a file naming the git
    /// directory) asks once and keeps the answer while that file stands unchanged. A directory inside a repository, with
    /// no <c>.git</c> of its own, still asks every time. Private in Swift; internal so the tests reach it (R-NAME-6).
    /// </summary>
    // PORT-ONLY DEVIATION OD-0020: AppModel needs the exact index file for transactional sync rollback.
    public async Task<string?> IndexPath()
    {
        // :623-626
        var dotGit = SwiftPath.FileURLAppendingPathComponentPath(Directory, ".git");
        var environment = LoginEnvironment.Current;
        var exists = File.Exists(dotGit) || System.IO.Directory.Exists(dotGit);
        var isDirectory = System.IO.Directory.Exists(dotGit);
        // :627-629
        if (exists && isDirectory && !environment.ContainsKey("GIT_DIR") && !environment.ContainsKey("GIT_INDEX_FILE"))
            return dotGit + "/index";
        // :630-633
        var stamp = exists ? new GitFileStamp(dotGit, environment) : null;
        if (stamp is not null && WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitFileStamp Stamp, string? Path)> paths) =>
                paths.TryGetValue(Directory, out var entry) ? entry : ((GitFileStamp Stamp, string? Path)?)null) is { } cached
            && cached.Stamp == stamp)
            return cached.Path;
        // :634-635
        var path = await SwiftTry.OptionalAsync<string>(async () =>
            (await Output(["rev-parse", "--path-format=absolute", "--git-path", "index"])).Trimmed());
        // :636-641
        if (stamp is not null)
        {
            WorktreeIndexPaths.WithLock((ref Dictionary<string, (GitFileStamp Stamp, string? Path)> paths) =>
            {
                if (paths.Count >= 64) paths.Clear();
                paths[Directory] = (stamp, path);
            });
        }
        // :642
        return path;
    }

    /// <summary>:645-646 Paths per <c>git diff</c> call when a caller names many: well under the argument limit.</summary>
    private const int DiffPathChunk = 400;

    /// <summary>
    /// :648-678 The patch between two trees or refs. <paramref name="binary"/> puts whole binary blobs in it, so a
    /// picture a head added applies elsewhere. Over <paramref name="paths"/> alone when given: a diff of a tree that
    /// holds build output runs to hundreds of megabytes, so the caller names the files it wants rather than cutting the
    /// rest out afterwards.
    /// </summary>
    public async Task<string> Diff(string from, string to, bool binary = false, IReadOnlyList<string>? paths = null)
    {
        // :653-660. `diff` takes no pathspec file, so the paths go on the command line, and as plain names: a `?` or `[`
        // in one is a character, not a pattern. --literal-pathspecs is a git option, so it goes before the subcommand,
        // and only when paths were given, an empty list included (porting/port_impacts.md entry 222).
        List<string> arguments = paths is null ? [] : ["--literal-pathspecs"];
        arguments.AddRange(["diff", "--no-ext-diff", "-M"]);
        if (binary) arguments.Add("--binary");
        arguments.AddRange([from, to]);
        // :661-666. Strictly more than 400 goes in chunks. UPSTREAM DEFECT 83, reproduced: an empty but given list is a
        // bare "--", which git reads as no restriction at all.
        if (paths is not { Count: > DiffPathChunk } named)
        {
            if (paths is { } given) arguments.AddRange(["--", .. given]);
            var result = await Run(arguments, timeout: 300);
            Check(result);
            return result.Output;
        }
        // :667-677 Long lists go in chunks: one diff per chunk joins into the same patch.
        var output = "";
        var start = 0;
        while (start < named.Count)
        {
            var chunk = named.Skip(start).Take(Math.Min(start + DiffPathChunk, named.Count) - start).ToList();
            var result = await Run([.. arguments, "--", .. chunk], timeout: 300);
            Check(result);
            if (output.Length != 0 && !SwiftString.HasSuffix(output, "\n")) output += "\n";
            output += result.Output;
            start += chunk.Count;
        }
        return output;
    }

    /// <summary>
    /// :680-702 What undoing a thread's file changes would touch: of <paramref name="paths"/> (the files its agent
    /// changed, relative to this directory), the ones that differ between <paramref name="base"/> and
    /// <paramref name="end"/>, the tree the thread last left. A file the checkout has changed again since then is
    /// somebody else's work now, so it is marked kept. Other threads' files never appear: only <paramref name="paths"/>
    /// are looked at. Without an <paramref name="end"/> tree (no turn finished) the files are compared to the checkout
    /// as it is, which then counts as what the thread left.
    /// </summary>
    public async Task<RevertPreview> PreviewRestore(string @base, string? end, IReadOnlyList<string> paths)
    {
        // :687
        if (paths.Count == 0) return new RevertPreview(new ValueList<RevertPreview.File>(Array.Empty<RevertPreview.File>()));
        // :688-689
        var endTree = end is { } known ? known : await CaptureTree(paths: paths);
        // :690-691 `git diff` takes no pathspec file; a turn touches tens of files, well within the arguments.
        // DEVIATION OD-0008 A4 (BLK-0004, PR-GIT-097, PR-GIT-098): --no-relative, so the paths numstat prints are
        // repository-root-relative whatever the user's diff.relative says, and the blob lookups, the kept test and a
        // later restore all act on exactly the files the turn touched. The pathspecs stay relative to this directory, the
        // process's working directory, as upstream. In git's default configuration the output is upstream's byte for
        // byte. ENGINEERING DECISION, the mechanism the ruling names: the command-line option rather than a
        // `-c diff.relative=false` pair, which porting/port_impacts.md entry 225 (written before the ruling) forbade;
        // both need git 2.28 or later (UNK-0011 item 4).
        var preview = new RevertPreview(await Output(
            ["diff", "--numstat", "-z", "--no-renames", "--no-ext-diff", "--no-relative", @base, endTree, "--", .. paths]));
        // :692
        if (preview.Files.Count == 0) return preview;
        // :693-695
        var root = await RepositoryRoot();
        var left = await Blobs(endTree, preview.Files.Select(file => file.Path).ToList(), root);
        var now = await WorkingBlobs(preview.Files.Select(file => file.Path).ToList(), root);
        // :696-701
        return preview with
        {
            Files = new ValueList<RevertPreview.File>(preview.Files.Select(file =>
                file with { IsKept = !SameBlob(Lookup(left, file.Path), Lookup(now, file.Path)) })),
        };
    }

    /// <summary>
    /// :704-726 Puts <paramref name="paths"/> (repository-relative) back as they are in <paramref name="from"/>,
    /// deleting the ones it lacks. Nothing outside <paramref name="paths"/> is touched. DEVIATION OD-0008 A4 and A3
    /// (PR-GIT-158): the deletion is upstream's; what reaches it is repository-root-relative paths the turn touched
    /// (PreviewRestore) against a checkpoint proven whole (the scratch capture), so a path the ref lacks is one the turn
    /// created.
    /// </summary>
    public async Task Restore(IReadOnlyList<string> paths, string from)
    {
        // :707
        if (paths.Count == 0) return;
        // :708-710
        var root = await RepositoryRoot();
        var present = await Blobs(from, paths, root);
        var restored = paths.Where(path => present.ContainsKey(path)).ToList();
        // :711-717. Absolute paths joined by NUL with no trailing NUL (PR-GIT-069), unlike nulSeparated.
        if (restored.Count != 0)
        {
            var result = await Run(
                ["restore", "--source", from, "--worktree", "--staged", "--pathspec-from-file=-", "--pathspec-file-nul"],
                input: Encoding.UTF8.GetBytes(string.Join("\0", restored.Select(path => Joined(root, path)))));
            Check(result);
        }
        // :718-725. UPSTREAM DEFECT 78, reproduced: the rm result and every removal error are swallowed.
        var created = paths.Where(path => !present.ContainsKey(path)).ToList();
        if (created.Count != 0)
        {
            await SwiftTry.IgnoreAsync(async () => await Run(
                ["rm", "-q", "--cached", "--ignore-unmatch", "--pathspec-from-file=-", "--pathspec-file-nul"],
                input: Encoding.UTF8.GetBytes(string.Join("\0", created.Select(path => Joined(root, path))))));
            foreach (var path in created) SwiftTry.Ignore(() => FileOps.RemoveItem(Joined(root, path)));
        }
    }

    /// <summary>
    /// :728-741 The blob of each of <paramref name="at"/> (repository-relative) in <paramref name="in"/>, for the ones it
    /// has. Absolute pathspecs in, the repository-relative names --full-name prints out; no --literal-pathspecs
    /// (upstream defect 78, reproduced), so a name with a glob character is a pattern.
    /// </summary>
    private async Task<Dictionary<string, string>> Blobs(string @in, IReadOnlyList<string> at, string root)
    {
        // :730
        var listing = await Output(["ls-tree", "-r", "-z", "--full-name", @in, "--", .. at.Select(path => Joined(root, path))]);
        // :731-732
        var blobs = new Dictionary<string, string>(at.Count, SwiftStringComparer.Instance);
        // :733-739 "<mode> <type> <hash>\t<path>"
        foreach (var entry in SwiftString.Split(listing, "\0"))
        {
            if (FirstIndex(entry, "\t") is not { } tab) continue;
            var fields = SwiftString.Split(entry[..tab], " ");
            if (fields.Count != 3) continue;
            blobs[entry[(tab + 1)..]] = fields[2];
        }
        // :740
        return blobs;
    }

    /// <summary>
    /// :743-753 The blob each of <paramref name="at"/> (repository-relative) would hash to as it is on disk; a missing
    /// file has none. Unlike presentPaths, the reply count is checked (porting/upstream_defects.md entry 80).
    /// </summary>
    private async Task<Dictionary<string, string>> WorkingBlobs(IReadOnlyList<string> at, string root)
    {
        // :746. fileExists: a file or a folder (R-FS-1).
        var existing = at.Where(path =>
        {
            var full = Joined(root, path);
            return File.Exists(full) || System.IO.Directory.Exists(full);
        }).ToList();
        // :747
        if (existing.Count == 0) return new Dictionary<string, string>(SwiftStringComparer.Instance);
        // :748-749. Absolute paths joined by a newline with no trailing newline (PR-GIT-069).
        var result = await Run(["hash-object", "--stdin-paths"],
            input: Encoding.UTF8.GetBytes(string.Join("\n", existing.Select(path => Joined(root, path)))));
        Check(result);
        // :750-751
        var hashes = SplitOnNewlines(result.Output);
        if (hashes.Count != existing.Count)
        {
            throw new ShellError("git hash-object answered for " + hashes.Count.ToString(CultureInfo.InvariantCulture) + " of " +
                existing.Count.ToString(CultureInfo.InvariantCulture) + " files.");
        }
        // :752 Dictionary(uniqueKeysWithValues:) traps on a repeated key (R-COLL-3).
        var blobs = new Dictionary<string, string>(existing.Count, SwiftStringComparer.Instance);
        for (var i = 0; i < existing.Count; i++)
            if (!blobs.TryAdd(existing[i], hashes[i])) throw SwiftTraps.Fatal("Duplicate values for key: '" + existing[i] + "'");
        return blobs;
    }

    /// <summary>
    /// :755-761 Removes every checkpoint ref of <paramref name="thread"/> in one update-ref batch, best-effort: a
    /// failed listing or batch is reported nowhere (porting/upstream_defects.md entry 85, reproduced).
    /// </summary>
    public async Task DeleteCheckpoints(Guid thread)
    {
        // :756. Built on its own, not from checkpointRef, with a trailing slash.
        var prefix = "refs/droppy-code/checkpoints/" + SwiftString.Lowercased(SwiftUUID.UuidString(thread)) + "/";
        // :757
        if (await SwiftTry.OptionalAsync<string>(async () => await Output(["for-each-ref", "--format=%(refname)", prefix])) is not { } refs)
            return;
        // :758-759. No old value: each delete is unconditional.
        var commands = string.Concat(SwiftString.Split(refs, "\n").Select(@ref => "delete " + @ref + "\n"));
        if (commands.Length == 0) return;
        // :760
        await SwiftTry.IgnoreAsync(async () => await Run(["update-ref", "--stdin"], input: Encoding.UTF8.GetBytes(commands)));
    }

    /// <summary><c>dict[key]</c> of the blob maps (R-COLL-3).</summary>
    private static string? Lookup(Dictionary<string, string> blobs, string path) =>
        blobs.TryGetValue(path, out var blob) ? blob : null;

    /// <summary>:698 <c>left[path] != now[path]</c>, negated: two optionals are equal when both are nil or both hold
    /// equal strings.</summary>
    private static bool SameBlob(string? left, string? right) =>
        left is null ? right is null : right is not null && SwiftString.Equal(left, right);

    /// <summary><c>root.appendingPathComponent(path).path</c> (:714, :722, :724, :730, :746, :748).</summary>
    private static string Joined(string root, string path) => SwiftPath.FileURLAppendingPathComponentPath(root, path);

    /// <summary>:750 <c>split(whereSeparator: \.isNewline)</c>: Character by Character, "\r\n" one separator, empty
    /// pieces dropped.</summary>
    private static List<string> SplitOnNewlines(string text)
    {
        var pieces = new List<string>();
        var current = new StringBuilder();
        foreach (var character in SwiftString.Characters(text))
        {
            if (character == "\r\n" || (character.Length == 1 && FoundationText.IsNewline(character[0])))
            {
                if (current.Length != 0) pieces.Add(current.ToString());
                current.Clear();
                continue;
            }
            current.Append(character);
        }
        if (current.Length != 0) pieces.Add(current.ToString());
        return pieces;
    }

    /// <summary>
    /// <c>FileManager.default.temporaryDirectory.appendingPathComponent(prefix + UUID().uuidString)</c> (:270, :291, :579,
    /// :804). ENGINEERING DECISION: temporaryDirectory is GetTempPathW on Windows, the folder swift-corelibs-foundation
    /// answers. The names keep upstream's "droppy-code" prefix, matching the ref namespace: they are neither shown nor
    /// sent (PR-GIT-070).
    /// </summary>
    private static string TemporaryItem(string prefix) =>
        SwiftPath.AppendingPathComponent(System.IO.Path.GetTempPath(), prefix + SwiftUUID.UuidString(Guid.NewGuid()));

    // MARK: - Copies of the checkout

    /// <summary>
    /// :765-768. DEVIATION OD-0012 F1: "Marrow" / "marrow@localhost" (upstream "Droppy Code" /
    /// "droppy-code@localhost").
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Identity = new Dictionary<string, string>
    {
        ["GIT_AUTHOR_NAME"] = "Nth", ["GIT_AUTHOR_EMAIL"] = "nth@localhost",
        ["GIT_COMMITTER_NAME"] = "Nth", ["GIT_COMMITTER_EMAIL"] = "nth@localhost",
    };

    /// <summary>
    /// :770-779 A commit of <paramref name="tree"/> on top of HEAD that no ref points at: the checkout as it is,
    /// uncommitted work included, with the history behind it. Something a worktree can start from.
    /// </summary>
    public async Task<string> CommitTree(string tree, string message)
    {
        List<string> arguments = ["commit-tree", tree, "-m", message];
        if (await HasCommits()) arguments.AddRange(["-p", "HEAD"]);
        var result = await Run(arguments, environment: Identity);
        Check(result);
        return result.TrimmedOutput;
    }

    /// <summary>
    /// :781-785 A worktree at <paramref name="at"/> with <paramref name="commit"/> checked out and no branch: a copy of
    /// the checkout to work in, whose own status shows only what changed in it. 300 s, unlike addWorktree's 120 s.
    /// </summary>
    public async Task AddDetachedWorktree(string at, string commit) =>
        Check(await Run(["worktree", "add", "--detach", at, commit], timeout: 300));

    /// <summary>:787-790 Forgets worktrees whose folders are gone from disk; the result is discarded.</summary>
    public async Task PruneWorktrees() =>
        await SwiftTry.IgnoreAsync(async () => await Run(["worktree", "prune"]));

    /// <summary>
    /// :792-821 Applies <paramref name="patch"/> to the working tree and stages nothing. A hunk that no longer fits is
    /// merged three-way against the blobs the patch names; a file the merge cannot settle keeps conflict markers, and
    /// those paths come back. Throws when nothing could be applied at all.
    /// </summary>
    public async Task<IReadOnlyList<string>> Apply(string patch)
    {
        // :797-799. The same bytes go to each attempt.
        var data = Encoding.UTF8.GetBytes(patch);
        var plain = await Run(["apply", "--binary", "--whitespace=nowarn", "-"], input: data);
        if (plain.Succeeded) return [];

        // :801-806 The three-way merge works through an index that matches the working tree, so it gets a throwaway one
        // that does, and the real index never changes.
        var index = TemporaryItem("droppy-code-apply-");
        try
        {
            var environment = new Dictionary<string, string> { ["GIT_INDEX_FILE"] = index };
            // :807-811
            if (await IndexPath() is { } indexPath && (File.Exists(indexPath) || System.IO.Directory.Exists(indexPath)))
                SwiftTry.Ignore(() => File.Copy(indexPath, index));
            else if (await HasCommits())
                Check(await Run(["read-tree", "HEAD"], environment: environment));
            // :812 at 300 s, while the apply after it keeps the 120 s default.
            Check(await Run(["add", "-A", "--", "."], environment: environment, timeout: 300));
            // :813-814
            var merged = await Run(["apply", "--3way", "--binary", "--whitespace=nowarn", "-"], environment: environment, input: data);
            if (merged.Succeeded) return [];
            // :815-818 "U path" names each file left with markers; without one, nothing was applied.
            var conflicts = new List<string>();
            foreach (var line in SwiftString.Split(merged.ErrorOutput, "\n"))
                if (SwiftString.HasPrefix(line, "U ")) conflicts.Add(FoundationText.Trim(SwiftString.DropFirst(line, 2)));
            // :819-820
            if (conflicts.Count == 0) throw new ShellError(merged.FailureMessage);
            return conflicts;
        }
        finally
        {
            // :805
            SwiftTry.Ignore(() => FileOps.RemoveItem(index));
        }
    }

    /// <summary>:824-827 <c>Substring.value(after:)</c>, a private extension of the file.</summary>
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

    /// <summary><c>contains(_:)</c> of a one-Character needle over Characters (:419).</summary>
    private static bool Contains(string text, string character) =>
        SwiftString.Characters(text).Any(each => SwiftString.Equal(each, character));
}
