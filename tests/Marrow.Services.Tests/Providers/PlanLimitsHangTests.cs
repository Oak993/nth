using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: owner-directed bounded Claude usage probe, ED-NEW-planlimits-hang-1.
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class PlanLimitsHangTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Decision", "ED-NEW-planlimits-hang-1")]
    public async Task A_stalled_probe_ends_at_twenty_seconds_without_a_main_actor_turn_and_leaves_no_child(bool partialFrame)
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("usage-stall");
        File.WriteAllText(Path.Combine(script.ControlDirectory, "stall.txt"), partialFrame ? "partial" : "silent");
        StdioProcess? child = null;
        using var launch = StdioProcess.LaunchRunner.Override((process, start) =>
        {
            child = process;
            start();
            return Task.CompletedTask;
        });
        var environment = new Dictionary<string, string> { [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value };
        var read = PlanLimitsReader.ReadClaudeFromCLI(
            SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!, environment);
        try
        {
            Assert.NotNull(child);
            Assert.True(child.IsRunning);
            Assert.False(read.IsCompleted);
            // No loop turn: the deadline must be armed by the read, not a queued watchdog body.
            Assert.Contains(TimeSpan.FromSeconds(20), runtime.Time.TimerDeadlines);
            runtime.Time.Advance(TimeSpan.FromSeconds(19));
            Assert.False(read.IsCompleted);
            runtime.Time.Advance(TimeSpan.FromSeconds(1));
            Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(15)));
            await child.WaitForExit().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(child.IsRunning);
        }
        finally
        {
            // A red regression still reaps its own stand-in; it never leaves a hung test host or child.
            child?.ProcessControl.KillTree();
            if (child is not null) await child.WaitForExit().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
