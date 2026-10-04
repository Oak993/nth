// PORT-ONLY: integ-sk8-sk9. Repair on real NTFS: the locked damaged folder renames aside, and a held handle defers Repair without changing anything.
using Marrow.Core.Port.Skills.State;
using Marrow.Services.Tests.Port.Skills.Store.Fixtures;

namespace Marrow.Services.Tests.Port.Skills.Pipelines;

[Trait("Platform", "WINDOWS-ONLY")]
public sealed class PipelineRepairWindowsTests
{
    [WindowsOnlyFact]
    [Trait("Feature", "SK-8")]
    [Trait("Rule", "R-TEST-4")]
    public async Task A_held_file_in_the_damaged_folder_defers_repair_until_it_closes()
    {
        using var rig = new PipelineRig(windows: true);
        var pipeline = await rig.Create();
        var folder = pipeline.Snapshot().Single().Folder;
        rig.Damage(folder);
        Assert.Null(pipeline.Acquire("fixture"));
        using (new FileStream(Path.Combine(folder, "payload.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(await pipeline.RepairAsync("fixture", rig.Progress));
            Assert.Equal(ItemState.Damaged, pipeline.State.Get("fixture").State);
            Assert.Equal("tampered", File.ReadAllText(Path.Combine(folder, "payload.txt")));
            Assert.Equal(0, rig.Downloads);
        }
        Assert.True(await pipeline.RepairAsync("fixture", rig.Progress));
        using var lease = pipeline.Acquire("fixture");
        Assert.NotNull(lease);
        Assert.Equal("offline fixture", File.ReadAllText(Path.Combine(folder, "payload.txt")));
        pipeline.Collect();
        Assert.Equal(new[] { folder }, Directory.GetDirectories(rig.Paths.Tools));
    }
}
