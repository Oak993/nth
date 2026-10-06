using Marrow.Services.Port.Skills.Providers;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// PORT-ONLY: SK-11e / WU-10 (r2) Command Code probe.
public sealed class CommandCodeSkillFlagProbeTests
{
    private const string Listed = "Options:\n  -p, --print\n  --skill <path>   Load extra skills from a path (a skill directory or a directory of skills); repeatable\n  --no-skills\n";
    private const string Old = "Options:\n  -p, --print\n  --resume <id>\n  --no-skills\n  --skills-dir x\n";

    private static string Exe(string label, string content = "x")
    {
        var path = Path.Combine(SkillTapFixtures.TempFolder(label), "cmd.exe");
        File.WriteAllText(path, content);
        return path;
    }

    private static (CommandCodeSkillFlagProbe Probe, Func<int> Calls) Make(Func<string, CancellationToken, Task<string?>> run)
    {
        var calls = 0;
        return (new CommandCodeSkillFlagProbe((p, c) => { Interlocked.Increment(ref calls); return run(p, c); }), () => calls);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task A_help_that_lists_the_flag_is_true_and_runs_once_per_identity()
    {
        var (probe, calls) = Make((_, _) => Task.FromResult<string?>(Listed));
        var exe = Exe("listed");
        Assert.False(probe.Listed(exe)); // before the probe: unknown means no bundle
        Assert.True(await probe.Probe(exe));
        Assert.True(await probe.Probe(exe));
        Assert.True(probe.Listed(exe));
        Assert.Equal(1, calls());
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task A_help_shaped_like_0_52_5_is_false_even_with_no_skills_and_skills_dir_lines()
    {
        var (probe, _) = Make((_, _) => Task.FromResult<string?>(Old));
        var exe = Exe("old");
        Assert.False(await probe.Probe(exe));
        Assert.False(probe.Listed(exe));
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task A_failing_throwing_or_null_runner_means_not_listed()
    {
        foreach (var run in new Func<string, CancellationToken, Task<string?>>[]
                 { (_, _) => Task.FromResult<string?>(null), (_, _) => throw new IOException("x"), (_, _) => Task.FromException<string?>(new TimeoutException()) })
        {
            var (probe, _) = Make(run);
            var exe = Exe("fail");
            Assert.False(await probe.Probe(exe));
            Assert.False(probe.Listed(exe));
        }
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task A_changed_executable_is_probed_again_and_a_missing_one_is_false_without_running()
    {
        var (probe, calls) = Make((_, _) => Task.FromResult<string?>(Listed));
        var exe = Exe("changed");
        await probe.Probe(exe);
        File.WriteAllText(exe, "a longer file content");
        Assert.False(probe.Listed(exe)); // new identity, nothing stored yet
        await probe.Probe(exe);
        Assert.Equal(2, calls());
        Assert.False(await probe.Probe(Path.Combine(Path.GetDirectoryName(exe)!, "gone.exe")));
        Assert.Equal(2, calls());
    }

    // Engineering choice: ED-365 (append-only ledger heading, not a numbered Decision trait).
    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public void The_real_runner_has_a_bounded_timeout_and_the_turn_path_member_never_runs_anything()
    {
        Assert.Equal(10, CommandCodeHelpRunner.TimeoutSeconds);
        var (probe, calls) = Make((_, _) => Task.FromResult<string?>(Listed));
        _ = probe.Listed(Exe("turn"));
        Assert.Equal(0, calls());
    }
}
