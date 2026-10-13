using System.Diagnostics;
using Marrow.Services.Providers;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Providers;

// PORT-ONLY: pending I/O must own its read continuation through GC, ED-NEW-planlimits-hang-2.
[Collection(PlanLimitsProcessCollection.Name)]
public sealed class PlanLimitsReadLifetimeTests
{
    [Fact]
    [Trait("Decision", "ED-NEW-planlimits-hang-2")]
    public async Task Pending_io_keeps_the_usage_read_alive_until_the_child_exits()
    {
        var script = new ClaudeScript("usage-gc");
        var completed = new TaskCompletionSource<PlanLimits?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var read = UnrootedPlanLimitsProbe.Start(script, completed);
        _ = script.CapturedControlRequestLine(0); // child has received initialize; native readers are now idle
        using var child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(script.ControlDirectory, "pid-0.txt")),
            System.Globalization.CultureInfo.InvariantCulture));
        _ = child.SafeHandle;
        try
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Assert.True(read.IsAlive, "The pending read/its idle manual runtime was collected while native I/O was still outstanding.");
            File.WriteAllText(Path.Combine(script.ControlDirectory, "exit-now.txt"), "1");
            Assert.Null(await completed.Task.WaitAsync(TimeSpan.FromSeconds(15)));
        }
        finally
        {
            File.WriteAllText(Path.Combine(script.ControlDirectory, "exit-now.txt"), "1");
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
}
