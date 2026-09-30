using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

// B3 52bc792: AppModel+HydraMerge.swift:778-817; OD-0020 rollback and final HEAD-check amendment.
public sealed class AppModelHydraMergeSafetyTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void A_valid_divergent_local_commit_declines_before_reading_changed_paths_or_mutating_the_checkout()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        var clone = f.Clone(project);
        File.WriteAllText(Path.Combine(clone.Root, "other.txt"), "remote\n");
        clone.Git("add", "other.txt"); clone.Git("commit", "-q", "-m", "remote advance"); clone.Git("push", "origin", "main");
        repo.Git("commit", "--allow-empty", "-q", "-m", "valid local divergence");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team\n");
        var index = File.ReadAllBytes(Path.Combine(project.Path, ".git", "index"));
        var calls = new List<ShellInvocation>();
        var result = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(calls.Add);
            return await HydraMergeReviewFixture.Sync(new Git(project.Path), head, ["a.txt"]);
        }, AppModelHarness.Bound);
        Assert.False(result);
        Assert.Equal(head, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(index, File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")));
        Assert.Equal("team\n", File.ReadAllText(Path.Combine(project.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(project.Path, "other.txt")));
        Assert.DoesNotContain(calls, call => HydraMergeReviewFixture.IsGit(call, "diff") || HydraMergeReviewFixture.IsGit(call, "reset"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void A_terminal_commit_at_either_HEAD_check_is_preserved_and_declines_at_that_check(int changedAtCheck)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, "other.txt"), "baseline other\n");
        repo.Git("add", "other.txt"); repo.Git("commit", "-q", "-m", "baseline"); repo.Git("push", "origin", "main");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(project);
        File.WriteAllText(Path.Combine(clone.Root, "other.txt"), "remote other\n");
        clone.Git("add", "other.txt"); clone.Git("commit", "-q", "-m", "remote"); clone.Git("push", "origin", "main");
        File.WriteAllText(Path.Combine(project.Path, "a.txt"), "team\n");
        var reads = 0;
        string? terminal = null;
        byte[]? terminalIndex = null;
        var calls = new List<ShellInvocation>();
        var result = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                calls.Add(call);
                if (!HydraMergeReviewFixture.GitArgs(call).SequenceEqual(["rev-parse", "--verify", "HEAD"])) return;
                reads++;
                if (reads != changedAtCheck) return;
                using var suppress = ShellObserver.Observe(_ => { });
                repo.Git("commit", "--allow-empty", "-q", "-m", "terminal commit during sync");
                terminal = repo.Git("rev-parse", "HEAD").Trim();
                terminalIndex = File.ReadAllBytes(Path.Combine(project.Path, ".git", "index"));
            });
            return await HydraMergeReviewFixture.Sync(new Git(project.Path), head, ["a.txt"]);
        }, AppModelHarness.Bound);
        Assert.False(result);
        Assert.NotNull(terminal);
        Assert.NotEqual(head, terminal);
        Assert.Equal(changedAtCheck, reads); // the first mismatch must not be hidden by the later guard.
        Assert.Equal(terminal, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal(terminalIndex, File.ReadAllBytes(Path.Combine(project.Path, ".git", "index")));
        Assert.Equal("team\n", File.ReadAllText(Path.Combine(project.Path, "a.txt")));
        Assert.Equal("baseline other\n", File.ReadAllText(Path.Combine(project.Path, "other.txt")));
        Assert.DoesNotContain(calls, call => HydraMergeReviewFixture.IsGit(call, "reset") || HydraMergeReviewFixture.IsGit(call, "checkout") || HydraMergeReviewFixture.IsGit(call, "rm"));
    }

    [Theory]
    [InlineData("unchanged")]
    [InlineData("edited-same-time")]
    [InlineData("deleted")]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void Sync_excludes_own_remote_changes_resets_their_index_and_writes_merged_bytes_only_after_other_paths(string laterEdit)
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, "kept.txt"), "baseline kept\n");
        File.WriteAllText(Path.Combine(project.Path, "gone.txt"), "baseline gone\n");
        repo.Git("add", "kept.txt", "gone.txt"); repo.Git("commit", "-q", "-m", "baseline"); repo.Git("push", "origin", "main");
        var head = repo.Git("rev-parse", "HEAD").Trim();
        var clone = f.Clone(project);
        File.WriteAllText(Path.Combine(clone.Root, "a.txt"), "remote own\n");
        File.WriteAllText(Path.Combine(clone.Root, "kept.txt"), "remote kept\n");
        File.Delete(Path.Combine(clone.Root, "gone.txt"));
        clone.Git("add", "a.txt", "kept.txt", "gone.txt"); clone.Git("commit", "-q", "-m", "remote"); clone.Git("push", "origin", "main");
        var remote = clone.Git("rev-parse", "HEAD").Trim();
        var ownFile = Path.Combine(project.Path, "a.txt");
        File.WriteAllText(ownFile, "team working\n");
        repo.Git("add", "a.txt"); // distinguish index reset from preserving the team's staged bytes.
        var initialWritten = File.GetLastWriteTimeUtc(ownFile);
        var calls = new List<ShellInvocation>();
        var result = f.H.Main.Loop.Run(async () =>
        {
            using var scope = ShellObserver.Observe(call =>
            {
                calls.Add(call);
                if (HydraMergeReviewFixture.IsGit(call, "reset") && call.Arguments.Contains("--soft"))
                {
                    if (laterEdit == "edited-same-time")
                    {
                        File.WriteAllText(ownFile, "late edit\n");
                        File.SetLastWriteTimeUtc(ownFile, initialWritten); // matching timestamp cannot authorize replacement.
                    }
                    else if (laterEdit == "deleted") File.Delete(ownFile);
                }
                if (HydraMergeReviewFixture.IsGit(call, "rm"))
                {
                    Assert.Equal("remote kept\n", File.ReadAllText(Path.Combine(project.Path, "kept.txt")));
                    if (laterEdit == "deleted") Assert.False(File.Exists(ownFile));
                    else Assert.Equal(laterEdit == "unchanged" ? "team working\n" : "late edit\n", File.ReadAllText(ownFile));
                }
            });
            return await HydraMergeReviewFixture.Sync(new Git(project.Path), head, ["a.txt"],
                new Dictionary<string, byte[]> { ["a.txt"] = Encoding.UTF8.GetBytes("merged bytes\n") });
        }, AppModelHarness.Bound);
        Assert.True(result);
        Assert.Equal(remote, repo.Git("rev-parse", "HEAD").Trim());
        Assert.Equal("remote own\n", repo.Git("show", ":a.txt"));
        if (laterEdit == "deleted") Assert.False(File.Exists(ownFile));
        else Assert.Equal(laterEdit == "unchanged" ? "merged bytes\n" : "late edit\n", File.ReadAllText(ownFile));
        Assert.Equal("remote kept\n", File.ReadAllText(Path.Combine(project.Path, "kept.txt")));
        Assert.False(File.Exists(Path.Combine(project.Path, "gone.txt")));
        Assert.False(repo.Raw("ls-files", "--error-unmatch", "gone.txt").Succeeded);
        var verbs = calls.Where(call => HydraMergeReviewFixture.IsGit(call, "reset") || HydraMergeReviewFixture.IsGit(call, "checkout") || HydraMergeReviewFixture.IsGit(call, "rm"))
            .Select(call => HydraMergeReviewFixture.GitArgs(call)[0]).ToArray();
        Assert.Equal(["reset", "reset", "checkout", "rm"], verbs);
        var reset = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "reset") && !call.Arguments.Contains("--soft"));
        Assert.Equal(["reset", "--quiet", "--pathspec-from-file=-", "--pathspec-file-nul"], HydraMergeReviewFixture.GitArgs(reset));
        Assert.Equal(Encoding.UTF8.GetBytes("a.txt\0"), reset.Input);
        var batch = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "cat-file"));
        Assert.Equal(Encoding.UTF8.GetBytes("origin/main:gone.txt\0origin/main:kept.txt\0"), batch.Input);
        Assert.Contains(calls, call => HydraMergeReviewFixture.GitArgs(call).SequenceEqual(["checkout", "--quiet", "origin/main", "--", "kept.txt"]));
        Assert.Contains(calls, call => HydraMergeReviewFixture.GitArgs(call).SequenceEqual(["rm", "--quiet", "-f", "--", "gone.txt"]));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-063")]
    public void The_compiled_sync_uses_atomic_writeback_after_the_source_ordered_Git_operations()
    {
        var sync = typeof(AppModel).GetMethod("SyncDefaultBranch", BindingFlags.Static | BindingFlags.NonPublic)!;
        var machine = sync.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var calls = Instructions(machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!)
            .Select(instruction => instruction.Member).OfType<MethodInfo>()
            .Where(member => member.DeclaringType == typeof(Git) && member.Name is "ResetSoft" or "ResetIndex" or "CheckoutPaths" or "RemovePathsAfterSoftReset" ||
                member.DeclaringType == typeof(FileOps) && member.Name == "WriteAtomic")
            .Select(member => member.Name).ToArray();
        Assert.Equal(["ResetSoft", "ResetIndex", "CheckoutPaths", "RemovePathsAfterSoftReset", "WriteAtomic", "ResetSoft", "WriteAtomic", "WriteAtomic"], calls);
    }
    private static IEnumerable<(OpCode Code, MemberInfo? Member)> Instructions(MethodInfo method)
    {
        var bytes = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        var codes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(code => unchecked((ushort)code.Value));
        for (var index = 0; index < bytes.Length;)
        {
            ushort value = bytes[index++];
            if (value == 0xfe) value = (ushort)(0xfe00 | bytes[index++]);
            var code = codes[value];
            var size = code.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, index),
                _ => 4,
            };
            MemberInfo? member = code.OperandType == OperandType.InlineMethod
                ? method.Module.ResolveMethod(BitConverter.ToInt32(bytes, index), method.DeclaringType?.GetGenericArguments(), method.GetGenericArguments())
                : null;
            yield return (code, member);
            index += size;
        }
    }
}
