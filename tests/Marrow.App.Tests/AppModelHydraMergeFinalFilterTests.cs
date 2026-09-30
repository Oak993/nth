using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text;
using Marrow.App.Tests.Fixtures;
using Marrow.Core.Models;
using Marrow.Core.Port;
using Marrow.Services.Git;

namespace Marrow.App.Tests;

// B3 52bc792: AppModel+HydraMerge.swift:646-663; Git.swift:426-459. Real local Git, no output replacement.
public sealed class AppModelHydraMergeFinalFilterTests
{
    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-077")]
    public void Final_filters_apply_claim_build_ignore_tracking_and_disk_rules_in_order_after_a_real_sweep()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, ".gitignore"), "i-ignored.txt\n");
        File.WriteAllText(Path.Combine(project.Path, "g-deleted.txt"), "tracked\n");
        repo.Git("add", ".gitignore", "g-deleted.txt");
        repo.Git("commit", "-q", "-m", "filter baseline");
        var checkpoint = repo.Git("rev-parse", "HEAD^{tree}").Trim();
        string[] reported = ["k-blocked.txt", "j-missing.txt", "i-ignored.txt", "h-late.txt", "g-deleted.txt",
            "f-dir", "e-file.txt", "d-reserved.txt", "c-lead.txt", "b-head.txt", "a-landing.txt"];
        var lead = f.AddLead(project, "Lead", reported, new Guid("00000000-0000-0000-0000-000000000001"), checkpoint: checkpoint);
        var blocker = f.AddLead(project, "Blocker", ["k-blocked.txt", "a-landing.txt", "b-head.txt", "c-lead.txt"],
            status: TurnStatus.Interrupted, hydra: false);
        var reservation = f.AddLead(project, "Reservation", ["d-reserved.txt"], new Guid("00000000-0000-0000-0000-000000000002"));
        var head = f.AddHead(lead, ["a-landing.txt"]);
        f.Tool(head, " b-head.txt "); // cold head tool edits and its landing are this job's own writes.
        f.Tool(lead, " c-lead.txt ");
        foreach (var path in new[] { "a-landing.txt", "b-head.txt", "c-lead.txt", "d-reserved.txt", "e-file.txt", "i-ignored.txt", "k-blocked.txt" })
            File.WriteAllText(Path.Combine(project.Path, path), "working\n");
        Directory.CreateDirectory(Path.Combine(project.Path, "f-dir"));
        Directory.CreateDirectory(Path.Combine(project.Path, "node_modules"));
        File.WriteAllText(Path.Combine(project.Path, "node_modules", "generated.txt"), "swept build\n");
        File.Delete(Path.Combine(project.Path, "g-deleted.txt"));
        f.Start(lead, blocker, reservation);
        var claims = f.Owned(lead, project);
        Assert.Equal("Reservation", claims.Reserved["d-reserved.txt"]);
        Assert.False(claims.Blocking.ContainsKey("d-reserved.txt"));
        foreach (var path in new[] { "a-landing.txt", "b-head.txt", "c-lead.txt", "k-blocked.txt" }) Assert.True(claims.Blocking.ContainsKey(path));

        var calls = new List<ShellInvocation>();
        object work;
        using (ShellObserver.Observe(call =>
        {
            calls.Add(call);
            if (HydraMergeReviewFixture.IsGit(call, "cat-file"))
                File.WriteAllText(Path.Combine(project.Path, "h-late.txt"), "arrived during tracking\n");
        })) work = f.Work(lead, project, sweep: true);

        string[] beforeIgnore = ["a-landing.txt", "b-head.txt", "c-lead.txt", "d-reserved.txt", "e-file.txt", "f-dir",
            "g-deleted.txt", "h-late.txt", "i-ignored.txt", "j-missing.txt"];
        var ignored = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "check-ignore"));
        Assert.Equal(["check-ignore", "-z", "--stdin"], HydraMergeReviewFixture.GitArgs(ignored));
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join('\0', beforeIgnore) + '\0'), ignored.Input);
        var batch = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "cat-file"));
        Assert.True(calls.IndexOf(ignored) < calls.IndexOf(batch));
        Assert.Equal(["cat-file", "--batch-check=%(objecttype)", "-Z"], HydraMergeReviewFixture.GitArgs(batch));
        var afterIgnore = beforeIgnore.Where(path => path != "i-ignored.txt").ToArray();
        Assert.Equal(Encoding.UTF8.GetBytes(string.Join('\0', afterIgnore.Select(path => "HEAD:" + path)) + '\0'), batch.Input);
        string[] expected = ["a-landing.txt", "b-head.txt", "c-lead.txt", "d-reserved.txt", "e-file.txt", "f-dir", "g-deleted.txt", "h-late.txt"];
        Assert.Equal(expected, HydraMergeProofFixture.Property<ValueList<string>>(work, "Paths"));
        Assert.Equal(new[] { "Heads", "Outside", "Paths", "TurnIDs", "HeadIDs" }.Order(StringComparer.Ordinal),
            work.GetType().GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)); // no claimed/held map.

        // Demonstrate that build output really reached the swept tree; it was never reported by the turn.
        var writeTree = Assert.Single(calls, call => HydraMergeReviewFixture.IsGit(call, "write-tree"));
        Assert.Contains(calls, call => HydraMergeReviewFixture.IsGit(call, "add") && call.Environment?.ContainsKey("GIT_INDEX_FILE") == true);
        var diffs = calls.Where(call => HydraMergeReviewFixture.IsGit(call, "diff") && call.Arguments.Contains("--name-only")).ToArray();
        Assert.Equal(2, diffs.Length);
        Assert.True(calls.IndexOf(writeTree) < calls.IndexOf(diffs[1]));
        var sweptTree = HydraMergeReviewFixture.GitArgs(diffs[1])[^1];
        Assert.Equal("blob", repo.Git("cat-file", "-t", sweptTree + ":node_modules/generated.txt").Trim());
        Assert.DoesNotContain("node_modules/generated.txt", reported);
        Assert.Equal(f.Documents[lead.Id].Turns.Select(turn => turn.Id), HydraMergeProofFixture.Property<ValueList<Guid>>(work, "TurnIDs"));
        var turnsBefore = f.Runtime(lead).HydraUnmergedTurns.Select(turn => turn.Id).ToArray();
        f.H.Main.OnMain(() => f.RemoveThread(blocker));
        Assert.Equal(expected.Append("k-blocked.txt"), HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));
        Assert.Equal(turnsBefore, f.Runtime(lead).HydraUnmergedTurns.Select(turn => turn.Id));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-077")]
    public void Windows_disk_case_insensitivity_keeps_an_existing_alias_but_Git_tracks_only_the_exact_case_after_deletion()
    {
        using var f = new HydraMergeProofFixture();
        var project = f.AddProject("Project");
        var repo = f.Repos[project.Id];
        File.WriteAllText(Path.Combine(project.Path, "Case.txt"), "tracked\n");
        repo.Git("add", "Case.txt"); repo.Git("commit", "-q", "-m", "case baseline");
        var lead = f.AddLead(project, "Lead", ["case.txt", "Case.txt"]);
        f.Start(lead);
        Assert.True(File.Exists(Path.Combine(project.Path, "case.txt"))); // actual Windows disk lookup.
        Assert.Equal(1, repo.Raw("check-ignore", "--", "Case.txt", "case.txt").Status);
        var tracked = f.H.Main.Loop.Run(() => new Git(project.Path).TrackedPaths(["Case.txt", "case.txt"]), AppModelHarness.Bound);
        Assert.Equal(["Case.txt"], tracked.Order(StringComparer.Ordinal));
        Assert.False(tracked.Contains("case.txt")); // exact Git tree name, not core.ignorecase's index lookup.
        Assert.Equal(["Case.txt", "case.txt"], HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));
        File.Delete(Path.Combine(project.Path, "Case.txt"));
        Assert.False(File.Exists(Path.Combine(project.Path, "case.txt")));
        Assert.Equal(["Case.txt"], HydraMergeProofFixture.Property<ValueList<string>>(f.Work(lead, project), "Paths"));
    }

    [Fact]
    [Trait("Req", "PR-HYDRAMERGE-077")]
    public void The_compiled_final_filters_preserve_the_source_order_and_accept_check_ignore_status_one()
    {
        var method = typeof(AppModel).GetMethod("HydraWorkFor", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var machine = method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var calls = Instructions(machine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!)
            .Select(instruction => instruction.Member).OfType<MethodInfo>()
            .Where(member => member.DeclaringType == typeof(HashSet<string>) && member.Name is "ExceptWith" or "RemoveWhere" ||
                member.DeclaringType == typeof(Git) && member.Name is "IgnoredPaths" or "TrackedPaths")
            .Select(member => member.Name).ToArray();
        Assert.Equal(["ExceptWith", "RemoveWhere", "IgnoredPaths", "ExceptWith", "TrackedPaths", "ExceptWith"], calls);
        var ignored = typeof(Git).GetMethod("IgnoredPaths")!;
        var ignoredMachine = ignored.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType;
        var instructions = Instructions(ignoredMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic)!).ToArray();
        var status = Assert.Single(instructions.Select((instruction, index) => (instruction, index)),
            item => item.instruction.Member is MethodInfo { Name: "get_Status" });
        Assert.Equal(OpCodes.Ldc_I4_1.Value, instructions[status.index + 1].Code.Value); // Git.swift:428 accepts status 1.
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
