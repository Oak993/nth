using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// Source: DroppyCode/App/AppModel+HydraMerge.swift (B3 52bc792), :271-348,871-882.
public sealed class AppModelHydraMergePreparationTests
{
    [Theory]
    [InlineData(1, false, 1, "main", false)]
    [InlineData(1, true, 2, "main", false)]
    [InlineData(3, false, 1, "main", false)]
    [InlineData(3, false, 1, "feature", true)]
    [InlineData(3, false, 1, "detached", false)]
    [Trait("Req", "PR-HYDRAMERGE-041")] // App/AppModel+HydraMerge.swift:322-331
    [Trait("Req", "PR-HYDRAMERGE-127")] // App/AppModel+HydraMerge.swift:273-327
    [Trait("Req", "PR-HYDRAMERGE-129")] // App/AppModel+HydraMerge.swift:274-276,314-321
    [Trait("Req", "PR-HYDRAMERGE-033")] // App/AppModel+HydraMerge.swift:277-308
    public void Moved_HEAD_reprepares_at_most_three_times_and_counts_offline_text_engine_calls_by_the_patch(int moves, bool changePatch, int messageCalls, string current, bool several)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        var repo = f.Repos[p.Id];
        if (current == "feature") repo.Git("checkout", "-b", current);
        if (current == "detached") repo.Git("checkout", "--detach");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team\n");
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        forge.ReplyAlways("Generated subject\n\n- Full body");
        forge.Reply(messageCalls, stdout: HydraMergeReviewFixture.Url); // Text calls precede forge create.
        f.H.Main.OnMain(() =>
        {
            f.Model.Settings.TextGeneration = TextGenerationChoice.Claude;
            f.Model.Settings.SetBinaryPath(ForgeStandIns.PathOf("glab"), ProviderKind.Claude);
        });
        var calls = new List<ShellInvocation>();
        var preparedHeads = new List<string>();
        var writing = new List<(string?, HydraMergeStage?)>();
        var tries = 0;
        var result = f.Pass(lead, p, several, observer: call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, "fetch")) writing.Add((f.Runtime(lead).HydraMergeStage, f.Runtime(lead).HydraMergeStep));
            if (!HydraMergeReviewFixture.IsGit(call, "commit-tree")) return;
            var args = HydraMergeReviewFixture.GitArgs(call);
            preparedHeads.Add(args[3]);
            tries++;
            if (tries > moves) return;
            using var suppress = ShellObserver.Observe(_ => { });
            repo.Git("commit", "--allow-empty", "-q", "-m", "terminal " + tries);
            if (changePatch) File.WriteAllText(Path.Combine(p.Path, "a.txt"), "changed patch\n");
        });
        var engineCalls = forge.Calls().Where(c => c.Arguments.Contains("-p")).ToArray();
        Assert.Equal(messageCalls, engineCalls.Length);
        var prompts = calls.Where(c => Path.GetFileNameWithoutExtension(c.Executable) == "glab" && c.Arguments.Contains("-p")).Select(c => Encoding.UTF8.GetString(c.Input!)).ToArray();
        Assert.All(prompts, text => Assert.Contains("Changed files:\nM a.txt", text, StringComparison.Ordinal));
        if (changePatch) Assert.NotEqual(prompts[0], prompts[1]);
        Assert.Equal(moves == 3 ? 3 : 2, tries);
        Assert.All(writing.Take(tries), s => Assert.Equal(("Writing the commit", HydraMergeStage.Committing), s));
        Assert.Equal(tries, preparedHeads.Distinct().Count());
        if (moves == 3)
        {
            Assert.Equal("Failed", result);
            var r = Assert.Single(f.Runtime(lead).HydraMerges);
            Assert.Equal((several ? "Project: " : "") + $"Hydra did not merge: the checkout kept moving.: Commits kept landing on `{(current == "detached" ? "main" : current)}` while the team's work was prepared. The work is in the checkout and goes out on the next try.", r.Detail);
            Assert.True(r.Retryable); Assert.Null(r.Label); Assert.Null(r.Url); Assert.Equal(0, r.Files);
            Assert.True(f.Runtime(lead).HasHydraMergeRetryPending);
            Assert.Empty(f.Notes(lead));
            Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "update-ref") || HydraMergeReviewFixture.IsGit(c, "push"));
            forge.Reply(forge.Count, stdout: "Retry subject"); forge.Reply(forge.Count + 1, stdout: HydraMergeReviewFixture.Url);
            f.H.Main.AdvanceMilliseconds(59_999);
            Assert.Single(f.Runtime(lead).HydraMerges);
            f.H.Main.AdvanceMilliseconds(1);
            f.H.Main.Loop.RunUntil(() => f.Runtime(lead).HydraMerges.Count == 2, AppModelHarness.Bound);
            Assert.Equal(HydraMergeRecord.Outcome.Merged, f.Runtime(lead).HydraMerges.Last().HydraMergeRecordOutcome);
            Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
        }
        else
        {
            Assert.Equal("Merged", result);
            var branch = f.Branch(lead);
            Assert.Equal(preparedHeads.Last(), repo.Git("rev-parse", branch + "^").Trim());
            Assert.Equal(changePatch ? "changed patch\n" : "team\n", repo.Git("show", branch + ":a.txt"));
            Assert.Equal("Generated subject\n\n- Full body", Encoding.UTF8.GetString(calls.Last(c => HydraMergeReviewFixture.IsGit(c, "commit-tree")).Input!));
            Assert.False(f.Runtime(lead).HasHydraMergeRetryPending);
            // Cache scope is one project pass: a second pass asks the offline engine anew.
            forge.Reply(forge.Count, stdout: "New pass subject");
            forge.Reply(forge.Count + 1, stdout: HydraMergeReviewFixture.Url);
            Assert.Equal("Merged", f.Pass(lead, p));
            Assert.Equal(messageCalls + 1, forge.Calls().Count(c => c.Arguments.Contains("-p")));
        }
    }

    [Theory]
    [InlineData("New chat", "team")]
    [InlineData("!!!", "team")]
    [InlineData("--Ä Équipe 一 ² Ⅻ--", "ä-équipe-一-²-ⅻ")]
    [Trait("Req", "PR-HYDRAMERGE-039")] // App/AppModel+HydraMerge.swift:871-882
    public void Slug_uses_the_source_title_sentinel_unicode_and_the_leads_stable_id_then_changes_on_rename(string title, string expected)
    {
        using var h = new AppModelHarness();
        var p = AppModelSamples.Project("Project", @"C:\repo");
        var lead = AppModelSamples.Thread(p) with { Id = new Guid("ABCDEF01-2345-6789-abcd-0123456789ab"), Title = title };
        if (title == "New chat") lead = lead with { Title = ChatThread.Untitled }; // Swift :875 uses the model's exact sentinel.
        Assert.Equal("hydra/" + expected + "-abcdef", HydraMergeProofFixture.Call("HydraBranchName", null, lead));
        Assert.Equal("hydra/" + expected + "-abcdef", HydraMergeProofFixture.Call("HydraBranchName", null, lead));
        Assert.Equal("hydra/renamed-abcdef", HydraMergeProofFixture.Call("HydraBranchName", null, lead with { Title = "Renamed" }));
        var longTitle = new string('A', 39) + "--" + new string('B', 20);
        Assert.Equal("hydra/" + new string('a', 39) + "-abcdef", HydraMergeProofFixture.Call("HydraBranchName", null, lead with { Title = longTitle }));
        Assert.Equal("hydra/" + new string('a', 40) + "-abcdef", HydraMergeProofFixture.Call("HydraBranchName", null, lead with { Title = new string('A', 60) }));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-129")] // App/AppModel+HydraMerge.swift:316; Swift String == is canonically equivalent.
    public void Canonically_equivalent_patch_text_reuses_the_message_across_a_moved_HEAD()
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        // Git's real textconv extension changes only the diff's Unicode normalization. The underlying
        // blob hashes stay identical across the two tries, so the whole patches are canonically equal.
        var script = Path.Combine(p.Path, ".git", "canonical.sh");
        var marker = Path.Combine(p.Path, ".git", "decompose");
        File.WriteAllText(script, "if [ -f '" + marker.Replace('\\', '/') + "' ]; then sed 's/é/e\u0301/g' \"$1\"; else cat \"$1\"; fi\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(p.Path, ".git", "info", "attributes"), "a.txt diff=canonical\n");
        repo.Git("config", "diff.canonical.textconv", "sh \"" + script.Replace('\\', '/') + "\"");
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "é\n");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        var forge = new ForgeScript(p.Path);
        forge.Reply(0, stdout: "Canonical subject"); forge.Reply(1, stdout: HydraMergeReviewFixture.Url);
        f.H.Main.OnMain(() =>
        {
            f.Model.Settings.TextGeneration = TextGenerationChoice.Claude;
            f.Model.Settings.SetBinaryPath(ForgeStandIns.PathOf("glab"), ProviderKind.Claude);
        });
        var tries = 0;
        var result = f.Pass(lead, p, observer: call =>
        {
            if (!HydraMergeReviewFixture.IsGit(call, "commit-tree") || ++tries != 1) return;
            using var suppress = ShellObserver.Observe(_ => { });
            File.WriteAllText(marker, "decompose");
            repo.Git("commit", "--allow-empty", "-q", "-m", "terminal");
        });
        Assert.True(result == "Merged", string.Join("\n", f.Runtime(lead).HydraMerges.Select(r => r.Detail)));
        Assert.Equal(2, tries);
        Assert.Single(forge.Calls(), c => c.Arguments.Contains("-p"));
        Assert.Equal("Canonical subject\n", repo.Git("show", "-s", "--format=%B", f.Branch(lead)).TrimEnd('\n') + "\n");
    }

    [Theory]
    [InlineData("fetch")]
    [InlineData("missing-ref")]
    [InlineData("equal")]
    [InlineData("not-ancestor")]
    [InlineData("feature")]
    [Trait("Req", "PR-HYDRAMERGE-033")] // App/AppModel+HydraMerge.swift:280-308
    [Trait("Req", "PR-HYDRAMERGE-128")] // App/AppModel+HydraMerge.swift:285-287
    public void Every_remote_rebuild_fallback_captures_only_team_paths_on_HEAD(string fallback)
    {
        using var f = new HydraMergeProofFixture();
        var p = f.AddProject("Project");
        var repo = f.Repos[p.Id];
        if (fallback == "not-ancestor") repo.Git("commit", "--allow-empty", "-q", "-m", "local ahead");
        if (fallback == "feature") repo.Git("checkout", "-b", "feature");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(p.Path, "a.txt"), "team\n");
        File.WriteAllText(Path.Combine(p.Path, "unrelated.txt"), "user\n");
        var lead = f.AddLead(p, "Lead", ["a.txt"]);
        f.Start(lead);
        f.Forge(p);
        var calls = new List<ShellInvocation>();
        Assert.Equal("Merged", f.Pass(lead, p, observer: call =>
        {
            calls.Add(call);
            if (f.Runtime(lead).HydraMergeStep != HydraMergeStage.Committing) return;
            if (fallback == "fetch" && HydraMergeReviewFixture.IsGit(call, "fetch") ||
                fallback == "missing-ref" && HydraMergeReviewFixture.IsGit(call, "rev-parse") && call.Arguments.Last() == "origin/main") throw new IOException("offline fallback");
        }));
        var branch = fallback == "feature" ? "feature" : f.Branch(lead);
        Assert.Equal(head, repo.Git("rev-parse", branch + "^").Trim());
        Assert.False(repo.Raw("cat-file", "-e", branch + ":unrelated.txt").Succeeded);
        var add = Assert.Single(calls, c => HydraMergeReviewFixture.IsGit(c, "add"));
        // Services/Git/Git.swift:265-278: scratch capture includes --ignore-errors and a 300-second add.
        Assert.Equal(["add", "-A", "--ignore-errors", "--pathspec-from-file=-", "--pathspec-file-nul"], HydraMergeReviewFixture.GitArgs(add));
        Assert.Equal(300, add.Timeout);
        Assert.Equal(Encoding.UTF8.GetBytes("a.txt\0"), add.Input);
        Assert.Contains(add.Environment!.Keys, k => k == "GIT_INDEX_FILE");
        Assert.Equal(["read-tree", "HEAD"], HydraMergeReviewFixture.GitArgs(Assert.Single(calls, c => HydraMergeReviewFixture.IsGit(c, "read-tree"))));
        if (fallback == "feature") Assert.DoesNotContain(calls, c => HydraMergeReviewFixture.IsGit(c, "fetch"));
    }
}
