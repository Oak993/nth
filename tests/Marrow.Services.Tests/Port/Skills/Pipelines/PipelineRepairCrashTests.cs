// PORT-ONLY: integ-sk8-sk9, cross-unit gap ED-323 (5)(b): an abrupt exit at every Repair step, on real NTFS ACLs, then restart and finish.
using System.Diagnostics;
using Marrow.Core.Port.Skills.State;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Platform", "WINDOWS-ONLY")]
[Trait("Spec", "DL-8")]
public sealed class PipelineRepairCrashTests
{
    [WindowsOnlyTheory]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("SetAside")]
    [InlineData("Downloaded")]
    [InlineData("install:Created")]
    [InlineData("install:Built")]
    [InlineData("install:Renamed")]
    [InlineData("install:Locked")]
    [InlineData("install:Verified")]
    [InlineData("install:Published")]
    [InlineData("Installed")]
    [InlineData("BeforeCommit")]
    [InlineData("AfterCommit")]
    public async Task Abrupt_exit_at_every_repair_step_leaves_a_damaged_or_installed_pin_that_restart_finishes(string step)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "pipeline-worker", "Marrow.Skills.PipelineWorker.dll"));
        start.ArgumentList.Add("repair:" + step);
        // Only the local Marrow-made worker runs. It contains no downloader or CLI process seam.
        using var worker = Process.Start(start)!;
        var output = await worker.StandardOutput.ReadToEndAsync();
        var error = await worker.StandardError.ReadToEndAsync();
        await worker.WaitForExitAsync();
        Assert.True(worker.ExitCode == (step == "AfterCommit" ? 72 : 71), $"exit {worker.ExitCode}: {error}");
        var root = output.Trim();
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "marrow-sk4-"), root, StringComparison.Ordinal);
        using var rig = new PipelineRig(windows: true, root: root);
        var pipeline = await rig.Reattach();
        if (step == "AfterCommit") Assert.Equal(ItemState.UpdateAvailable, pipeline.State.Get("fixture").State);
        else
        {
            Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
            Assert.Null(pipeline.Acquire("fixture"));
            Assert.True(await pipeline.RepairAsync("fixture", rig.Progress));
        }
        using (var lease = pipeline.Acquire("fixture"))
        {
            Assert.NotNull(lease);
            Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(lease.Folder, "payload.txt")));
        }
        pipeline.Collect();
        var only = Assert.Single(Directory.GetDirectories(rig.Paths.Tools));
        Assert.StartsWith("fixture-1234567890abcdef", Path.GetFileName(only), StringComparison.Ordinal);
        Assert.Equal(only, Assert.Single(pipeline.Snapshot()).Folder);
    }
}
