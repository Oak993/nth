using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers"), unit U9 (ED-NEW-inst-u9-1). A sweep deletes only what is in
// nth Setup's own folder: a directory junction inside it is removed as a link and never followed, and a junction standing where
// the setup's folder should be is left alone. Junctions need no privilege; the targets are temporary folders.
public sealed class SetupSweeperLinkTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_inside_a_staging_folder_is_removed_as_a_link_and_its_target_is_left_whole()
    {
        var precious = area.Put(Path.Combine(area.Base, "precious", "keep.txt"), "not ours");
        area.PutInSetup("1.0.0", "install-options.json");
        SweepArea.MakeJunction(Path.Combine(area.SetupRoot, "1.0.0", "link"), Path.Combine(area.Base, "precious"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.True(result.Clean);
        Assert.False(Directory.Exists(area.SetupRoot));
        Assert.Equal("not ours", File.ReadAllText(precious));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_as_a_unit_of_its_own_is_removed_without_following_it()
    {
        var precious = area.Put(Path.Combine(area.Base, "precious", "keep.txt"), "not ours");
        Directory.CreateDirectory(area.SetupRoot);
        SweepArea.MakeJunction(Path.Combine(area.SetupRoot, "1.0.0"), Path.Combine(area.Base, "precious"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(1, result.Removed);
        Assert.Equal("not ours", File.ReadAllText(precious));
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "1.0.0")));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_where_the_setup_folder_should_be_is_not_followed_and_not_removed()
    {
        var precious = area.Put(Path.Combine(area.Base, "elsewhere", "1.0.0", "keep.txt"), "not ours");
        SweepArea.MakeJunction(area.SetupRoot, Path.Combine(area.Base, "elsewhere"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.Equal(0, result.Removed);
        var kept = Assert.Single(result.Kept);
        Assert.Equal(area.SetupRoot, kept.Path);
        Assert.Equal(SweepKeptReason.NotRemoved, kept.Reason);
        Assert.Equal("not ours", File.ReadAllText(precious));
        Assert.True(Directory.Exists(area.SetupRoot));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Removing_a_folder_below_a_junction_is_refused_quietly_and_leaves_the_target_whole()
    {
        var precious = area.Put(Path.Combine(area.Base, "elsewhere", "1.0.0", "keep.txt"), "not ours");
        SweepArea.MakeJunction(area.SetupRoot, Path.Combine(area.Base, "elsewhere"));

        var result = SetupSweeper.RemoveFolder(area.LocalAppData, Path.Combine(area.SetupRoot, "1.0.0"));

        Assert.Equal(0, result.Removed);
        Assert.Equal("not ours", File.ReadAllText(precious));
        Assert.True(Directory.Exists(area.SetupRoot), "the link stays");
    }
}
