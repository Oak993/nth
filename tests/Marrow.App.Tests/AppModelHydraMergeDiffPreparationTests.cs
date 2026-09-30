using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.App.Tests;

// B3 52bc792: AppModel+HydraMerge.swift:277-327,822-833. Existing offline engine/forge stand-in only.
public sealed class AppModelHydraMergeDiffPreparationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-034")]
    public void Each_preparation_try_diffs_its_actual_parent_to_its_captured_tree_once_including_remote_rebuild_and_retry(bool retry)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(project);
        File.WriteAllText(Path.Combine(clone.Root, "remote-only.txt"), "remote only\n");
        clone.Git("add", "remote-only.txt"); clone.Git("commit", "-q", "-m", "remote advance"); clone.Git("push", "origin", "main");
        var remote = clone.Git("rev-parse", "HEAD").Trim();
        var lead = f.AddLead(project, "Lead", ["a.txt"]);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team\n");
        f.Start(lead); f.Forge(project);
        var calls = new List<ShellInvocation>();
        var tries = 0;
        string? terminal = null;
        Assert.Equal("Merged", f.Pass(lead, project, observer: call =>
        {
            calls.Add(call);
            if (!HydraMergeReviewFixture.IsGit(call, "commit-tree")) return;
            tries++;
            if (!retry || tries != 1) return;
            using var suppress = ShellObserver.Observe(_ => { });
            repo.Git("commit", "--allow-empty", "-q", "-m", "terminal commit between preparation tries");
            terminal = repo.Git("rev-parse", "HEAD").Trim();
        }));
        var diffs = calls.Where(call => HydraMergeReviewFixture.IsGit(call, "diff") && !call.Arguments.Contains("--name-only")).ToArray();
        var commits = calls.Where(call => HydraMergeReviewFixture.IsGit(call, "commit-tree")).ToArray();
        Assert.Equal(retry ? 2 : 1, tries);
        Assert.Equal(tries, diffs.Length);
        Assert.Equal(tries, commits.Length);
        Assert.NotEqual(head, remote);
        for (var index = 0; index < tries; index++)
        {
            var commit = HydraMergeReviewFixture.GitArgs(commits[index]);
            var parent = index == 0 ? remote : terminal!;
            Assert.Equal(["commit-tree", commit[1], "-p", parent, "-F", "-"], commit);
            Assert.Equal(["diff", "--no-ext-diff", "-M", parent, commit[1]], HydraMergeReviewFixture.GitArgs(diffs[index]));
            Assert.True(calls.IndexOf(diffs[index]) < calls.IndexOf(commits[index]));
            if (index > 0) Assert.True(calls.IndexOf(commits[index - 1]) < calls.IndexOf(diffs[index]));
        }
        Assert.Equal("remote only\n", repo.Git("show", HydraMergeReviewFixture.GitArgs(commits[0])[1] + ":remote-only.txt"));
        Assert.Equal("team\n", repo.Git("show", f.Branch(lead) + ":a.txt"));
        Assert.Equal(1, Assert.Single(f.Runtime(lead).HydraMerges).Files);
        if (retry) Assert.NotNull(terminal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Req", "PR-HYDRAMERGE-034")]
    public void Parsed_file_changes_and_the_exact_patch_reach_the_existing_offline_commit_engine_and_a_thrown_diff_reaches_it_empty(bool throwDiff)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, "gone.txt"), "old gone\n");
        repo.Git("add", "gone.txt"); repo.Git("commit", "-q", "-m", "baseline deletion"); repo.Git("push", "origin", "main");
        var parent = repo.Git("rev-parse", "HEAD").Trim();
        var lead = f.AddLead(project, "Lead", ["a.txt", "gone.txt", "new.txt"]);
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team\n");
        File.Delete(Path.Combine(project.Path, "gone.txt"));
        File.WriteAllText(Path.Combine(project.Path, "new.txt"), "new team file\n");
        f.Start(lead);
        var forge = new ForgeScript(project.Path);
        const string message = "Offline subject\n\n- Offline detail";
        forge.ReplyAlways(message);
        forge.Reply(1, stdout: HydraMergeReviewFixture.Url);
        var enginePath = ForgeStandIns.PathOf("glab");
        f.H.Main.OnMain(() =>
        {
            f.Model.Settings.TextGeneration = TextGenerationChoice.Claude;
            f.Model.Settings.SetBinaryPath(enginePath, ProviderKind.Claude);
        });
        var calls = new List<ShellInvocation>();
        Assert.Equal("Merged", f.Pass(lead, project, observer: call =>
        {
            calls.Add(call);
            if (throwDiff && HydraMergeReviewFixture.IsGit(call, "diff") && !call.Arguments.Contains("--name-only"))
                throw new IOException("offline diff failure");
        }));
        var engine = Assert.Single(calls, call => Path.GetFileNameWithoutExtension(call.Executable) == "glab" && call.Arguments.Contains("-p"));
        Assert.Equal(enginePath.Replace('\\', '/'), engine.Executable); // ProviderRegistry.FileUrl: Windows executable spelling uses forward separators.
        Assert.Equal(project.Path, engine.Directory);
        var prompt = Encoding.UTF8.GetString(engine.Input!);
        var commit = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "commit-tree"));
        Assert.Equal(message, Encoding.UTF8.GetString(commit.Input!));
        var tree = HydraMergeReviewFixture.GitArgs(commit)[1];
        var patch = throwDiff ? "" : repo.Git("diff", "--no-ext-diff", "-M", parent, tree);
        var summary = throwDiff ? "" : "M a.txt\nD gone.txt\nA new.txt";
        Assert.EndsWith("Changed files:\n" + summary + "\n\nPatch:\n" + patch, prompt, StringComparison.Ordinal);
        Assert.Equal(throwDiff ? 0 : 3, Assert.Single(f.Runtime(lead).HydraMerges).Files);
        Assert.Equal("team\n", repo.Git("show", f.Branch(lead) + ":a.txt"));
        Assert.Equal("new team file\n", repo.Git("show", f.Branch(lead) + ":new.txt"));
        Assert.False(repo.Raw("cat-file", "-e", f.Branch(lead) + ":gone.txt").Succeeded);
    }
}