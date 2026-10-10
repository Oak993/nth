using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :350-372, :433-494, :844-869 (the merge request body,
// how it is opened and merged). The forge CLIs are the offline stand-ins of tests/fixtures/forge-cli; no network is reached.
public sealed class AppModelHydraMergeForgeTests
{
    private const string Opened = "Opened by Nth once its Hydra team finished.";
    private const string LongTitle = "Rework the Hydra merge pipeline so that every long chat title is cut at seventy-two characters";

    // One Swift Character made of two UTF-16 units: e and a combining acute accent.
    private static readonly string Cluster = "e" + (char)0x0301;

    private static string Join(IEnumerable<string> lines) => string.Join((char)10, lines);

    private static string ForgeUrl(string forge) => forge switch
    {
        "gitlab" => "https://gitlab.example/g/p/-/merge_requests/7",
        "github" => "https://github.example/o/r/pull/7",
        _ => HydraMergeSpec.RequestURL,
    };

    private static string Exe(string forge) => forge switch { "gitlab" => "glab.exe", "github" => "gh.exe", _ => "tea.exe" };

    // :384-385 / :441-450 / :182-191: what each forge is asked to create, in argument order.
    private static string[] CreateVector(string forge, string subject, string body, string branch) => forge switch
    {
        "gitlab" => ["mr", "create", "--title", subject, "--description", body, "--yes", "--squash-before-merge",
            "--source-branch", branch, "--target-branch", "main"],
        "github" => ["pr", "create", "--title", subject, "--body", body, "--head", branch, "--base", "main"],
        _ => ["pulls", "create", "--title", subject, "--description", body, "--head", branch, "--base", "main"],
    };

    private static string[] MergeVector(string forge) => forge switch
    {
        "gitlab" => ["mr", "merge", "7", "--yes", "--squash", "--remove-source-branch"],
        "github" => ["pr", "merge", "7", "--merge"],
        _ => ["pulls", "merge", "7"],
    };

    private static bool IsForge(ShellInvocation call) =>
        Path.GetFileNameWithoutExtension(call.Executable) is "glab" or "gh" or "tea";

    // The ForgeScript calls' replies for one forge: GitLab checks the sign-in first, so its create is the second call.
    private static ForgeScript Script(HydraMergeProofFixture f, Project p, string forge)
    {
        var script = new ForgeScript(p.Path);
        script.Reply(forge == "gitlab" ? 1 : 0, stdout: ForgeUrl(forge));
        return script;
    }

    [Theory]
    [InlineData("gitlab")]
    [InlineData("github")]
    [InlineData("gitea")]
    [Trait("Req", "PR-HYDRAMERGE-047")] // AppModel+HydraMerge.swift:354, :434-435
    [Trait("Req", "PR-HYDRAMERGE-048")] // AppModel+HydraMerge.swift:441-450 (no remote web page: no --repo)
    [Trait("Req", "PR-HYDRAMERGE-057")] // AppModel+HydraMerge.swift:367-369; Git.swift:210-227
    [Trait("Req", "UI-HYDRAMERGE-034")] // AppModel+HydraMerge.swift:334, :775-798 (title and description on the forge)
    public void Each_forge_is_asked_to_create_and_merge_the_request_with_its_own_cli_vector(string forge)
    {
        using var f = new HydraMergeProofFixture();
        // The origin's own path names the forge: Git.forge() reads it off the remote URL (:224-226).
        var p = f.AddProject(forge == "gitea" ? "Project" : forge + "-project");
        var lead = f.AddLead(p, LongTitle, ["a.txt"]);
        f.AddHead(lead, ["a.txt"], index: 1, task: "Parser fix");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        HydraMergeSpec.AddAssistant(f, lead, f.Documents[lead.Id].Turns[0].Id, "All done.\n");
        f.Start(lead);
        var script = Script(f, p, forge);
        var trail = new List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)>();

        f.Run(lead, HydraMergeSpec.Trail(f, lead, trail));

        var branch = f.Branch(lead);
        // :334 the subject is the commit message, single-lined to 72 characters: the title's first 71 and an ellipsis.
        var subject = LongTitle[..71] + "…";
        Assert.Equal(72, subject.Length);
        var body = string.Join("\n", Opened, "", "## Heads", "- **Walter**: Parser fix (1 file)", "", "## The lead's summary", "All done.", "",
            "## Files", "- `a.txt` (+1 −1)");
        var calls = script.Calls();
        var createAt = forge == "gitlab" ? 1 : 0;
        Assert.Equal(forge == "gitlab" ? 3 : 2, calls.Count);
        Assert.All(calls, call => Assert.Equal(Exe(forge), call.Exe));
        Assert.All(calls, call => Assert.Equal(p.Path, call.WorkingDirectory));
        // GitLab with glab on the path goes through the wrapped path, which checks the sign-in first (:454); the others go straight.
        if (forge == "gitlab") Assert.Equal(["auth", "status"], calls[0].Arguments);
        Assert.Equal(CreateVector(forge, subject, body, branch), calls[createAt].Arguments);
        Assert.Equal(MergeVector(forge), calls[createAt + 1].Arguments);
        // The description is what colleagues read: it opens with the ruled line (OD-0008 B10) and never names Droppy Code.
        Assert.StartsWith(Opened + "\n\n", calls[createAt].Arguments.Single(a => a.StartsWith(Opened, StringComparison.Ordinal)), StringComparison.Ordinal);
        // Every forge call has its time budget: 300 seconds, and 30 for the sign-in check.
        var forgeCalls = trail.Where(entry => IsForge(entry.Call)).ToArray();
        Assert.Equal(calls.Count, forgeCalls.Length);
        Assert.Equal(forge == "gitlab" ? new[] { 30d, 300d, 300d } : new[] { 300d, 300d }, forgeCalls.Select(entry => entry.Call.Timeout));
        // The stage words while the request is opened, and while it is merged (raw values 4 and 5).
        Assert.All(forgeCalls.Take(forgeCalls.Length - 1), entry => Assert.Equal(("Opening the merge request", HydraMergeStage.Opening), (entry.Stage, entry.Step)));
        Assert.Equal(("Merging", HydraMergeStage.Merging), (forgeCalls[^1].Stage, forgeCalls[^1].Step));
        Assert.Equal((4, 5), ((int)HydraMergeStage.Opening, (int)HydraMergeStage.Merging));
        // The forge's own request merged: the note is titled with the label its URL gives.
        var label = forge == "gitlab" ? "!7" : "#7";
        Assert.Equal("Hydra merged " + label, HydraMergeSpec.Title(Assert.Single(HydraMergeSpec.NoteEntries(f, lead))));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-045")] // AppModel+HydraMerge.swift:350-351
    public void The_request_body_is_built_from_this_groups_turns_before_the_opening_stage()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var turn = f.Documents[lead.Id].Turns[0];
        // An earlier job's turn that a merge already took: its summary comes later in the timeline but is not this group's.
        var earlier = turn with { Id = Guid.NewGuid(), Index = 0, HydraMerged = true, TouchedPaths = ValueList<string>.Empty };
        f.Documents[lead.Id] = f.Documents[lead.Id] with { Turns = new ValueList<TurnRecord>([earlier, turn]) };
        HydraMergeSpec.AddAssistant(f, lead, turn.Id, "Fresh summary of the job");
        HydraMergeSpec.AddAssistant(f, lead, earlier.Id, "Stale summary of an earlier job");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var script = HydraMergeSpec.Forge(p.Path);
        var trail = new List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)>();

        f.Run(lead, HydraMergeSpec.Trail(f, lead, trail));

        var create = script.Calls()[0].Arguments;
        Assert.Equal(string.Join("\n", Opened, "", "## The lead's summary", "Fresh summary of the job", "", "## Files", "- `a.txt` (+1 −1)"),
            create[create.ToList().IndexOf("--description") + 1]);
        var opening = trail.First(entry => HydraMergeSpec.Verb(entry.Call) == "tea pulls create");
        Assert.Equal(("Opening the merge request", HydraMergeStage.Opening), (opening.Stage, opening.Step));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-046")] // AppModel+HydraMerge.swift:844-869
    [Trait("Req", "UI-HYDRAMERGE-034")] // AppModel+HydraMerge.swift:775-798 states: no heads, no reply, truncated reply, no files
    public void The_request_body_has_the_ruled_opening_line_a_heads_section_a_lead_summary_and_a_files_list()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var full = f.AddLead(p, "Full", []);
        var blank = f.AddLead(p, "Blank", []);
        var longer = f.AddLead(p, "Long", []);
        var exact = f.AddLead(p, "Exact", []);
        var combining = f.AddLead(p, "Combining", []);
        var cut = f.AddLead(p, "Cut", []);
        var bare = f.AddLead(p, "Bare", []);
        f.AddHead(full, ["x.txt", "y.txt"], index: 1, task: "Parser fix");
        f.AddHead(full, ["z.txt"], index: 2, task: "Docs");
        f.AddHead(full, [], index: 3, task: "  Multi\nline task  ");
        f.AddHead(full, ["unnamed.txt"], index: 4, task: "");
        f.AddHead(full, [], index: 5, task: new string('b', 120));
        f.AddHead(full, [], index: 6, task: new string('c', 121));
        var older = Guid.NewGuid();
        Guid Turn(ChatThread lead) => f.Documents[lead.Id].Turns[0].Id;
        HydraMergeSpec.AddAssistant(f, full, Turn(full), "first reply");
        HydraMergeSpec.AddAssistant(f, full, Turn(full), "\n  Final reply  \n");
        HydraMergeSpec.AddAssistant(f, full, older, "stale reply of a turn this group does not hold");
        HydraMergeSpec.AddAssistant(f, blank, Turn(blank), "earlier reply");
        HydraMergeSpec.AddAssistant(f, blank, Turn(blank), " \n ");
        HydraMergeSpec.AddAssistant(f, longer, Turn(longer), new string('x', 2001));
        HydraMergeSpec.AddAssistant(f, exact, Turn(exact), new string('y', 2000));
        // Swift counts Characters: 1500 grapheme clusters are 3000 UTF-16 units and are not cut; 2001 are cut at 2000.
        HydraMergeSpec.AddAssistant(f, combining, Turn(combining), string.Concat(Enumerable.Repeat(Cluster, 1500)));
        HydraMergeSpec.AddAssistant(f, cut, Turn(cut), string.Concat(Enumerable.Repeat(Cluster, 2001)));
        f.Start(full, blank, longer, exact, combining, cut, bare);
        DiffFile[] noFiles = [];
        DiffFile[] files =
        [
            new("src/a.txt", DiffFile.Change.Modified, false, ValueList<DiffHunk>.Empty, reportedAdditions: 12, reportedDeletions: 3),
            new("docs/b.md", DiffFile.Change.Added, false, ValueList<DiffHunk>.Empty, reportedAdditions: 0, reportedDeletions: 7),
        ];
        string Body(ChatThread lead, DiffFile[] diff, params Guid[] through) => f.H.Main.OnMain(() => (string)HydraMergeProofFixture.Call(
            "MergeRequestBody", f.Model, lead, new ValueList<DiffFile>(diff), through.ToHashSet(), f.Runtime(lead))!);
        var fileLines = new[] { "## Files", "- `src/a.txt` (+12 −3)", "- `docs/b.md` (+0 −7)" };

        // Roster order: Walter 1, Ada 2, Otto 3, Nova 4 (no task: not listed), Remy 5, Iris 6. The task is one line, at most 120 characters.
        string[] heads = [Opened, "", "## Heads", "- **Walter**: Parser fix (2 files)", "- **Ada**: Docs (1 file)", "- **Otto**: Multi",
            "- **Remy**: " + new string('b', 120), "- **Iris**: " + new string('c', 119) + "…", ""];
        Assert.Equal(Join([.. heads, "## The lead's summary", "Final reply", "", .. fileLines]), Body(full, files, Turn(full)));
        // A reply from turns this group does not hold is not the summary.
        Assert.Equal(Join([.. heads, .. fileLines]), Body(full, files, Guid.NewGuid()));
        // The last reply of the group's turns decides, even when it is blank.
        Assert.Equal(Join([Opened, "", .. fileLines]), Body(blank, files, Turn(blank)));
        Assert.Equal(Join([Opened, "", "## The lead's summary", new string('x', 2000) + "…", "", .. fileLines]), Body(longer, files, Turn(longer)));
        Assert.Equal(Join([Opened, "", "## The lead's summary", new string('y', 2000), "", .. fileLines]), Body(exact, files, Turn(exact)));
        Assert.Equal(Join([Opened, "", "## The lead's summary", string.Concat(Enumerable.Repeat(Cluster, 1500)), "", .. fileLines]),
            Body(combining, files, Turn(combining)));
        Assert.Equal(Join([Opened, "", "## The lead's summary", string.Concat(Enumerable.Repeat(Cluster, 2000)) + "…", "", .. fileLines]),
            Body(cut, files, Turn(cut)));
        // No heads, no reply and no files: "## Files" is the last line, with nothing under it.
        Assert.Equal(Opened + "\n\n## Files", Body(bare, noFiles, Turn(bare)));
    }

    [Theory]
    [InlineData("ssh://git@gitlab.example/group/repo.git", "https://gitlab.example/group/repo")]
    [InlineData("git@gitlab.example:group/repo.git", "https://gitlab.example/group/repo")]
    [InlineData("https://gitlab.example/group/repo.git", "https://gitlab.example/group/repo")]
    [InlineData("gitlab-mirror-without-a-web-page", null)]
    [InlineData("http-gitlab-without-a-host", null)]
    [Trait("Req", "PR-HYDRAMERGE-048")] // AppModel+HydraMerge.swift:441-450
    public void The_glab_vector_has_squash_before_merge_and_names_the_repo_only_when_the_remote_has_a_web_page_with_a_host(string remote, string? repo)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", remote);
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        script.Reply(1, stdout: ForgeUrl("gitlab"));
        var calls = new List<ShellInvocation>();

        var url = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            return await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!;
        }, AppModelHarness.Bound);

        Assert.Equal(ForgeUrl("gitlab"), url!.AbsoluteString);
        var glab = script.Calls();
        string[] head = ["mr", "create", "--title", "Subject", "--description", "Body", "--yes", "--squash-before-merge"];
        string[] tail = ["--source-branch", "hydra/source", "--target-branch", "main"];
        Assert.Equal(repo is null ? [.. head, .. tail] : [.. head, "--repo", repo, .. tail], glab[1].Arguments);
        // Run in the checkout, with the 300 second budget.
        Assert.Equal(p.Path, glab[1].WorkingDirectory);
        Assert.Equal(300d, calls.Single(call => call.Arguments.Contains("create")).Timeout);
    }

    [Fact]
    [Trait("Rule", "R-TEST-4")] // PR-HYDRAMERGE-048 only in what it adds: the git processes the source starts before glab, App/AppModel+HydraMerge.swift:433-442
    public void The_origin_remote_is_read_three_times_before_glab_starts_once_for_the_forge_and_once_each_for_the_host_and_the_repo_argument()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", "https://gitlab.example/group/repo.git");
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        script.Reply(1, stdout: ForgeUrl("gitlab"));
        var calls = new List<ShellInvocation>();

        f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            return await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!;
        }, AppModelHarness.Bound);

        // :434 git.forge() reads the remote, :437 reads its web page for the host, :442 reads it again for --repo.
        var firstForge = calls.FindIndex(IsForge);
        Assert.True(firstForge > 0);
        var reads = calls.Take(firstForge).Count(call => HydraMergeReviewFixture.IsGit(call, "remote") && call.Arguments.Contains("get-url"));
        Assert.Equal(3, reads);
    }

    [Theory]
    [InlineData("https://user:pass@gitlab.example/group/repo.git", "https://gitlab.example/group/repo")]
    [InlineData("https://token@gitlab.example/group/repo.git", "https://gitlab.example/group/repo")]
    [InlineData("https://user:pass@gitlab.example:8443/group/repo.git", "https://gitlab.example:8443/group/repo")]
    [InlineData("https://gitlab.example/group/repo.git", "https://gitlab.example/group/repo")]
    [Trait("Req", "PR-HYDRAMERGE-049")] // AppModel+HydraMerge.swift:442-449
    public void Credentials_in_the_remote_never_reach_the_repo_argument(string remote, string expected)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        f.Repos[p.Id].Git("remote", "add", "origin", remote);
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        script.Reply(1, stdout: ForgeUrl("gitlab"));

        f.H.Main.Loop.Run(async () =>
            await (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!,
            AppModelHarness.Bound);

        var arguments = script.Calls()[1].Arguments;
        Assert.Equal(expected, arguments[arguments.ToList().IndexOf("--repo") + 1]);
        Assert.DoesNotContain(arguments, argument => argument.Contains("pass", StringComparison.Ordinal) || argument.Contains("token", StringComparison.Ordinal) || argument.Contains('@', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("signed-in", "", "0", null)]
    [InlineData("token", "token is expired\n", "1", "token is expired")]
    // Shell.swift:15-19: failureMessage is never empty ("The command failed with exit code N."), so the literal "glab is not signed
    // in to this GitLab host." of :493 is reachable in neither the Swift nor the port; a silent failure reads like this instead.
    [InlineData("silent", "", "1", "The command failed with exit code 1.")]
    [InlineData("cannot-run", "", "0", null)]
    [InlineData("no-host", "token is expired", "1", "token is expired")]
    [Trait("Req", "PR-HYDRAMERGE-050")] // AppModel+HydraMerge.swift:437, :452-455, :485-494
    public void The_sign_in_is_checked_with_glab_auth_status_before_the_request_is_attempted(string case_, string stderr, string status, string? detail)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var host = case_ == "no-host" ? "your GitLab host" : "gitlab.example";
        f.Repos[p.Id].Git("remote", "add", "origin", case_ == "no-host" ? "gitlab-without-a-web-page" : "https://gitlab.example/group/repo.git");
        var lead = f.AddLead(p, "Lead", []);
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        script.Reply(0, stdout: case_ == "cannot-run" ? ForgeUrl("gitlab") : "", stderr: stderr, status: status);
        script.Reply(1, stdout: ForgeUrl("gitlab"));
        var calls = new List<ShellInvocation>();

        Task<SwiftURL?> Create() => (Task<SwiftURL?>)HydraMergeProofFixture.Call("HydraCreateMergeRequest", f.Model, new Git(p.Path), "Subject", "Body", "hydra/source", "main")!;
        object outcome;
        try
        {
            outcome = f.H.Main.Loop.Run(async () =>
            {
                using var scope = ShellObserver.Observe(call =>
                {
                    calls.Add(call);
                    // Glab could not be run at all: Shell throws, and the check answers "nothing wrong" (:490).
                    if (case_ == "cannot-run" && call.Arguments.SequenceEqual(["auth", "status"])) throw new ShellError("glab could not run");
                });
                return await Create();
            }, AppModelHarness.Bound)!;
        }
        catch (SwiftError error) { outcome = error; }

        var glab = calls.Where(IsForge).ToArray();
        // The check itself: glab auth status, in the checkout, within 30 seconds.
        Assert.Equal(["auth", "status"], glab[0].Arguments);
        Assert.Equal(p.Path, glab[0].Directory);
        Assert.Equal(30d, glab[0].Timeout);
        if (detail is null)
        {
            // A signed-in glab, or one that could not run: the creation attempt follows and surfaces whatever there is.
            Assert.Equal(2, glab.Length);
            Assert.Contains("create", glab[1].Arguments);
            Assert.Equal(ForgeUrl("gitlab"), ((SwiftURL)outcome).AbsoluteString);
        }
        else
        {
            Assert.Single(glab);
            var error = Assert.IsAssignableFrom<SwiftError>(outcome);
            Assert.Equal("GitLab refused the login (401 Unauthorized) for " + host + ": the token glab signs in with is missing or expired.\n\n" + detail +
                "\n\nSign back in with `glab auth login --hostname " + host + "` (or export a valid GITLAB_TOKEN), then ask for the merge again. The branch `hydra/source` is already pushed, so the team's work is safe.",
                SwiftErrors.LocalizedDescription(error));
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-057")] // Git.swift:213-225 (reproduced: the sixth failure still sleeps its 13 seconds)
    public void GitLab_retries_a_merge_it_calls_not_allowed_after_3_5_7_9_11_and_13_seconds_and_the_last_sleep_still_runs()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("gitlab-project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        script.Reply(1, stdout: ForgeUrl("gitlab"));
        // The merge is refused six times in a row: 405 or "not allowed" or "checking" in the output each time.
        string[] refusals = ["405 Method Not Allowed", "Merge request is not allowed yet", "still checking", "405", "NOT ALLOWED", "Request is still Checking"];
        for (var attempt = 0; attempt < 6; attempt++) script.Reply(2 + attempt, stderr: refusals[attempt], status: "1");
        var recorder = new RecordingTimeProvider(f.H.Main.Time);
        using var clock = SwiftClock.Provider.Override(recorder);
        var merges = new List<DateTimeOffset>();
        Task run = null!;
        f.H.Main.OnMain(() =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                if (Path.GetFileNameWithoutExtension(call.Executable) == "glab" && call.Arguments.Take(2).SequenceEqual(["mr", "merge"]))
                    merges.Add(f.H.Main.Time.GetUtcNow());
            });
            run = f.Model.AutoMergeHydraWork(lead.Id);
        });

        for (var attempt = 0; attempt < 6; attempt++)
        {
            var wait = TimeSpan.FromSeconds(3 + attempt * 2);
            // The sleep after this refusal exists once its timer was made; only then does the clock move.
            f.H.Main.Loop.RunUntil(() => recorder.Created(wait) == 1, AppModelHarness.Bound);
            Assert.Equal(attempt + 1, merges.Count);
            var asleepAt = f.H.Main.Time.GetUtcNow();
            f.H.Main.Advance(wait - TimeSpan.FromMilliseconds(1));
            Assert.Equal(attempt + 1, merges.Count); // one millisecond short: no new attempt, and the run is not over
            Assert.False(run.IsCompleted);
            f.H.Main.Advance(TimeSpan.FromMilliseconds(1));
            if (attempt < 5)
            {
                f.H.Main.Loop.RunUntil(() => merges.Count == attempt + 2, AppModelHarness.Bound);
                Assert.Equal(asleepAt + wait, merges[attempt + 1]);
            }
            else
            {
                // The sixth refusal still sleeps 13 seconds before the loop ends and the error is thrown.
                f.H.Main.Loop.RunUntil(() => run.IsCompleted, AppModelHarness.Bound);
                Assert.Equal(6, merges.Count);
            }
        }

        Assert.Equal(6, script.Calls().Count(call => call.Arguments.Take(2).SequenceEqual(["mr", "merge"])));
        var record = Assert.Single(HydraMergeSpec.Records(f, lead));
        Assert.Equal(HydraMergeRecord.Outcome.Failed, record.HydraMergeRecordOutcome);
        // The error is the last refusal's own message.
        Assert.Equal("Hydra opened !7 but could not merge it.: " + ForgeUrl("gitlab") + "\n\nRequest is still Checking\n\nThe next try brings the request up to date with `main` and merges it.", record.Detail);
    }

    [Theory]
    [InlineData("github", "405 Method Not Allowed: still checking")]
    [InlineData("gitea", "405 Method Not Allowed: still checking")]
    [InlineData("gitlab", "Merge request has conflicts")]
    [Trait("Req", "PR-HYDRAMERGE-057")] // Git.swift:218-224
    public void Only_a_gitlab_refusal_that_looks_like_still_checking_is_tried_again(string forge, string refusal)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject(forge == "gitea" ? "Project" : forge + "-project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        var script = new ForgeScript(p.Path);
        var createAt = forge == "gitlab" ? 1 : 0;
        script.Reply(createAt, stdout: ForgeUrl(forge));
        script.ReplyAlways(stderr: refusal, status: "1");
        script.Reply(createAt, stdout: ForgeUrl(forge));
        if (forge == "gitlab") script.Reply(0);
        var recorder = new RecordingTimeProvider(f.H.Main.Time);
        using var clock = SwiftClock.Provider.Override(recorder);

        f.Run(lead);

        // The first refusal ends the attempts at once: one merge call, no sleep, the error is the refusal.
        var calls = script.Calls();
        Assert.Equal(createAt + 2, calls.Count);
        Assert.Equal(MergeVector(forge), calls[^1].Arguments);
        Assert.Equal(0, new[] { 3, 5 }.Sum(seconds => recorder.Created(TimeSpan.FromSeconds(seconds))));
        var record = Assert.Single(HydraMergeSpec.Records(f, lead));
        Assert.EndsWith("\n\n" + refusal + "\n\nThe next try brings the request up to date with `main` and merges it.", record.Detail, StringComparison.Ordinal);
    }
}
