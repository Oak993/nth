using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.App.Views.Chat;
using Marrow.Core.Models;
using Marrow.Core.Port;


namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :125-126, :336-348, :374-405 (which checkout a merge
// works in, how the branch and the checkouts follow it, and the success note). Real git in disposable repositories; the
// forge CLI is the offline stand-in of tests/fixtures/forge-cli.
public sealed class AppModelHydraMergeCheckoutTests
{
    private static bool IsGit(ShellInvocation call) =>
        Path.GetFileNameWithoutExtension(call.Executable).Equals("git", StringComparison.OrdinalIgnoreCase);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRAMERGE-014")] // AppModel+HydraMerge.swift:125-126
    public void The_leads_own_checkout_is_its_worktree_when_it_has_one_else_the_project_path(bool inWorktree)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        ChatThread lead;
        string checkout;
        if (inWorktree) (lead, checkout) = HydraMergeSpec.AddWorktreeLead(f, p, "Lead", ["lead-file.txt"]);
        else
        {
            lead = f.AddLead(p, "Lead", ["lead-file.txt"]);
            checkout = p.Path;
        }
        // The head's landing path is relative: it is anchored at the lead's checkout (:126 leadCheckout is the same path).
        f.AddHead(lead, ["head-file.txt"]);
        File.WriteAllText(Path.Combine(checkout, "lead-file.txt"), "lead\n");
        File.WriteAllText(Path.Combine(checkout, "head-file.txt"), "head\n");
        f.Start(lead);
        HydraMergeSpec.Forge(checkout);
        var calls = new List<ShellInvocation>();

        f.Run(lead, calls.Add);

        // Every git call up to the push worked in that checkout and nowhere else.
        var beforePush = calls.TakeWhile(call => !HydraMergeReviewFixture.IsGit(call, "push")).Where(IsGit).ToArray();
        Assert.NotEmpty(beforePush);
        Assert.All(beforePush, call => Assert.Equal(checkout, call.Directory));
        var record = Assert.Single(HydraMergeSpec.Records(f, lead), r => r.HydraMergeRecordOutcome == HydraMergeRecord.Outcome.Merged);
        Assert.Equal("Project", record.Project);
        var branch = inWorktree ? "wt-branch" : f.Branch(lead);
        var tree = AppModelGitRepo.At(checkout).Git("ls-tree", "-r", "--name-only", branch).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("lead-file.txt", tree);
        Assert.Contains("head-file.txt", tree);
        if (inWorktree) Assert.False(File.Exists(Path.Combine(p.Path, "lead-file.txt")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRAMERGE-043")] // AppModel+HydraMerge.swift:336-346; extracts/git-command-vectors.md:248
    public void On_a_branch_of_the_checkouts_own_the_index_catches_up_to_the_teams_paths_after_the_ref_moves(bool nonDefaultBranch)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        if (nonDefaultBranch) repo.Git("checkout", "-b", "feat");
        var lead = f.AddLead(p, "Lead", ["a.txt", "n.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        File.WriteAllText(Path.Combine(p.Path, "n.txt"), "new file\n");
        var before = repo.Git("rev-parse", "HEAD").Trim();
        f.Start(lead);
        f.Forge(p);
        var calls = new List<ShellInvocation>();

        f.Run(lead, calls.Add);

        var git = calls.Where(IsGit).ToList();
        var verbs = git.Select(call => HydraMergeReviewFixture.GitArgs(call)[0]).ToList();
        var move = verbs.IndexOf("update-ref");
        Assert.True(move >= 0);
        if (!nonDefaultBranch)
        {
            // On the default branch the commit goes to a branch of its own and nothing resets the index before the push.
            Assert.Equal("push", verbs[move + 1]);
            return;
        }
        Assert.Equal("reset", verbs[move + 1]);
        Assert.Equal("push", verbs[move + 2]);
        // A lead without a copy has no second checkout to bring along (:394).
        Assert.DoesNotContain(verbs.Skip(move + 3), verb => verb is "status" or "pull");
        var reset = git[move + 1];
        Assert.Equal(["reset", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"], HydraMergeReviewFixture.GitArgs(reset));
        Assert.Equal(Encoding.UTF8.GetBytes("a.txt\0n.txt\0"), reset.Input);
        // The branch moved under the checkout, the working tree already had the content, and the index matches the new tip.
        Assert.NotEqual(before, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("", repo.Git("status", "--porcelain").Trim());
        Assert.Equal("team edit\n", File.ReadAllText(Path.Combine(p.Path, "a.txt")));
        Assert.Equal("new file\n", File.ReadAllText(Path.Combine(p.Path, "n.txt")));
    }

    [Theory]
    [InlineData("clean", true)]
    [InlineData("other-branch", false)]
    [InlineData("dirty", false)]
    [InlineData("operation", false)]
    [InlineData("pull-fails", false)]
    [Trait("Req", "PR-HYDRAMERGE-064")] // AppModel+HydraMerge.swift:394-400; extracts/git-command-vectors.md:251
    public void A_lead_in_a_worktree_pulls_the_projects_own_checkout_only_when_that_is_safe(string state, bool pulled)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var (lead, copy) = HydraMergeSpec.AddWorktreeLead(f, p, "Lead", ["lead-file.txt"]);
        File.WriteAllText(Path.Combine(copy, "lead-file.txt"), "lead\n");
        switch (state)
        {
            case "other-branch": repo.Git("checkout", "-q", "-b", "other"); break;
            case "dirty": File.WriteAllText(Path.Combine(p.Path, "a.txt"), "dirty\n"); break;
            case "operation": File.WriteAllText(Path.Combine(p.Path, ".git", "MERGE_HEAD"), repo.Git("rev-parse", "HEAD")); break;
        }
        f.Start(lead);
        HydraMergeSpec.Forge(copy);
        var trail = new List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)>();
        var record = HydraMergeSpec.Trail(f, lead, trail);

        f.Run(lead, call =>
        {
            record(call);
            // A pull that throws: the branch's upstream names a ref that is not there.
            if (state == "pull-fails" && IsGit(call) && call.Directory == p.Path && call.Arguments.Contains("pull"))
            {
                using var suppress = ShellObserver.Observe(_ => { });
                repo.Git("config", "branch.main.merge", "refs/heads/not-there");
            }
        });

        var pushed = trail.FindIndex(entry => HydraMergeReviewFixture.IsGit(entry.Call, "push"));
        var onProject = trail.Skip(pushed).Where(entry => IsGit(entry.Call) && entry.Call.Directory == p.Path).ToArray();
        // The syncing stage opens with the project's own checkout, whatever the guards then decide.
        Assert.NotEmpty(onProject);
        Assert.Equal(("Bringing the checkout up to date", HydraMergeStage.Syncing), (onProject[0].Stage, onProject[0].Step));
        Assert.Equal(6, (int)HydraMergeStage.Syncing);
        var pulls = onProject.Where(entry => HydraMergeReviewFixture.IsGit(entry.Call, "pull")).ToArray();
        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        Assert.Equal(HydraMergeRecord.Outcome.Merged, Assert.Single(HydraMergeSpec.Records(f, lead)).HydraMergeRecordOutcome);
        var lines = HydraMergeSpec.BodyLines(note);
        if (state is "clean" or "pull-fails")
        {
            var pull = Assert.Single(pulls).Call;
            Assert.Equal(["pull", "--ff-only", "--quiet"], HydraMergeReviewFixture.GitArgs(pull));
            Assert.Equal(300, pull.Timeout);
        }
        else Assert.Empty(pulls);
        // A failed pull adds nothing at all: the note stays silent about the project's checkout.
        Assert.Equal(pulled, lines.Contains("The project checkout is up to date."));
        if (pulled) Assert.Equal("The project checkout is up to date.", lines[^1]);
        else Assert.DoesNotContain(lines, line => line.Contains("up to date", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Req", "PR-HYDRAMERGE-062")] // AppModel+HydraMerge.swift:390-394; Hydra.swift:527
    public void On_an_own_branch_the_syncing_stage_brings_the_checkout_up_to_date_and_says_whether_it_worked(bool synced)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        f.Start(lead);
        f.Forge(p);
        var trail = new List<(ShellInvocation Call, string? Stage, HydraMergeStage? Step)>();
        var record = HydraMergeSpec.Trail(f, lead, trail);
        string? terminalCommit = null;

        f.Run(lead, call =>
        {
            record(call);
            // A commit made in a terminal while the forge merges: the sync must leave the checkout alone.
            if (!synced && HydraMergeSpec.Verb(call) == "tea pulls merge")
            {
                using var suppress = ShellObserver.Observe(_ => { });
                repo.Git("commit", "--allow-empty", "-q", "-m", "terminal");
                terminalCommit = repo.Git("rev-parse", "HEAD").Trim();
            }
        });

        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        var lines = HydraMergeSpec.BodyLines(note);
        var syncCalls = trail.SkipWhile(entry => !(HydraMergeSpec.Verb(entry.Call) == "tea pulls merge"))
            .Skip(1).Where(entry => IsGit(entry.Call)).ToArray();
        Assert.NotEmpty(syncCalls);
        Assert.All(syncCalls, entry => Assert.Equal(("Bringing the checkout up to date", HydraMergeStage.Syncing), (entry.Stage, entry.Step)));
        if (synced) Assert.Equal("The checkout is up to date.", lines[^1]);
        else
        {
            Assert.Equal("The checkout was left as it was: `main` moved on in other ways meanwhile, or the team's files changed again; `git pull` when it suits you.", lines[^1]);
            Assert.Equal(terminalCommit, repo.Git("rev-parse", "HEAD").Trim());
        }
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-065")] // AppModel+HydraMerge.swift:402-405
    public void A_successful_pass_records_the_merge_posts_the_note_and_refreshes_the_diff_view()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt", "b.txt"]);
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "l1\nl2\nl3\nl4\nl5\n");
        File.WriteAllText(Path.Combine(p.Path, "b.txt"), "x\ny\nz\n");
        f.Start(lead);
        f.Forge(p);
        var revision = f.H.Main.OnMain(() => f.Runtime(lead).DiffRevision);

        f.Run(lead);

        var record = Assert.Single(HydraMergeSpec.Records(f, lead));
        Assert.Equal(HydraMergeRecord.Outcome.Merged, record.HydraMergeRecordOutcome);
        Assert.Equal("Project", record.Project);
        Assert.Equal("#7", record.Label);
        Assert.Equal(HydraMergeSpec.RequestURL, record.Url?.AbsoluteString);
        Assert.Equal(2, record.Files); // the parsed file count, not the line totals
        Assert.Null(record.Detail);
        Assert.Equal(f.H.Main.Time.GetUtcNow(), record.At);
        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        // The title has no trailing full stop, unlike every failure title.
        Assert.Equal("Hydra merged #7", HydraMergeSpec.Title(note));
        Assert.Equal(["Hydra merged #7", HydraMergeSpec.RequestURL, "", "2 files (+8 −1) landed on `main` from `" + f.Branch(lead) + "`.", "The checkout is up to date."],
            note.Text.Split('\n'));
        Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).DiffRevision) > revision);
        Assert.True(f.H.Main.OnMain(() => f.Runtime(lead).Turns.Single().HydraMerged));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-060")] // AppModel+HydraMerge.swift:375-385; HydraMergePopover.swift:25-75
    public void The_success_note_lists_every_head_with_its_files_under_a_Heads_line()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        // Team order is the roster order: Walter (1), Ada (2), Otto (3). Ada's only file is gone, so none of it goes out.
        f.AddHead(lead, ["c.txt", "b.txt", "c.txt"], index: 1, task: "Parser fix");
        f.AddHead(lead, ["gone.txt"], index: 2, task: "Docs");
        f.AddHead(lead, ["d.txt"], index: 3, task: "One line");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team edit\n");
        foreach (var name in new[] { "b.txt", "c.txt", "d.txt" }) File.WriteAllText(Path.Combine(p.Path, name), name + "\n");
        f.Start(lead);
        f.Forge(p);

        f.Run(lead);

        var note = Assert.Single(HydraMergeSpec.NoteEntries(f, lead));
        // A blank line, the bare word "Heads", one line per head with U+2014 around the count, then the sync line.
        Assert.Equal([
            HydraMergeSpec.RequestURL, "", "4 files (+4 −1) landed on `main` from `" + f.Branch(lead) + "`.",
            "", "Heads",
            "- Walter (1): Parser fix — 2 files: c.txt, b.txt",
            "- Ada (2): Docs — no files",
            "- Otto (3): One line — 1 file: d.txt",
            "The checkout is up to date."], HydraMergeSpec.BodyLines(note));
        // The merge popover reads the very same lines back.
        var link = MergeRequestLink.Create(SwiftURL.Create(HydraMergeSpec.RequestURL)!);
        var parsed = HydraMergeNote.Parse(string.Join("\n", HydraMergeSpec.BodyLines(note).Skip(2)), link!);
        Assert.NotNull(parsed);
        Assert.Equal([("Walter", 1L, "Parser fix", new[] { "c.txt", "b.txt" }), ("Ada", 2L, "Docs", Array.Empty<string>()), ("Otto", 3L, "One line", new[] { "d.txt" })],
            parsed!.Heads.Select(h => (h.Name, h.Index, h.Task, h.Files.ToArray())));
    }
}
