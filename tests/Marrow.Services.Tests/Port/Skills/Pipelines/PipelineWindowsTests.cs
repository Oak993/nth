using Marrow.Services.Tests.Port.Skills.Store.Fixtures;
namespace Marrow.Services.Tests.Port.Skills.Pipelines;
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class PipelineWindowsTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Locked_update_keeps_old_handle_usable_and_GC_retries_after_close()
    {
        using var rig = new PipelineRig(windows: true);
        var pipeline = await rig.Create();
        var old = pipeline.Snapshot().Single().Folder;
        using var handle = new FileStream(Path.Combine(old, "payload.txt"), FileMode.Open, FileAccess.Read, FileShare.Read);
        await pipeline.UpdateAsync("fixture", rig.Progress);
        pipeline.Collect();
        Assert.True(Directory.Exists(old));
        using var current = pipeline.Acquire("fixture");
        Assert.NotNull(current);
        handle.Dispose();
        pipeline.Collect();
        Assert.False(Directory.Exists(old));
    }

    [WindowsOnlyFact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task Reset_retries_held_bin_files_after_restart_and_preserves_enabled_and_terms()
    {
        using var rig = new PipelineRig(nonOpen: true, windows: true);
        var pipeline = await rig.Create();
        pipeline.AcceptTerms("fixture", new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        Directory.CreateDirectory(rig.Paths.Skills);
        File.WriteAllText(rig.Paths.EnabledFile, "fixture enabled switches");
        var bin = Path.Combine(rig.Paths.Addons, "bin");
        Directory.CreateDirectory(bin);
        var file = Path.Combine(bin, "fixture.exe");
        File.WriteAllText(file, "Marrow-made held file");
        using var handle = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
        await pipeline.ResetAsync();
        Assert.True(File.Exists(file));
        handle.Dispose();
        var restarted = rig.Restart();
        restarted.Collect();
        Assert.False(Directory.Exists(bin));
        Assert.Equal(2, restarted.State.AcceptedTerms.Count);
        Assert.Equal("fixture enabled switches", File.ReadAllText(rig.Paths.EnabledFile));
    }
}
