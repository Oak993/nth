using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Git;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :79-482,871-947.
public sealed class AppModelHydraMergePart1Tests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-061")] // App/AppModel+HydraMerge.swift:374-389: omitted files add no lines.
    [Trait("Req", "PR-HYDRAMERGE-078")] // App/AppModel+HydraMerge.swift:664-669,177-179: all turns/heads are spent.
    public void A_success_books_filtered_heads_and_turns_and_reports_no_omissions_of_any_kind()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        File.WriteAllText(Path.Combine(p.Path, ".gitignore"), "ignored.txt\n");
        repo.Git("add", ".gitignore"); repo.Git("commit", "-q", "-m", "ignore"); repo.Git("push", "origin", "main");
        File.WriteAllText(Path.Combine(p.Path, "ignored.txt"), "ignored\n");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team\n");
        File.WriteAllText(Path.Combine(p.Path, "observed.txt"), "other chat\n");
        File.WriteAllText(Path.Combine(p.Path, "unrecorded.txt"), "user\n");
        var lead = f.AddLead(p, "Lead", ["a.txt", "observed.txt", "gone.txt", "ignored.txt", "node_modules/output.txt"]);
        var sibling = f.AddLead(p, "Sibling", ["observed.txt"], status: TurnStatus.Interrupted, hydra: false);
        var head = f.AddHead(lead, ["gone.txt"], task: "empty head");
        f.Start(lead, sibling); f.Forge(p);
        f.Run(lead);
        Assert.True(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.NotNull(f.Model.Thread(head.Id)!.Hydra!.MergedAt);
        var branch = f.Branch(lead);
        Assert.Equal($"Hydra merged #7\n{HydraMergeReviewFixture.Url}\n\n1 file (+1 −1) landed on `main` from `{branch}`.\n\nHeads\n- {head.Hydra!.Persona.Name} (1): empty head — no files\nThe checkout is up to date.", Assert.Single(f.Notes(lead)));
        Assert.Equal("other chat\n", File.ReadAllText(Path.Combine(p.Path, "observed.txt")));
        Assert.False(repo.Raw("cat-file", "-e", branch + ":observed.txt").Succeeded);
    }

    [Theory]
    [InlineData("main")]
    [InlineData("feature")]
    [InlineData("detached")]
    [Trait("Req", "PR-HYDRAMERGE-038")] // App/AppModel+HydraMerge.swift:263-269
    [Trait("Req", "PR-HYDRAMERGE-040")] // App/AppModel+HydraMerge.swift:322; Services/Git/Git.swift:371-374
    [Trait("Req", "PR-HYDRAMERGE-042")] // App/AppModel+HydraMerge.swift:336-345
    [Trait("Req", "PR-HYDRAMERGE-044")] // App/AppModel+HydraMerge.swift:347-348; Services/Git/Git.swift:427-430
    [Trait("Req", "PR-HYDRAMERGE-061")] // App/AppModel+HydraMerge.swift:374-389
    public void Branch_commit_and_push_contract_preserves_the_checkout_and_user_identity(string branch)
    {
        using var f = new HydraMergeReviewFixture(branch: branch == "main" ? null : branch);
        f.Edit();
        var oldHead = f.Repo.Git("rev-parse", "HEAD").Trim();
        var oldIndex = File.ReadAllBytes(Path.Combine(f.Repo.Root, ".git", "index"));
        var calls = new List<ShellInvocation>();
        var stage = new List<(string? Words, HydraMergeStage? Step)>();
        f.Run(call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, "push")) stage.Add((f.Runtime.HydraMergeStage, f.Runtime.HydraMergeStep));
        });
        var own = branch != "feature";
        var name = own ? (string)HydraMergeProofFixture.Call("HydraBranchName", null, f.Lead)! : "feature";
        var commit = Assert.Single(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree"));
        var args = HydraMergeReviewFixture.GitArgs(commit);
        Assert.Equal("commit-tree", args[0]);
        Assert.Equal(["-p", oldHead, "-F", "-"], args[2..]);
        Assert.Equal("Team job", Encoding.UTF8.GetString(commit.Input!)); // Swift :835-840 fallback, no heads.
        Assert.DoesNotContain(commit.Environment!.Keys, k => k.StartsWith("GIT_AUTHOR_", StringComparison.Ordinal) || k.StartsWith("GIT_COMMITTER_", StringComparison.Ordinal));
        var update = HydraMergeReviewFixture.GitArgs(Assert.Single(calls, c => HydraMergeReviewFixture.IsGit(c, "update-ref")));
        var built = f.Repo.Git("rev-parse", name).Trim();
        Assert.Equal(own ? ["update-ref", "refs/heads/" + name, built] : ["update-ref", "refs/heads/feature", built, oldHead], update);
        var push = Assert.Single(calls, c => HydraMergeReviewFixture.IsGit(c, "push"));
        Assert.Equal(own ? ["push", "-u", "--force", "origin", $"refs/heads/{name}:refs/heads/{name}"] :
            ["push", "-u", "origin", "refs/heads/feature:refs/heads/feature"], HydraMergeReviewFixture.GitArgs(push));
        Assert.Equal(300, push.Timeout);
        Assert.Equal(("Pushing the branch", HydraMergeStage.Pushing), Assert.Single(stage)); // Swift :347.
        Assert.Equal("Port Test <port-test@localhost>", f.Repo.Git("show", "-s", "--format=%an <%ae>", built).Trim());
        Assert.Equal("Port Test <port-test@localhost>", f.Repo.Git("show", "-s", "--format=%cn <%ce>", built).Trim());
        Assert.Equal(branch == "detached" ? "HEAD" : branch, f.Repo.Git("rev-parse", "--abbrev-ref", "HEAD").Trim());
        if (own) { Assert.Equal(oldHead, f.Repo.Git("rev-parse", "HEAD").Trim()); Assert.Equal(oldIndex, File.ReadAllBytes(Path.Combine(f.Repo.Root, ".git", "index"))); }
        Assert.Equal("team edit\n", File.ReadAllText(Path.Combine(f.Repo.Root, "a.txt")));
        Assert.Equal(HydraMergeRecord.Outcome.Merged, Assert.Single(f.Runtime.HydraMerges).HydraMergeRecordOutcome);
        Assert.Equal($"Hydra merged #7\n{HydraMergeReviewFixture.Url}\n\n1 file (+1 −1) landed on `main` from `{name}`." +
            (own ? "\nThe checkout is up to date." : ""), Assert.Single(f.Notes)); // :374,:393; no omitted-file lines.
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-042")] // App/AppModel+HydraMerge.swift:342,406-408
    public void A_checkout_branch_compare_and_swap_refuses_a_terminal_commit_landing_after_preparation()
    {
        using var f = new HydraMergeReviewFixture(branch: "feature");
        f.Edit();
        string? terminal = null;
        f.Run(call =>
        {
            if (!HydraMergeReviewFixture.IsGit(call, "update-ref")) return;
            using var suppress = ShellObserver.Observe(_ => { });
            f.Repo.Git("commit", "--allow-empty", "-q", "-m", "terminal");
            terminal = f.Repo.Git("rev-parse", "HEAD").Trim();
        });
        Assert.Equal(terminal, f.Repo.Git("rev-parse", "HEAD").Trim());
        Assert.True(Assert.Single(f.Runtime.HydraMerges).Retryable);
        Assert.True(f.Runtime.HasHydraMergeRetryPending);
        Assert.Empty(f.Notes);
        Assert.Empty(f.Forge.Calls());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Req", "PR-HYDRAMERGE-015")] // App/AppModel+HydraMerge.swift:127,136-148
    [Trait("Req", "PR-HYDRAMERGE-024")] // App/AppModel+HydraMerge.swift:166-179
    // PR-HYDRAMERGE-017 (iteration follows the sidebar's own project order; `swapped` adds Third BEFORE Other, so
    // sidebar order differs from the name/path order the folders sort in): App/AppModel+HydraMerge.swift:136 loops `projects`.
    public void Project_groups_follow_sidebar_order_keep_the_own_project_first_and_do_not_sweep_other_checkouts(bool emptyOwn, bool swapped)
    {
        using var f = new HydraMergeProofFixture();
        var own = f.AddProject("Own", origin: false);
        var other = swapped ? null! : f.AddProject("Other", origin: false);
        var third = f.AddProject("Third", origin: false);
        if (swapped) other = f.AddProject("Other", origin: false);
        var ownRepo = f.Repos[own.Id];
        var baseTree = ownRepo.Git("rev-parse", "HEAD^{tree}").Trim();
        var lead = f.AddLead(own, "Lead", emptyOwn ? ["gone"] : ["a.txt"], checkpoint: baseTree);
        f.Tool(lead, Path.Combine(other.Path, "a.txt"), Path.Combine(third.Path, "a.txt"));
        File.WriteAllText(Path.Combine(other.Path, "unrecorded.txt"), "other user\n");
        File.WriteAllText(Path.Combine(third.Path, "unrecorded.txt"), "third user\n");
        f.Start(lead);
        var calls = new List<ShellInvocation>();
        f.Run(lead, calls.Add);
        var records = f.Runtime(lead).HydraMerges;
        var order = swapped ? new[] { "Third", "Other" } : new[] { "Other", "Third" };
        Assert.Equal(emptyOwn ? order : ["Own", .. order], records.Select(r => r.Project));
        foreach (var r in records)
        {
            Assert.Equal(HydraMergeRecord.Outcome.Failed, r.HydraMergeRecordOutcome);
            Assert.Equal($"{r.Project}: Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", r.Detail);
        }
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.DoesNotContain(calls, c => c.Directory != own.Path && HydraMergeReviewFixture.IsGit(c, "add"));
        Assert.Equal("?? unrecorded.txt", f.Repos[other.Id].Git("status", "--porcelain").Trim());
        Assert.Equal("?? unrecorded.txt", f.Repos[third.Id].Git("status", "--porcelain").Trim());
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-017")] // App/AppModel+HydraMerge.swift:122-139,148,636-643
    public void Each_other_owning_project_gets_its_own_pass_in_sidebar_order_with_the_sweep_off_and_the_leads_checkout_anchoring_relative_edits()
    {
        using var f = new HydraMergeProofFixture();
        // Sidebar order is Own, Third, Other, Untouched: not the order the names or folders sort in (:136 loops `projects`).
        var own = f.AddProject("Own");
        var third = f.AddProject("Third");
        var other = f.AddProject("Other");
        var untouched = f.AddProject("Untouched"); // owns none of the lead's outside paths: no pass of its own (:136 owners.values.contains)
        var baseTree = f.Repos[own.Id].Git("rev-parse", "HEAD^{tree}").Trim();
        var lead = f.AddLead(own, "Lead", [], checkpoint: baseTree); // The lead's own checkout is swept from this checkpoint (:636-640)...
        // ...a relative edit is anchored at the LEAD's checkout (:137 leadCheckout: ownCheckout), where rel.txt does not exist.
        f.Tool(lead, "rel.txt", Path.Combine(other.Path, "o.txt"), Path.Combine(third.Path, "t.txt"));
        File.WriteAllText(Path.Combine(other.Path, "o.txt"), "o\n");
        File.WriteAllText(Path.Combine(other.Path, "rel.txt"), "same name in the other project\n");
        File.WriteAllText(Path.Combine(other.Path, "unrecorded.txt"), "user, in the checkout the sweep must not read\n");
        File.WriteAllText(Path.Combine(third.Path, "t.txt"), "t\n");
        f.Start(lead);
        f.Forge(third);
        f.Forge(other);
        var calls = new List<ShellInvocation>();
        var visited = new List<string>();
        f.Run(lead, call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, "remote") && call.Arguments.Contains("get-url") && !visited.Contains(call.Directory!)) visited.Add(call.Directory!);
        });
        // Own's group had nothing to send and was pruned (:148); the others went in sidebar order, the first-come order of the loop.
        Assert.Equal([third.Path, other.Path], visited);
        var records = f.Runtime(lead).HydraMerges;
        Assert.Equal(["Third", "Other"], records.Select(r => r.Project));
        Assert.All(records, r => Assert.Equal(HydraMergeRecord.Outcome.Merged, r.HydraMergeRecordOutcome));
        var branch = f.Branch(lead);
        Assert.Equal([
            $"Third: Hydra merged #7\n{HydraMergeReviewFixture.Url}\n\n1 file (+1 \u22120) landed on `main` from `{branch}`.\nThe checkout is up to date.",
            $"Other: Hydra merged #7\n{HydraMergeReviewFixture.Url}\n\n1 file (+1 \u22120) landed on `main` from `{branch}`.\nThe checkout is up to date."], f.Notes(lead));
        // The other checkout's branch holds the recorded o.txt only: not rel.txt (anchored at the lead's checkout), not the user's file.
        var repo = f.Repos[other.Id];
        Assert.True(repo.Raw("cat-file", "-e", branch + ":o.txt").Succeeded);
        Assert.False(repo.Raw("cat-file", "-e", branch + ":rel.txt").Succeeded);
        Assert.False(repo.Raw("cat-file", "-e", branch + ":unrecorded.txt").Succeeded);
        // sweepsCheckout: false: the lead's first checkpoint was read in the lead's checkout and in no other one (:636).
        bool ReadsCheckpoint(ShellInvocation call, string directory) => call.Directory == directory && call.Arguments.Contains(baseTree);
        Assert.Contains(calls, call => ReadsCheckpoint(call, own.Path));
        Assert.DoesNotContain(calls, call => ReadsCheckpoint(call, other.Path) || ReadsCheckpoint(call, third.Path));
        Assert.DoesNotContain(calls, call => call.Directory == untouched.Path);
        Assert.True(f.Runtime(lead).Turns.Single().HydraMerged);
    }

    [Theory]
    [InlineData("MERGE_HEAD")]
    [InlineData("rebase-merge")]
    [Trait("Req", "PR-HYDRAMERGE-030")] // App/AppModel+HydraMerge.swift:245-249,414-423
    [Trait("Req", "PR-HYDRAMERGE-024")] // App/AppModel+HydraMerge.swift:168-171
    public void An_operation_defers_silently_and_attempts_exactly_one_retry_after_a_minute(string marker)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Operation");
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        f.Start(lead);
        var path = Path.Combine(project.Path, ".git", marker);
        if (marker == "MERGE_HEAD") File.WriteAllText(path, f.Repos[project.Id].Git("rev-parse", "HEAD")); else Directory.CreateDirectory(path);
        var calls = new List<ShellInvocation>();
        f.Run(lead, calls.Add);
        Assert.Equal("Deferred", f.Pass(lead, project));
        Assert.Empty(f.Runtime(lead).HydraMerges);
        Assert.Empty(f.Notes(lead));
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.True(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree") || HydraMergeReviewFixture.IsGit(c, "push"));
        // Removal makes the retry's capture equal HEAD, so the job settles instead of repeating the deferral. It is removed
        // BEFORE the clock moves: a retry that fired earlier than the minute (:418 sleeps 60 s) would settle the job below.
        if (File.Exists(path)) File.Delete(path); else Directory.Delete(path);
        f.H.Main.AdvanceMilliseconds(59_999);
        Assert.Empty(f.Runtime(lead).HydraMerges);
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.True(f.Runtime(lead).HasHydraMergeRetryPending);
        f.H.Main.AdvanceMilliseconds(1);
        f.H.Main.Loop.RunUntil(() => f.Runtime(lead).Turns.Single().HydraMerged, AppModelHarness.Bound);
        Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.Equal(HydraMergeRecord.Outcome.Settled, Assert.Single(f.Runtime(lead).HydraMerges).HydraMergeRecordOutcome);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [Trait("Req", "PR-HYDRAMERGE-029")] // App/AppModel+HydraMerge.swift:226-242
    public void Missing_remote_contract_is_exact_project_prefixed_when_needed_and_deduplicates(long count, bool several)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project", origin: false);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        Assert.Equal("Failed", f.Pass(lead, p, several));
        Assert.Equal("Failed", f.Pass(lead, p, several));
        var r = Assert.Single(f.Runtime(lead).HydraMerges);
        var prefix = several ? "Project: " : "";
        Assert.Equal(HydraMergeRecord.Outcome.Failed, r.HydraMergeRecordOutcome);
        Assert.Equal("Project", r.Project);
        Assert.Null(r.Label); Assert.Null(r.Url); Assert.Null(r.Retryable); Assert.Equal(0, r.Files);
        Assert.Equal(f.H.Main.Time.GetUtcNow(), r.At);
        Assert.Equal(prefix + "Hydra did not merge: no remote.: The team's work is in the checkout; there is no origin to push it to.", r.Detail);
        Assert.Equal(prefix + "Hydra did not merge: no remote.\nThe team's work is in the checkout; there is no origin to push it to.", Assert.Single(f.Notes(lead)));
        Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.True(count > 0); // Theory source values select single/multiple project contexts, not file counts.
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [Trait("Req", "PR-HYDRAMERGE-031")] // App/AppModel+HydraMerge.swift:251-259
    public void Conflict_markers_never_reach_a_commit_and_the_exact_record_and_note_are_written_once(int count, bool several)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var paths = Enumerable.Range(0, count).Select(i => $"{i}.txt").ToArray();
        var lead = f.AddLead(p, "Lead", paths);
        foreach (var path in paths) File.WriteAllText(Path.Combine(p.Path, path), "<<<<<<< ours\nours\n=======\ntheirs\n>>>>>>> theirs\n");
        f.Start(lead);
        var calls = new List<ShellInvocation>();
        Assert.Equal("Failed", f.Pass(lead, p, several, calls.Add));
        Assert.Equal("Failed", f.Pass(lead, p, several, calls.Add));
        var prefix = several ? "Project: " : "";
        var title = prefix + $"Hydra did not merge: conflict markers in {(count == 1 ? "a file" : "2 files")}.";
        var body = string.Join(", ", paths.Select(p => $"`{p}`")) + $" still {(count == 1 ? "holds" : "hold")} conflict markers from a head's landing. Resolve them and the merge goes out by itself.";
        var r = Assert.Single(f.Runtime(lead).HydraMerges);
        Assert.Equal(title + ": " + body, r.Detail);
        Assert.Equal(title + "\n" + body, Assert.Single(f.Notes(lead)));
        Assert.Null(r.Retryable); Assert.Null(r.Label); Assert.Null(r.Url); Assert.Equal(0, r.Files);
        Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree") || HydraMergeReviewFixture.IsGit(c, "update-ref") || HydraMergeReviewFixture.IsGit(c, "push"));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-034")] // App/AppModel+HydraMerge.swift:310-313,374
    public void A_thrown_diff_becomes_an_empty_patch_and_zero_files_while_the_real_tree_is_committed()
    {
        using var f = new HydraMergeReviewFixture();
        f.Edit();
        f.Run(call =>
        {
            if (HydraMergeReviewFixture.IsGit(call, "diff") && !call.Arguments.Contains("--name-only"))
                throw new IOException("offline diff failure");
        });
        var r = Assert.Single(f.Runtime.HydraMerges);
        Assert.Equal(HydraMergeRecord.Outcome.Merged, r.HydraMergeRecordOutcome);
        Assert.Equal(0, r.Files);
        Assert.Contains("0 files (+0 −0) landed on `main`", Assert.Single(f.Notes), StringComparison.Ordinal);
        var create = Assert.Single(f.Forge.Calls(), c => c.Arguments.Contains("create"));
        Assert.Equal("Opened by nth once its Hydra team finished.\n\n## Files", create.Arguments.Single(a => a.StartsWith("Opened by nth", StringComparison.Ordinal))); // :845,:866-868 join, no trailing newline when files is empty.
        Assert.Equal("team edit\n", f.Repo.Git("show", f.Repo.Git("branch", "--list", "hydra/*").Trim() + ":a.txt"));
    }

    [Theory]
    [InlineData(" --Fix\r\n  the\tissue-- ")]
    [InlineData("abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyz")]
    [Trait("Req", "PR-HYDRAMERGE-035")] // App/AppModel+HydraMerge.swift:314,334,350-354
    public void Describing_uses_the_full_message_for_the_commit_and_a_single_line_72_character_subject(string title)
    {
        using var f = new HydraMergeReviewFixture(title: title);
        f.Edit();
        var stages = new List<(string?, HydraMergeStage?)>();
        f.Run(call =>
        {
            if (HydraMergeReviewFixture.IsGit(call, "commit-tree")) stages.Add((f.Runtime.HydraMergeStage, f.Runtime.HydraMergeStep));
        });
        Assert.Equal(("Writing the commit message", HydraMergeStage.Describing), Assert.Single(stages));
        var create = Assert.Single(f.Forge.Calls(), c => c.Arguments.Contains("create"));
        var expected = title.Length > 72 ? "abcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrstuvwxyzabcdefghijklmnopqrs…" : "--Fix";
        Assert.Equal(expected, create.Arguments[create.Arguments.ToList().IndexOf("--title") + 1]); // Text.swift: singleLine truncates with ellipsis.
        var branch = f.Repo.Git("branch", "--list", "hydra/*").Trim();
        Assert.Equal(title.Trim() + "\n", f.Repo.Git("show", "-s", "--format=%B", branch).TrimEnd('\n') + "\n");
    }
}
