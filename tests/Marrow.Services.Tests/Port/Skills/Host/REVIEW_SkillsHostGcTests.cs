// REVIEW (SK-13 independent review): the startup GC must not delete a tree the host said it would leave in place, and must not follow a link.
using Marrow.Services.Port.Skills.Host;
using Marrow.Services.Tests.Fixtures;
using Marrow.Services.Tests.Port.Skills.Pipelines;

namespace Marrow.Services.Tests.Port.Skills.Host;

[Trait("Feature", "SK-13")]
[Trait("Spec", "ED-326")]
[Trait("Rule", "R-TEST-9")]
[Trait("Review", "SK-13")]
[Collection("Dev data root env")]
public sealed class REVIEW_SkillsHostGcTests
{
    [Fact]
    public async Task A_tree_left_in_place_because_its_trust_could_not_be_rebuilt_survives_the_NEXT_launch_too()
    {
        using var rig = new PipelineRig(root: SkillsHostRig.FreshRoot());
        await rig.Create();
        var folder = rig.Trusted(rig.Old.Pin!).Folder;
        using (var first = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old)))
        {
            Assert.Equal(["fixture"], first.Unrestored);
            Assert.True(Directory.Exists(folder));
        }
        using var second = SkillsHost.Open(rig.Paths.Root, SkillsHostRig.Options(rig, rig.Old));
        await second.CollectInBackground(); // the startup GC no longer runs inside Open (it runs in the background after the window shows)
        Assert.True(Directory.Exists(folder), "the second launch's GC deleted the tree the first launch promised to leave in place");
    }
}
