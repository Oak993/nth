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

    /// <summary>Snapshot: every file as it was copied, so a copy back can tell the owner's later edits from the mission's.</summary>
    public ValueList<SnapshotEntry> Manifest { get; init; } = ValueList<SnapshotEntry>.Empty;
}

/// <summary>What one file of a mission did.</summary>
public enum WorkspaceChangeKind { Added, Modified, Deleted }

public sealed record WorkspaceChange(string Path, WorkspaceChangeKind Kind)
{
    public int Insertions { get; init; }

    public int Deletions { get; init; }
}

/// <summary>What a merge card shows (12.6a): the changes against the owner's branch or folder.</summary>
public sealed record MergeReview(ValueList<WorkspaceChange> Changes, string Patch)
{
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
public static class MissionWorkspaces
{
    /// <summary>The largest folder a snapshot copies; a larger one is refused (Blocked), never half copied.</summary>
    public const long SnapshotByteCap = 1L << 30;

    public const int SnapshotFileCap = 20_000;

    /// <summary>How long a finished mission's workspace stays before the sweep removes it (12.6a).</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromDays(7);

    private const string BranchPrefix = "nth/mission-";
    private const int PatchCap = 40_000;

    /// <summary>The mission's branch: <c>nth/mission-&lt;first 8 of the id&gt;-&lt;slug of the title&gt;</c>.</summary>
    public static string BranchName(Guid missionId, string title) =>
        BranchPrefix + missionId.ToString("N", CultureInfo.InvariantCulture)[..8] + "-" + Slug(title);

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
        long byteCap = SnapshotByteCap, int fileCap = SnapshotFileCap)
    {
        if (!Directory.Exists(source)) throw new MissionWorkspaceError($"The folder {source} does not exist.");
        if (Directory.Exists(folder) || File.Exists(folder)) throw new MissionWorkspaceError("This mission already has a workspace.");
        var git = new GitValue(source);
        if (await git.IsRepository() && await git.HasCommits()) return await CreateWorktree(missionId, title, source, folder, git);
        return CreateSnapshot(source, folder, byteCap, fileCap);
    }

    private static async Task<MissionWorkspace> CreateWorktree(Guid missionId, string title, string source, string folder, GitValue git)
    {
        var root = (await git.Output(["rev-parse", "--show-toplevel"])).Trim();
        var repository = new GitValue(root);
        var prefix = (await git.Output(["rev-parse", "--show-prefix"])).Trim().TrimEnd('/');
        var head = await repository.Run(["symbolic-ref", "--quiet", "--short", "HEAD"]);
        var baseBranch = head.Succeeded && head.TrimmedOutput.Length > 0 ? head.TrimmedOutput : null;
        var baseCommit = await repository.CommitHash("HEAD");
        var branch = BranchName(missionId, title);
        GitValue.Check(await repository.Run([.. NoHooks(folder), "worktree", "add", "-b", branch, folder, baseCommit]));
        var working = prefix.Length == 0 ? folder : System.IO.Path.Combine(folder, prefix.Replace('/', System.IO.Path.DirectorySeparatorChar));
        return new MissionWorkspace(MissionWorkspaceKind.Worktree, source, folder)
        {
            WorkingFolder = working, Repository = root, Branch = branch, BaseBranch = baseBranch, BaseCommit = baseCommit,
        };
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

    /// <summary>Every regular file under the folder, relative; links and junctions are never followed or copied.</summary>
    private static List<string> SnapshotFiles(string source, long byteCap = long.MaxValue, int fileCap = int.MaxValue)
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
                    throw new MissionWorkspaceError(string.Create(CultureInfo.InvariantCulture,
                        $"The folder is too large to copy for a mission (over {fileCap} files or {byteCap >> 20} MB). Put it in a git repository to use a worktree."));
            }
        }
        return files;
    }

    /// <summary>The merge card's contents: the branch's changes (uncommitted work included) or the copy's changes.</summary>
    public static async Task<MergeReview> Review(MissionWorkspace workspace)
    {
        if (workspace.Kind == MissionWorkspaceKind.Snapshot) return ReviewSnapshot(workspace);
        await CommitWork(workspace);
        var repository = new GitValue(workspace.Repository!);
        var tip = await OwnerTip(workspace);
        var mergeBase = (await repository.Output(["merge-base", tip, workspace.Branch!])).Trim();
        var changes = new List<WorkspaceChange>();
        var status = Records(await repository.Output(["diff", "--name-status", "-z", "--no-renames", mergeBase, workspace.Branch!]));
        var counts = Records(await repository.Output(["diff", "--numstat", "-z", "--no-renames", mergeBase, workspace.Branch!]));
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
        var patch = await repository.Output(["diff", "--no-ext-diff", "--no-color", mergeBase, workspace.Branch!]);
        return new MergeReview(new(changes), Cap(patch)) { BaseMoved = !string.Equals(tip, workspace.BaseCommit, StringComparison.Ordinal) };
    }

    /// <summary>
    /// Merges the mission's branch into the owner's branch, on his tap only. His checkout must be on that branch with no
    /// uncommitted change; a conflict aborts the merge and leaves everything as it was.
    /// </summary>
    public static async Task<MergeResult> Merge(MissionWorkspace workspace)
    {
        if (workspace.Kind == MissionWorkspaceKind.Snapshot) return CopyBack(workspace);
        await CommitWork(workspace);
        var repository = new GitValue(workspace.Repository!);
        if (workspace.BaseBranch is not { } baseBranch)
            return new MergeResult(MergeOutcome.Refused, "The mission started from a detached HEAD; merge its branch " + workspace.Branch + " yourself.");
        var current = await repository.Run(["symbolic-ref", "--quiet", "--short", "HEAD"]);
        if (!current.Succeeded || current.TrimmedOutput != baseBranch)
            return new MergeResult(MergeOutcome.Refused, $"Switch {workspace.Repository} to {baseBranch} first; nothing was merged.");
        if ((await repository.Output(["status", "--porcelain=v1", "--untracked-files=no"])).Trim().Length > 0)
            return new MergeResult(MergeOutcome.Refused, $"Commit or stash your changes in {workspace.Repository} first; nothing was merged.");
        if (await repository.IsAncestor(workspace.Branch!, "HEAD")) return new MergeResult(MergeOutcome.NothingToMerge);
        var merged = await repository.Run([.. NoHooks(workspace.Path), "merge", "--no-ff", "--no-edit", "-m", "Merge " + workspace.Branch, workspace.Branch!]);
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
        var rebased = await worktree.Run([.. NoHooks(workspace.Path), "rebase", tip]);
        if (rebased.Succeeded) return new MergeResult(MergeOutcome.Merged);
        var conflicts = Records(await worktree.Output(["diff", "--name-only", "--diff-filter=U", "-z"]));
        await worktree.Run([.. NoHooks(workspace.Path), "rebase", "--abort"]);
        return new MergeResult(MergeOutcome.Conflict, "The branch does not rebase cleanly; it was left as it was.") { Conflicts = new(conflicts) };
    }

    /// <summary>Discard: removes the worktree and its branch, or the copy. The owner's own folder is never touched.</summary>
    public static async Task Discard(MissionWorkspace workspace)
    {
        if (workspace.Kind == MissionWorkspaceKind.Worktree && workspace.Repository is { } root && Directory.Exists(root))
        {
            var repository = new GitValue(root);
            await repository.Run(["worktree", "remove", "--force", workspace.Path]);
            if (workspace.Branch is { } branch && branch.StartsWith(BranchPrefix, StringComparison.Ordinal))
                await repository.Run(["branch", "-D", branch]);
            await repository.Run(["worktree", "prune"]);
        }
        TryDelete(workspace.Path);
    }

    /// <summary>Whether a finished mission's workspace is due for the sweep (7 days after it ended, unless kept).</summary>
    public static bool Due(DateTimeOffset ended, bool kept, DateTimeOffset now) => !kept && now - ended >= KeepFor;

    // ---- Snapshot ----

    private static MergeReview ReviewSnapshot(MissionWorkspace workspace)
    {
        var (changes, conflicts) = SnapshotChanges(workspace);
        var patch = new StringBuilder();
        foreach (var change in changes) patch.Append(change.Kind.ToString().ToLowerInvariant()).Append(' ').Append(change.Path).Append('\n');
        return new MergeReview(new(changes), Cap(patch.ToString())) { Conflicts = new(conflicts) };
    }

    private static (List<WorkspaceChange> Changes, List<string> Conflicts) SnapshotChanges(MissionWorkspace workspace)
    {
        var copied = workspace.Manifest.ToDictionary(e => e.Path, StringComparer.Ordinal);
        var now = SnapshotFiles(workspace.Path).Select(p => p.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        var changes = new List<WorkspaceChange>();
        foreach (var path in now.Order(StringComparer.Ordinal))
        {
            var hash = Hash(System.IO.Path.Combine(workspace.Path, path));
            if (!copied.TryGetValue(path, out var entry)) changes.Add(new WorkspaceChange(path, WorkspaceChangeKind.Added));
            else if (entry.Hash != hash) changes.Add(new WorkspaceChange(path, WorkspaceChangeKind.Modified));
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

    /// <summary>Copies the mission's changes back, all or none: any file the owner changed since the copy refuses it.</summary>
    private static MergeResult CopyBack(MissionWorkspace workspace)
    {
        var (changes, conflicts) = SnapshotChanges(workspace);
        if (conflicts.Count > 0)
            return new MergeResult(MergeOutcome.Conflict, "You changed some of these files since the mission started; nothing was copied back.") { Conflicts = new(conflicts) };
        if (changes.Count == 0) return new MergeResult(MergeOutcome.NothingToMerge);
        foreach (var change in changes)
        {
            var original = System.IO.Path.Combine(workspace.Source, change.Path);
            if (change.Kind == WorkspaceChangeKind.Deleted) { File.Delete(original); continue; }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(original)!);
            var staged = original + ".nth-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..8];
            File.Copy(System.IO.Path.Combine(workspace.Path, change.Path), staged);
            File.Move(staged, original, overwrite: true);
        }
        return new MergeResult(MergeOutcome.Merged);
    }

    // ---- Shared ----

    /// <summary>Commits whatever the agents left uncommitted on the mission's branch, so the card shows all of it.</summary>
    private static async Task CommitWork(MissionWorkspace workspace)
    {
        var worktree = new GitValue(workspace.Path);
        if ((await worktree.Output(["status", "--porcelain=v1", "--untracked-files=all"])).Trim().Length == 0) return;
        GitValue.Check(await worktree.Run(["add", "-A"]));
        var identity = (await worktree.Run(["config", "user.email"])).TrimmedOutput.Length > 0
            ? null
            : new Dictionary<string, string>
            {
                ["GIT_AUTHOR_NAME"] = "nth", ["GIT_AUTHOR_EMAIL"] = "nth@localhost", ["GIT_COMMITTER_NAME"] = "nth", ["GIT_COMMITTER_EMAIL"] = "nth@localhost",
            };
        GitValue.Check(await worktree.Run([.. NoHooks(workspace.Path), "commit", "--no-verify", "--no-gpg-sign", "-q", "-m", "Mission work"], identity));
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
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
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
            BaseBranch = (string?)json["baseBranch"], BaseCommit = (string?)json["baseCommit"], Manifest = new(manifest),
        };
    }
}
