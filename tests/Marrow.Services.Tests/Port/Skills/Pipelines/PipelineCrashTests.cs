using System.Diagnostics;
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Port.Skills.Pipelines;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
[Trait("Spec", "DL-8")]
public sealed class PipelineCrashTests
{
    [Theory]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    [InlineData("install:Created")]
    [InlineData("install:Built")]
    [InlineData("install:Renamed")]
    [InlineData("install:Locked")]
    [InlineData("install:Verified")]
    [InlineData("install:Published")]
    [InlineData("Downloaded")]
    [InlineData("Installed")]
    [InlineData("RoutesPrepared")]
    [InlineData("BeforeCommit")]
    [InlineData("AfterCommit")]
    public async Task Abrupt_process_exit_at_every_step_keeps_old_payload_and_one_complete_routing_snapshot(string step)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "pipeline-worker", "Marrow.Skills.PipelineWorker.dll"));
        start.ArgumentList.Add(step);
        // Only the local Marrow-made worker runs. It contains no downloader or CLI process seam.
        using var worker = Process.Start(start)!;
        var output = await worker.StandardOutput.ReadToEndAsync();
        var error = await worker.StandardError.ReadToEndAsync();
        await worker.WaitForExitAsync();
        Assert.True(worker.ExitCode is 71 or 72, error);
        var root = output.Trim();
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "marrow-sk4-"), root, StringComparison.Ordinal);
        try
        {
            var stateFile = Path.Combine(root, "addons", "state.json");
            var state = AddonsStateStore.Load(stateFile);
            var selected = Assert.Single(PipelineRoutes.Read(stateFile));
            Assert.Equal(state.Get("fixture").InstalledPin, selected.Pin);
            Assert.Equal(Path.Combine(selected.Folder, "payload.txt"), PipelineRoutingReader.ResolveShim(stateFile, "fixture.exe").Target);
            var old = Directory.GetDirectories(Path.Combine(root, "addons", "tools"), "fixture-1234567890abcdef*").Single();
            Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(old, "payload.txt")));
            if (step != "AfterCommit") Assert.Equal("1234567890abcdef1234567890abcdef12345678", selected.Pin);
            else
            {
                Assert.Equal("abcdef1234567890abcdef1234567890abcdef12", selected.Pin);
                Assert.Equal("new offline fixture", File.ReadAllText(Path.Combine(selected.Folder, "payload.txt")));
            }
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
