using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :280-295,778-818.
public sealed class AppModelHydraMergeSyncTests
{
    [Theory]
    [InlineData("overlap", false)]
    [InlineData("team-deleted", false)]
    [InlineData("remote-deleted", false)]
    [InlineData("both-added", false)]
    [InlineData("overlap", true)]
    [Trait("Req", "PR-HYDRAMERGE-128")] // App/AppModel+HydraMerge.swift:288-294; Services/Git/Git.swift:679-718
    [Trait("Req", "PR-HYDRAMERGE-127")] // App/AppModel+HydraMerge.swift:288-290: an overlap ends the prepare loop.
    public void Every_remote_overlap_fails_nonretryably_before_a_commit_with_the_exact_singular_or_plural_note(string kind, bool several)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var clone = f.Clone(p);
        var path = kind == "both-added" ? "new.txt" : "a.txt";
        if (kind == "remote-deleted") File.Delete(Path.Combine(clone.Root, path));
        else File.WriteAllText(Path.Combine(clone.Root, path), "remote\n");
        clone.Git("add", "-A"); clone.Git("commit", "-q", "-m", "remote"); clone.Git("push", "origin", "main");
        if (kind == "team-deleted") File.Delete(Path.Combine(p.Path, path));
        else File.WriteAllText(Path.Combine(p.Path, path), "team\n");
        var paths = new List<string> { path };
        if (several) { File.WriteAllText(Path.Combine(p.Path, "b.txt"), "team b\n"); paths.Add("b.txt"); }
        var lead = f.AddLead(p, "Lead", paths.ToArray());
        f.Start(lead);
        var calls = new List<ShellInvocation>();
        Assert.Equal("Failed", f.Pass(lead, p, several, calls.Add));
        var title = (several ? "Project: " : "") + "Hydra did not merge: `main` changed the same lines.";
        var body = "The remote changed " + (several ? "files" : "a file") + " the team changed too, in the same places. The work is in the checkout: bring `main` in (`git pull`), settle the overlap, and the merge goes out by itself.";
        var r = Assert.Single(f.Runtime(lead).HydraMerges);
        Assert.Equal(title + ": " + body, r.Detail); Assert.Null(r.Retryable);
        Assert.Equal(title + "\n" + body, Assert.Single(f.Notes(lead)));
        Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "commit-tree") || HydraMergeReviewFixture.IsGit(c, "update-ref") || HydraMergeReviewFixture.IsGit(c, "push"));
        Assert.Equal("Failed", f.Pass(lead, p, several));
        Assert.Single(f.Runtime(lead).HydraMerges); Assert.Single(f.Notes(lead));
        Assert.False(f.Runtime(lead).Turns.Single().HydraMerged);
        Assert.True(repo.Raw("cat-file", "-e", "HEAD:a.txt").Succeeded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-128")] // App/AppModel+HydraMerge.swift:280-295
    [Trait("Req", "PR-HYDRAMERGE-131")] // App/AppModel+HydraMerge.swift:781-789,809-813
    public void A_real_three_way_merge_uses_the_remote_parent_and_writes_back_only_unchanged_team_files(bool editedAgain)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        const string baseline = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\n";
        const string team = "one\nTEAM-TWO\nthree\nfour\nfive\nsix\nseven\neight\nnine\n";
        const string remote = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nREMOTE-NINE\n";
        const string merged = "one\nTEAM-TWO\nthree\nfour\nfive\nsix\nseven\neight\nREMOTE-NINE\n";
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), baseline);
        repo.Git("add", "a.txt"); repo.Git("commit", "-q", "-m", "baseline"); repo.Git("push", "origin", "main");
        var clone = f.Clone(p);
        File.WriteAllText(Path.Combine(clone.Root, "a.txt"), remote);
        File.WriteAllText(Path.Combine(clone.Root, "remote.txt"), "remote only\n");
        clone.Git("add", "-A"); clone.Git("commit", "-q", "-m", "remote"); clone.Git("push", "origin", "main");
        var remoteHead = clone.Git("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), team);
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        f.Forge(p);
        var branch = f.Branch(lead);
        var sync = false;
        f.Run(lead, call =>
        {
            if (call.Arguments.Contains("merge") && Path.GetFileNameWithoutExtension(call.Executable) == "tea")
            {
                using var suppress = ShellObserver.Observe(_ => { });
                repo.Git("push", "origin", branch + ":main"); // Offline forge's merge effect in the local bare origin.
            }
            if (HydraMergeReviewFixture.IsGit(call, "reset") && call.Arguments.Contains("--soft"))
            {
                sync = true;
                if (editedAgain) File.WriteAllText(Path.Combine(p.Path, "a.txt"), "edited again\n");
            }
        });
        Assert.True(sync);
        Assert.Equal(remoteHead, repo.Git("rev-parse", branch + "^").Trim());
        Assert.Equal(merged, repo.Git("show", branch + ":a.txt"));
        Assert.Equal(editedAgain ? "edited again\n" : merged, File.ReadAllText(Path.Combine(p.Path, "a.txt")));
        Assert.Equal("remote only\n", File.ReadAllText(Path.Combine(p.Path, "remote.txt")));
        Assert.Equal(repo.Git("rev-parse", branch).Trim(), repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(HydraMergeRecord.Outcome.Merged, Assert.Single(f.Runtime(lead).HydraMerges).HydraMergeRecordOutcome);
        Assert.Contains("The checkout is up to date.", Assert.Single(f.Notes(lead)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("unreadable")]
    [InlineData("write-failure")]
    [Trait("Req", "PR-HYDRAMERGE-131")] // App/AppModel+HydraMerge.swift:787,812,815-817
    public void Unreadable_merged_files_decline_before_reset_and_a_failed_atomic_write_keeps_the_already_moved_branch(string failure)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var index = File.ReadAllBytes(Path.Combine(p.Path, ".git", "index"));
        var clone = f.Clone(p);
        clone.Git("commit", "--allow-empty", "-q", "-m", "advance"); clone.Git("push", "origin", "main");
        var advanced = clone.Git("rev-parse", "HEAD").Trim();
        var path = failure == "unreadable" ? "gone.txt" : "a.txt";
        var map = new Dictionary<string,byte[]> { [path] = Encoding.UTF8.GetBytes("merged\n") };
        using var locked = failure == "write-failure" ? new FileStream(Path.Combine(p.Path, "a.txt"), FileMode.Open, FileAccess.Read, FileShare.Read) : null;
        // R-FS-1 / PA-0015's synchronous atomic-write retries await SwiftClock. A real clock lets those
        // prescribed retries finish while this actor is blocked in the synchronous write.
        using var clock = SwiftClock.Provider.Override(TimeProvider.System);
        var calls = new List<ShellInvocation>();
        var result = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            return await HydraMergeReviewFixture.Sync(new Git(p.Path), head, ["a.txt"], map);
        }, AppModelHarness.Bound);
        Assert.False(result);
        Assert.Equal(failure == "unreadable" ? head : advanced, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("a\n", File.ReadAllText(Path.Combine(p.Path, "a.txt")));
        if (failure == "unreadable")
        {
            Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "reset"));
            Assert.Equal(index, File.ReadAllBytes(Path.Combine(p.Path, ".git", "index")));
        }
        else Assert.Contains(calls, c => HydraMergeReviewFixture.IsGit(c, "reset") && c.Arguments.Contains("--soft"));
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("ancestor")]
    [InlineData("changed-list")]
    [InlineData("dirty-other")]
    [InlineData("head-moved")]
    [Trait("Rule", "R-FS-1")] // App/AppModel+HydraMerge.swift:780,790-805; partial Req-063 proof pending stream seam.
    public void Every_sync_decline_branch_preserves_working_content_and_the_terminal_commit(string decline)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(p);
        File.WriteAllText(Path.Combine(clone.Root, "other.txt"), "remote\n");
        clone.Git("add", "-A"); clone.Git("commit", "-q", "-m", "remote"); clone.Git("push", "origin", "main");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team\n");
        if (decline == "dirty-other") File.WriteAllText(Path.Combine(p.Path, "other.txt"), "local other\n");
        string? terminal = null;
        var calls = new List<ShellInvocation>();
        var result = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                calls.Add(call);
                if (decline == "fetch" && HydraMergeReviewFixture.IsGit(call, "fetch") || decline == "changed-list" && HydraMergeReviewFixture.IsGit(call, "diff") && call.Arguments.Contains("--name-only")) throw new IOException("decline probe");
                if (decline == "head-moved" && HydraMergeReviewFixture.IsGit(call, "rev-parse") && call.Arguments.Last() == "HEAD")
                {
                    using var suppress = ShellObserver.Observe(_ => { });
                    repo.Git("commit", "--allow-empty", "-q", "-m", "terminal"); terminal = repo.Git("rev-parse", "HEAD").Trim();
                }
            });
            return await HydraMergeReviewFixture.Sync(new Git(p.Path), decline == "ancestor" ? new string('f', 40) : head, ["a.txt"]);
        }, AppModelHarness.Bound);
        Assert.False(result);
        Assert.Equal(terminal ?? head, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("team\n", File.ReadAllText(Path.Combine(p.Path, "a.txt")));
        Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "reset"));
        if (decline == "dirty-other") Assert.Equal("local other\n", File.ReadAllText(Path.Combine(p.Path, "other.txt")));
        else Assert.False(File.Exists(Path.Combine(p.Path, "other.txt")));
    }
}
