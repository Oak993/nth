using System.Diagnostics;
using Marrow.Core.Port;
using Marrow.Core.Support;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: a usage CLI can exit while one of its children still holds stdout, ED-NEW-planlimits-hang-3.
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class PlanLimitsExitCleanupTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-planlimits-hang-3")]
    public async Task A_probe_that_exits_mid_read_does_not_leave_its_pipe_holding_child_behind()
    {
        using var runtime = new TestRuntime();
        var script = new ClaudeScript("usage-orphan");
        File.WriteAllText(Path.Combine(script.ControlDirectory, "orphan.txt"), "1");
        var eventName = @"Local\nth-planlimits-exit-" + Guid.NewGuid().ToString("N");
        using var exit = new EventWaitHandle(false, EventResetMode.ManualReset, eventName);
        StdioProcess? root = null;
        Process? helper = null;
        using var launch = StdioProcess.LaunchRunner.Override((process, start) =>
        {
            root = process;
            start();
            return Task.CompletedTask;
        });
        var environment = new Dictionary<string, string>
        {
            [script.EnvironmentEntry.Key] = script.EnvironmentEntry.Value,
            ["CLAUDE_STANDIN_EXIT_EVENT"] = eventName,
        };
        var read = PlanLimitsReader.ReadClaudeFromCLI(
            SwiftURL.Create("marrow-native-path:" + script.ExecutablePath.Replace('\\', '/'))!, environment);
        try
        {
            var pidFile = Path.Combine(script.ControlDirectory, "orphan-pid.txt");
            // Join the fixture's publication on a separate loop; the read's main loop stays idle.
            new ManualMainLoop().RunUntil(() => File.Exists(pidFile), TimeSpan.FromSeconds(15));
            helper = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture));
            _ = helper.SafeHandle; // pin this process identity before allowing its parent to exit
            Assert.False(helper.HasExited);
            exit.Set();
            await root!.WaitForExit().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Null(await read.WaitAsync(TimeSpan.FromSeconds(15)));
            await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(helper.HasExited);
        }
        finally
        {
            exit.Set();
            root?.ProcessControl.KillTree();
            if (helper is not null)
            {
                if (!helper.HasExited) helper.Kill(entireProcessTree: true);
                await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                helper.Dispose();
            }
            if (root is not null) await root.WaitForExit().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
