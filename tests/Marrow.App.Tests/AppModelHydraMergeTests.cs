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

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-115")] // AppModel+HydraMerge.swift:846-850
    public void Held_detail_sorts_the_distinct_names_and_paths_and_shows_only_six_paths()
    {
        var held = new Dictionary<string, string>
        {
            [@"C:\repo\file-07"] = "Zulu",
            [@"C:\repo\file-03"] = "Alpha",
            [@"C:\repo\file-01"] = "Zulu",
            [@"C:\repo\file-06"] = "Alpha",
            [@"C:\repo\file-02"] = "Alpha",
            [@"C:\repo\file-05"] = "Zulu",
            [@"C:\repo\file-04"] = "Alpha",
        };

        var detail = InvokeHeldDetail(held);

        Assert.Equal(
            "7 files the team changed are also claimed by Alpha, Zulu: `C:\\repo\\file-01`, `C:\\repo\\file-02`, `C:\\repo\\file-03`, `C:\\repo\\file-04`, `C:\\repo\\file-05`, `C:\\repo\\file-06` and 1 more.",
            detail);
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

    private static string InvokeHeldDetail(IReadOnlyDictionary<string, string> held)
    {
        var method = typeof(AppModel).GetMethod("HeldDetail", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, [held]));
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
        var result = method!.Invoke(null, [git, head, target, ownPaths]);
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

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-115")] // AppModel+HydraMerge.swift:846-850
    public void Held_detail_uses_singular_wording_for_one_path()
    {
        var detail = InvokeHeldDetail(new Dictionary<string, string> { [@"C:\repo\one.txt"] = "Another chat" });

        Assert.Equal("1 file the team changed is also claimed by Another chat: \u0060C:\\repo\\one.txt\u0060.", detail);
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-113")] // AppModel+HydraMerge.swift:829-833
    [Trait("Req", "PR-HYDRAMERGE-114")] // AppModel+HydraMerge.swift:835-841
    [Trait("Req", "PR-CHAT-189")] // AppModel+HydraMerge.swift:835-841: recordHydraHold writes once per distinct
    // holding detail (the guard last?.outcome != .held || last?.detail != detail), which this test's two
    // recordHold calls with the same `first` dictionary already prove by observing only one Held record for them.
    public void Hold_records_deduplicate_by_exact_detail_and_settlement_waits_for_no_unmerged_files()
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
            TouchedPaths = ValueList<string>.Empty,
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

        var observed = h.Main.OnMain(() =>
        {
            var runtime = model.ExistingRuntime(lead.Id);
            Assert.NotNull(runtime);
            var recordHold = typeof(AppModel).GetMethod("RecordHydraHold", BindingFlags.Instance | BindingFlags.NonPublic);
            var recordSettlement = typeof(AppModel).GetMethod("RecordHydraSettlement", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(recordHold);
            Assert.NotNull(recordSettlement);
            var first = new Dictionary<string, string> { [@"C:\repo\a.txt"] = "Zulu" };
            var changed = new Dictionary<string, string> { [@"C:\repo\a.txt"] = "Alpha" };

            recordHold!.Invoke(model, [first, lead.Id, runtime]);
            recordHold.Invoke(model, [first, lead.Id, runtime]);
            recordHold.Invoke(model, [changed, lead.Id, runtime]);
            recordSettlement!.Invoke(model, [lead.Id, runtime]);

            var notes = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Where(content => content.Value.HydraHeads is { Count: 0 })
                .Select(content => content.Value.Text)
                .ToArray();
            return (Records: runtime.HydraMerges.ToArray(), Notes: notes);
        });

        Assert.Collection(
            observed.Records,
            first =>
            {
                Assert.Equal(HydraMergeRecord.Outcome.Held, first.HydraMergeRecordOutcome);
                Assert.Equal(1, first.Files);
                Assert.Null(first.Project);
                Assert.Null(first.Label);
                Assert.Null(first.Url);
                Assert.Equal("1 file the team changed is also claimed by Zulu: \u0060C:\\repo\\a.txt\u0060.", first.Detail);
            },
            second =>
            {
                Assert.Equal(HydraMergeRecord.Outcome.Held, second.HydraMergeRecordOutcome);
                Assert.Equal(1, second.Files);
                Assert.Equal("1 file the team changed is also claimed by Alpha: \u0060C:\\repo\\a.txt\u0060.", second.Detail);
            },
            settled =>
            {
                Assert.Equal(HydraMergeRecord.Outcome.Settled, settled.HydraMergeRecordOutcome);
                Assert.Equal(0, settled.Files);
                Assert.Null(settled.Project);
                Assert.Null(settled.Label);
                Assert.Null(settled.Url);
                Assert.Null(settled.Detail);
            });
        Assert.Equal(
            [
                "Hydra is waiting for another chat's files.\n1 file the team changed is also claimed by Zulu: \u0060C:\\repo\\a.txt\u0060. " +
                "The work stays in the checkout. Hydra retries after the other chat finishes or its queued merge completes.",
                "Hydra is waiting for another chat's files.\n1 file the team changed is also claimed by Alpha: \u0060C:\\repo\\a.txt\u0060. " +
                "The work stays in the checkout. Hydra retries after the other chat finishes or its queued merge completes.",
                "Hydra has no files left to merge.\nThe checkout has no remaining changes from this job to send. No new merge request was needed.",
            ],
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
                    new Dictionary<string, string>(),
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
    [Trait("Req", "PR-HYDRAMERGE-030")] // AppModel+HydraMerge.swift:225-229
    public void An_in_progress_merge_records_the_specific_failure_and_note()
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
            var messages = runtime!.Entries
                .Select(entry => entry.Item.TimelineItemContent)
                .OfType<TimelineItem.Content.User>()
                .Select(content => content.Value);
            var note = Assert.Single(messages, message => message.HydraHeads is { Count: 0 });
            return (Record: Assert.Single(runtime.HydraMerges), Note: note);
        });

        Assert.Equal(HydraMergeRecord.Outcome.Failed, result.Record.HydraMergeRecordOutcome);
        Assert.Equal("Project", result.Record.Project);
        Assert.Equal(0, result.Record.Files);
        Assert.Equal(
            "Hydra did not merge: a rebase or merge is underway.: The team's work is in the checkout; finish that first and merge by hand.",
            result.Record.Detail);
        Assert.Equal(
            "Hydra did not merge: a rebase or merge is underway.\nThe team's work is in the checkout; finish that first and merge by hand.",
            result.Note.Text);
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
        Assert.Equal(
            "Hydra did not merge: conflict markers in a file.: `a.txt` still holds conflict markers from a head's landing.",
            result.Record.Detail);
        Assert.Equal(
            "Hydra did not merge: conflict markers in a file.\n`a.txt` still holds conflict markers from a head's landing. Resolve them, then ask for the merge again.",
            result.Note.Text);
        Assert.Equal("main", repository.Git("branch", "--show-current").Trim());
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
