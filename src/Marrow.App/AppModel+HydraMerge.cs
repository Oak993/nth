using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Marrow.App.Runtime;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Providers;
using Marrow.Services.Store;

namespace Marrow.App;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B2 1fbf81e), :1-894.

// :12-18
internal sealed record HydraMergeHead(string Name, long Index, string Task, ValueList<string> Files);

// :20-25
internal enum HydraProjectMerge
{
    Merged,
    AlreadyOnMain,
    Failed,
}

public sealed partial class AppModel
{
    // :28-32
    // Swift: HydraWork; renamed because AppModel+Hydra.swift already ports a same-named method.
    private sealed record HydraMergeWork(
        ValueList<string> Paths,
        ValueList<Guid> TurnIDs,
        ValueList<Guid> HeadIDs,
        ValueList<string> Outside,
        ValueList<HydraMergeHead> Heads,
        IReadOnlyDictionary<string, string> Held);

    // :35
    public bool IsAnyHydraMergeRunning => LiveRuntimes.Any(runtime => runtime.IsHydraMerging);

    // :41
    private static readonly TimeSpan HydraMergeWindow = TimeSpan.FromDays(3);

    // :48-74
    public async Task ResumeHydraMerges()
    {
        if (!Settings.HydraAutoMerge) return;
        var cutoff = SwiftDate.Now - HydraMergeWindow;
        var leads = Threads
            .Where(thread => !thread.IsArchived && HydraIsOn(thread) && thread.UpdatedAt > cutoff)
            .OrderBy(thread => thread.Id.ToString("D"), StringComparer.Ordinal)
            .ToArray();

        // Load competitors before selecting a merge winner, so a cold runtime does not
        // keep finished work looking unfinished until a later sweep.
        foreach (var lead in leads) await Runtime(lead.Id).EnsureLoaded(); // :53-57

        foreach (var lead in leads) // :58
        {
            if (ExistingRuntime(lead.Id) is not { } runtime || !runtime.IsReadyForHydraMerge || runtime.IsHydraMerging)
                continue; // :59-60
            var finished = runtime.HydraUnmergedTurns.Where(turn => turn.Status == TurnStatus.Completed); // :65
            var hasLandedHeadWork = HydraTeam(lead.Id).Any(head => head.Hydra is { MergedAt: null, Landing.Landed: true }); // :69-70
            var hasOwnWork = finished.Any(turn => turn.TouchedPaths is { Count: > 0 }) || hasLandedHeadWork; // :69-71
            if (hasOwnWork) await AutoMergeHydraWork(lead.Id); // :72
        }
    }

    // :78-183
    public async Task AutoMergeHydraWork(Guid leadID)
    {
        if (!Settings.HydraAutoMerge || Thread(leadID) is not { } lead || !HydraIsOn(lead) ||
            Project(lead.ProjectID) is not { } project || ExistingRuntime(leadID) is not { } runtime ||
            runtime.IsHydraMerging || IsAnyHydraMergeRunning || !runtime.IsReadyForHydraMerge)
            return; // :85-87

        runtime.IsHydraMerging = true; // :88
        runtime.HydraMergeStartedAt = SwiftDate.Now; // :89
        runtime.HydraMergeNoteID = Guid.NewGuid().ToString("D").ToUpperInvariant(); // :90
        runtime.HydraMergeStage = HydraMergeStage.Gathering.Words; // :91
        runtime.HydraMergeStep = HydraMergeStage.Gathering; // :91-92
        try
        {
            if (runtime.HydraLanding is { } landing) await landing.Completion; // :103
            if (!runtime.IsReadyForHydraMerge) return; // :104

            var ownCheckout = lead.WorktreePath ?? project.Path; // :112
            var own = await HydraWorkFor(leadID, runtime, ownCheckout, new Git(ownCheckout), project.Id,
                ownCheckout, project.Id, sweepsCheckout: true); // :113
            var groups = new List<(Project Project, string Checkout, HydraMergeWork Work)> { (project, ownCheckout, own) }; // :114
            var owners = new Dictionary<string, Project>(SwiftStringComparer.Instance); // :117
            foreach (var path in own.Outside) // :118
            {
                var holder = Projects
                    .Where(candidate => candidate.Id != project.Id && TouchedPaths.Relative(path, candidate.Path) is not null)
                    .OrderByDescending(candidate => candidate.Path.Length)
                    .FirstOrDefault();
                if (holder is not null) owners[path] = holder; // :119-121
            }
            foreach (var other in Projects.Where(candidate => candidate.Id != project.Id && owners.Values.Any(owner => owner.Id == candidate.Id))) // :123
            {
                var work = await HydraWorkFor(leadID, runtime, other.Path, new Git(other.Path), other.Id,
                    ownCheckout, project.Id, sweepsCheckout: false); // :124
                groups.Add((other, other.Path, work)); // :125
            }

            var stray = own.Outside.Where(path => !IsScratchPath(path) &&
                !Projects.Any(candidate => TouchedPaths.Relative(path, candidate.Path) is not null)).ToArray(); // :130
            string StrayBody(IReadOnlyList<string> paths) // :131-134
            {
                var shown = string.Join(", ", paths.Take(6).Select(path => $"`{path}`")) +
                    (paths.Count > 6 ? $" and {paths.Count - 6} more" : "");
                return $"{(paths.Count == 1 ? "1 file was" : $"{paths.Count} files were")} changed outside every project: {shown}. Add the folder that holds them to the sidebar and ask for the merge again.";
            }

            var held = new Dictionary<string, string>(SwiftStringComparer.Instance); // :137
            foreach (var group in groups) // :138
                foreach (var entry in group.Work.Held)
                    held[SwiftPath.AppendingPathComponent(group.Checkout, entry.Key)] = entry.Value; // :139-140
            if (held.Count > 0) // :143
            {
                RecordHydraHold(held, leadID, runtime); // :144
                return; // :145
            }

            groups.RemoveAll(group => group.Work.Paths.Count == 0); // :147
            if (groups.Count == 0) // :148
            {
                if (stray.Length == 0) // :149
                {
                    runtime.MarkHydraMerged(own.TurnIDs); // :153
                    var spentAt = SwiftDate.Now; // :154
                    foreach (var headID in own.HeadIDs) UpdateHydraHead(headID, info => info with { MergedAt = spentAt }); // :155
                    if (runtime.HydraMerges.LastOrDefault()?.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Held)
                        RecordHydraSettlement(leadID, runtime); // :156-158
                    return; // :159
                }
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Stray,
                    Project = null, Label = null, Url = null, Files = stray.Length, Detail = StrayBody(stray),
                }); // :161
                Note(leadID, "Hydra did not merge: the team's files are outside every project.", StrayBody(stray)); // :162
                return; // :163
            }

            var several = groups.Count > 1; // :165
            var outcomes = new List<HydraProjectMerge>(); // :166
            foreach (var group in groups) // :167
                outcomes.Add(await MergeHydraProject(leadID, lead, group.Project, group.Checkout, group.Work, runtime, several)); // :168
            if (outcomes.Contains(HydraProjectMerge.Failed)) return; // :170

            runtime.MarkHydraMerged(groups.SelectMany(group => group.Work.TurnIDs).ToArray()); // :173
            var mergedAt = SwiftDate.Now; // :174
            foreach (var headID in groups.SelectMany(group => group.Work.HeadIDs))
                UpdateHydraHead(headID, info => info with { MergedAt = mergedAt }); // :175
            if (stray.Length > 0) // :177
            {
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Stray,
                    Project = null, Label = null, Url = null, Files = stray.Length, Detail = StrayBody(stray),
                }); // :178
                Note(leadID, $"Hydra left {(stray.Length == 1 ? "a file" : "files")} outside every project.", StrayBody(stray)); // :179
            }
            else if (outcomes.All(outcome => outcome == HydraProjectMerge.AlreadyOnMain))
            {
                RecordHydraSettlement(leadID, runtime); // :180-181
            }
        }
        finally
        {
            runtime.IsHydraMerging = false; // :93-94
            runtime.HydraMergeStage = null; // :95
            runtime.HydraMergeStep = null; // :96
            runtime.HydraMergeStartedAt = null; // :97
            runtime.HydraMergeNoteID = null; // :98
        }
    }

    // :190-206. ED-037: Windows scratch roots are the app worktrees directory, TEMP, APPDATA,
    // LOCALAPPDATA, and dot-folders directly beneath the user's profile.
    private static bool IsScratchPath(string path)
    {
        var full = SwiftPath.StandardizingPath(SwiftPath.ExpandingTildeInPath(path));
        var home = SwiftPath.StandardizingPath(LoginEnvironment.HomeDirectory);
        var underHome = SwiftPath.TailUnder(full, home);
        if (underHome is { Length: > 0 } && underHome[0] == '.') return true; // :195-196

        var roots = new List<string> { Storage.WorktreesDirectory }; // :193, ED-037
        foreach (var name in new[] { "TEMP", "APPDATA", "LOCALAPPDATA" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } root) roots.Add(root);
        return roots.Any(root => SwiftPath.TailUnder(full, SwiftPath.StandardizingPath(root)) is not null); // :201-205
    }

    // :212-364
    private async Task<HydraProjectMerge> MergeHydraProject(Guid leadID, ChatThread lead, Project project, string checkout,
        HydraMergeWork work, ThreadRuntime runtime, bool several)
    {
        string Title(string value) => several ? $"{project.Name}: {value}" : value; // :213-214
        void Advance(HydraMergeStage step) // :216
        {
            runtime.HydraMergeStage = several ? $"{project.Name}: {step.Words}" : step.Words;
            runtime.HydraMergeStep = step;
        }

        var git = new Git(checkout); // :218
        if (!await git.IsRepository() || !await git.HasCommits()) return HydraProjectMerge.Failed; // :219
        if (await git.RemoteURL() is null) // :220
        {
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                Label = null, Url = null, Files = 0,
                Detail = Title("Hydra did not merge: no remote.") + ": The team's work is in the checkout; there is no origin to push it to.",
            }); // :221
            Note(leadID, Title("Hydra did not merge: no remote."), "The team's work is in the checkout; there is no origin to push it to."); // :222
            return HydraProjectMerge.Failed; // :223
        }
        if (await git.HasOperationInProgress()) // :225
        {
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                Label = null, Url = null, Files = 0,
                Detail = Title("Hydra did not merge: a rebase or merge is underway.") + ": The team's work is in the checkout; finish that first and merge by hand.",
            }); // :226
            Note(leadID, Title("Hydra did not merge: a rebase or merge is underway."), "The team's work is in the checkout; finish that first and merge by hand."); // :227
            return HydraProjectMerge.Failed; // :228
        }

        var sorted = work.Paths; // :231
        var conflicted = await PathsWithConflictMarkers(sorted, checkout); // :235
        if (conflicted.Count > 0) // :236
        {
            var files = string.Join(", ", conflicted.Select(path => $"`{path}`")); // :237
            var many = conflicted.Count == 1 ? "a file" : $"{conflicted.Count} files";
            var verb = conflicted.Count == 1 ? "holds" : "hold";
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                Label = null, Url = null, Files = 0,
                Detail = Title($"Hydra did not merge: conflict markers in {many}.") + $": {files} still {verb} conflict markers from a head's landing.",
            }); // :238
            Note(leadID, Title($"Hydra did not merge: conflict markers in {many}."),
                $"{files} still {verb} conflict markers from a head's landing. Resolve them, then ask for the merge again."); // :239
            return HydraProjectMerge.Failed; // :240
        }

        try // :243
        {
            var head = await git.CommitHash(); // :244
            Advance(HydraMergeStage.Committing); // :245
            var tree = await git.CaptureTree(sorted); // :246
            if (!runtime.IsReadyForHydraMerge) return HydraProjectMerge.Failed; // :249
            var currentClaims = SiblingOwnedPaths(checkout, project.Id, leadID).Blocking; // :250
            var held = new Dictionary<string, string>(SwiftStringComparer.Instance); // :251
            foreach (var path in sorted)
                if (currentClaims.TryGetValue(path, out var owner)) held[SwiftPath.AppendingPathComponent(checkout, path)] = owner; // :252-254
            if (held.Count > 0) // :256
            {
                RecordHydraHold(held, leadID, runtime); // :257
                return HydraProjectMerge.Failed; // :258
            }
            if (tree == await git.TreeHash("HEAD")) return HydraProjectMerge.AlreadyOnMain; // :263

            var patch = await SwiftTry.OptionalAsync<string>(async () => await git.Diff(head, tree)) ?? ""; // :265
            var files = await Task.Run(() => DiffParser.Parse(patch)); // :268
            Advance(HydraMergeStage.Describing); // :269
            var message = await CommitMessage(lead, files, patch, checkout); // :270
            var subject = TextCleanup.SingleLine(message, 72); // :271

            var status = await git.Status(); // :273
            var target = await git.DefaultBranch(); // :274
            var current = status?.Branch; // :275
            var ownBranch = current is null || current == target; // :279
            var branch = ownBranch ? HydraBranchName(lead) : current!; // :279
            var commit = await git.CommitWork(tree, head, message); // :280
            if (await git.CommitHash() != head) // :286
            {
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                    Label = null, Url = null, Files = 0,
                    Detail = Title("Hydra did not merge: the checkout moved while it worked.") + $": A commit landed on `{current ?? target}` while the team's work was being prepared.",
                }); // :287
                Note(leadID, Title("Hydra did not merge: the checkout moved while it worked."),
                    $"A commit landed on `{current ?? target}` while the team's work was being prepared, and moving the branch now would orphan it. The work is still in the checkout; ask for the merge again."); // :288
                return HydraProjectMerge.Failed; // :289
            }
            await git.UpdateRef($"refs/heads/{branch}", commit, ownBranch ? null : head); // :293
            if (!ownBranch) await git.ResetIndex(sorted); // :294-298
            Advance(HydraMergeStage.Pushing); // :299
            await git.PushBranch(branch); // :300

            var body = MergeRequestBody(lead, files, work.TurnIDs.ToHashSet(), runtime); // :302
            Advance(HydraMergeStage.Opening); // :303
            SwiftURL? requestURL;
            try
            {
                requestURL = await HydraCreateMergeRequest(git, subject, body, branch, target); // :304-306
            }
            catch (Exception error)
            {
                var detail = SwiftErrors.LocalizedDescription(error); // :308-309
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                    Label = null, Url = null, Files = 0,
                    Detail = Title($"Hydra pushed {branch} but could not open a merge request.") + ": " + detail,
                });
                Note(leadID, Title($"Hydra pushed {branch} but could not open a merge request."),
                    $"{detail}\n\nOpen one for `{branch}` into `{target}` and merge it from there. The work is still in the checkout.");
                return HydraProjectMerge.Failed;
            }
            if (requestURL is null || MergeRequestLink.Create(requestURL) is not { } link) // :312-317
            {
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                    Label = null, Url = null, Files = 0, Detail = Title($"Hydra pushed {branch} but could not open a merge request."),
                }); // :314
                Note(leadID, Title($"Hydra pushed {branch} but could not open a merge request."),
                    $"Open one for `{branch}` into `{target}` and merge it from there."); // :315
                return HydraProjectMerge.Failed; // :316
            }

            Advance(HydraMergeStage.Merging); // :318
            try
            {
                await git.MergePullRequest(link); // :320
            }
            catch (Exception error)
            {
                var detail = SwiftErrors.LocalizedDescription(error); // :322-323
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                    Label = link.Label, Url = requestURL, Files = 0,
                    Detail = Title($"Hydra opened {link.Label} but could not merge it.") + ": " + detail,
                });
                Note(leadID, Title($"Hydra opened {link.Label} but could not merge it."),
                    $"{requestURL.AbsoluteString}\n\n{detail}\n\nMerge it from the link once it is ready; the branch `{branch}` has the team's work.");
                return HydraProjectMerge.Failed;
            }

            var lines = new List<string> { requestURL.AbsoluteString, "", FilesLine(files) + $" landed on `{target}` from `{branch}`." }; // :327
            if (work.Heads.Count > 0) // :328-338
            {
                lines.Add("");
                lines.Add("Heads");
                foreach (var mergeHead in work.Heads)
                {
                    lines.Add(mergeHead.Files.Count == 0
                        ? $"- {mergeHead.Name} ({mergeHead.Index}): {mergeHead.Task} — no files"
                        : $"- {mergeHead.Name} ({mergeHead.Index}): {mergeHead.Task} — {(mergeHead.Files.Count == 1 ? "1 file" : $"{mergeHead.Files.Count} files")}: {string.Join(", ", mergeHead.Files)}");
                }
            }
            if (ownBranch) // :343-346
            {
                Advance(HydraMergeStage.Syncing);
                var synced = await SyncDefaultBranch(git, head, target, sorted);
                lines.Add(synced ? "The checkout is up to date." : $"The checkout was left as it was: `{target}` moved on in other ways meanwhile, or the team's files changed again; `git pull` when it suits you.");
            }
            else if (lead.WorktreePath is not null) // :347-353
            {
                Advance(HydraMergeStage.Syncing);
                var main = new Git(project.Path);
                if ((await main.Status())?.Branch == target && (await main.DirtyPaths()).Count == 0 && !await main.HasOperationInProgress())
                {
                    try
                    {
                        await main.PullFastForward();
                        lines.Add("The project checkout is up to date.");
                    }
                    catch { }
                }
            }
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged, Project = project.Name,
                Label = link.Label, Url = requestURL, Files = files.Count, Detail = null,
            }); // :355
            Note(leadID, Title($"Hydra merged {link.Label}"), string.Join("\n", lines)); // :356
            ExistingRuntime(leadID)?.NoteDiffChanged(); // :357
            return HydraProjectMerge.Merged; // :358
        }
        catch (Exception error)
        {
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                Label = null, Url = null, Files = 0,
                Detail = "Hydra could not merge the team's work: " + SwiftErrors.LocalizedDescription(error),
            }); // :360
            Note(leadID, Title("Hydra could not merge the team's work."),
                SwiftErrors.LocalizedDescription(error) + "\n\nThe work is still in the checkout."); // :361
            return HydraProjectMerge.Failed;
        }
    }

    // :374-421
    private async Task<SwiftURL?> HydraCreateMergeRequest(Git git, string title, string body, string source, string target)
    {
        if (await git.Forge() != MergeRequestLink.Forge.Gitlab || LoginEnvironment.Which("glab") is null)
            return await git.CreatePullRequest(title, body, source, target); // :375-376

        var remoteWeb = await git.RemoteWebURL(); // :378
        var host = remoteWeb?.Host ?? "your GitLab host";
        var arguments = new List<string> { "mr", "create", "--title", title, "--description", body,
            "--yes", "--squash-before-merge" }; // :382
        if (remoteWeb is { Host: not null } web)
        {
            if (URLComponents.Create(web, resolvingAgainstBaseURL: false) is { User: not null } components)
            {
                components = components with { User = null, Password = null }; // :384-388
                if (components.Url is { } stripped) web = stripped;
            }
            arguments.AddRange(["--repo", web.AbsoluteString]); // :389
        }
        arguments.AddRange(["--source-branch", source, "--target-branch", target]); // :391
        if (await HydraGitLabAuthFailure(git.Directory) is { } authFailure)
            throw new HydraMergeRequestError.GitLabAuth(host, source, authFailure); // :395-397

        var failure = await Shell.RunTool("glab", arguments, @in: git.Directory, timeout: 300); // :399
        if (failure.Succeeded) return HydraMergeRequestURL(failure); // :400
        if (IsGitLabAuthFailure(failure)) // :401
        {
            if (await HydraGitLabAuthFailure(git.Directory) is null) // :404
            {
                var retry = await Shell.RunTool("glab", arguments, @in: git.Directory, timeout: 300); // :405
                if (retry.Succeeded) return HydraMergeRequestURL(retry); // :406
                failure = retry; // :407
            }
            if (IsGitLabAuthFailure(failure))
                throw new HydraMergeRequestError.GitLabAuth(host, source, failure.FailureMessage); // :409-410
        }
        if (MentionsRecoverFile(failure)) // :415
        {
            var retry = await Shell.RunTool("glab", arguments.Concat(["--recover"]).ToArray(), @in: git.Directory, timeout: 300); // :416
            if (retry.Succeeded) return HydraMergeRequestURL(retry); // :417
            failure = retry; // :418
        }
        throw new HydraMergeRequestError.CreationFailed(failure.FailureMessage); // :420
    }

    // :425-432
    private static async Task<string?> HydraGitLabAuthFailure(string directory)
    {
        ShellResult status;
        try { status = await Shell.RunTool("glab", ["auth", "status"], @in: directory, timeout: 30); }
        catch { return null; } // :428
        if (status.Succeeded) return null; // :429
        var message = status.FailureMessage; // :430
        return message.Length == 0 ? "glab is not signed in to this GitLab host." : message; // :431
    }

    // :435-444
    private static bool IsGitLabAuthFailure(ShellResult result)
    {
        var text = SwiftString.Lowercased(result.Output + "\n" + result.ErrorOutput); // :436
        return text.Contains("401", StringComparison.Ordinal) || text.Contains("unauthorized", StringComparison.Ordinal) ||
            (text.Contains("authentication", StringComparison.Ordinal) &&
                (text.Contains("failed", StringComparison.Ordinal) || text.Contains("expired", StringComparison.Ordinal) ||
                 text.Contains("invalid", StringComparison.Ordinal) || text.Contains("required", StringComparison.Ordinal))); // :437-438
    }

    private static bool MentionsRecoverFile(ShellResult result) =>
        SwiftString.Lowercased(result.Output + "\n" + result.ErrorOutput).Contains("recover", StringComparison.Ordinal); // :442-444

    // :447-451
    private static SwiftURL? HydraMergeRequestURL(ShellResult result)
    {
        var text = result.Output + "\n" + result.ErrorOutput;
        var match = Regex.Match(text, @"https://\S+", RegexOptions.CultureInvariant);
        return match.Success ? SwiftURL.Create(match.Value) : null;
    }

    // :471-614
    private async Task<HydraMergeWork> HydraWorkFor(Guid leadID, ThreadRuntime runtime, string checkout, Git git,
        Guid projectID, string leadCheckout, Guid leadProjectID, bool sweepsCheckout)
    {
        var turns = runtime.HydraUnmergedTurns; // :472
        var reported = turns.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty).ToList(); // :473
        var headPaths = new List<(Guid ID, string Name, long Index, string Task, List<string> Paths)>(); // :480
        foreach (var head in HydraTeam(leadID)) // :481
        {
            if (head.Hydra is not { MergedAt: null } info || info.Landing?.PatchPath is not null || info.Landing?.Error is not null)
                continue; // :482-483
            var headRoot = head.ProjectID == leadProjectID ? leadCheckout : Project(head.ProjectID)?.Path ?? leadCheckout; // :491
            string Anchored(string path) // :492
            {
                var trimmed = path.Trimmed();
                if (trimmed.Length == 0) return ""; // :494
                if (head.WorktreePath is { } copy && TouchedPaths.Relative(trimmed, copy) is { } inCopy)
                    return SwiftPath.AppendingPathComponent(headRoot, inCopy); // :495-496
                if (SwiftPath.IsAbsolute(trimmed) || trimmed.StartsWith('~') || trimmed.Contains("://", StringComparison.Ordinal))
                    return trimmed; // ED-138, :498
                return SwiftPath.AppendingPathComponent(headRoot, trimmed); // :499
            }

            var own = new List<string>(); // :501
            var seen = new HashSet<string>(SwiftStringComparer.Instance); // :502
            void Collect(string path) // :503
            {
                if (path.Length == 0 || TouchedPaths.Relative(Anchored(path), checkout) is not { } relative || !seen.Add(relative)) return; // :504
                own.Add(relative);
            }
            foreach (var file in info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty) Collect(file.Path); // :507
            reported.AddRange((info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty).Select(file => Anchored(file.Path))); // :508

            ValueList<TimelineItem> items;
            if (ExistingRuntime(head.Id) is { } live) items = new ValueList<TimelineItem>(live.Entries.Select(entry => entry.Item)); // :513-516
            else // :517-519
            {
                var headID = head.Id;
                items = await Task.Run(() => Storage.DecodeDocument(headID)?.Items ?? ValueList<TimelineItem>.Empty);
            }
            foreach (var item in items) // :520
            {
                if (item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue; // :521
                reported.AddRange(tool.Value.Edits.Select(edit => Anchored(edit.Path))); // :522
                foreach (var edit in tool.Value.Edits) Collect(edit.Path); // :523
            }
            headPaths.Add((head.Id, info.Persona.Name, info.Index, TextCleanup.SingleLine(info.Task, 120), own)); // :525
        }

        var unmerged = turns.Select(turn => turn.Id).ToHashSet(); // :531
        foreach (var entry in runtime.Entries.Where(entry => entry.TurnID is { } turnID && unmerged.Contains(turnID))) // :532
        {
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue; // :533
            foreach (var edit in tool.Value.Edits) // :534
            {
                var trimmed = edit.Path.Trimmed();
                if (trimmed.Length == 0) continue; // :535-536
                reported.Add(SwiftPath.IsAbsolute(trimmed) || trimmed.StartsWith('~') || trimmed.Contains("://", StringComparison.Ordinal)
                    ? trimmed : SwiftPath.AppendingPathComponent(leadCheckout, trimmed)); // ED-138, :537-541
            }
        }

        var paths = new HashSet<string>(SwiftStringComparer.Instance); // :545
        var outside = new HashSet<string>(SwiftStringComparer.Instance); // :546
        foreach (var path in reported.Where(path => path.Length > 0 && !TouchedPaths.IsBuildOutput(path))) // :547
        {
            if (TouchedPaths.Relative(path, checkout) is { } relative) paths.Add(relative); // :548-550
            else outside.Add(path); // :551-552
        }

        var siblingFiles = SiblingOwnedPaths(checkout, projectID, leadID); // :565
        var siblingClaims = siblingFiles.Reserved.Keys.ToHashSet(SwiftStringComparer.Instance); // :566
        var blockingClaims = siblingFiles.Blocking; // :567
        if (sweepsCheckout && turns.FirstOrDefault()?.BaseCheckpoint is { } baseCheckpoint &&
            await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths("HEAD", baseCheckpoint)) is { } alreadyDirty &&
            await SwiftTry.OptionalAsync<string>(async () => await git.CaptureTree()) is { } now &&
            await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths(baseCheckpoint, now)) is { } changedSince) // :568-572
        {
            var theirs = alreadyDirty.ToHashSet(SwiftStringComparer.Instance); // :573
            var fresh = changedSince.Where(path => !theirs.Contains(path)); // :574
            paths.UnionWith(fresh.Where(path => !siblingClaims.Contains(path))); // :575
        }

        var claimed = paths.Where(blockingClaims.ContainsKey).ToHashSet(SwiftStringComparer.Instance); // :586
        paths.ExceptWith(blockingClaims.Keys); // :587
        paths.RemoveWhere(TouchedPaths.IsBuildOutput); // :588
        paths.ExceptWith(await git.IgnoredPaths(paths.Order(StringComparer.Ordinal).ToArray())); // :589
        var tracked = await git.TrackedPaths(paths.Order(StringComparer.Ordinal).ToArray()); // :590
        var missing = paths.Where(path => !File.Exists(SwiftPath.AppendingPathComponent(checkout, path)) && !tracked.Contains(path))
            .ToHashSet(SwiftStringComparer.Instance); // :591-596
        paths.ExceptWith(missing); // :596

        var heads = new ValueList<HydraMergeHead>(headPaths.Select(head => new HydraMergeHead(head.Name, head.Index, head.Task,
            new ValueList<string>(head.Paths.Where(paths.Contains))))); // :597-599
        var turnIDs = new ValueList<Guid>(turns.Where(turn => (turn.TouchedPaths ?? ValueList<string>.Empty).All(path =>
        {
            var relative = TouchedPaths.Relative(path, checkout);
            return relative is null || !claimed.Contains(relative);
        })).Select(turn => turn.Id)); // :603-608
        var headIDs = new ValueList<Guid>(headPaths.Where(head => !head.Paths.Any(claimed.Contains)).Select(head => head.ID)); // :609
        var held = claimed.ToDictionary(path => path, path => blockingClaims[path], SwiftStringComparer.Instance); // :612
        return new HydraMergeWork(new ValueList<string>(paths.Order(SwiftStringComparer.Instance)), turnIDs, headIDs,
            new ValueList<string>(outside.Order(SwiftStringComparer.Instance)), heads, held); // :613
    }

    // :634-672
    private (Dictionary<string, string> Reserved, Dictionary<string, string> Blocking) SiblingOwnedPaths(
        string checkout, Guid projectID, Guid leadID)
    {
        var team = HydraTeam(leadID).Select(head => head.Id).ToHashSet(); // :635
        var reserved = new Dictionary<string, string>(SwiftStringComparer.Instance); // :636
        var blocking = new Dictionary<string, string>(SwiftStringComparer.Instance); // :637
        void Reserve(string path, string owner, bool blocks) // :638
        {
            if (TouchedPaths.Relative(path, checkout) is not { } relative) return; // :639
            reserved[relative] = owner; // :640
            if (blocks) blocking[relative] = owner; // :641
        }
        foreach (var other in Threads.Where(thread => thread.Id != leadID && thread.ProjectID == projectID && !team.Contains(thread.Id))) // :643
        {
            if (other.IsHydraHead) // :644
            {
                if (other.Hydra is not { MergedAt: null } info) continue; // :645
                var parent = other.ParentThreadID is { } parentID ? Thread(parentID) : null; // :648
                if (info.HydraHeadInfoStatus != HydraHeadInfo.Status.Running && (parent is null || !CanStillMerge(parent))) continue; // :649
                var rankedOut = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed && parent is not null &&
                    HydraRanksAfter(parent, leadID) && HydraIsFinishedIdleEligible(parent); // :654-655
                foreach (var file in info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty)
                    Reserve(file.Path, parent?.Title ?? other.Title, !rankedOut); // :656-657
            }
            else if (ExistingRuntime(other.Id) is { } live) // :659
            {
                var rankedOut = HydraRanksAfter(other, leadID) && HydraIsFinishedIdleEligible(other); // :663
                foreach (var turn in live.HydraUnmergedTurns.Where(turn => CanStillMerge(other) || turn.Status != TurnStatus.Completed)) // :664
                    foreach (var path in turn.TouchedPaths ?? ValueList<string>.Empty)
                        Reserve(path, other.Title, !rankedOut); // :665-667
            }
        }
        return (reserved, blocking); // :671
    }

    // :679-715
    private bool HydraIsFinishedIdleEligible(ChatThread lead)
    {
        if (!HydraIsOn(lead) || ExistingRuntime(lead.Id) is not { } live) return false; // :680
        if (!live.IsReadyForHydraMerge || live.IsHydraMerging || HydraHasPendingLanding(lead.Id)) return false; // :681-682
        return CanStillMerge(lead); // :683
    }

    private bool HydraHasPendingLanding(Guid leadID) => HydraTeam(leadID).Any(head =>
        head.Hydra is { MergedAt: null, Landing: { } landing } && landing.Files.Count > 0 && !landing.Landed); // :689-694

    private bool HydraRanksAfter(ChatThread lead, Guid other) => // :699-701
        string.Compare(lead.Id.ToString("D"), other.ToString("D"), StringComparison.Ordinal) > 0;

    private bool CanStillMerge(ChatThread lead) // :705-715
    {
        var live = ExistingRuntime(lead.Id);
        if (live is not null && (live.Phase != RuntimePhase.Idle || live.IsHydraMerging)) return true; // :707
        if (lead.IsArchived || !HydraIsOn(lead) || lead.UpdatedAt <= SwiftDate.Now - HydraMergeWindow) return false; // :708-709
        var landedHeadWork = HydraTeam(lead.Id).Any(head => head.Hydra is { MergedAt: null, Landing.Landed: true }); // :712
        if (live is null) return lead.LastStatus == TurnStatus.Completed; // :713
        return live.Turns.LastOrDefault()?.Status == TurnStatus.Completed &&
            (live.HydraUnmergedTurns.Count > 0 || landedHeadWork); // :714
    }

    // :722-748
    private static async Task<bool> SyncDefaultBranch(Git git, string head, string target, IReadOnlyList<string> ownPaths)
    {
        var remote = $"origin/{target}"; // :723
        if (await SwiftTry.OptionalAsync<string>(async () => { await git.Fetch(); return "fetched"; }) is null ||
            !await git.IsAncestor(head, remote)) return false; // :724
        var changed = await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths(head, remote)); // :725
        if (changed is null) return false;
        var own = ownPaths.ToHashSet(SwiftStringComparer.Instance); // :726
        var others = changed.Where(path => !own.Contains(path)).ToArray(); // :727
        var dirty = (await git.DirtyPaths()).ToHashSet(SwiftStringComparer.Instance); // :728
        if (others.Any(dirty.Contains)) return false; // :729
        var present = await git.PresentPaths(others, remote); // :732
        var kept = others.Where(present.Contains).ToArray(); // :733
        var gone = others.Where(path => !present.Contains(path)).ToArray(); // :734
        if (await SwiftTry.OptionalAsync<string>(async () => await git.CommitHash()) != head) return false; // :738
        try
        {
            await git.ResetSoft(remote); // :740
            await git.ResetIndex(ownPaths); // :741
            await git.CheckoutPaths(remote, kept); // :742
            await git.RemovePaths(gone); // :743
            return true; // :744
        }
        catch { return false; } // :745-747
    }

    // :752-771
    private async Task<string> CommitMessage(ChatThread lead, IReadOnlyList<DiffFile> files, string patch, string directory)
    {
        if (TextEngine(lead.Provider) is { } engine) // :753
        {
            var summary = string.Join("\n", files.Select(file =>
                $"{(file.FileChange == DiffFile.Change.Added ? "A" : file.FileChange == DiffFile.Change.Deleted ? "D" : "M")} {file.Path}")); // :754
            if (await TextGeneration.CommitMessage(summary, patch, Settings.CommitInstructions, engine, directory) is { Length: > 0 } text)
                return text; // :755-763
        }
        var lines = new List<string> { lead.Title == ChatThread.Untitled ? "Hydra: the team's work" : lead.Title, "" }; // :765
        foreach (var head in HydraTeam(lead.Id)) // :766
            if (head.Hydra is { Task.Length: > 0 } info)
                lines.Add($"- {info.Persona.Name}: {TextCleanup.SingleLine(info.Task, 100)}"); // :767-769
        return string.Join("\n", lines).Trimmed(); // :770
    }

    // :774-799
    private string MergeRequestBody(ChatThread lead, IReadOnlyList<DiffFile> files, IReadOnlySet<Guid> through, ThreadRuntime runtime)
    {
        var lines = new List<string> { "Opened by Marrow once its Hydra team finished.", "" }; // :775; DEVIATION OD-0008: the owner names Marrow as the opener.
        var heads = HydraTeam(lead.Id).Select(head => head.Hydra).Where(info => info is not null && info.Task.Length > 0).Cast<HydraHeadInfo>().ToArray(); // :776
        if (heads.Length > 0) // :777
        {
            lines.Add("## Heads"); // :778
            foreach (var info in heads) // :779
            {
                var line = $"- **{info.Persona.Name}**: {TextCleanup.SingleLine(info.Task, 120)}";
                if (info.Landing is { Files.Count: > 0 } landing)
                    line += $" ({(landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count} files")})"; // :780-783
                lines.Add(line);
            }
            lines.Add(""); // :786
        }
        var reply = runtime.Entries.LastOrDefault(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Assistant &&
            entry.TurnID is { } turnID && through.Contains(turnID))?.Item.TimelineItemContent is TimelineItem.Content.Assistant assistant
            ? assistant.Value.Text.Trimmed() : null; // :788-791
        if (!string.IsNullOrEmpty(reply)) // :791
        {
            lines.Add("## The lead's summary"); // :792
            lines.Add(SwiftString.Count(reply!) > 2_000 ? SwiftString.Prefix(reply, 2_000) + "…" : reply); // :793
            lines.Add(""); // :794
        }
        lines.Add("## Files"); // :796
        lines.AddRange(files.Select(file => $"- `{file.Path}` (+{file.Additions} −{file.Deletions})")); // :797
        return string.Join("\n", lines); // :798
    }

    // :801-809
    private static string HydraBranchName(ChatThread lead)
    {
        var title = lead.Title == ChatThread.Untitled ? "team" : lead.Title;
        var slugBuilder = new StringBuilder();
        var characters = StringInfo.GetTextElementEnumerator(title.ToLowerInvariant());
        while (characters.MoveNext())
        {
            var character = (string)characters.Current!;
            var first = character.EnumerateRunes().First();
            slugBuilder.Append(Rune.IsLetterOrDigit(first) ? character : "-"); // :803
        }
        var slug = slugBuilder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal); // :804
        slug = slug.Trim('-'); // :805
        if (SwiftString.Count(slug) > 40) slug = SwiftString.Prefix(slug, 40).TrimEnd('-'); // :806
        var suffix = lead.Id.ToString("D").ToLowerInvariant()[..6]; // :880: stable per chat, from lead.id, not a fresh id
        return $"hydra/{(slug.Length == 0 ? "team" : slug)}-{suffix}"; // :881
    }

    // :814-826
    private static Task<ValueList<string>> PathsWithConflictMarkers(IReadOnlyList<string> paths, string checkout) => Task.Run(() =>
    {
        var found = new List<string>();
        var opening = Encoding.UTF8.GetBytes("\n<<<<<<< ");
        var closing = Encoding.UTF8.GetBytes("\n>>>>>>> ");
        foreach (var path in paths)
        {
            var fullPath = SwiftPath.AppendingPathComponent(checkout, path);
            try
            {
                var length = new FileInfo(fullPath).Length;
                if (length > 4_000_000) continue; // :821
                var bytes = File.ReadAllBytes(fullPath);
                var body = new byte[bytes.Length + 1]; // :823
                body[0] = 0x0A;
                bytes.CopyTo(body, 1);
                if (Contains(body, opening) && Contains(body, closing)) found.Add(path); // :824
            }
            catch { }
        }
        return new ValueList<string>(found); // :819-825
    });

    private static bool Contains(ReadOnlySpan<byte> body, ReadOnlySpan<byte> value)
    {
        return body.IndexOf(value) >= 0;
    }

    // :829-833
    private void RecordHydraSettlement(Guid leadID, ThreadRuntime runtime)
    {
        if (HydraUnmergedFileCount(leadID) != 0) return; // :830
        runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Settled,
            Project = null, Label = null, Url = null, Files = 0, Detail = null,
        }); // :831
        Note(leadID, "Hydra has no files left to merge.", "The checkout has no remaining changes from this job to send. No new merge request was needed."); // :832
    }

    // :835-841
    private void RecordHydraHold(IReadOnlyDictionary<string, string> held, Guid leadID, ThreadRuntime runtime)
    {
        var detail = HeldDetail(held); // :836
        var last = runtime.HydraMerges.LastOrDefault(); // :837
        if (last?.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Held && last.Detail == detail) return; // :838
        runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Held,
            Project = null, Label = null, Url = null, Files = held.Count, Detail = detail,
        }); // :839
        Note(leadID, "Hydra is waiting for another chat's files.",
            detail + " The work stays in the checkout. Hydra retries after the other chat finishes or its queued merge completes."); // :840
    }

    // :846-851
    private static string HeldDetail(IReadOnlyDictionary<string, string> held)
    {
        var files = held.Keys.Order(SwiftStringComparer.Instance).ToArray(); // :847
        var shown = string.Join(", ", files.Take(6).Select(path => $"`{path}`")) + (files.Length > 6 ? $" and {files.Length - 6} more" : ""); // :848
        var names = string.Join(", ", held.Values.Distinct(SwiftStringComparer.Instance).Order(SwiftStringComparer.Instance)); // :849
        return $"{(files.Length == 1 ? "1 file the team changed is" : $"{files.Length} files the team changed are")} also claimed by {names}: {shown}."; // :850
    }

    // :853-857
    private static string FilesLine(IReadOnlyList<DiffFile> files)
    {
        var additions = files.Sum(file => file.Additions); // :854
        var deletions = files.Sum(file => file.Deletions); // :855
        return $"{(files.Count == 1 ? "1 file" : $"{files.Count} files")} (+{additions} −{deletions})"; // :856
    }

    // :860-875
    private void Note(Guid leadID, string title, string body)
    {
        var runtime = ExistingRuntime(leadID); // :864
        var id = runtime?.HydraMergeNoteID; // :865
        if (runtime is not null) runtime.HydraMergeNoteID = null; // :866
        runtime?.AppendHydraNote(title + "\n" + body, id); // :867
        if (IsApplicationActive() && SelectedThreadID == leadID) return; // :868-869
        UpdateThread(leadID, thread => thread with { HasUnread = true }); // :870
        UpdateDockBadge(); // :871
        if (Settings.NotifyWhenFinished && Thread(leadID) is { } lead)
            Notify(leadID, lead.Title, title); // :872-873
    }
}

// :879-893
internal abstract class HydraMergeRequestError(string message) : SwiftError
{
    public override string ErrorDescription => message;

    public sealed class GitLabAuth(string host, string branch, string detail) : HydraMergeRequestError(
        $"GitLab refused the login (401 Unauthorized) for {host}: the token glab signs in with is missing or expired.\n\n{detail}\n\nSign back in with `glab auth login --hostname {host}` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `{branch}` is already pushed, so the team's work is safe.");

    public sealed class CreationFailed(string detail) : HydraMergeRequestError(detail);
}
