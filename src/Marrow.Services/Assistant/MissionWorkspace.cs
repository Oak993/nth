// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6a "Each mission in its own worktree", 15.3; unit AS-5; ED-NEW-dextra-as5-4).
// No Swift twin.
// Where a mission works: its own git worktree on its own branch (nth/mission-<id>-<slug>) made from the owner's current
// branch, or, for a folder that is no repository, a snapshot copy. The owner's own working tree is touched only by a
// Merge he tapped, and never by Discard. Git runs with no hooks (an agent could have planted one) and with the app's own
// invariant prefix (Git.Run). Every step that cannot finish cleanly undoes itself and says so; nothing is half merged.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using Marrow.Core.Support;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Assistant;

public enum MissionWorkspaceKind { Worktree, Snapshot }

/// <summary>One file of a snapshot as it was copied: its relative path (forward slashes), its SHA-256 and its length.</summary>
public sealed record SnapshotEntry(string Path, string Hash, long Length);

/// <summary>A mission's workspace (12.6a).</summary>
public sealed record MissionWorkspace(MissionWorkspaceKind Kind, string Source, string Path)
{
    /// <summary>The folder the mission's agents work in: the worktree's twin of the owner's folder, or the copy.</summary>
    public string WorkingFolder { get; init; } = "";

    /// <summary>Worktree: the owner's repository root, the mission's branch, the owner's branch at the start (null when
    /// his HEAD was detached) and the commit the mission started from.</summary>
    public string? Repository { get; init; }

    public string? Branch { get; init; }

    public string? BaseBranch { get; init; }

    public string? BaseCommit { get; init; }

    /// <summary>
    /// Worktree: its admin folder in the owner's repository (<c>.git/worktrees/&lt;name&gt;</c>), read once when it was made
    /// and before any agent ran. nth's own git steps name it explicitly and never follow the worktree's <c>.git</c> link,
    /// which lies in the folder the agents can write (ED-NEW-dextra-as5-15).
    /// </summary>
    public string? GitDir { get; init; }

    /// <summary>Snapshot: every file as it was copied, so a copy back can tell the owner's later edits from the mission's.</summary>
    public ValueList<SnapshotEntry> Manifest { get; init; } = ValueList<SnapshotEntry>.Empty;
}

/// <summary>What one file of a mission did.</summary>
public enum WorkspaceChangeKind { Added, Modified, Deleted }

public sealed record WorkspaceChange(string Path, WorkspaceChangeKind Kind)
{
    public int Insertions { get; init; }

    public int Deletions { get; init; }

    /// <summary>Snapshot: the SHA-256 of the copy's file when it was reviewed (null for a deleted file).</summary>
    public string? Hash { get; init; }
}

/// <summary>What a merge card shows (12.6a): the changes against the owner's branch or folder.</summary>
public sealed record MergeReview(ValueList<WorkspaceChange> Changes, string Patch)
{
    /// <summary>
    /// Worktree: the mission branch's commit this review read. A Merge merges exactly this commit, so nothing written
    /// after the review reaches the owner's branch with it (ED-NEW-dextra-as5-15).
    /// </summary>
    public string? Commit { get; init; }

    /// <summary>Snapshot: one hash over every change this review read (kind, path, file hash); a copy back refuses any other.</summary>
    public string? Fingerprint { get; init; }

    /// <summary>The owner's branch moved since the mission started: the card offers Rebase first.</summary>
    public bool BaseMoved { get; init; }

    /// <summary>Snapshot: files the owner changed himself since the copy; copying back would overwrite them.</summary>
    public ValueList<string> Conflicts { get; init; } = ValueList<string>.Empty;

    public int Insertions => Changes.Sum(c => c.Insertions);

    public int Deletions => Changes.Sum(c => c.Deletions);

    /// <summary>The card's one-line summary ("3 files changed, 12 insertions, 4 deletions").</summary>
    public string Summary => Changes.Count == 0
        ? "No changes."
        : string.Create(CultureInfo.InvariantCulture,
            $"{Changes.Count} file{(Changes.Count == 1 ? "" : "s")} changed, {Insertions} insertion{(Insertions == 1 ? "" : "s")}, {Deletions} deletion{(Deletions == 1 ? "" : "s")}");
}

public enum MergeOutcome { Merged, NothingToMerge, Conflict, Refused }

/// <summary>The result of Merge or Rebase first. <see cref="Refused"/> says why nothing changed.</summary>
public sealed record MergeResult(MergeOutcome Outcome, string? Refused = null)
{
    public ValueList<string> Conflicts { get; init; } = ValueList<string>.Empty;
}

/// <summary>A workspace the operations refused to make or change; the message is for the board's Blocked line.</summary>
public sealed class MissionWorkspaceError(string message) : Exception(message);

/// <summary>The workspace operations of 12.6a.</summary>
public static partial class MissionWorkspaces
{
    /// <summary>The largest folder a snapshot copies; a larger one is refused (Blocked), never half copied.</summary>
    public const long SnapshotByteCap = 1L << 30;

    public const int SnapshotFileCap = 20_000;

    /// <summary>
    /// The most a snapshot copy may hold when it is reviewed or copied back (twice the copy's own caps): a copy that grew
    /// past it is refused, never scanned without end (ED-NEW-dextra-as5-15).
    /// </summary>
    public const long ReviewByteCap = SnapshotByteCap * 2;

    public const int ReviewFileCap = SnapshotFileCap * 2;

    /// <summary>The largest <c>.git</c> link nth reads.</summary>
    private const int GitLinkCap = 4_096;

    /// <summary>How long a finished mission's workspace stays before the sweep removes it (12.6a).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    private const string BranchPrefix = "nth/mission-";
    private const int PatchCap = 40_000;

    /// <summary>
    /// The mission's branch: <c>nth/mission-&lt;first 8 of the id&gt;-&lt;slug of the title&gt;</c>, and <c>-r2</c>, <c>-r3</c>
    /// for each restart, so a fresh branch never meets the failed one the owner may still be reviewing (12.6b).
    /// </summary>
    public static string BranchName(Guid missionId, string title, int attempt = 1) =>
        BranchPrefix + missionId.ToString("N", CultureInfo.InvariantCulture)[..8] + "-" + Slug(title)
        + (attempt > 1 ? "-r" + attempt.ToString(CultureInfo.InvariantCulture) : "");

    /// <summary>A branch-safe slug: lowercase ASCII letters and digits, runs of anything else as one dash, at most 32.</summary>
    public static string Slug(string title)
    {
        var slug = new StringBuilder();
        foreach (var c in title.ToLowerInvariant())
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
            if (slug.Length >= 32) break;
        }
        var text = slug.ToString().Trim('-');
        return text.Length == 0 ? "work" : text;
    }

    /// <summary>
    /// Makes the workspace of a mission working in <paramref name="source"/>, at <paramref name="folder"/> (the store's
    /// <c>worktrees/&lt;mission-id&gt;</c>, which must not exist yet). A repository with a commit gets a worktree, else
    /// the folder is copied.
    /// </summary>
    public static async Task<MissionWorkspace> Create(Guid missionId, string title, string source, string folder,
        long byteCap = SnapshotByteCap, int fileCap = SnapshotFileCap, int attempt = 1)
    {
        if (!Directory.Exists(source)) throw new MissionWorkspaceError($"The folder {source} does not exist.");
        if (Directory.Exists(folder) || File.Exists(folder)) throw new MissionWorkspaceError("This mission already has a workspace.");
        var git = new GitValue(source);
        if (await git.IsRepository() && await git.HasCommits()) return await CreateWorktree(missionId, title, source, folder, git, attempt);
        return CreateSnapshot(source, folder, byteCap, fileCap);
    }

    private static async Task<MissionWorkspace> CreateWorktree(Guid missionId, string title, string source, string folder, GitValue git, int attempt)
    {
        var root = (await git.Output(["rev-parse", "--show-toplevel"])).Trim();
        var repository = new GitValue(root);
        var prefix = (await git.Output(["rev-parse", "--show-prefix"])).Trim().TrimEnd('/');
        var head = await repository.Run(["symbolic-ref", "--quiet", "--short", "HEAD"]);
        var baseBranch = head.Succeeded && head.TrimmedOutput.Length > 0 ? head.TrimmedOutput : null;
        var baseCommit = await repository.CommitHash("HEAD");
        var branch = BranchName(missionId, title, attempt);
        GitValue.Check(await repository.Run([.. NoHooks(folder), "worktree", "add", "-b", branch, folder, baseCommit]));
        var working = prefix.Length == 0 ? folder : System.IO.Path.Combine(folder, prefix.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var made = new MissionWorkspace(MissionWorkspaceKind.Worktree, source, folder)
        {
            WorkingFolder = working, Repository = root, Branch = branch, BaseBranch = baseBranch, BaseCommit = baseCommit,
        };
        // Read from the owner's repository now, before any agent can touch the worktree's .git link.
        if (await FindGitDir(made) is not { } gitDir)
        {
            await Discard(made);
            throw new MissionWorkspaceError("The mission's worktree could not be found in its repository.");
        }
        return made with { GitDir = gitDir };
    }

    private static MissionWorkspace CreateSnapshot(string source, string folder, long byteCap, int fileCap)
    {
        var files = SnapshotFiles(source, byteCap, fileCap);
        var manifest = new List<SnapshotEntry>(files.Count);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var relative in files)
            {
                var from = System.IO.Path.Combine(source, relative);
                var to = System.IO.Path.Combine(folder, relative);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
                File.Copy(from, to, overwrite: false);
                manifest.Add(new SnapshotEntry(relative.Replace('\\', '/'), Hash(to), new FileInfo(to).Length));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            TryDelete(folder);
            throw new MissionWorkspaceError("The folder could not be copied for the mission: " + error.Message);
        }
        return new MissionWorkspace(MissionWorkspaceKind.Snapshot, source, folder)
        {
            WorkingFolder = folder, Manifest = new(manifest.OrderBy(e => e.Path, StringComparer.Ordinal)),
        };
    }

    /// <summary>
    /// Every regular file under the folder, relative; links and junctions are never followed or copied. Past either cap it
    /// stops at once and throws (<paramref name="tooLarge"/> says why), so no folder is scanned without end.
    /// </summary>
    private static List<string> SnapshotFiles(string source, long byteCap, int fileCap, string? tooLarge = null)
    {
        var files = new List<string>();
        long bytes = 0;
        var pending = new Stack<string>([source]);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
            {
                if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                if (entry is DirectoryInfo child) { pending.Push(child.FullName); continue; }
                bytes += ((FileInfo)entry).Length;
                files.Add(System.IO.Path.GetRelativePath(source, entry.FullName));
                if (files.Count > fileCap || bytes > byteCap)
                    throw new MissionWorkspaceError(tooLarge ?? string.Create(CultureInfo.InvariantCulture,
                        $"The folder is too large to copy for a mission (over {fileCap} files or {byteCap >> 20} MB). Put it in a git repository to use a worktree."));
            }
        }
        return files;
    }

    /// <summary>
    /// The merge card's contents: the branch's changes (uncommitted work included) or the copy's changes, with the commit
    /// or fingerprint they were read from, which is what a Merge then applies (ED-NEW-dextra-as5-15).
    /// </summary>
    public static async Task<MergeReview> Review(MissionWorkspace workspace, long byteCap = ReviewByteCap, int fileCap = ReviewFileCap)
    {
        if (workspace.Kind == MissionWorkspaceKind.Snapshot) return ReviewSnapshot(workspace, byteCap, fileCap);
        await CommitWork(workspace);
        var repository = new GitValue(workspace.Repository!);
        var tip = await OwnerTip(workspace);
        var commit = (await repository.Output(["rev-parse", "--verify", "refs/heads/" + workspace.Branch!])).Trim();
        var mergeBase = (await repository.Output(["merge-base", tip, commit])).Trim();
        var changes = new List<WorkspaceChange>();
        var status = Records(await repository.Output(["diff", "--name-status", "-z", "--no-renames", mergeBase, commit]));
        var counts = Records(await repository.Output(["diff", "--numstat", "-z", "--no-renames", mergeBase, commit]));
        var numbers = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        foreach (var record in counts)
        {
            var parts = record.Split('\t', 3);
            if (parts.Length == 3) numbers[parts[2]] = (Count(parts[0]), Count(parts[1]));
        }
        for (var i = 0; i + 1 < status.Count; i += 2)
        {
            var kind = status[i] switch { "A" => WorkspaceChangeKind.Added, "D" => WorkspaceChangeKind.Deleted, _ => WorkspaceChangeKind.Modified };
            var (insertions, deletions) = numbers.GetValueOrDefault(status[i + 1]);
            changes.Add(new WorkspaceChange(status[i + 1], kind) { Insertions = insertions, Deletions = deletions });
        }
        var patch = await repository.Output(["diff", "--no-ext-diff", "--no-color", mergeBase, commit]);
        return new MergeReview(new(changes), Cap(patch)) { Commit = commit, BaseMoved = !string.Equals(tip, workspace.BaseCommit, StringComparison.Ordinal) };
    }

    /// <summary>
    /// Merges exactly the commit the owner's card showed (<paramref name="reviewed"/>) into his branch, on his tap only;
    /// nothing written in the worktree after that review comes with it (ED-NEW-dextra-as5-15). His checkout must be on that
    /// branch with no uncommitted change; a conflict aborts the merge and leaves everything as it was.
    /// </summary>
    public static async Task<MergeResult> Merge(MissionWorkspace workspace, MergeReview reviewed, long byteCap = ReviewByteCap, int fileCap = ReviewFileCap)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (workspace.Kind == MissionWorkspaceKind.Snapshot) return CopyBack(workspace, reviewed, byteCap, fileCap);
        if (reviewed.Commit is not { Length: > 0 } commit)
            return new MergeResult(MergeOutcome.Refused, "This card names no reviewed commit; review the mission again. Nothing was merged.");
        var repository = new GitValue(workspace.Repository!);
        if (workspace.BaseBranch is not { } baseBranch)
            return new MergeResult(MergeOutcome.Refused, "The mission started from a detached HEAD; merge its branch " + workspace.Branch + " yourself.");
        var current = await repository.Run(["symbolic-ref", "--quiet", "--short", "HEAD"]);
        if (!current.Succeeded || current.TrimmedOutput != baseBranch)
            return new MergeResult(MergeOutcome.Refused, $"Switch {workspace.Repository} to {baseBranch} first; nothing was merged.");
        if ((await repository.Output(["status", "--porcelain=v1", "--untracked-files=no"])).Trim().Length > 0)
            return new MergeResult(MergeOutcome.Refused, $"Commit or stash your changes in {workspace.Repository} first; nothing was merged.");
        if (!(await repository.Run(["rev-parse", "--verify", "--quiet", commit + "^{commit}"])).Succeeded)
            return new MergeResult(MergeOutcome.Refused, "The reviewed commit is gone; review the mission again. Nothing was merged.");
        if (await repository.IsAncestor(commit, "HEAD")) return new MergeResult(MergeOutcome.NothingToMerge);
        var merged = await repository.Run([.. NoHooks(workspace.Path), "merge", "--no-ff", "--no-edit", "-m", "Merge " + workspace.Branch, commit]);
        if (merged.Succeeded) return new MergeResult(MergeOutcome.Merged);
        var conflicts = Records(await repository.Output(["diff", "--name-only", "--diff-filter=U", "-z"]));
        await repository.Run([.. NoHooks(workspace.Path), "merge", "--abort"]);
        return new MergeResult(MergeOutcome.Conflict, "The branch does not merge cleanly; nothing was merged.") { Conflicts = new(conflicts) };
    }

    /// <summary>Rebase first: replays the mission's branch onto the owner's branch as it is now, in the mission's worktree.</summary>
    public static async Task<MergeResult> RebaseFirst(MissionWorkspace workspace)
    {
        if (workspace.Kind == MissionWorkspaceKind.Snapshot)
            return new MergeResult(MergeOutcome.Refused, "A copied folder has no branch to rebase.");
        await CommitWork(workspace);
        var worktree = new GitValue(workspace.Path);
        var tip = await OwnerTip(workspace);
        var hardened = await Hardened(workspace);
        var rebased = await worktree.Run([.. hardened, "rebase", tip]);
        if (rebased.Succeeded) return new MergeResult(MergeOutcome.Merged);
        var conflicts = Records(await worktree.Output([.. hardened, "diff", "--name-only", "--diff-filter=U", "-z"]));
        await worktree.Run([.. hardened, "rebase", "--abort"]);
        return new MergeResult(MergeOutcome.Conflict, "The branch does not rebase cleanly; it was left as it was.") { Conflicts = new(conflicts) };
    }

    /// <summary>Discard: removes the worktree and its branch, or the copy. The owner's own folder is never touched.</summary>
    public static async Task Discard(MissionWorkspace workspace)
    {
        var removed = true;
        if (workspace.Kind == MissionWorkspaceKind.Worktree && workspace.Repository is { } root && Directory.Exists(root))
        {
            var repository = new GitValue(root);
            // ED-NEW-secfinal-22: git says whether this folder was a worktree of the repository and is now gone. A folder git did not remove is
            // not deleted here: it may be something else a changed record points at.
            removed = (await repository.Run(["-c", "core.fsmonitor=false", "worktree", "remove", "--force", workspace.Path])).Succeeded;
            if (workspace.Branch is { } branch && branch.StartsWith(BranchPrefix, StringComparison.Ordinal))
                await repository.Run(["branch", "-D", branch]);
            await repository.Run(["worktree", "prune"]);
        }
        if (removed) TryDelete(workspace.Path);
    }

    /// <summary>
    /// Discard for a workspace read back from a mission's record. ED-NEW-secfinal-26: git runs in the record's Repository, so a worktree is
    /// discarded only when that is the repository of <paramref name="recordedSource"/>, the mission's own project folder as its record keeps it.
    /// Otherwise (another repository, or a source that is gone or is no repository) this refuses with a <see cref="MissionWorkspaceError"/>
    /// and neither git nor the folder is touched. A copy has no repository and is not asked.
    /// </summary>
    public static async Task Discard(MissionWorkspace workspace, string recordedSource)
    {
        if (!await IsRecordedRepository(workspace, recordedSource).ConfigureAwait(false))
            throw new MissionWorkspaceError("The mission's workspace does not belong to the folder the mission was made from, so nth does not touch it.");
        await Discard(workspace).ConfigureAwait(false);
    }

    private static async Task<bool> IsRecordedRepository(MissionWorkspace workspace, string recordedSource)
    {
        if (workspace.Kind != MissionWorkspaceKind.Worktree || workspace.Repository is not { } repository) return true;
        if (string.IsNullOrWhiteSpace(recordedSource) || !Directory.Exists(recordedSource)) return false;
        var top = await new GitValue(recordedSource).Run(["rev-parse", "--show-toplevel"]).ConfigureAwait(false);
        if (!top.Succeeded || top.TrimmedOutput.Length == 0) return false;
        try
        {
            return string.Equals(System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(top.TrimmedOutput)),
                System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(repository)), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or System.IO.PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>Whether a finished mission's workspace is due for the sweep (7 days after it ended, unless kept).</summary>
    public static bool Due(DateTimeOffset ended, bool kept, DateTimeOffset now) => !kept && now - ended >= KeepFor;

    // ---- Snapshot ----

    private static MergeReview ReviewSnapshot(MissionWorkspace workspace, long byteCap, int fileCap)
    {
        var (changes, conflicts) = SnapshotChanges(workspace, byteCap, fileCap);
        var patch = new StringBuilder();
        foreach (var change in changes) patch.Append(change.Kind.ToString().ToLowerInvariant()).Append(' ').Append(change.Path).Append('\n');
        return new MergeReview(new(changes), Cap(patch.ToString())) { Conflicts = new(conflicts), Fingerprint = Fingerprint(changes) };
    }

    private static (List<WorkspaceChange> Changes, List<string> Conflicts) SnapshotChanges(MissionWorkspace workspace, long byteCap, int fileCap)
    {
        var copied = workspace.Manifest.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var tooLarge = string.Create(CultureInfo.InvariantCulture,
            $"The mission's copy grew past {fileCap} files or {byteCap >> 20} MB, so nth does not review or copy it back. Discard it, or copy what you need yourself.");
        var now = SnapshotFiles(workspace.Path, byteCap, fileCap, tooLarge).Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var changes = new List<WorkspaceChange>();
        foreach (var path in now.Order(StringComparer.Ordinal))
        {
            var hash = Hash(System.IO.Path.Combine(workspace.Path, path));
            if (!copied.TryGetValue(path, out var entry)) changes.Add(new WorkspaceChange(path, WorkspaceChangeKind.Added) { Hash = hash });
            else if (entry.Hash != hash) changes.Add(new WorkspaceChange(path, WorkspaceChangeKind.Modified) { Hash = hash });
        }
        foreach (var entry in workspace.Manifest)
            if (!now.Contains(entry.Path)) changes.Add(new WorkspaceChange(entry.Path, WorkspaceChangeKind.Deleted));
        var conflicts = new List<string>();
        foreach (var change in changes)
        {
            var original = System.IO.Path.Combine(workspace.Source, change.Path);
            var unchanged = copied.TryGetValue(change.Path, out var entry)
                ? File.Exists(original) && Hash(original) == entry.Hash
                : !File.Exists(original) && !Directory.Exists(original);
            if (!unchanged) conflicts.Add(change.Path);
        }
        return (changes, conflicts);
    }

    /// <summary>One hash over a snapshot's changes: each change's kind, path and file hash, in path order.</summary>
    private static string Fingerprint(IEnumerable<WorkspaceChange> changes)
    {
        var text = new StringBuilder();
        foreach (var change in changes.OrderBy(c => c.Path, StringComparer.Ordinal).ThenBy(c => c.Kind))
            text.Append(change.Kind).Append('\t').Append(change.Path).Append('\t').Append(change.Hash ?? "-").Append('\n');
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>
    /// Copies back exactly the changes the owner's card showed (<paramref name="reviewed"/>), all or none: a copy that
    /// changed since that review, or any file the owner changed since the copy, refuses it. Every file is staged beside
    /// its place and checked against the reviewed hash before any is put in place (ED-NEW-dextra-as5-15).
    /// </summary>
    private static MergeResult CopyBack(MissionWorkspace workspace, MergeReview reviewed, long byteCap, int fileCap)
    {
        const string ChangedSinceReview = "The copy changed after the review; nothing was copied back. Review it again.";
        var (changes, conflicts) = SnapshotChanges(workspace, byteCap, fileCap);
        if (reviewed.Fingerprint is not { Length: > 0 } fingerprint || Fingerprint(changes) != fingerprint)
            return new MergeResult(MergeOutcome.Refused, ChangedSinceReview);
        if (conflicts.Count > 0)
            return new MergeResult(MergeOutcome.Conflict, "You changed some of these files since the mission started; nothing was copied back.") { Conflicts = new(conflicts) };
        if (changes.Count == 0) return new MergeResult(MergeOutcome.NothingToMerge);
        var staged = new List<(string Staged, string Original)>();
        try
        {
            foreach (var change in changes.Where(c => c.Kind != WorkspaceChangeKind.Deleted))
            {
                var original = System.IO.Path.Combine(workspace.Source, change.Path);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(original)!);
                var copy = original + ".nth-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
                File.Copy(System.IO.Path.Combine(workspace.Path, change.Path), copy);
                staged.Add((copy, original));
                if (Hash(copy) != change.Hash)
                {
                    RemoveStaged(staged);
                    return new MergeResult(MergeOutcome.Refused, ChangedSinceReview);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            RemoveStaged(staged);
            return new MergeResult(MergeOutcome.Refused, "The changes could not be copied back (" + error.Message + "); nothing was copied back.");
        }
        foreach (var (copy, original) in staged) File.Move(copy, original, overwrite: true);
        foreach (var change in changes.Where(c => c.Kind == WorkspaceChangeKind.Deleted)) File.Delete(System.IO.Path.Combine(workspace.Source, change.Path));
        return new MergeResult(MergeOutcome.Merged);
    }

    private static void RemoveStaged(IEnumerable<(string Staged, string Original)> staged)
    {
        foreach (var (copy, _) in staged)
        {
            try { File.Delete(copy); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A staged copy left behind is harmless (its name ends .nth-<id>), and the owner's file was not touched
                // (ED-NEW-dextra-as5-15).
            }
        }
    }

    // ---- Shared ----

    /// <summary>Commits whatever the agents left uncommitted on the mission's branch, so the card shows all of it.</summary>
    private static async Task CommitWork(MissionWorkspace workspace)
    {
        var worktree = new GitValue(workspace.Path);
        var hardened = await Hardened(workspace);
        if ((await worktree.Output([.. hardened, "status", "--porcelain=v1", "--untracked-files=all", "--ignore-submodules=all"])).Trim().Length == 0) return;
        GitValue.Check(await worktree.Run([.. hardened, "add", "-A"]));
        var identity = (await worktree.Run([.. hardened, "config", "user.email"])).TrimmedOutput.Length > 0
            ? null
            : new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = "nth", ["GIT_AUTHOR_EMAIL"] = "nth@localhost", ["GIT_COMMITTER_NAME"] = "nth", ["GIT_COMMITTER_EMAIL"] = "nth@localhost",
            };
        GitValue.Check(await worktree.Run([.. hardened, "commit", "--no-verify", "--no-gpg-sign", "-q", "-m", "Mission work"], identity));
    }

    /// <summary>
    /// The global options of every git step nth runs in a mission's worktree, the folder its agents control
    /// (ED-NEW-dextra-as5-15): the admin folder and the work tree named explicitly, so git never follows the worktree's own
    /// <c>.git</c> link (which must still point where it did, or nothing runs); no hooks; no fsmonitor command; submodules
    /// and nested repositories never entered, so no config an agent wrote in one is ever read.
    /// </summary>
    private static async Task<string[]> Hardened(MissionWorkspace workspace)
    {
        var gitDir = workspace.GitDir ?? await FindGitDir(workspace)
            ?? throw new MissionWorkspaceError("The mission's worktree is not in its repository any more, so nth runs no git there.");
        CheckGitLink(workspace.Path, gitDir);
        return
        [
            "--git-dir=" + gitDir, "--work-tree=" + workspace.Path, .. NoHooks(workspace.Path),
            "-c", "core.fsmonitor=false", "-c", "diff.ignoreSubmodules=all", "-c", "status.submoduleSummary=false",
            "-c", "submodule.recurse=false", "-c", "commit.gpgSign=false",
        ];
    }

    /// <summary>
    /// The worktree's admin folder, found from the owner's repository: each <c>worktrees/&lt;name&gt;/gitdir</c> there
    /// names the <c>.git</c> link of its worktree. Nothing in the worktree itself is read. Null when none names this one.
    /// </summary>
    private static async Task<string?> FindGitDir(MissionWorkspace workspace)
    {
        if (workspace.Repository is not { } root || !Directory.Exists(root)) return null;
        var common = (await new GitValue(root).Output(["rev-parse", "--git-common-dir"])).Trim();
        var folder = System.IO.Path.Combine(System.IO.Path.GetFullPath(System.IO.Path.Combine(root, common)), "worktrees");
        if (!Directory.Exists(folder)) return null;
        var link = System.IO.Path.Combine(workspace.Path, ".git");
        foreach (var admin in Directory.EnumerateDirectories(folder))
        {
            string target;
            try { target = File.ReadAllText(System.IO.Path.Combine(admin, "gitdir")).Trim(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }
            if (SamePath(target, link)) return System.IO.Path.GetFullPath(admin);
        }
        return null;
    }

    /// <summary>
    /// The worktree's <c>.git</c> must still be a plain, small file naming its admin folder; a folder, a link, or a file
    /// naming another git folder means it was changed, and nth runs no git there.
    /// </summary>
    private static void CheckGitLink(string worktree, string gitDir)
    {
        const string Changed = "The mission's .git link was changed, so nth runs no git in its worktree. Discard the mission, or look at it yourself.";
        var link = new FileInfo(System.IO.Path.Combine(worktree, ".git"));
        if (Directory.Exists(link.FullName) || !link.Exists || link.Attributes.HasFlag(FileAttributes.ReparsePoint) || link.Length > GitLinkCap)
            throw new MissionWorkspaceError(Changed);
        string text;
        try { text = File.ReadAllText(link.FullName).Trim(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { throw new MissionWorkspaceError(Changed + " (" + error.Message + ")"); }
        if (!text.StartsWith("gitdir:", StringComparison.Ordinal)) throw new MissionWorkspaceError(Changed);
        var target = text["gitdir:".Length..].Trim();
        if (!System.IO.Path.IsPathRooted(target)) target = System.IO.Path.Combine(worktree, target);
        if (!SamePath(target, gitDir)) throw new MissionWorkspaceError(Changed);
    }

    private static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(System.IO.Path.GetFullPath(a).TrimEnd('\\', '/'), System.IO.Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>The owner's branch as it is now, or the start commit when he was detached.</summary>
    private static async Task<string> OwnerTip(MissionWorkspace workspace)
    {
        if (workspace.BaseBranch is not { } baseBranch) return workspace.BaseCommit!;
        var repository = new GitValue(workspace.Repository!);
        var tip = await repository.Run(["rev-parse", "--verify", "--quiet", "refs/heads/" + baseBranch]);
        return tip.Succeeded ? tip.TrimmedOutput : workspace.BaseCommit!;
    }

    /// <summary>No hook runs for nth's own git steps: a hooks path that does not exist, beside the workspace.</summary>
    private static string[] NoHooks(string workspace) => ["-c", "core.hooksPath=" + workspace + ".no-hooks"];

    private static List<string> Records(string output) => output.Split('\0', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static int Count(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static string Cap(string patch) => patch.Length > PatchCap ? patch[..PatchCap] : patch;

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (!Directory.Exists(folder)) return;
            // ED-NEW-secfinal-22: a link is removed as a link and never followed out of the folder. The folder itself being a link loses only the
            // link. .NET unlinks a junction inside a tree but then throws and leaves the folder, so the links go first, each on its own; the rest
            // is an ordinary recursive delete.
            if (File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(folder, recursive: false);
                return;
            }
            UnlinkLinks(folder);
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A folder of nth's own (the mission's copy, never the owner's) that cannot be removed now only takes disk space;
            // the branch and the journal are still cleaned up (ED-NEW-dextra-as5-13).
        }
    }

    // Removes every link (junction or symbolic link to a folder) below the folder, without entering one.
    private static void UnlinkLinks(string folder)
    {
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            if (File.GetAttributes(directory).HasFlag(FileAttributes.ReparsePoint)) Directory.Delete(directory, recursive: false);
            else UnlinkLinks(directory);
        }
    }

    // ---- Journal ----

    public static JsonObject ToJson(MissionWorkspace workspace)
    {
        var json = new JsonObject
        {
            ["kind"] = workspace.Kind == MissionWorkspaceKind.Worktree ? "worktree" : "snapshot",
            ["source"] = workspace.Source, ["path"] = workspace.Path, ["workingFolder"] = workspace.WorkingFolder,
        };
        if (workspace.Repository is { } repository) json["repository"] = repository;
        if (workspace.Branch is { } branch) json["branch"] = branch;
        if (workspace.BaseBranch is { } baseBranch) json["baseBranch"] = baseBranch;
        if (workspace.BaseCommit is { } baseCommit) json["baseCommit"] = baseCommit;
        if (workspace.GitDir is { } gitDir) json["gitDir"] = gitDir;
        if (workspace.Manifest.Count > 0)
            json["manifest"] = new JsonArray([.. workspace.Manifest.Select(e => (JsonNode)new JsonObject { ["path"] = e.Path, ["hash"] = e.Hash, ["length"] = e.Length })]);
        return json;
    }

    public static MissionWorkspace? FromJson(JsonNode? node)
    {
        if (node is not JsonObject json) return null;
        var kind = (string?)json["kind"] switch { "worktree" => MissionWorkspaceKind.Worktree, "snapshot" => MissionWorkspaceKind.Snapshot, _ => (MissionWorkspaceKind?)null };
        if (kind is not { } k || (string?)json["source"] is not { } source || (string?)json["path"] is not { } path) return null;
        var manifest = (json["manifest"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(e => new SnapshotEntry((string?)e["path"] ?? "", (string?)e["hash"] ?? "", (long?)e["length"] ?? 0))
            .Where(e => e.Path.Length > 0).ToList();
        return new MissionWorkspace(k, source, path)
        {
            WorkingFolder = (string?)json["workingFolder"] ?? path, Repository = (string?)json["repository"], Branch = (string?)json["branch"],
            BaseBranch = (string?)json["baseBranch"], BaseCommit = (string?)json["baseCommit"], GitDir = (string?)json["gitDir"],
            Manifest = new(manifest),
        };
    }
}
