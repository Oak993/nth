// PORT-ONLY: Dextra (DESIGN.md rev 5, 12.6a, 12.6b "Revert changes" and "Resume or Restart a stopped task"; unit AS-6;
// ED-NEW-dextra-as6-6, -7). No Swift twin.
// Revert changes: the stopped task's worktree changes are taken out, and the branch tip is kept as a hidden ref
// (refs/nth/reverted/<mission>/<stamp>, which `git branch` and `git tag` never list) that the Activity log can restore
// for 7 days. A copied folder is set aside beside itself the same way. The owner's own files are never touched. Restore
// puts the work back where it was, Drop forgets it, and the sweep drops what is older than 7 days.
using System.Globalization;
using System.Text.Json.Nodes;
using Marrow.Core.Port;
using GitValue = Marrow.Services.Git.Git;

namespace Marrow.Services.Assistant;

/// <summary>The work a Revert took out of a task's workspace, kept for 7 days (12.6b).</summary>
public sealed record RevertedWork(MissionWorkspace Workspace, string Ref, string? Commit, DateTimeOffset At, int ChangedFiles)
{
    /// <summary>When the sweep drops it (7 days after the revert).</summary>
    public DateTimeOffset Expires => At + MissionWorkspaces.KeepFor;
}

public static partial class MissionWorkspaces
{
    /// <summary>The namespace of the hidden refs a Revert keeps. Not under refs/heads or refs/tags, so no tool lists them.</summary>
    public const string RevertedRefRoot = "refs/nth/reverted/";

    private const string RevertedSuffix = ".reverted-";

    /// <summary>
    /// Takes the task's changes out of its workspace and keeps them for 7 days. A worktree's work is committed on its
    /// branch, the branch tip is kept as a hidden ref, then the worktree and branch are removed (Discard); a copy is
    /// moved aside. Nothing in the owner's own folder or branches is touched.
    /// </summary>
    public static async Task<RevertedWork> Revert(MissionWorkspace workspace, Guid missionId, DateTimeOffset now, int changedFiles = 0)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var stamp = now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        if (workspace.Kind == MissionWorkspaceKind.Snapshot)
        {
            if (!Directory.Exists(workspace.Path)) throw new MissionWorkspaceError("The mission's copy is gone, so there is nothing to revert.");
            var aside = workspace.Path + RevertedSuffix + stamp;
            if (Directory.Exists(aside) || File.Exists(aside)) throw new MissionWorkspaceError("A reverted copy of this mission already exists beside it.");
            try { Directory.Move(workspace.Path, aside); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new MissionWorkspaceError("The mission's copy could not be set aside: " + error.Message);
            }
            return new RevertedWork(workspace, aside, null, now, changedFiles);
        }
        if (workspace.Repository is not { } root || !Directory.Exists(root) || workspace.Branch is not { } branch
            || !branch.StartsWith(BranchPrefix, StringComparison.Ordinal))
            throw new MissionWorkspaceError("The mission's repository or branch is not one nth made, so nothing was reverted.");
        await CommitWork(workspace);
        var repository = new GitValue(root);
        var commit = (await repository.Output(["rev-parse", "--verify", "refs/heads/" + branch])).Trim();
        var kept = RevertedRefRoot + missionId.ToString("N", CultureInfo.InvariantCulture) + "/" + stamp;
        await repository.UpdateRef(kept, commit);
        await Discard(workspace);
        return new RevertedWork(workspace, kept, commit, now, changedFiles);
    }

    /// <summary>
    /// Puts reverted work back where it was: the worktree on its own branch at the saved commit, or the copy in its place.
    /// Refused when the place or the branch is taken again, or the saved work is gone. The hidden ref goes once the branch
    /// holds the work.
    /// </summary>
    public static async Task<MissionWorkspace> Restore(RevertedWork reverted)
    {
        ArgumentNullException.ThrowIfNull(reverted);
        var workspace = reverted.Workspace;
        if (workspace.Kind == MissionWorkspaceKind.Snapshot)
        {
            if (!Directory.Exists(reverted.Ref)) throw new MissionWorkspaceError("The reverted copy is gone.");
            if (Directory.Exists(workspace.Path) || File.Exists(workspace.Path)) throw new MissionWorkspaceError("The mission's place is taken again, so its copy cannot come back.");
            try { Directory.Move(reverted.Ref, workspace.Path); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                throw new MissionWorkspaceError("The reverted copy could not be put back: " + error.Message);
            }
            return workspace;
        }
        if (workspace.Repository is not { } root || !Directory.Exists(root) || workspace.Branch is not { } branch
            || !branch.StartsWith(BranchPrefix, StringComparison.Ordinal) || reverted.Commit is not { Length: > 0 } commit)
            throw new MissionWorkspaceError("The reverted work names no repository, branch or commit nth made, so it cannot come back.");
        var repository = new GitValue(root);
        if (!(await repository.Run(["rev-parse", "--verify", "--quiet", reverted.Ref])).Succeeded
            || !(await repository.Run(["rev-parse", "--verify", "--quiet", commit + "^{commit}"])).Succeeded)
            throw new MissionWorkspaceError("The reverted work is gone from the repository.");
        if ((await repository.Run(["rev-parse", "--verify", "--quiet", "refs/heads/" + branch])).Succeeded)
            throw new MissionWorkspaceError($"The branch {branch} exists again, so the work cannot come back onto it.");
        if (Directory.Exists(workspace.Path) || File.Exists(workspace.Path))
            throw new MissionWorkspaceError("The mission's place is taken again, so its worktree cannot come back.");
        GitValue.Check(await repository.Run([.. NoHooks(workspace.Path), "worktree", "add", "-b", branch, workspace.Path, commit]));
        var back = workspace with { GitDir = null };
        if (await FindGitDir(back) is not { } gitDir)
        {
            await Discard(back);
            throw new MissionWorkspaceError("The restored worktree could not be found in its repository.");
        }
        await repository.Run(["update-ref", "-d", reverted.Ref]);
        return back with { GitDir = gitDir };
    }

    /// <summary>Forgets reverted work for good: its hidden ref, or its set-aside copy. Safe to repeat.</summary>
    public static async Task DropReverted(RevertedWork reverted)
    {
        ArgumentNullException.ThrowIfNull(reverted);
        var workspace = reverted.Workspace;
        if (workspace.Kind == MissionWorkspaceKind.Snapshot)
        {
            if (reverted.Ref.Contains(RevertedSuffix, StringComparison.Ordinal)) TryDelete(reverted.Ref);
            return;
        }
        if (workspace.Repository is { } root && Directory.Exists(root) && reverted.Ref.StartsWith(RevertedRefRoot, StringComparison.Ordinal))
            await new GitValue(root).Run(["update-ref", "-d", reverted.Ref]);
    }

    /// <summary>Whether reverted work is due for the sweep (7 days after the revert).</summary>
    public static bool RevertedDue(RevertedWork reverted, DateTimeOffset now) => now - reverted.At >= KeepFor;

    /// <summary>Whether the reverted work can still be restored.</summary>
    public static bool Restorable(RevertedWork reverted, DateTimeOffset now) => !RevertedDue(reverted, now);

    /// <summary>
    /// Whether <paramref name="path"/> lies strictly under <paramref name="root"/>, compared fully resolved (no <c>..</c>,
    /// no case games) and with no link or junction on the way down. A path that fails this is never acted on (the lean
    /// rules, item 10): the caller refuses and stops, it does not fall back to a wider folder.
    /// </summary>
    public static bool StrictlyUnder(string root, string path)
    {
        try
        {
            var top = System.IO.Path.GetFullPath(root).TrimEnd('\\', '/');
            var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.Length <= top.Length + 1) return false;
            if (!full.StartsWith(top + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
            // Every folder between the root and the path (the path itself included, when it exists) is a plain folder.
            var walk = full;
            while (walk.Length > top.Length)
            {
                var info = new DirectoryInfo(walk);
                if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
                walk = System.IO.Path.GetDirectoryName(walk) ?? top;
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // ---- journal ----

    public static JsonObject ToJson(RevertedWork reverted)
    {
        var json = new JsonObject
        {
            ["workspace"] = ToJson(reverted.Workspace),
            ["ref"] = reverted.Ref,
            ["at"] = reverted.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["changedFiles"] = reverted.ChangedFiles,
        };
        if (reverted.Commit is { } commit) json["commit"] = commit;
        return json;
    }

    public static RevertedWork? RevertedFromJson(JsonNode? node)
    {
        if (node is not JsonObject json || FromJson(json["workspace"]) is not { } workspace || (string?)json["ref"] is not { } kept
            || (string?)json["at"] is not { } at
            || !DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when))
            return null;
        return new RevertedWork(workspace, kept, (string?)json["commit"], when, (int?)json["changedFiles"] ?? 0);
    }
}
