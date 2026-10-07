using Marrow.Core.Port;
using Marrow.Services.Port.Skills.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Providers;

// SK-11e / DL-6 every child PATH and WU-10 single-flight by executable identity.
public sealed class SkillsProviderWindowsReviewTests
{
    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-9")]
    public void Tools_only_with_no_skill_generation_still_puts_the_bound_bin_before_WindowsApps()
    {
        using var scratch = new StorageScratchFolder();
        var bin = Path.Combine(scratch.Root, "addons", "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "installed-tool-marker.txt"), "fixture only; never executable");
        var apps = Path.Combine(scratch.Root, "Microsoft", "WindowsApps");
        var path = string.Join(Path.PathSeparator, "first", apps, "last");
        var env = new ValueMap<string,string>(new Dictionary<string,string> { ["PATH"] = path, ["UNCHANGED"] = "bytes" });
        var child = ProviderSkillEnvironment.Apply(env, null);
        Assert.Equal(string.Join(Path.PathSeparator, "first", bin, apps, "last"), child["PATH"]);
        Assert.Equal("bytes", child["UNCHANGED"]);
        Assert.Equal(env.Count, child.Count);
        Assert.Equal(path, env["PATH"]);
    }

    [Fact, Trait("Feature", "SK-11e"), Trait("Rule", "R-TEST-4")]
    public async Task Concurrent_capability_checks_share_one_help_runner_for_the_same_identity()
    {
        var executable = Path.Combine(SkillTapFixtures.TempFolder("single-flight-review"), "identity-only.txt");
        File.WriteAllText(executable, "fixture identity; never launched");
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResult = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var probe = new CommandCodeSkillFlagProbe((_, _) =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("test runner gate");
            return Task.FromResult<string?>("Options: --skill <path>");
        });
        var first = Task.Run(() => probe.Probe(executable));
        async Task RunSecond()
        {
            try { secondResult.TrySetResult(await probe.Probe(executable)); }
            catch (Exception error) { secondResult.TrySetException(error); }
        }
        var second = new Thread(() => _ = RunSecond()) { IsBackground = true, Name = "test-owned second capability check" };
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second.Start();
            // Positive synchronization: old code blocks inside its second runner; fixed code blocks
            // on the already-published Lazy. Do not release the first factory before this witness.
            Assert.True(await Task.Run(() => SpinWait.SpinUntil(() => (second.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5))));
        }
        finally { release.Set(); }
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(await secondResult.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(second.Join(TimeSpan.FromSeconds(5)));
        Assert.True(probe.Listed(executable));
        Assert.Equal(1, calls);
    }
}
