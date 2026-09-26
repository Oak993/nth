using System.Reflection;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Store;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B2 1fbf81e), :48-72, :814-826, :846-850.
public sealed class AppModelHydraMergeTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-005")] // AppModel+HydraMerge.swift:48-52
    [Trait("Req", "PR-HYDRAMERGE-006")] // AppModel+HydraMerge.swift:53-60
    [Trait("Req", "PR-HYDRAMERGE-008")] // AppModel+HydraMerge.swift:65-72
    [Trait("Req", "PR-HYDRAMERGE-010")] // AppModel+HydraMerge.swift:78-87
    [Trait("Req", "PR-HYDRAMERGE-029")] // AppModel+HydraMerge.swift:218-223
    public void ResumeHydraMerges_loads_a_recent_finished_lead_and_records_a_missing_remote()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("project"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var messages = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Select(content => content.Value);
            var note = Assert.Single(messages, message => message.HydraHeads is { Count: 0 });
            return (Runtime: runtime, Record: Assert.Single(runtime.HydraMerges), Note: note);
        });

        Assert.Equal(HydraMergeRecord.Outcome.Failed, state.Record.HydraMergeRecordOutcome);
        Assert.Equal("Project", state.Record.Project);
        Assert.Null(state.Record.Label);
        Assert.Null(state.Record.Url);
        Assert.Equal(0, state.Record.Files);
        Assert.Equal("Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", state.Record.Detail);
        Assert.Equal("Hydra did not merge: no remote.\nThe team's work is in the checkout; there is no origin to push it to.", state.Note.Text);
        Assert.False(state.Runtime.IsHydraMerging);
        Assert.Null(state.Runtime.HydraMergeStage);
        Assert.Null(state.Runtime.HydraMergeStep);
        Assert.Null(state.Runtime.HydraMergeStartedAt);
        Assert.Null(state.Runtime.HydraMergeNoteID);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-032")] // AppModel+HydraMerge.swift:814-826
    public async Task Conflict_marker_scan_requires_both_lf_prefixed_markers_and_skips_oversized_files()
    {
        using var h = new AppModelHarness();
        var folder = h.NewFolder("conflict-scan");
        File.WriteAllBytes(Path.Combine(folder, "first-line.txt"), Encoding.UTF8.GetBytes("<<<<<<< ours\r\ncontent\r\n>>>>>>> theirs\r\n"));
        File.WriteAllBytes(Path.Combine(folder, "opening-only.txt"), Encoding.UTF8.GetBytes("<<<<<<< ours\ncontent\n"));
        var tooLarge = new byte[4_000_001];
        Array.Fill(tooLarge, (byte)'x');
        Encoding.UTF8.GetBytes("<<<<<<< ours\n>>>>>>> theirs\n").CopyTo(tooLarge, 0);
        File.WriteAllBytes(Path.Combine(folder, "too-large.txt"), tooLarge);

        var found = await InvokeConflictMarkerScan(
            new ValueList<string>(["too-large.txt", "opening-only.txt", "first-line.txt"]), folder);

        Assert.Equal(["first-line.txt"], found);
    }

    private static async Task InvokeResumeHydraMerges(AppModel model)
    {
        var method = typeof(AppModel).GetMethod("ResumeHydraMerges", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(model, null);
        var task = Assert.IsAssignableFrom<Task>(result);
        await task;
    }

    private static async Task<ValueList<string>> InvokeConflictMarkerScan(IReadOnlyList<string> paths, string checkout)
    {
        var method = typeof(AppModel).GetMethod("PathsWithConflictMarkers", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [paths, checkout]);
        var task = Assert.IsAssignableFrom<Task<ValueList<string>>>(result);
        return await task;
    }

    // SyncDefaultBranch is `private static async Task<bool>` (AppModel+HydraMerge.cs:647): MethodInfo.Invoke on an
    // async method hands back the Task, never the eventual bool, exactly like InvokeConflictMarkerScan above for
    // PathsWithConflictMarkers. Asserting the raw Invoke() result IsType<bool> (as this helper's one call site used
    // to) fails every time (reproduced 4/4): under this runtime's reflection invoker the failure surfaced as the
    // compiler-generated async state machine's AsyncTaskMethodBuilder, not even a boxed Task<bool>, but either way
    // a bare bool was never going to come back from an async method through reflection.
    private static async Task<bool> InvokeSyncDefaultBranch(
        Marrow.Services.Git.Git git, string head, string target, IReadOnlyList<string> ownPaths)
    {
        var method = typeof(AppModel).GetMethod("SyncDefaultBranch", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        // :778 merged defaults to [:] in Swift; SyncDefaultBranch's `merged` C# parameter is optional too, but
        // reflection's Invoke does not apply parameter defaults on its own, so it is passed explicitly here.
        var result = method!.Invoke(null, [git, head, target, ownPaths, null]);
        var task = Assert.IsAssignableFrom<Task<bool>>(result);
        return await task;
    }
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-039")] // AppModel+HydraMerge.swift:874-882 (B3)
    public void Hydra_branch_name_uses_a_normalized_title_slug_and_six_lowercase_hex_characters_from_the_leads_own_id()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", @"C:\repo");
        var cases = new (string Title, string Slug)[]
        {
            ("A Very--Cool! Hydra", "a-very-cool-hydra"),
            ("!!!", "team"),
            ("Équipe", "équipe"),
            (new string('A', 50), new string('a', 40)),
        };

        foreach (var (title, expectedSlug) in cases)
        {
            var lead = AppModelSamples.Thread(project) with { Title = title };
            var expectedSuffix = lead.Id.ToString("D").ToLowerInvariant()[..6]; // :880

            var branch = InvokeHydraBranchName(lead);

            Assert.StartsWith("hydra/", branch, StringComparison.Ordinal);
            var suffixSeparator = branch.LastIndexOf('-');
            Assert.Equal(expectedSlug, branch["hydra/".Length..suffixSeparator]);
            Assert.Matches("^[0-9a-f]{6}$", branch[(suffixSeparator + 1)..]);
            Assert.Equal(expectedSuffix, branch[(suffixSeparator + 1)..]); // suffix comes from lead.id, not a fresh id
            Assert.Equal(branch, InvokeHydraBranchName(lead)); // same on every try for the same chat (:875-877)
        }
    }

    [Theory]
    [InlineData("401", "", true)]
    [InlineData("", "UNAUTHORIZED", true)]
    [InlineData("authentication failed", "", true)]
    [InlineData("", "authentication expired", true)]
    [InlineData("authentication invalid", "", true)]
    [InlineData("authentication required", "", true)]
    [InlineData("authorization is pending", "", false)]
    [Trait("Req", "PR-HYDRAMERGE-052")] // AppModel+HydraMerge.swift:435-439
    public void GitLab_auth_failure_matches_only_the_packet_substrings(string output, string errorOutput, bool expected)
    {
        var result = ShellResultFor(output, errorOutput);

        Assert.Equal(expected, InvokeHydraMergeHelper<bool>("IsGitLabAuthFailure", result));
    }

    [Theory]
    [InlineData("glab cannot continue: recovery file is locked", "", true)]
    [InlineData("", "RECOVER operation is required", true)]
    [InlineData("refresh token is missing", "", false)]
    [Trait("Req", "PR-HYDRAMERGE-053")] // AppModel+HydraMerge.swift:442-444
    public void Merge_request_recovery_check_is_a_case_insensitive_substring(string output, string errorOutput, bool expected)
    {
        Assert.Equal(expected, InvokeHydraMergeHelper<bool>("MentionsRecoverFile", ShellResultFor(output, errorOutput)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-054")] // AppModel+HydraMerge.swift:447-451
    public void Merge_request_url_uses_the_first_https_address_in_stdout_then_stderr()
    {
        var result = ShellResultFor(
            "glab opened https://gitlab.example/group/project/-/merge_requests/7",
            "later https://gitlab.example/group/project/-/merge_requests/8",
            status: 0);

        var url = InvokeHydraMergeRequestUrl(result);

        Assert.NotNull(url);
        Assert.Equal("https://gitlab.example/group/project/-/merge_requests/7", url!.AbsoluteString);
        var stderrOnly = InvokeHydraMergeRequestUrl(
            ShellResultFor("http://ignored.example/mr/1", "opened https://gitlab.example/mr/2", status: 0));
        Assert.Equal("https://gitlab.example/mr/2", stderrOnly?.AbsoluteString);
        var noHttps = InvokeHydraMergeRequestUrl(
            ShellResultFor("http://ignored.example/mr/1", "", status: 0));
        Assert.Null(noHttps);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-109")] // AppModel+HydraMerge.swift:879-892
    public void GitLab_auth_error_has_the_exact_four_paragraph_guidance()
    {
        var errorType = typeof(AppModel).Assembly.GetType("Marrow.App.HydraMergeRequestError+GitLabAuth", throwOnError: true)!;
        var error = Assert.IsAssignableFrom<Exception>(
            Activator.CreateInstance(errorType, "gitlab.example", "hydra/fix-abc123", "token expired"));

        Assert.Equal(
            "GitLab refused the login (401 Unauthorized) for gitlab.example: the token glab signs in with is missing or expired.\n\n" +
            "token expired\n\n" +
            "Sign back in with \u0060glab auth login --hostname gitlab.example\u0060 (or export a valid GITLAB_TOKEN), then ask for the merge again. " +
            "The branch \u0060hydra/fix-abc123\u0060 is already pushed, so the team's work is safe.",
            SwiftErrors.LocalizedDescription(error));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-110")] // AppModel+HydraMerge.swift:882-890
    public void Merge_request_creation_error_returns_glab_message_without_added_guidance()
    {
        const string detail = "glab could not create the request: permission denied";
        var errorType = typeof(AppModel).Assembly.GetType("Marrow.App.HydraMergeRequestError+CreationFailed", throwOnError: true)!;
        var error = Assert.IsAssignableFrom<Exception>(Activator.CreateInstance(errorType, detail));

        Assert.Equal(detail, SwiftErrors.LocalizedDescription(error));
    }

    // B3 removes the Held outcome from every write path (PR-HYDRAMERGE-122): AppModel+HydraMerge.swift no longer
    // declares recordHydraHold or heldDetail (the lane's "Retire the HeldDetail and RecordHydraHold tests"), so the
    // B2-era Held_detail_* and Hold_records_deduplicate_* tests that exercised RecordHydraHold/HeldDetail are
    // retired along with those methods. RecordHydraSettlement itself is still B3 source (:902-906) and is checked
    // directly below.
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-113")] // AppModel+HydraMerge.swift:902-906
    public void Record_settlement_writes_once_when_no_files_remain_unmerged()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("settlement-project"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        h.WriteLibrary([project], [lead]);
        AppModelHarness.WriteDocument(lead.Id);
        var model = h.MakeModel();

        var observed = h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(lead.Id);
            var recordSettlement = typeof(AppModel).GetMethod("RecordHydraSettlement", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(recordSettlement);

            recordSettlement!.Invoke(model, [lead.Id, runtime]);

            var notes = runtime.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Where(content => content.Value.HydraHeads is { Count: 0 })
                .Select(content => content.Value.Text)
                .ToArray();
            return (Records: runtime.HydraMerges.ToArray(), Notes: notes);
        });

        var record = Assert.Single(observed.Records);
        Assert.Equal(HydraMergeRecord.Outcome.Settled, record.HydraMergeRecordOutcome);
        Assert.Equal(0, record.Files);
        Assert.Null(record.Project);
        Assert.Null(record.Label);
        Assert.Null(record.Url);
        Assert.Null(record.Detail);
        Assert.Equal(
            ["Hydra has no files left to merge.\nThe checkout has no remaining changes from this job to send. No new merge request was needed."],
            observed.Notes);
    }

    private static T InvokeHydraMergeHelper<T>(string name, ShellResult result)
    {
        var method = typeof(AppModel).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<T>(method!.Invoke(null, [result]));
    }

    private static SwiftURL? InvokeHydraMergeRequestUrl(ShellResult result)
    {
        var method = typeof(AppModel).GetMethod("HydraMergeRequestURL", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (SwiftURL?)method!.Invoke(null, [result]);
    }

    private static string InvokeHydraBranchName(ChatThread lead)
    {
        var method = typeof(AppModel).GetMethod("HydraBranchName", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [lead]));
    }

    private static ShellResult ShellResultFor(string output, string errorOutput, int status = 1) =>
        new(status, Encoding.UTF8.GetBytes(output), Encoding.UTF8.GetBytes(errorOutput));


    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-009")] // AppModel+HydraMerge.swift:34-35
    public void Is_any_hydra_merge_running_is_false_when_no_live_runtime_is_merging()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("project"));
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();

        var isRunning = h.Main.OnMain(() => model.IsAnyHydraMergeRunning);

        Assert.False(isRunning);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-019")] // AppModel+HydraMerge.swift:190-205
    public void Scratch_path_recognizes_a_dot_folder_directly_under_the_user_profile()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.False(string.IsNullOrEmpty(profile));
        var dotFolder = SwiftPath.AppendingPathComponent(profile, ".marrow-hydra-merge-test");
        var nested = SwiftPath.AppendingPathComponent(dotFolder, "state.json");

        Assert.True(InvokeIsScratchPath(dotFolder));
        Assert.True(InvokeIsScratchPath(nested));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-021")] // AppModel+HydraMerge.swift:148-159
    public void No_groups_and_no_strays_marks_an_empty_completed_turn_merged_without_a_merge_record()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("project"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            // A build-output-only path: non-empty so ResumeHydraMerges' hasOwnWork gate (:65) calls
            // AutoMergeHydraWork, but HydraWorkFor strips it via TouchedPaths.IsBuildOutput (:588), leaving
            // own.Paths empty so groups.RemoveAll (:147) drops the lead's own group -- the actual "no groups" case.
            TouchedPaths = new ValueList<string>([".build/output.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            return (Runtime: runtime!, Records: runtime!.HydraMerges.ToArray());
        });

        Assert.Empty(state.Records);
        Assert.Contains(state.Runtime.Turns, candidate => candidate.Id == turn.Id && candidate.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-028")] // AppModel+HydraMerge.swift:218-219
    public void An_empty_repository_project_pass_returns_failed_without_a_record_or_note()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.Empty(h.NewFolder("empty-project"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        h.WriteLibrary([project], [lead]);
        AppModelHarness.WriteDocument(lead.Id);
        var model = h.MakeModel();

        var observed = h.Main.OnMain(() =>
        {
            var runtime = model.Runtime(lead.Id);
            var method = typeof(AppModel).GetMethod(
                "MergeHydraProject",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var workType = typeof(AppModel).GetNestedType("HydraMergeWork", BindingFlags.NonPublic);
            Assert.NotNull(workType);
            var work = Activator.CreateInstance(
                workType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args:
                [
                    ValueList<string>.Empty,
                    ValueList<Guid>.Empty,
                    ValueList<Guid>.Empty,
                    ValueList<string>.Empty,
                    ValueList<HydraMergeHead>.Empty,
                ],
                culture: null);
            Assert.NotNull(work);

            var result = method!.Invoke(model,
            [
                lead.Id,
                lead,
                project,
                repository.Root,
                work,
                runtime,
                false,
            ]);
            Assert.IsAssignableFrom<Task>(result);
            return (Runtime: runtime, Task: (Task)result!);
        });

        h.Main.Loop.Run(async () => await observed.Task, AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var notes = observed.Runtime.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Where(content => content.Value.HydraHeads is { Count: 0 })
                .ToArray();
            return (Records: observed.Runtime.HydraMerges.ToArray(), Notes: notes);
        });

        Assert.Empty(state.Records);
        Assert.Empty(state.Notes);
    }

    private static bool InvokeIsScratchPath(string path)
    {
        var method = typeof(AppModel).GetMethod("IsScratchPath", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<bool>(method!.Invoke(null, [path]));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-030")] // AppModel+HydraMerge.swift:245-249 (B3): an operation in progress is quiet
    // and retried, not recorded or noted. The B2-era text ("Hydra did not merge: a rebase or merge is underway.")
    // is gone from B3 DroppyCode entirely: `guard !(await git.hasOperationInProgress()) else { scheduleHydraMergeRetry(of:
    // leadID); return .deferred }` writes nothing and says nothing, unlike B2's fail(...).
    public void An_in_progress_merge_is_deferred_silently_and_schedules_a_retry()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("operation-in-progress"));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");

        repository.Git("checkout", "-q", "-b", "side");
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("side\n"));
        repository.Git("add", "a.txt");
        repository.Git("commit", "-q", "-m", "side");
        repository.Git("checkout", "-q", "main");
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("main\n"));
        repository.Git("add", "a.txt");
        repository.Git("commit", "-q", "-m", "main");
        Assert.False(repository.Raw("merge", "side").Succeeded);

        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var result = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var notes = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Where(content => content.Value.HydraHeads is { Count: 0 });
            return (Runtime: runtime, Notes: notes.ToArray());
        });

        // :246-249 records nothing and says nothing; the turn stays unmerged so the sweep can pick it up again.
        Assert.Empty(result.Runtime.HydraMerges);
        Assert.Empty(result.Notes);
        Assert.DoesNotContain(result.Runtime.Turns, candidate => candidate.Id == turn.Id && candidate.HydraMerged);
        // :247 scheduleHydraMergeRetry sets the one-pending-per-chat flag (ThreadRuntime.cs:621) before sleeping.
        Assert.True(result.Runtime.HasHydraMergeRetryPending);
    }

    /// <summary>A checkout with one commit and a remote, shared by the PR-HYDRAMERGE-122/123 tests below: a
    /// remote so a merge can reach the tree comparison at :308 instead of failing at the unconditional :242
    /// remote check first.</summary>
    private static (AppModelGitRepo Repository, Project Project) QueuedMergeProject(AppModelHarness h, string label)
    {
        var repository = AppModelGitRepo.WithCommit(h.NewFolder(label));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");
        return (repository, AppModelSamples.Project("Project", repository.Root));
    }

    /// <summary>A ready-to-merge lead: HydraEnabled, one completed turn touching <paramref name="path"/>.</summary>
    private static ChatThread ReadyLead(Project project, string path, out TurnRecord turn)
    {
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>([path]),
        };
        return lead;
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-122")] // AppModel+HydraMerge.swift:80-101 (B3, new): a job that finishes while
    // another chat's merge is running sets isHydraMergeQueued, polls isAnyHydraMergeRunning every 400 ms, and
    // goes the moment that merge ends -- every precondition read again with no suspension before isHydraMerging
    // is set, so of several waiters exactly one goes.
    public void A_job_that_finishes_while_another_merge_runs_waits_and_goes_within_one_poll_of_it_ending()
    {
        using var h = new AppModelHarness();
        var (_, project) = QueuedMergeProject(h, "queued-project");
        var busy = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        // a.txt is reported but unchanged from HEAD, so once `waiting` goes its pass returns AlreadyOnMain
        // (:308) without ever touching the network.
        var waiting = ReadyLead(project, "a.txt", out var turn);
        h.WriteLibrary([project], [busy, waiting]);
        AppModelHarness.WriteDocument(busy.Id);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(waiting.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(waiting.Id));

        var model = h.MakeModel();
        var waitingRuntime = h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
            model.Runtime(busy.Id).IsHydraMerging = true; // another chat's merge is already running
            return model.Runtime(waiting.Id);
        });
        h.Main.Loop.Run(() => waitingRuntime.EnsureLoaded(), AppModelHarness.Bound); // loads its turn (:2916)

        Task task = null!;
        h.Main.OnMain(() => task = model.AutoMergeHydraWork(waiting.Id));

        // Set before the first sleep, with no suspension after the isAnyHydraMergeRunning check (:88-89).
        Assert.True(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));
        Assert.False(task.IsCompleted);

        h.Main.Advance(TimeSpan.FromSeconds(1)); // polls at 0.4 and 0.8 s find the other merge still running
        Assert.True(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));
        Assert.False(task.IsCompleted);

        h.Main.OnMain(() => model.ExistingRuntime(busy.Id)!.IsHydraMerging = false); // ends between two polls
        h.Main.AdvanceMilliseconds(199); // to 1.199 s: still short of the poll sleeping since 0.8 s wakes at 1.2 s
        Assert.True(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));
        h.Main.AdvanceMilliseconds(1); // the poll at 1.2 s finds no merge running; the wait ends
        Assert.False(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued)); // cleared on every exit (:90 defer)

        // The rest of the merge is real git IO, pumped the same way every other test in this file drives it.
        h.Main.Loop.RunUntil(() => task.IsCompleted, AppModelHarness.Bound);

        Assert.False(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));
        Assert.False(h.Main.OnMain(() => waitingRuntime.IsHydraMerging));
        // AlreadyOnMain on the only group with no stray files settles the job (PR-CHAT-189, :184-186).
        var record = Assert.Single(h.Main.OnMain(() => waitingRuntime.HydraMerges.ToArray()));
        Assert.Equal(HydraMergeRecord.Outcome.Settled, record.HydraMergeRecordOutcome);
        Assert.Contains(h.Main.OnMain(() => waitingRuntime.Turns.ToArray()), candidate => candidate.Id == turn.Id && candidate.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-122")] // AppModel+HydraMerge.swift:89-95 (B3, new): isHydraMergeQueued is
    // cleared on every exit from the wait, including cancellation, and a cancelled wait leaves no record or note.
    public void Cancelling_the_wait_clears_the_queued_flag_and_leaves_no_record_or_note()
    {
        using var h = new AppModelHarness();
        var (_, project) = QueuedMergeProject(h, "queued-cancel-project");
        var busy = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var waiting = ReadyLead(project, "a.txt", out var turn);
        h.WriteLibrary([project], [busy, waiting]);
        AppModelHarness.WriteDocument(busy.Id);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(waiting.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(waiting.Id));

        var model = h.MakeModel();
        var waitingRuntime = h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
            model.Runtime(busy.Id).IsHydraMerging = true;
            return model.Runtime(waiting.Id);
        });
        h.Main.Loop.Run(() => waitingRuntime.EnsureLoaded(), AppModelHarness.Bound); // loads its turn (:2916)

        // A plain call has no ambient Swift task to cancel (SwiftTask.IsCancelled is false outside any task, as
        // in Swift): MainActor.Spawn gives the waiting job the task identity a real trigger site would.
        var handle = h.Main.OnMain(() => MainActor.Spawn(() => model.AutoMergeHydraWork(waiting.Id)));

        Assert.True(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));
        h.Main.Advance(TimeSpan.FromMilliseconds(800)); // two polls; the other merge is still running
        Assert.True(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued));

        h.Main.OnMain(() => handle.Cancel());

        Assert.True(handle.Completion.IsCompleted); // :93 returns at once; no suspension needed for that check
        Assert.False(h.Main.OnMain(() => waitingRuntime.IsHydraMergeQueued)); // cleared on this exit too (:90 defer)
        Assert.False(h.Main.OnMain(() => waitingRuntime.IsHydraMerging));
        Assert.Empty(h.Main.OnMain(() => waitingRuntime.HydraMerges.ToArray()));
        Assert.DoesNotContain(h.Main.OnMain(() => waitingRuntime.Turns.ToArray()), candidate => candidate.Id == turn.Id && candidate.HydraMerged);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-123")] // AppModel+HydraMerge.swift:171-174 (B3, new): after a run with no failed
    // or deferred project, a task is scheduled -- fire-and-forget, before the turns and heads are marked merged
    // -- that awaits resumeHydraMerges(), so a lead this run put off or that was waiting goes now.
    public void A_clean_run_schedules_the_resume_sweep_which_picks_up_a_waiting_lead_before_the_next_periodic_sweep()
    {
        using var h = new AppModelHarness();
        var (_, moverProject) = QueuedMergeProject(h, "resume-sweep-mover-project");
        // `mover`'s pass returns AlreadyOnMain (:308) with no network: it is the clean run that must schedule the
        // sweep. `other`'s project has no remote (PR-HYDRAMERGE-010's missing-remote fail, real and fast, no
        // network), so its only path into a record is that scheduled sweep -- never mover's own call, which only
        // ever touches mover's own lead and project.
        var otherRepository = AppModelGitRepo.WithCommit(h.NewFolder("resume-sweep-other-project"));
        var otherProject = AppModelSamples.Project("Other project", otherRepository.Root);
        var mover = ReadyLead(moverProject, "a.txt", out var moverTurn);
        var other = ReadyLead(otherProject, "a.txt", out var otherTurn);
        h.WriteLibrary([moverProject, otherProject], [mover, other]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(mover.Id) { Turns = new ValueList<TurnRecord>([moverTurn]) },
            Storage.ThreadURL(mover.Id));
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(other.Id) { Turns = new ValueList<TurnRecord>([otherTurn]) },
            Storage.ThreadURL(other.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });
        h.Main.Loop.Run(() => model.Runtime(mover.Id).EnsureLoaded(), AppModelHarness.Bound); // loads its turn (:2916)

        // Only mover's own pass is invoked directly; `other` is reachable only through the scheduled sweep.
        h.Main.Loop.Run(() => model.AutoMergeHydraWork(mover.Id), AppModelHarness.Bound);
        h.Main.Loop.RunUntil(() => model.ExistingRuntime(other.Id) is { } o && o.HydraMerges.Count > 0, AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var moverRuntime = model.ExistingRuntime(mover.Id);
            var otherRuntime = model.ExistingRuntime(other.Id);
            Assert.NotNull(moverRuntime);
            Assert.NotNull(otherRuntime);
            return (Mover: moverRuntime!, Other: otherRuntime!);
        });

        // AlreadyOnMain on mover's only group with no stray files settles the job (PR-CHAT-189, :184-186).
        var moverRecord = Assert.Single(state.Mover.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Settled, moverRecord.HydraMergeRecordOutcome);
        var otherRecord = Assert.Single(state.Other.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Failed, otherRecord.HydraMergeRecordOutcome); // no remote (:242)
        Assert.Equal("Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", otherRecord.Detail);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-123")] // AppModel+HydraMerge.swift:171 (B3, new): `guard !outcomes.contains(.failed),
    // !outcomes.contains(.deferred) else { return }` -- a deferred run schedules nothing, so a lead that would
    // otherwise be picked up by the sweep is left untouched.
    public void A_deferred_run_schedules_no_resume_sweep()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("no-sweep-project"));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");
        // A rebase left in progress: MergeHydraProject returns .deferred at :248 before ever reaching the
        // outcome guard's caller-side effects.
        repository.Git("checkout", "-q", "-b", "side");
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("side\n"));
        repository.Git("add", "a.txt");
        repository.Git("commit", "-q", "-m", "side");
        repository.Git("checkout", "-q", "main");
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("main\n"));
        repository.Git("add", "a.txt");
        repository.Git("commit", "-q", "-m", "main");
        Assert.False(repository.Raw("merge", "side").Succeeded);

        var project = AppModelSamples.Project("Project", repository.Root);
        var deferring = ReadyLead(project, "a.txt", out var deferringTurn);
        var other = ReadyLead(project, "b.txt", out var otherTurn); // has its own work; no remote of its own
        h.WriteLibrary([project], [deferring, other]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(deferring.Id) { Turns = new ValueList<TurnRecord>([deferringTurn]) },
            Storage.ThreadURL(deferring.Id));
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(other.Id) { Turns = new ValueList<TurnRecord>([otherTurn]) },
            Storage.ThreadURL(other.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });
        h.Main.Loop.Run(() => model.Runtime(deferring.Id).EnsureLoaded(), AppModelHarness.Bound); // loads its turn

        h.Main.Loop.Run(() => model.AutoMergeHydraWork(deferring.Id), AppModelHarness.Bound);
        h.Main.Loop.RunUntilIdle(); // drains anything already queued; nothing was scheduled to drain

        // A sweep that ran would have created and loaded `other`'s runtime (resumeHydraMerges :54-58); a sweep
        // that was never scheduled never touches it, so it stays entirely absent.
        Assert.Null(h.Main.OnMain(() => model.ExistingRuntime(other.Id)));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-031")] // AppModel+HydraMerge.swift:231-241
    public void Conflict_markers_stop_the_merge_and_the_record_omits_the_note_resolution_sentence()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("conflict-markers"));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");
        File.WriteAllBytes(
            Path.Combine(repository.Root, "a.txt"),
            Encoding.UTF8.GetBytes("before\n<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n"));

        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var result = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var messages = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Select(content => content.Value);
            var note = Assert.Single(messages, message => message.HydraHeads is { Count: 0 });
            return (Record: Assert.Single(runtime.HydraMerges), Note: note);
        });

        Assert.Equal(HydraMergeRecord.Outcome.Failed, result.Record.HydraMergeRecordOutcome);
        Assert.Equal(0, result.Record.Files);
        // :259 (B3): the record's detail and the note's body are both the full sentence, resolution text included
        // ("Resolve them and the merge goes out by itself."), not the shorter B2 wording ("Resolve them, then ask
        // for the merge again.") that stayed only in the note there.
        Assert.Equal(
            "Hydra did not merge: conflict markers in a file.: `a.txt` still holds conflict markers from a head's landing. Resolve them and the merge goes out by itself.",
            result.Record.Detail);
        Assert.Equal(
            "Hydra did not merge: conflict markers in a file.\n`a.txt` still holds conflict markers from a head's landing. Resolve them and the merge goes out by itself.",
            result.Note.Text);
        Assert.Equal("main", repository.Git("branch", "--show-current").Trim());
        Assert.Null(result.Record.Retryable); // :259 not retryable: fail(headline, body) with no retryable argument
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-033")] // AppModel+HydraMerge.swift:243-263
    public void A_team_tree_equal_to_HEAD_does_not_move_the_checkout_branch()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("tree-already-on-head"));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");
        var originalHead = repository.Git("rev-parse", "HEAD").Trim();

        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        Assert.Equal(originalHead, repository.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("main", repository.Git("branch", "--show-current").Trim());
        Assert.Empty(repository.Git("branch", "--list", "hydra/*").Trim());
    }

    [Fact]
[Trait("Req", "PR-HYDRAMERGE-059")] // AppModel+HydraMerge.swift:327, 853-857
public void Files_line_for_zero_files_keeps_both_zero_totals()
{
    var method = typeof(AppModel).GetMethod("FilesLine", BindingFlags.Static | BindingFlags.NonPublic);
    Assert.NotNull(method);
    var elementType = method!.GetParameters()[0].ParameterType.GetGenericArguments()[0];
    var emptyFiles = Array.CreateInstance(elementType, 0);

    var line = Assert.IsType<string>(method.Invoke(null, [emptyFiles]));

    Assert.Equal("0 files (+0 −0)", line);
}

[Fact]
[Trait("Req", "PR-HYDRAMERGE-063")] // AppModel+HydraMerge.swift:722-748
public async Task Sync_default_branch_fast_forwards_and_checks_out_remote_changes()
{
    using var h = new AppModelHarness();
    var repository = AppModelGitRepo.WithCommit(h.NewFolder("sync-project"));
    var bare = h.NewFolder("sync-remote.git");
    // -b main, exactly like AppModelGitRepo.Empty's own checkout: without it, the bare repo's initial branch (and
    // so its HEAD) follows the host's init.defaultbranch config (this machine: "master"), which git does not
    // retarget to "main" just because "main" is what gets pushed to it. That left HEAD at refs/heads/master (an
    // unborn ref no push ever touches), so the clone below warned "remote HEAD refers to nonexistent ref, unable
    // to checkout" and landed on an empty local "master" - not "main" - branch. The later `push origin main` from
    // that clone then failed deterministically with "src refspec main does not match any" (reproduced 4/4, no
    // load needed): every commit made on the clone lands on "master", so no local "main" ever exists to push.
    // Reproduced directly against this host's git (2.54.0.windows.1, init.defaultbranch=master) before this fix.
    repository.Git("init", "--bare", "-b", "main", bare);
    repository.Git("remote", "add", "origin", bare);
    repository.Git("push", "-u", "origin", "main");
    var head = repository.Git("rev-parse", "HEAD").Trim();

    var clone = h.NewFolder("sync-clone");
    repository.Git("clone", bare, clone);
    var remote = AppModelGitRepo.At(clone);
    remote.Git("config", "user.name", "Port Test");
    remote.Git("config", "user.email", "port-test@localhost");
    File.WriteAllBytes(
        SwiftPath.AppendingPathComponent(clone, "remote.txt"),
        Encoding.UTF8.GetBytes("remote\n"));
    remote.Git("add", "remote.txt");
    remote.Git("commit", "-q", "-m", "remote change");
    remote.Git("push", "origin", "main");

    var synced = await InvokeSyncDefaultBranch(
        new Marrow.Services.Git.Git(repository.Root), head, "main", Array.Empty<string>());

    Assert.True(synced);
    Assert.Equal(remote.Git("rev-parse", "HEAD").Trim(), repository.Git("rev-parse", "HEAD").Trim());
    Assert.Equal("remote\n", Encoding.UTF8.GetString(File.ReadAllBytes(
        SwiftPath.AppendingPathComponent(repository.Root, "remote.txt"))));
}

[Fact]
[Trait("Req", "PR-HYDRAMERGE-074")] // AppModel+HydraMerge.swift:545-553
public void Reported_paths_drop_empty_and_build_output_and_keep_inside_paths_relative()
{
    Assert.Null(TouchedPaths.Relative("", @"C:\repo"));
    // IsBuildOutput (:40-44) splits on '/': its callers always pass an already-normalized path (Normalize
    // replaces '\' with '/'), never a raw Windows path.
    Assert.True(TouchedPaths.IsBuildOutput(".build/output.txt"));
    // Relative (:15-29) normalizes to forward slashes (Normalize replaces '\' with '/').
    Assert.Equal("src/file.txt", TouchedPaths.Relative(@"C:\repo\src\file.txt", @"C:\repo"));
}

        [Fact]
    [Trait("Req", "PR-HYDRAMERGE-084")] // AppModel+HydraMerge.swift:860-867
    public void Merge_note_appends_a_user_message_under_the_reserved_id_and_clears_it()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("note-project"));
        var lead = AppModelSamples.Thread(project) with
        {
            HydraEnabled = true,
            UpdatedAt = SwiftDate.Now - TimeSpan.FromMinutes(1),
        };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var observed = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            runtime!.HydraMergeNoteID = "merge-note-id";

            var method = typeof(AppModel).GetMethod("Note", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(model, [lead.Id, "Hydra has no files left to merge.", "The checkout has no remaining changes from this job to send. No new merge request was needed."]);

            var entry = Assert.Single(runtime.Entries);
            var user = Assert.IsType<TimelineItem.Content.User>(entry.Item.TimelineItemContent);
            return (
                ItemID: entry.Item.Id,
                TurnID: entry.TurnID,
                Message: user.Value,
                NoteID: runtime.HydraMergeNoteID,
                UpdatedAt: model.Thread(lead.Id)!.UpdatedAt);
        });

        Assert.Equal("merge-note-id", observed.ItemID);
        Assert.Null(observed.TurnID);
        Assert.Equal("Hydra has no files left to merge.\nThe checkout has no remaining changes from this job to send. No new merge request was needed.", observed.Message.Text);
        Assert.Empty(observed.Message.HydraHeads!);
        Assert.Null(observed.NoteID);
        Assert.Equal(SwiftDate.Now, observed.UpdatedAt);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-119")] // AppModel+HydraMerge.swift:686-694
    public void Pending_landing_is_false_when_the_lead_has_no_team_heads()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("pending-landing-project"));
        var lead = AppModelSamples.Thread(project);
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();

        var method = typeof(AppModel).GetMethod("HydraHasPendingLanding", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var pending = h.Main.OnMain(() => Assert.IsType<bool>(method!.Invoke(model, [lead.Id])));

        Assert.False(pending);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-118")] // AppModel+HydraMerge.swift:674-684
    public void Finished_idle_eligibility_is_false_without_a_live_runtime()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("eligibility-project"));
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();

        var method = typeof(AppModel).GetMethod("HydraIsFinishedIdleEligible", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var eligible = h.Main.OnMain(() => Assert.IsType<bool>(method!.Invoke(model, [lead])));

        Assert.False(eligible);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-120")] // AppModel+HydraMerge.swift:696-701
    public void Competing_jobs_are_ordered_by_the_lead_uuid_string()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("uuid-order-project"));
        var earlier = AppModelSamples.Thread(project) with { Id = new Guid("00000000-0000-0000-0000-000000000001") };
        var later = AppModelSamples.Thread(project) with { Id = new Guid("00000000-0000-0000-0000-000000000002") };
        h.WriteLibrary([project], [earlier, later]);
        var model = h.MakeModel();

        var method = typeof(AppModel).GetMethod("HydraRanksAfter", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var results = h.Main.OnMain(() => (
            LaterAfterEarlier: Assert.IsType<bool>(method!.Invoke(model, [later, earlier.Id])),
            EarlierAfterLater: Assert.IsType<bool>(method.Invoke(model, [earlier, later.Id]))));

        Assert.True(results.LaterAfterEarlier);
        Assert.False(results.EarlierAfterLater);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-121")] // AppModel+HydraMerge.swift:703-715
    public void Archived_lead_without_an_active_runtime_cannot_still_merge()
    {
        using var h = new AppModelHarness();
        var project = AppModelSamples.Project("Project", h.NewFolder("archived-merge-project"));
        var lead = AppModelSamples.Thread(project) with { IsArchived = true, HydraEnabled = true };
        h.WriteLibrary([project], [lead]);
        var model = h.MakeModel();

        var method = typeof(AppModel).GetMethod("CanStillMerge", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var canStillMerge = h.Main.OnMain(() => Assert.IsType<bool>(method!.Invoke(model, [lead])));

        Assert.False(canStillMerge);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-127")] // AppModel+HydraMerge.swift:336-339 (B3): ownBranch moves refs/heads/<branch>
    // with the two-argument UpdateRef (Git.cs:644), not the compare-and-swap overload with an empty "must not
    // exist" old value.
    [Trait("Req", "PR-HYDRAMERGE-128")] // AppModel+HydraMerge.swift:348 (B3): pushBranch(branch, force: ownBranch).
    public void Second_own_branch_merge_reuses_and_force_pushes_the_existing_hydra_branch()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("own-branch-project"));
        var bare = h.NewFolder("own-branch-remote.git");
        repository.Git("init", "--bare", "-b", "main", bare);
        repository.Git("remote", "add", "origin", bare);
        repository.Git("push", "-u", "origin", "main");
        // The scripted, offline tea.exe (Forge() reads a plain bare path as Gitea) answers every call with success
        // and no output, so HydraCreateMergeRequest always returns null: the merge fails at "could not open a merge
        // request" every time, never reaching a real forge. That is what both attempts below must fail with; a
        // "reference already exists" (the :293 bug this test targets) or a rejected non-fast-forward push (the
        // missing --force at :300) would instead surface as the generic catch-all's "Hydra could not merge the
        // team's work" at :407.
        Marrow.Services.Tests.Fixtures.ForgeStandIns.Ensure();

        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("changed once\n"));
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);
        // Changed again before the retry, so the two attempts build genuinely different commits (never a trivial
        // no-op push) regardless of whether they land in the same wall-clock second.
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("changed twice\n"));
        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var branches = repository.Git("branch", "--list", "hydra/*")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // The second try moves the existing hydra/<slug>-<id6> ref rather than failing to create a sibling one.
        Assert.Single(branches);

        var records = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            return runtime!.HydraMerges.ToArray();
        });

        // Both attempts push and fail to open a request with the exact same detail (the branch name is stable, so
        // the message never changes), so fail()'s own dedup (:230) collapses them to one record. With either bug
        // present, the second attempt instead throws (a locked ref, or a rejected non-fast-forward push) and is
        // caught by the generic catch-all at :407, whose distinct "Hydra could not merge the team's work" detail
        // does not match the first record and so is NOT deduplicated, leaving two records behind.
        Assert.Single(records);
        Assert.All(records, record =>
        {
            Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
            Assert.StartsWith("Hydra pushed hydra/", record.Detail, StringComparison.Ordinal);
            Assert.True(record.Retryable);
        });
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-113")] // AppModel+HydraMerge.swift:183 (B3): "Hydra left <N> files outside every
    // project." for more than one stray file (was "Hydra left files outside every project.", missing the count).
    public void Stray_note_title_after_a_successful_merge_uses_the_files_count_for_plural()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("stray-plural-project"));
        repository.Git("remote", "add", "origin", "https://example.invalid/group/project.git");
        var originalHead = repository.Git("rev-parse", "HEAD").Trim();

        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        // a.txt is reported but unchanged from HEAD, so its group's MergeHydraProject returns AlreadyOnMain
        // (:308) without ever touching the network; the two absolute paths outside the checkout and outside
        // every scratch root (:194-209) and every project stay in `outside`, becoming the stray group (:143).
        var strayRoot = @"C:\marrow-hydra-merge-stray-test\" + Guid.NewGuid().ToString("N");
        var stray1 = strayRoot + @"\stray-1.txt";
        var stray2 = strayRoot + @"\stray-2.txt";
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt", stray1, stray2]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));

        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var messages = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Select(content => content.Value);
            return (Records: runtime.HydraMerges.ToArray(), Messages: messages.ToArray());
        });

        // AlreadyOnMain on the only group plus a non-empty stray list takes the :181-183 branch, not :184-186.
        var record = Assert.Single(state.Records);
        Assert.Equal(HydraMergeRecord.Outcome.Stray, record.HydraMergeRecordOutcome);
        Assert.Equal(2, record.Files);
        var note = Assert.Single(state.Messages, message => message.HydraHeads is { Count: 0 });
        Assert.StartsWith("Hydra left 2 files outside every project.\n", note.Text, StringComparison.Ordinal);
        Assert.Equal(originalHead, repository.Git("rev-parse", "HEAD").Trim());
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-029")] // AppModel+HydraMerge.swift:226-236 (B3): fail() de-duplicates against the
    // last record of the same outcome and detail before recording or noting again.
    public void Repeated_identical_failure_is_recorded_and_noted_only_once()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("dedup-project"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        // No remote: every attempt hits the same non-retryable fail("Hydra did not merge: no remote.", ...) with
        // the exact same detail, so a second run must not add a second record or a second note (:230).
        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);
        h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

        var state = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var messages = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Where(content => content.Value.HydraHeads is { Count: 0 })
                .ToArray();
            return (Records: runtime.HydraMerges.ToArray(), Messages: messages);
        });

        Assert.Single(state.Records);
        Assert.Single(state.Messages);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-125")] // AppModel+HydraMerge.swift:226-237, :407: a retryable failure whose detail
    // changes from try to try (the last-record short-circuit at :230 never matches) is recorded every time but
    // announced only once the two most recent records are both failed and retryable -- silent on the first two,
    // said from the third.
    public void Retryable_failure_with_a_changing_detail_speaks_only_from_its_third_record()
    {
        using var h = new AppModelHarness();
        var repository = AppModelGitRepo.WithCommit(h.NewFolder("retry-dedup-project"));
        // Left uncommitted for every attempt below: with a.txt unchanged from HEAD the pass would return
        // AlreadyOnMain (:308) before ever reaching PushBranch, never exercising fail() at all.
        File.WriteAllBytes(Path.Combine(repository.Root, "a.txt"), Encoding.UTF8.GetBytes("the team's edit\n"));
        var project = AppModelSamples.Project("Project", repository.Root);
        var lead = AppModelSamples.Thread(project) with { HydraEnabled = true, UpdatedAt = SwiftDate.Now };
        var turn = new TurnRecord
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Status = TurnStatus.Completed,
            CompletedAt = SwiftDate.Now,
            TouchedPaths = new ValueList<string>(["a.txt"]),
        };
        h.WriteLibrary([project], [lead]);
        DiskWriter.Shared.WriteSynchronously(
            new ThreadDocument(lead.Id) { Turns = new ValueList<TurnRecord>([turn]) },
            Storage.ThreadURL(lead.Id));
        var model = h.MakeModel();
        h.Main.OnMain(() =>
        {
            model.Settings.HydraEnabled = true;
            model.Settings.HydraAutoMerge = true;
        });

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            // A different unreachable "remote" each time, so `git push` fails at the transport level (before any
            // refspec is resolved) with a different message each attempt -- the generic catch at :407 fires with
            // retryable: true, and the detail text differs, so the identical-last-record short-circuit at :230
            // never suppresses it. The probe push is byte-for-byte the same command shape as the production
            // PushBranch(force: true) call, so its FailureMessage is what the real attempt will also produce.
            var unreachable = h.NewFolder($"unreachable-remote-{attempt}");
            repository.Git("remote", attempt == 1 ? "add" : "set-url", "origin", unreachable);
            var probe = repository.Raw(
                "push", "-u", "--force", "origin", "HEAD:refs/heads/__hydramerge_probe__");
            Assert.False(probe.Succeeded);
            var expectedError = probe.FailureMessage;

            h.Main.Loop.Run(() => InvokeResumeHydraMerges(model), AppModelHarness.Bound);

            var state = h.Main.OnMain(() =>
            {
                var runtime = model.ExistingRuntime(lead.Id);
                Assert.NotNull(runtime);
                var notes = runtime!.Entries
                    .Select(entry => entry.Item.TimelineItemContent)
                    .OfType<TimelineItem.Content.User>()
                    .Where(content => content.Value.HydraHeads is { Count: 0 })
                    .ToArray();
                return (Records: runtime.HydraMerges.ToArray(), Notes: notes);
            });

            Assert.Equal(attempt, state.Records.Length);
            var record = state.Records[^1];
            Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
            Assert.Equal(true, record.Retryable);
            Assert.Equal("Project", record.Project);
            Assert.Equal(0, record.Files);
            Assert.Equal(
                "Hydra could not merge the team's work.: " + expectedError +
                "\n\nThe work is in the checkout; the next try sends it again.",
                record.Detail);

            if (attempt < 3)
            {
                Assert.Empty(state.Notes);
            }
            else
            {
                var note = Assert.Single(state.Notes);
                Assert.Equal(
                    "Hydra could not merge the team's work.\n" + expectedError +
                    "\n\nThe work is in the checkout; the next try sends it again.",
                    note.Value.Text);
            }
        }
    }

    // UNRESOLVED: PR-HYDRAMERGE-077 and PR-HYDRAMERGE-078 need a controlled sibling-claim fixture, including tracked/deleted paths and an injected git cat-file stream failure. The supplied fixtures do not expose the required git-command seam.
    // UNRESOLVED: PR-HYDRAMERGE-085 needs a safe, controllable application-foreground/notification seam; the supplied material does not identify one.
    // UNRESOLVED: PR-HYDRAMERGE-111 needs a controllable in-flight Hydra landing and a way to change readiness while it is awaited; the supplied fixtures do not expose either.
    // UNRESOLVED: PR-HYDRAMERGE-112 and PR-HYDRAMERGE-117 need representative competing leads, head landing records, and controllable runtime readiness to exercise blocking and ranked-out claims. The packet does not provide enough model construction details to build those states safely.
    // UNRESOLVED: PR-HYDRAMERGE-116 needs a controlled tree-capture/claim-change seam to verify the post-snapshot checks without relying on timing or external processes.
    // UNRESOLVED: UI-CHAT-083, UI-CHAT-084, UI-HYDRAMERGE-035, and UI-HYDRAMERGE-036 need the actual timeline View/Control test harness to verify pill rendering and interactions.
    // UNRESOLVED: UI-HYDRAMERGE-032 needs the platform notification test seam to verify notification delivery and targeting.
    // UNRESOLVED: UI-HYDRAMERGE-033 needs the platform badge test seam to verify taskbar badge updates.
    // UNRESOLVED: UI-HYDRAMERGE-034 needs a known DiffFile construction fixture and a forge-body assertion seam; neither is supplied here.
}
