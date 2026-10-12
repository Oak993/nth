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

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :1-949.

// :12-18
internal sealed record HydraMergeHead(string Name, long Index, string Task, ValueList<string> Files);

// :20-28
internal enum HydraProjectMerge
{
    Merged,
    AlreadyOnMain,
    Failed,
    /// Not now, and nothing to tell: the checkout is mid-rebase or mid-merge, or the lead took up work
    /// again while the files were gathered. The sweep picks the job up again. (:25-27)
    Deferred,
}

public sealed partial class AppModel
{
    // :32-33
    // Swift: HydraWork; renamed because AppModel+Hydra.swift already ports a same-named method. No `Held` member
    // at B3: a merge never waits on another chat's files any more (PR-HYDRAMERGE-122).
    private sealed record HydraMergeWork(
        ValueList<string> Paths,
        ValueList<Guid> TurnIDs,
        ValueList<Guid> HeadIDs,
        ValueList<string> Outside,
        ValueList<HydraMergeHead> Heads);

    // :36
    public bool IsAnyHydraMergeRunning => LiveRuntimes.Any(runtime => runtime.IsHydraMerging);

    // :42
    private static readonly TimeSpan HydraMergeWindow = TimeSpan.FromDays(3);

    // :49-75
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
        foreach (var lead in leads) await Runtime(lead.Id).EnsureLoaded(); // :54-58

        foreach (var lead in leads) // :59
        {
            if (ExistingRuntime(lead.Id) is not { } runtime || !runtime.IsReadyForHydraMerge || runtime.IsHydraMerging)
                continue; // :60-61
            var finished = runtime.HydraUnmergedTurns.Where(turn => turn.Status == TurnStatus.Completed); // :66
            var hasLandedHeadWork = HydraTeam(lead.Id).Any(head => head.Hydra is { MergedAt: null, Landing.Landed: true }); // :70-71
            var hasOwnWork = finished.Any(turn => turn.TouchedPaths is { Count: > 0 }) || hasLandedHeadWork; // :70-72
            if (hasOwnWork) await AutoMergeHydraWork(lead.Id); // :73
        }
    }

    // :79-187
    public async Task AutoMergeHydraWork(Guid leadID)
    {
        if (InstallerHoldsNewTurns || !Settings.HydraAutoMerge || Thread(leadID) is not { } waiting || !HydraIsOn(waiting) ||
            ExistingRuntime(leadID) is not { } queued || queued.IsHydraMerging || queued.IsHydraMergeQueued ||
            !queued.IsReadyForHydraMerge)
            return; // :80-82

        // One merge goes out at a time: several jobs finishing in the same moment would otherwise snapshot
        // shared files side by side. A job that finishes while another merges waits its turn right here and
        // goes the moment that one is done. (:83-95, PR-HYDRAMERGE-010/-122)
        if (IsAnyHydraMergeRunning) // :88
        {
            queued.IsHydraMergeQueued = true; // :89
            try
            {
                while (IsAnyHydraMergeRunning) // :91
                {
                    await SwiftTask.TrySleep(TimeSpan.FromMilliseconds(400)); // :92
                    if (SwiftTask.IsCancelled) return; // :93
                }
            }
            finally { queued.IsHydraMergeQueued = false; } // :90 defer
        }

        // Everything is read again after the wait, and the flag below is set with no suspension after this
        // guard, so of several waiters exactly one goes.
        if (InstallerHoldsNewTurns || !Settings.HydraAutoMerge || Thread(leadID) is not { } lead || !HydraIsOn(lead) ||
            Project(lead.ProjectID) is not { } project || ExistingRuntime(leadID) is not { } runtime ||
            runtime.IsHydraMerging || IsAnyHydraMergeRunning || !runtime.IsReadyForHydraMerge)
            return; // :98-100

        runtime.IsHydraMerging = true; // :101
        runtime.HydraMergeStartedAt = SwiftDate.Now; // :102
        runtime.HydraMergeNoteID = Guid.NewGuid().ToString("D").ToUpperInvariant(); // :103
        runtime.HydraMergeStage = HydraMergeStage.Gathering.Words; // :104
        runtime.HydraMergeStep = HydraMergeStage.Gathering; // :105
        try
        {
            // A head's work still going into the checkout finishes first: the snapshot the commit is built
            // from must hold the patch whole or not at all.
            if (runtime.HydraLanding is { } landing) await landing.Completion; // :116
            if (!runtime.IsReadyForHydraMerge) return; // :117

            var ownCheckout = lead.WorktreePath ?? project.Path; // :125
            var own = await HydraWorkFor(leadID, runtime, ownCheckout, new Git(ownCheckout), project.Id,
                ownCheckout, project.Id, sweepsCheckout: true); // :126
            var groups = new List<(Project Project, string Checkout, HydraMergeWork Work)> { (project, ownCheckout, own) }; // :127
            var owners = new Dictionary<string, Project>(SwiftStringComparer.Instance); // :130
            foreach (var path in own.Outside) // :131
            {
                var holder = Projects
                    .Where(candidate => candidate.Id != project.Id && TouchedPaths.Relative(path, candidate.Path) is not null)
                    .OrderByDescending(candidate => candidate.Path.Length)
                    .FirstOrDefault();
                if (holder is not null) owners[path] = holder; // :132-134
            }
            foreach (var other in Projects.Where(candidate => candidate.Id != project.Id && owners.Values.Any(owner => owner.Id == candidate.Id))) // :136
            {
                var work = await HydraWorkFor(leadID, runtime, other.Path, new Git(other.Path), other.Id,
                    ownCheckout, project.Id, sweepsCheckout: false); // :137
                groups.Add((other, other.Path, work)); // :138
            }

            var stray = own.Outside.Where(path => !IsScratchPath(path) &&
                !Projects.Any(candidate => TouchedPaths.Relative(path, candidate.Path) is not null)).ToArray(); // :143
            string StrayBody(IReadOnlyList<string> paths) // :144-146
            {
                var shown = string.Join(", ", paths.Take(6).Select(path => $"`{path}`")) +
                    (paths.Count > 6 ? $" and {paths.Count - 6} more" : "");
                return $"{(paths.Count == 1 ? "1 file was" : $"{paths.Count} files were")} changed outside every project: {shown}. Add the folder that holds them to the sidebar and ask for the merge again.";
            }

            groups.RemoveAll(group => group.Work.Paths.Count == 0); // :148
            if (groups.Count == 0) // :149
            {
                if (stray.Length == 0) // :150
                {
                    // Nothing to send and nothing outside a project: whatever the turns recorded is already
                    // on the branch, ignored, build output, or gone. Mark it spent, or the merge note counts
                    // those files forever.
                    runtime.MarkHydraMerged(own.TurnIDs); // :154
                    var spentAt = SwiftDate.Now; // :155
                    foreach (var headID in own.HeadIDs) UpdateHydraHead(headID, info => info with { MergedAt = spentAt }); // :156
                    if (runtime.HydraMerges.LastOrDefault()?.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Held)
                        RecordHydraSettlement(leadID, runtime); // :157-159
                    return; // :160
                }
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Stray,
                    Project = null, Label = null, Url = null, Files = stray.Length, Detail = StrayBody(stray),
                }); // :162
                Note(leadID, "Hydra did not merge: the team's files are outside every project.", StrayBody(stray)); // :163
                return; // :164
            }

            var several = groups.Count > 1; // :166
            var outcomes = new List<HydraProjectMerge>(); // :167
            foreach (var group in groups) // :168
                outcomes.Add(await MergeHydraProject(leadID, lead, group.Project, group.Checkout, group.Work, runtime, several)); // :169
            if (outcomes.Contains(HydraProjectMerge.Failed) || outcomes.Contains(HydraProjectMerge.Deferred)) return; // :171

            // Another job that waited on this one, or was put off by it, goes now rather than at the next
            // sweep. Scheduled, so it starts once this merge has let go. (:172-174, PR-HYDRAMERGE-123)
            var weakSelf = new WeakReference<AppModel>(this);
            _ = MainActor.Spawn(async () => { if (weakSelf.TryGetTarget(out var self)) await self.ResumeHydraMerges(); });

            // The work is on the default branch now, so these turns are spent: the next job merges what
            // comes after them and never this again.
            runtime.MarkHydraMerged(groups.SelectMany(group => group.Work.TurnIDs).ToArray()); // :177
            var mergedAt = SwiftDate.Now; // :178
            foreach (var headID in groups.SelectMany(group => group.Work.HeadIDs))
                UpdateHydraHead(headID, info => info with { MergedAt = mergedAt }); // :179
            if (stray.Length > 0) // :181
            {
                runtime.RecordHydraMerge(new HydraMergeRecord
                {
                    At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Stray,
                    Project = null, Label = null, Url = null, Files = stray.Length, Detail = StrayBody(stray),
                }); // :182
                Note(leadID, $"Hydra left {(stray.Length == 1 ? "a file" : $"{stray.Length} files")} outside every project.", StrayBody(stray)); // :183
            }
            else if (outcomes.All(outcome => outcome == HydraProjectMerge.AlreadyOnMain))
            {
                RecordHydraSettlement(leadID, runtime); // :184-186
            }
        }
        finally
        {
            runtime.IsHydraMerging = false; // :106-107
            runtime.HydraMergeStage = null; // :108
            runtime.HydraMergeStep = null; // :109
            runtime.HydraMergeStartedAt = null; // :110
            runtime.HydraMergeNoteID = null; // :111
        }
    }

    // :194-210. ED-037: Windows scratch roots are the app worktrees directory, TEMP, APPDATA,
    // LOCALAPPDATA, and dot-folders directly beneath the user's profile.
    private static bool IsScratchPath(string path)
    {
        var full = SwiftPath.StandardizingPath(SwiftPath.ExpandingTildeInPath(path));
        var home = SwiftPath.StandardizingPath(LoginEnvironment.HomeDirectory);
        var underHome = SwiftPath.TailUnder(full, home);
        if (underHome is { Length: > 0 } && underHome[0] == '.') return true; // :199-200

        var roots = new List<string> { Storage.WorktreesDirectory }; // :197, ED-037
        foreach (var name in new[] { "TEMP", "APPDATA", "LOCALAPPDATA" })
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } root) roots.Add(root);
        return roots.Any(root => SwiftPath.TailUnder(full, SwiftPath.StandardizingPath(root)) is not null); // :205-209
    }

    // :212-409
    private async Task<HydraProjectMerge> MergeHydraProject(Guid leadID, ChatThread lead, Project project, string checkout,
        HydraMergeWork work, ThreadRuntime runtime, bool several)
    {
        string Stage(string value) => several ? $"{project.Name}: {value}" : value; // :222
        string Title(string value) => several ? $"{project.Name}: {value}" : value; // :223
        void Advance(HydraMergeStage step) // :225
        {
            runtime.HydraMergeStage = Stage(step.Words);
            runtime.HydraMergeStep = step;
        }

        // :226-237. Builds the record's detail, schedules a quiet retry, de-duplicates against the last
        // record of the same kind, and only speaks once a retryable failure has kept failing.
        HydraProjectMerge Fail(string headline, string body, string? label = null, SwiftURL? url = null, bool retryable = false)
        {
            var detail = Title(headline) + ": " + body; // :227
            if (retryable) ScheduleHydraMergeRetry(leadID); // :228
            // The same failure again (the sweep retries every job it can) is not news.
            if (runtime.HydraMerges.LastOrDefault() is { } last && last.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Failed &&
                last.Detail is { } lastDetail && SwiftString.Equal(lastDetail, detail)) return HydraProjectMerge.Failed; // :230
            // A failure that clears by itself is said once it has kept failing, never on the first try: the
            // retry a minute from now normally just lands.
            var saysIt = !retryable || runtime.HydraMerges.TakeLast(2).Count(record =>
                record.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Failed && record.Retryable == true) >= 2; // :233
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Failed, Project = project.Name,
                Label = label, Url = url, Files = 0, Detail = detail, Retryable = retryable ? true : null,
            }); // :234
            if (saysIt) Note(leadID, Title(headline), body); // :235
            return HydraProjectMerge.Failed; // :236
        }

        var git = new Git(checkout); // :239
        if (!await git.IsRepository() || !await git.HasCommits()) return HydraProjectMerge.Failed; // :240
        if (await git.RemoteURL() is null) // :241
        {
            return Fail("Hydra did not merge: no remote.", "The team's work is in the checkout; there is no origin to push it to."); // :242
        }
        // Someone is rebasing or merging in this checkout: the work waits for them, quietly, and looks again
        // in a minute.
        if (await git.HasOperationInProgress()) // :246
        {
            ScheduleHydraMergeRetry(leadID); // :247
            return HydraProjectMerge.Deferred; // :248
        }

        var sorted = work.Paths; // :251
        // A head's landing can leave conflict markers behind. Those markers must never reach the remote: the
        // job stays in the checkout until they are gone.
        var conflicted = await PathsWithConflictMarkers(sorted, checkout); // :255
        if (conflicted.Count > 0) // :256
        {
            var files = string.Join(", ", conflicted.Select(path => $"`{path}`")); // :257
            var headline = $"Hydra did not merge: conflict markers in {(conflicted.Count == 1 ? "a file" : $"{conflicted.Count} files")}."; // :258
            return Fail(headline, $"{files} still {(conflicted.Count == 1 ? "holds" : "hold")} conflict markers from a head's landing. Resolve them and the merge goes out by itself."); // :259
        }

        try // :262
        {
            var status = await git.Status(); // :263
            var target = await git.DefaultBranch(); // :264
            var current = status?.Branch; // :265
            // On the default branch the commit goes to a branch of its own, made and pushed without ever
            // being checked out; on any other branch it is that branch's.
            var ownBranch = current is null || SwiftString.Equal(current, target); // :268
            var branch = ownBranch ? HydraBranchName(lead) : current!; // :269

            // A commit landing while the work is prepared (the user's, in a terminal) moves HEAD under it.
            // That is no reason to stop: the work is prepared again on top.
            (string Head, string Commit, string Message, ValueList<DiffFile> Files, IReadOnlyDictionary<string, byte[]> Merged)? prepared = null; // :273
            // The text engine writes the message from the patch; a retry with the same patch reuses it
            // rather than asking again.
            (string Patch, string Message)? described = null; // :276
            for (var attempt = 0; attempt < 3; attempt++) // :277
            {
                var head = await git.CommitHash(); // :278
                Advance(HydraMergeStage.Committing); // :279
                // On the default branch the work is built on the remote as it is now, so a checkout that
                // fell behind never sends a request that conflicts on the forge.
                var parent = head; // :282
                string? tree = null; // :283
                var merged = new Dictionary<string, byte[]>(SwiftStringComparer.Instance); // :284
                var rebase = false; // :285 (try? await git.fetch()) != nil, short-circuited on ownBranch
                if (ownBranch)
                {
                    var fetched = true;
                    try { await git.Fetch(); } catch (Exception error) when (SwiftTry.IsThrownError(error)) { fetched = false; }
                    rebase = fetched;
                }
                if (rebase &&
                    await SwiftTry.OptionalAsync<string>(async () => await git.CommitHash($"origin/{target}")) is { } remote &&
                    remote != head && await git.IsAncestor(head, remote)) // :285-287
                {
                    if (await git.CaptureTree(sorted, remote, head) is not { } rebased) // :288
                    {
                        var many = sorted.Count == 1 ? "a file" : "files";
                        return Fail($"Hydra did not merge: `{target}` changed the same lines.",
                            $"The remote changed {many} the team changed too, in the same places. The work is in the checkout: bring `{target}` in (`git pull`), settle the overlap, and the merge goes out by itself."); // :289-290
                    }
                    parent = remote; // :292
                    tree = rebased.Tree; // :293
                    merged = new Dictionary<string, byte[]>(rebased.Merged, SwiftStringComparer.Instance); // :294
                }
                var captured = tree ?? await git.CaptureTree(sorted); // :296-301
                // A lead can resume while gathering or capturing files: it has work under way again, and
                // this job goes out with it once it finishes.
                if (!runtime.IsReadyForHydraMerge) return HydraProjectMerge.Deferred; // :304
                // Everything the team did is committed already: taken along by another chat's merge from
                // the same checkout, or committed by hand. The job is spent either way.
                if (captured == await git.TreeHash(parent)) return HydraProjectMerge.AlreadyOnMain; // :308

                var patch = await SwiftTry.OptionalAsync<string>(async () => await git.Diff(parent, captured)) ?? ""; // :310
                var files = await Task.Run(() => DiffParser.Parse(patch)); // :313
                Advance(HydraMergeStage.Describing); // :314
                string message; // :315
                if (described is { } describedValue && SwiftString.Equal(describedValue.Patch, patch))
                {
                    message = describedValue.Message; // :316-317
                }
                else
                {
                    message = await CommitMessage(lead, files, patch, checkout); // :319
                    described = (patch, message); // :320
                }
                var commit = await git.CommitWork(captured, parent, message); // :322
                // Writing the branch to a commit prepared from `head` drops anything committed since: HEAD
                // is read again, and a checkout that moved is prepared again.
                if (await git.CommitHash() != head) continue; // :325
                prepared = (head, commit, message, files, merged); // :326
                break; // :327
            }
            if (prepared is not { } preparedValue) // :329
            {
                return Fail("Hydra did not merge: the checkout kept moving.",
                    $"Commits kept landing on `{current ?? target}` while the team's work was prepared. The work is in the checkout and goes out on the next try.",
                    retryable: true); // :330
            }
            var head2 = preparedValue.Head; // :332
            var files2 = preparedValue.Files; // :333
            var subject = TextCleanup.SingleLine(preparedValue.Message, 72); // :334

            if (ownBranch) // :336
            {
                // The job's own branch: one per chat, rebuilt on each try, so a retry updates the request it
                // opened instead of opening another.
                await git.UpdateRef($"refs/heads/{branch}", preparedValue.Commit); // :339
            }
            else
            {
                // The checkout's own branch must still be where it was read.
                await git.UpdateRef($"refs/heads/{branch}", preparedValue.Commit, head2); // :342
                // The branch moved under the checkout: the index catches up, the working tree already has
                // the content.
                await git.ResetIndex(sorted); // :345
            }
            Advance(HydraMergeStage.Pushing); // :347
            await git.PushBranch(branch, force: ownBranch); // :348

            var body = MergeRequestBody(lead, files2, work.TurnIDs.ToHashSet(), runtime); // :350
            Advance(HydraMergeStage.Opening); // :351
            SwiftURL? requestURL;
            try
            {
                requestURL = await HydraCreateMergeRequest(git, subject, body, branch, target); // :354
            }
            catch (HydraMergeRequestError.GitLabAuth error)
            {
                return Fail($"Hydra pushed {branch} but could not open a merge request.", SwiftErrors.LocalizedDescription(error)); // :356-357
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                return Fail($"Hydra pushed {branch} but could not open a merge request.",
                    $"{SwiftErrors.LocalizedDescription(error)}\n\nThe work is in the checkout and on `{branch}`; the next try opens the request.",
                    retryable: true); // :360-361
            }
            if (requestURL is null || MergeRequestLink.Create(requestURL) is not { } link) // :363-364
            {
                return Fail($"Hydra pushed {branch} but could not open a merge request.",
                    "The forge did not say where the request is. The next try opens it again.", retryable: true); // :365
            }

            Advance(HydraMergeStage.Merging); // :367
            try
            {
                await git.MergePullRequest(link); // :369
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error))
            {
                return Fail($"Hydra opened {link.Label} but could not merge it.",
                    $"{requestURL.AbsoluteString}\n\n{SwiftErrors.LocalizedDescription(error)}\n\nThe next try brings the request up to date with `{target}` and merges it.",
                    label: link.Label, url: requestURL, retryable: true); // :371
            }

            var lines = new List<string> { requestURL.AbsoluteString, "", FilesLine(files2) + $" landed on `{target}` from `{branch}`." }; // :374
            if (work.Heads.Count > 0) // :375-385
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
            // What the merge left out is not said: none of it was this merge's to take.
            if (ownBranch) // :390
            {
                Advance(HydraMergeStage.Syncing); // :391
                var synced = await SyncDefaultBranch(git, head2, target, sorted, preparedValue.Merged); // :392
                lines.Add(synced ? "The checkout is up to date." : $"The checkout was left as it was: `{target}` moved on in other ways meanwhile, or the team's files changed again; `git pull` when it suits you.");
            }
            else if (lead.WorktreePath is not null) // :394
            {
                // The lead worked in a worktree: the project's own checkout follows when it can.
                Advance(HydraMergeStage.Syncing); // :396
                var main = new Git(project.Path); // :397
                if ((await main.Status())?.Branch is { } mainBranch && SwiftString.Equal(mainBranch, target) &&
                    (await main.DirtyPaths()).Count == 0 && !await main.HasOperationInProgress()) // :398
                {
                    // :399 (try? await main.pullFastForward()) != nil
                    try { await main.PullFastForward(); lines.Add("The project checkout is up to date."); }
                    catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
                }
            }
            runtime.RecordHydraMerge(new HydraMergeRecord
            {
                At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Merged, Project = project.Name,
                Label = link.Label, Url = requestURL, Files = files2.Count, Detail = null,
            }); // :402
            Note(leadID, Title($"Hydra merged {link.Label}"), string.Join("\n", lines)); // :403
            ExistingRuntime(leadID)?.NoteDiffChanged(); // :404
            return HydraProjectMerge.Merged; // :405
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error))
        {
            return Fail("Hydra could not merge the team's work.",
                $"{SwiftErrors.LocalizedDescription(error)}\n\nThe work is in the checkout; the next try sends it again.", retryable: true); // :407
        }
    }

    // :414-423. A retry a minute after a failure that clears by itself, rather than at the sweep five
    // minutes on. One is pending per chat at a time.
    private void ScheduleHydraMergeRetry(Guid leadID)
    {
        if (ExistingRuntime(leadID) is not { } runtime || runtime.HasHydraMergeRetryPending) return; // :415
        runtime.HasHydraMergeRetryPending = true; // :416
        var weakSelf = new WeakReference<AppModel>(this);
        _ = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(60)); // :418
            if (!weakSelf.TryGetTarget(out var self)) return; // :419
            self.ExistingRuntime(leadID)?.HasHydraMergeRetryPending = false; // :420
            await self.AutoMergeHydraWork(leadID); // :421
        });
    }

    // :433-446
    private async Task<SwiftURL?> HydraCreateMergeRequest(Git git, string title, string body, string source, string target)
    {
        if (await git.Forge() != MergeRequestLink.Forge.Gitlab || LoginEnvironment.Which("glab") is null)
            return await git.CreatePullRequest(title, body, source, target); // :434-435

        var host = (await git.RemoteWebURL())?.Host ?? "your GitLab host"; // :437
        var arguments = new List<string> { "mr", "create", "--title", title, "--description", body,
            "--yes", "--squash-before-merge" }; // :441
        if (await git.RemoteWebURL() is { Host: not null } web) // :442 (read again, as the source does)
        {
            if (URLComponents.Create(web, resolvingAgainstBaseURL: false) is { User: not null } components)
            {
                components = components with { User = null, Password = null }; // :443-446
                if (components.Url is { } stripped) web = stripped;
            }
            arguments.AddRange(["--repo", web.AbsoluteString]); // :448
        }
        arguments.AddRange(["--source-branch", source, "--target-branch", target]); // :450
        if (await HydraGitLabAuthFailure(git.Directory) is { } authFailure)
            throw new HydraMergeRequestError.GitLabAuth(host, source, authFailure); // :454-455

        var failure = await Shell.RunTool("glab", arguments, @in: git.Directory, timeout: 300); // :458
        if (failure.Succeeded) return HydraMergeRequestURL(failure); // :459
        // The chat's branch already has an open request from an earlier try: the push just brought it up to
        // date, so that request is the one to merge.
        if (Git.ExistingRequestURL(failure, await git.RemoteWebURL()) is { } existing) return existing; // :462
        if (IsGitLabAuthFailure(failure)) // :463
        {
            if (await HydraGitLabAuthFailure(git.Directory) is null) // :466
            {
                var retry = await Shell.RunTool("glab", arguments, @in: git.Directory, timeout: 300); // :467
                if (retry.Succeeded) return HydraMergeRequestURL(retry); // :468
                failure = retry; // :469
            }
            if (IsGitLabAuthFailure(failure))
                throw new HydraMergeRequestError.GitLabAuth(host, source, failure.FailureMessage); // :471-472
        }
        if (MentionsRecoverFile(failure)) // :477
        {
            var retry = await Shell.RunTool("glab", arguments.Concat(["--recover"]).ToArray(), @in: git.Directory, timeout: 300); // :478
            if (retry.Succeeded) return HydraMergeRequestURL(retry); // :479
            failure = retry; // :480
        }
        throw new HydraMergeRequestError.CreationFailed(failure.FailureMessage); // :482
    }

    // :487-494
    private static async Task<string?> HydraGitLabAuthFailure(string directory)
    {
        ShellResult status;
        try { status = await Shell.RunTool("glab", ["auth", "status"], @in: directory, timeout: 30); }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return null; } // :490
        if (status.Succeeded) return null; // :491
        var message = status.FailureMessage; // :492
        return message.Length == 0 ? "glab is not signed in to this GitLab host." : message; // :493
    }

    // :497-501
    private static bool IsGitLabAuthFailure(ShellResult result)
    {
        var text = SwiftString.Lowercased(result.Output + "\n" + result.ErrorOutput); // :498
        return text.Contains("401", StringComparison.Ordinal) || text.Contains("unauthorized", StringComparison.Ordinal) ||
            (text.Contains("authentication", StringComparison.Ordinal) &&
                (text.Contains("failed", StringComparison.Ordinal) || text.Contains("expired", StringComparison.Ordinal) ||
                 text.Contains("invalid", StringComparison.Ordinal) || text.Contains("required", StringComparison.Ordinal))); // :499-500
    }

    private static bool MentionsRecoverFile(ShellResult result) =>
        SwiftString.Lowercased(result.Output + "\n" + result.ErrorOutput).Contains("recover", StringComparison.Ordinal); // :504-505

    // :508-512
    private static SwiftURL? HydraMergeRequestURL(ShellResult result)
    {
        var text = result.Output + "\n" + result.ErrorOutput;
        var match = Regex.Match(text, @"https://\S+", RegexOptions.CultureInvariant);
        return match.Success ? SwiftURL.Create(match.Value) : null;
    }

    // :533-670
    private async Task<HydraMergeWork> HydraWorkFor(Guid leadID, ThreadRuntime runtime, string checkout, Git git,
        Guid projectID, string leadCheckout, Guid leadProjectID, bool sweepsCheckout)
    {
        var turns = runtime.HydraUnmergedTurns; // :534
        var reported = turns.SelectMany(turn => turn.TouchedPaths ?? ValueList<string>.Empty).ToList(); // :535
        // What this job's own tools and landings wrote, as opposed to what its turns saw change on disk: a
        // file another chat also claims goes out here only when this job edited it itself. (:539)
        var edited = new List<string>();
        var headPaths = new List<(Guid ID, string Name, long Index, string Task, List<string> Paths)>(); // :546
        foreach (var head in HydraTeam(leadID)) // :547
        {
            if (head.Hydra is not { MergedAt: null } info || info.Landing?.PatchPath is not null || info.Landing?.Error is not null)
                continue; // :548-549
            var headRoot = head.ProjectID == leadProjectID ? leadCheckout : Project(head.ProjectID)?.Path ?? leadCheckout; // :557
            string Anchored(string path) // :558
            {
                var trimmed = path.Trimmed();
                if (trimmed.Length == 0) return ""; // :560
                if (head.WorktreePath is { } copy && TouchedPaths.Relative(trimmed, copy) is { } inCopy)
                    return SwiftPath.AppendingPathComponent(headRoot, inCopy); // :561-563
                if (SwiftPath.IsAbsolute(trimmed) || trimmed.StartsWith('~') || trimmed.Contains("://", StringComparison.Ordinal))
                    return trimmed; // ED-138, :564
                return SwiftPath.AppendingPathComponent(headRoot, trimmed); // :565
            }

            var own = new List<string>(); // :567
            var seen = new HashSet<string>(SwiftStringComparer.Instance); // :568
            void Collect(string path) // :569
            {
                if (path.Length == 0 || TouchedPaths.Relative(Anchored(path), checkout) is not { } relative || !seen.Add(relative)) return; // :570
                own.Add(relative);
            }
            foreach (var file in info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty) Collect(file.Path); // :573
            var landingAnchored = (info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty).Select(file => Anchored(file.Path)).ToArray();
            reported.AddRange(landingAnchored); // :574
            edited.AddRange(landingAnchored); // :575

            // A head's timeline runs to megabytes. One still open is read as it is; one that has gone cold is
            // decoded off the main actor.
            ValueList<TimelineItem> items;
            if (ExistingRuntime(head.Id) is { } live) items = new ValueList<TimelineItem>(live.Entries.Select(entry => entry.Item)); // :581-582
            else
            {
                var headID = head.Id;
                items = await Task.Run(() => Storage.DecodeDocument(headID)?.Items ?? ValueList<TimelineItem>.Empty); // :584-585
            }
            foreach (var item in items) // :587
            {
                if (item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue; // :588
                var toolAnchored = tool.Value.Edits.Select(edit => Anchored(edit.Path)).ToArray();
                reported.AddRange(toolAnchored); // :589
                edited.AddRange(toolAnchored); // :590
                foreach (var edit in tool.Value.Edits) Collect(edit.Path); // :591
            }
            headPaths.Add((head.Id, info.Persona.Name, info.Index, TextCleanup.SingleLine(info.Task, 120), own)); // :593
        }

        // The lead's own tool edits, from its timeline: a lead works in any sidebar project too, and a path
        // it wrote outside its checkout is that project's share of the merge.
        var unmerged = turns.Select(turn => turn.Id).ToHashSet(); // :599
        foreach (var entry in runtime.Entries.Where(entry => entry.TurnID is { } turnID && unmerged.Contains(turnID))) // :600
        {
            if (entry.Item.TimelineItemContent is not TimelineItem.Content.Tool tool) continue; // :601
            foreach (var edit in tool.Value.Edits) // :602
            {
                var trimmed = edit.Path.Trimmed();
                if (trimmed.Length == 0) continue; // :603-604
                var path = SwiftPath.IsAbsolute(trimmed) || trimmed.StartsWith('~') || trimmed.Contains("://", StringComparison.Ordinal)
                    ? trimmed : SwiftPath.AppendingPathComponent(leadCheckout, trimmed); // ED-138, :605-607
                reported.Add(path); // :608
                edited.Add(path); // :609
            }
        }

        var paths = new HashSet<string>(SwiftStringComparer.Instance); // :613
        var outside = new HashSet<string>(SwiftStringComparer.Instance); // :614
        foreach (var path in reported.Where(path => path.Length > 0 && !TouchedPaths.IsBuildOutput(path))) // :615
        {
            if (TouchedPaths.Relative(path, checkout) is { } relative) paths.Add(relative); // :616-617
            else outside.Add(path); // :618-619
        }

        // Files another chat's team put in this same checkout that no merge has taken yet: that chat's merge
        // carries them, never this one's.
        var siblingFiles = SiblingOwnedPaths(checkout, projectID, leadID); // :633
        var siblingClaims = siblingFiles.Reserved.Keys.ToHashSet(SwiftStringComparer.Instance); // :634
        var blockingClaims = siblingFiles.Blocking; // :635
        if (sweepsCheckout && turns.FirstOrDefault()?.BaseCheckpoint is { } baseCheckpoint &&
            await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths("HEAD", baseCheckpoint)) is { } alreadyDirty &&
            await SwiftTry.OptionalAsync<string>(async () => await git.CaptureTree()) is { } now &&
            await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths(baseCheckpoint, now)) is { } changedSince) // :636-640
        {
            var theirs = alreadyDirty.ToHashSet(SwiftStringComparer.Instance); // :641
            var fresh = changedSince.Where(path => !theirs.Contains(path)); // :642
            paths.UnionWith(fresh.Where(path => !siblingClaims.Contains(path))); // :643
        }

        // Nothing waits on another chat. A file another chat also claims goes out with this job when this
        // job's own tools or heads wrote it: whole, the way the checkout has it, and that chat's merge later
        // finds it on the branch already. (:646-654, PR-HYDRAMERGE-130/-077)
        var ownEdits = edited
            .Select(path => TouchedPaths.Relative(path, checkout))
            .Where(relative => relative is not null)
            .Select(relative => relative!)
            .ToHashSet(SwiftStringComparer.Instance); // :653
        paths.ExceptWith(blockingClaims.Keys.Where(key => !ownEdits.Contains(key))); // :654
        paths.RemoveWhere(TouchedPaths.IsBuildOutput); // :655
        paths.ExceptWith(await git.IgnoredPaths(paths.Order(StringComparer.Ordinal).ToArray())); // :656
        var tracked = await git.TrackedPaths(paths.Order(StringComparer.Ordinal).ToArray()); // :657
        var missing = paths.Where(path => !File.Exists(SwiftPath.AppendingPathComponent(checkout, path)) && !Directory.Exists(SwiftPath.AppendingPathComponent(checkout, path)) && !tracked.Contains(path))
            .ToHashSet(SwiftStringComparer.Instance); // :660-662
        paths.ExceptWith(missing); // :663
        var heads = new ValueList<HydraMergeHead>(headPaths.Select(head => new HydraMergeHead(head.Name, head.Index, head.Task,
            new ValueList<string>(head.Paths.Where(paths.Contains))))); // :664-666
        // What this merge settles: every turn and head of the job. A file left to another chat goes out with
        // that chat's merge, not this one's. (:667-669)
        return new HydraMergeWork(new ValueList<string>(paths.Order(SwiftStringComparer.Instance)),
            new ValueList<Guid>(turns.Select(turn => turn.Id)), new ValueList<Guid>(headPaths.Select(head => head.ID)),
            new ValueList<string>(outside.Order(SwiftStringComparer.Instance)), heads);
    }

    // :690-728
    private (Dictionary<string, string> Reserved, Dictionary<string, string> Blocking) SiblingOwnedPaths(
        string checkout, Guid projectID, Guid leadID)
    {
        var team = HydraTeam(leadID).Select(head => head.Id).ToHashSet(); // :691
        var reserved = new Dictionary<string, string>(SwiftStringComparer.Instance); // :692
        var blocking = new Dictionary<string, string>(SwiftStringComparer.Instance); // :693
        void Reserve(string path, string owner, bool blocks) // :694
        {
            if (TouchedPaths.Relative(path, checkout) is not { } relative) return; // :695
            reserved[relative] = owner; // :696
            if (blocks) blocking[relative] = owner; // :697
        }
        foreach (var other in Threads.Where(thread => thread.Id != leadID && thread.ProjectID == projectID && !team.Contains(thread.Id))) // :699
        {
            if (other.IsHydraHead) // :700
            {
                if (other.Hydra is not { MergedAt: null } info) continue; // :701
                var parent = other.ParentThreadID is { } parentID ? Thread(parentID) : null; // :704
                if (info.HydraHeadInfoStatus != HydraHeadInfo.Status.Running && (parent is null || !CanStillMerge(parent))) continue; // :705
                var rankedOut = info.HydraHeadInfoStatus == HydraHeadInfo.Status.Completed && parent is not null &&
                    HydraRanksAfter(parent, leadID) && HydraIsFinishedIdleEligible(parent); // :710-711
                foreach (var file in info.Landing?.Files ?? ValueList<HydraLanding.File>.Empty)
                    Reserve(file.Path, parent?.Title ?? other.Title, !rankedOut); // :712-713
            }
            else if (ExistingRuntime(other.Id) is { } live) // :715
            {
                var rankedOut = HydraRanksAfter(other, leadID) && HydraIsFinishedIdleEligible(other); // :719
                foreach (var turn in live.HydraUnmergedTurns.Where(turn => CanStillMerge(other) || turn.Status != TurnStatus.Completed)) // :720
                    foreach (var path in turn.TouchedPaths ?? ValueList<string>.Empty)
                        Reserve(path, other.Title, !rankedOut); // :721-722
            }
        }
        return (reserved, blocking); // :727
    }

    // :735-740
    private bool HydraIsFinishedIdleEligible(ChatThread lead)
    {
        if (!HydraIsOn(lead) || ExistingRuntime(lead.Id) is not { } live) return false; // :736
        if (!live.IsReadyForHydraMerge || live.IsHydraMerging || HydraHasPendingLanding(lead.Id)) return false; // :737-738
        return CanStillMerge(lead); // :739
    }

    // :745-749
    private bool HydraHasPendingLanding(Guid leadID) => HydraTeam(leadID).Any(head =>
        head.Hydra is { MergedAt: null, Landing: { } landing } && landing.Files.Count > 0 && !landing.Landed);

    // :755-757
    private bool HydraRanksAfter(ChatThread lead, Guid other) =>
        string.Compare(lead.Id.ToString("D"), other.ToString("D"), StringComparison.Ordinal) > 0;

    // :761-771
    private bool CanStillMerge(ChatThread lead)
    {
        var live = ExistingRuntime(lead.Id);
        if (live is not null && (live.Phase != RuntimePhase.Idle || live.IsHydraMerging)) return true; // :763
        if (lead.IsArchived || !HydraIsOn(lead) || lead.UpdatedAt <= SwiftDate.Now - HydraMergeWindow) return false; // :764-765
        var landedHeadWork = HydraTeam(lead.Id).Any(head => head.Hydra is { MergedAt: null, Landing.Landed: true }); // :768
        if (live is null) return lead.LastStatus == TurnStatus.Completed; // :769
        return live.Turns.LastOrDefault()?.Status == TurnStatus.Completed &&
            (live.HydraUnmergedTurns.Count > 0 || landedHeadWork); // :770
    }

    // :778-818
    private static async Task<bool> SyncDefaultBranch(Git git, string head, string target, IReadOnlyList<string> ownPaths,
        IReadOnlyDictionary<string, byte[]>? merged = null)
    {
        merged ??= new Dictionary<string, byte[]>(SwiftStringComparer.Instance); // :778
        var remote = $"origin/{target}"; // :779
        var fetched = true; // :780 (try? await git.fetch()) != nil
        try { await git.Fetch(); } catch (Exception error) when (SwiftTry.IsThrownError(error)) { fetched = false; }
        if (!fetched || !await git.IsAncestor(head, remote)) return false; // :780
        // A file merged three ways went out holding the remote's changes as well: the checkout takes that
        // result, but only while the file still holds what was merged from. (:781-789)
        var root = git.Directory;
        var mergedFrom = new Dictionary<string, byte[]>(SwiftStringComparer.Instance); // :785
        foreach (var (path, _) in merged)
        {
            if (SwiftTry.Optional(() => File.ReadAllBytes(SwiftPath.AppendingPathComponent(root, path))) is not { } working)
                return false; // :787
            mergedFrom[path] = working; // :788
        }
        var changed = await SwiftTry.OptionalAsync<IReadOnlyList<string>>(async () => await git.ChangedPaths(head, remote)); // :790
        if (changed is null) return false;
        var own = ownPaths.ToHashSet(SwiftStringComparer.Instance); // :791
        var others = changed.Where(path => !own.Contains(path)).ToArray(); // :792
        var dirty = (await git.DirtyPaths()).ToHashSet(SwiftStringComparer.Instance); // :793
        if (others.Any(dirty.Contains)) return false; // :794
        var present = await git.PresentPaths(others, remote); // :797
        var kept = others.Where(present.Contains).ToArray(); // :798
        var gone = others.Where(path => !present.Contains(path)).ToArray(); // :799
        // `reset --soft` moves the branch with no expected old value, so a commit made in a terminal while
        // the merge ran would be left with nothing pointing at it. The checkout is left alone instead.
        if (await SwiftTry.OptionalAsync<string>(async () => await git.CommitHash()) != head) return false; // :803
        // DEVIATION OD-0020 Part B: retain the exact index bytes and every working file this sync may touch.
        // A later Git or atomic-write failure must restore the branch, index and working tree before reporting false.
        var indexPath = await git.IndexPath();
        if (indexPath is null) return false;
        byte[]? indexBefore;
        var workingBefore = new Dictionary<string, (byte[]? Bytes, FileAttributes Attributes, DateTime Created, DateTime Written)>(SwiftStringComparer.Instance);
        try
        {
            indexBefore = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null;
            foreach (var path in others.Concat(merged.Keys).Distinct(SwiftStringComparer.Instance))
            {
                var full = SwiftPath.AppendingPathComponent(root, path);
                if (Directory.Exists(full) || new FileInfo(full).LinkTarget is not null) return false;
                workingBefore[full] = File.Exists(full)
                    ? (File.ReadAllBytes(full), File.GetAttributes(full), File.GetCreationTimeUtc(full), File.GetLastWriteTimeUtc(full))
                    : (null, default, default, default);
            }
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) { return false; }
        if (await SwiftTry.OptionalAsync<string>(async () => await git.CommitHash()) != head) return false;
        try
        {
            await git.ResetSoft(remote); // :805
            await git.ResetIndex(ownPaths); // :806
            await git.CheckoutPaths(remote, kept); // :807
            await git.RemovePathsAfterSoftReset(gone); // :808; DEVIATION OD-0020 Part A: git rm --quiet -f --
            foreach (var (path, content) in merged) // :809
            {
                var url = SwiftPath.AppendingPathComponent(root, path);
                if (!mergedFrom.TryGetValue(path, out var from) ||
                    SwiftTry.Optional(() => File.ReadAllBytes(url)) is not { } current || !current.AsSpan().SequenceEqual(from))
                    continue; // :811
                FileOps.WriteAtomic(url, content); // :812
            }
            return true; // :814
        }
        catch (Exception error) when (SwiftTry.IsThrownError(error)) // :815-817; DEVIATION OD-0020 Part B
        {
            var rollbackErrors = new List<Exception>();
            try { await git.ResetSoft(head); }
            catch (Exception restore) when (SwiftTry.IsThrownError(restore)) { rollbackErrors.Add(restore); }
            foreach (var (full, before) in workingBefore)
            {
                try
                {
                    if (before.Bytes is null)
                    {
                        if (File.Exists(full)) File.Delete(full);
                    }
                    else
                    {
                        if (!File.Exists(full) || !File.ReadAllBytes(full).AsSpan().SequenceEqual(before.Bytes))
                        {
                            if (File.Exists(full)) File.SetAttributes(full, FileAttributes.Normal);
                            FileOps.WriteAtomic(full, before.Bytes);
                        }
                        File.SetCreationTimeUtc(full, before.Created);
                        File.SetLastWriteTimeUtc(full, before.Written);
                        File.SetAttributes(full, before.Attributes);
                    }
                }
                catch (Exception restore) when (SwiftTry.IsThrownError(restore)) { rollbackErrors.Add(restore); }
            }
            try
            {
                if (indexBefore is null)
                {
                    if (File.Exists(indexPath)) File.Delete(indexPath);
                }
                else FileOps.WriteAtomic(indexPath, indexBefore);
            }
            catch (Exception restore) when (SwiftTry.IsThrownError(restore)) { rollbackErrors.Add(restore); }
            if (rollbackErrors.Count > 0)
                throw new AggregateException("Hydra sync rollback failed.", [error, .. rollbackErrors]);
            return false;
        }
    }

    // :822-841
    private async Task<string> CommitMessage(ChatThread lead, IReadOnlyList<DiffFile> files, string patch, string directory)
    {
        if (TextEngine(lead.Provider) is { } engine) // :823
        {
            var summary = string.Join("\n", files.Select(file =>
                $"{(file.FileChange == DiffFile.Change.Added ? "A" : file.FileChange == DiffFile.Change.Deleted ? "D" : "M")} {file.Path}")); // :824
            if (await TextGeneration.CommitMessage(summary, patch, Settings.CommitInstructions, engine, directory) is { Length: > 0 } text)
                return text; // :825-833
        }
        var lines = new List<string> { lead.Title == ChatThread.Untitled ? "Hydra: the team's work" : lead.Title, "" }; // :835
        foreach (var head in HydraTeam(lead.Id)) // :836
            if (head.Hydra is { Task.Length: > 0 } info)
                lines.Add($"- {info.Persona.Name}: {TextCleanup.SingleLine(info.Task, 100)}"); // :837-838
        return string.Join("\n", lines).Trimmed(); // :840
    }

    // :844-869
    private string MergeRequestBody(ChatThread lead, IReadOnlyList<DiffFile> files, IReadOnlySet<Guid> through, ThreadRuntime runtime)
    {
        var lines = new List<string> { "Opened by nth once its Hydra team finished.", "" }; // :845; DEVIATION OD-0008: the owner names Marrow as the opener.
        var heads = HydraTeam(lead.Id).Select(head => head.Hydra).Where(info => info is not null && info.Task.Length > 0).Cast<HydraHeadInfo>().ToArray(); // :846
        if (heads.Length > 0) // :847
        {
            lines.Add("## Heads"); // :848
            foreach (var info in heads) // :849
            {
                var line = $"- **{info.Persona.Name}**: {TextCleanup.SingleLine(info.Task, 120)}";
                if (info.Landing is { Files.Count: > 0 } landing)
                    line += $" ({(landing.Files.Count == 1 ? "1 file" : $"{landing.Files.Count} files")})"; // :850-853
                lines.Add(line);
            }
            lines.Add(""); // :856
        }
        var reply = runtime.Entries.LastOrDefault(entry => entry.TimelineEntryKind == TimelineEntry.Kind.Assistant &&
            entry.TurnID is { } turnID && through.Contains(turnID))?.Item.TimelineItemContent is TimelineItem.Content.Assistant assistant
            ? assistant.Value.Text.Trimmed() : null; // :858-861
        if (!string.IsNullOrEmpty(reply)) // :861
        {
            lines.Add("## The lead's summary"); // :862
            lines.Add(SwiftString.Count(reply!) > 2_000 ? SwiftString.Prefix(reply, 2_000) + "…" : reply); // :863
            lines.Add(""); // :864
        }
        lines.Add("## Files"); // :866
        lines.AddRange(files.Select(file => $"- `{file.Path}` (+{file.Additions} −{file.Deletions})")); // :867
        return string.Join("\n", lines); // :868
    }

    // :871-882
    private static string HydraBranchName(ChatThread lead)
    {
        var title = lead.Title == ChatThread.Untitled ? "team" : lead.Title;
        var slugBuilder = new StringBuilder();
        // R-STR-4/R-STR-5: Swift's `title.lowercased().map { $0.isLetter || $0.isNumber ? String($0) : "-" }`
        // is SwiftString.Lowercased (full ICU case mapping, e.g. U+0130 -> "i" + U+0307) over
        // SwiftCharacter.IsLetter (Alphabetic) / IsNumber (Numeric_Type is not None: U+00B2, U+00BD, Roman and
        // CJK numerals too), never .ToLowerInvariant()/Rune.IsLetterOrDigit (L*/Nd only).
        foreach (var character in SwiftString.Characters(SwiftString.Lowercased(title)))
            slugBuilder.Append(SwiftCharacter.IsLetter(character) || SwiftCharacter.IsNumber(character) ? character : "-"); // :876
        var slug = slugBuilder.ToString();
        while (slug.Contains("--", StringComparison.Ordinal)) slug = slug.Replace("--", "-", StringComparison.Ordinal); // :877
        slug = slug.Trim('-'); // :878
        if (SwiftString.Count(slug) > 40) slug = SwiftString.Prefix(slug, 40).TrimEnd('-'); // :879
        var suffix = lead.Id.ToString("D").ToLowerInvariant()[..6]; // :880: stable per chat, from lead.id, not a fresh id
        return $"hydra/{(slug.Length == 0 ? "team" : slug)}-{suffix}"; // :881
    }

    // :884-900
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
                if (length > 4_000_000) continue; // :894
                var bytes = File.ReadAllBytes(fullPath);
                var body = new byte[bytes.Length + 1]; // :896
                body[0] = 0x0A;
                bytes.CopyTo(body, 1);
                if (Contains(body, opening) && Contains(body, closing)) found.Add(path); // :897
            }
            catch (Exception error) when (SwiftTry.IsThrownError(error)) { }
        }
        return new ValueList<string>(found); // :892-898
    });

    private static bool Contains(ReadOnlySpan<byte> body, ReadOnlySpan<byte> value)
    {
        return body.IndexOf(value) >= 0;
    }

    // :902-906
    private void RecordHydraSettlement(Guid leadID, ThreadRuntime runtime)
    {
        if (HydraUnmergedFileCount(leadID) != 0) return; // :903
        runtime.RecordHydraMerge(new HydraMergeRecord
        {
            At = SwiftDate.Now, HydraMergeRecordOutcome = HydraMergeRecord.Outcome.Settled,
            Project = null, Label = null, Url = null, Files = 0, Detail = null,
        }); // :904
        Note(leadID, "Hydra has no files left to merge.", "The checkout has no remaining changes from this job to send. No new merge request was needed."); // :905
    }

    // :908-912
    private static string FilesLine(IReadOnlyList<DiffFile> files)
    {
        var additions = files.Sum(file => file.Additions); // :909
        var deletions = files.Sum(file => file.Deletions); // :910
        return $"{(files.Count == 1 ? "1 file" : $"{files.Count} files")} (+{additions} −{deletions})"; // :911
    }

    // :914-930
    private void Note(Guid leadID, string title, string body)
    {
        var runtime = ExistingRuntime(leadID); // :919
        var id = runtime?.HydraMergeNoteID; // :920
        if (runtime is not null) runtime.HydraMergeNoteID = null; // :921
        runtime?.AppendHydraNote(title + "\n" + body, id); // :922
        if (IsApplicationActive() && SelectedThreadID == leadID) return; // :923-924
        UpdateThread(leadID, thread => thread with { HasUnread = true }); // :925
        UpdateDockBadge(); // :926
        if (Settings.NotifyWhenFinished && Thread(leadID) is { } lead)
            Notify(leadID, lead.Title, title); // :927-928
    }
}

// :934-948
internal abstract class HydraMergeRequestError(string message) : SwiftError
{
    public override string ErrorDescription => message;

    public sealed class GitLabAuth(string host, string branch, string detail) : HydraMergeRequestError(
        $"GitLab refused the login (401 Unauthorized) for {host}: the token glab signs in with is missing or expired.\n\n{detail}\n\nSign back in with `glab auth login --hostname {host}` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `{branch}` is already pushed, so the team's work is safe.");

    public sealed class CreationFailed(string detail) : HydraMergeRequestError(detail);
}
