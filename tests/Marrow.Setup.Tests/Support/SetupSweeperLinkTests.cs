using Marrow.Core.Support;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers"), unit U9 (ED-NEW-inst-u9-1, amended by ED-NEW-inst-c7-8 and -9).
// A sweep deletes only what nth Setup can prove it made: a directory junction is never followed and never removed; the sweep
// keeps it and reports where it stopped (ED-NEW-inst-merge-16 replaced the two older "removed as a link" facts, which
// ED-NEW-inst-c7-8 reverses). Junctions need no privilege; the targets are temporary folders.
public sealed class SetupSweeperLinkTests : IDisposable
{
    private readonly SweepArea area = new();

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_inside_a_staging_folder_is_never_followed_and_the_whole_unit_is_kept_and_reported()
    {
        var precious = area.Put(Path.Combine(area.Base, "precious", "keep.txt"), "not ours");
        var options = area.PutInSetup("1.0.0", "install-options.json");
        var unit = Path.Combine(area.SetupRoot, "1.0.0");
        var link = Path.Combine(unit, "link");
        SweepArea.MakeJunction(link, Path.Combine(area.Base, "precious"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.False(result.Clean);
        Assert.Equal(0, result.Removed);
        var kept = Assert.Single(result.Kept);
        Assert.Equal(unit, kept.Path);
        Assert.Equal(SweepKeptReason.NotRemoved, kept.Reason);
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "the link stays a link");
        Assert.True(File.Exists(options), "the preflight refuses the whole unit before it deletes anything");
        Assert.True(File.Exists(Path.Combine(unit, ".nth-setup-owner")), "the unit stays marked for a later sweep");
        Assert.Equal("not ours", File.ReadAllText(precious));
        Assert.Equal([precious], Directory.GetFileSystemEntries(Path.Combine(area.Base, "precious")));
        Assert.Equal(kept, Assert.Single(SetupSweeper.Sweep(area.LocalAppData, area.Temp).Kept));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_junction_as_a_unit_of_its_own_is_never_followed_and_is_kept_and_reported()
    {
        var precious = area.Put(Path.Combine(area.Base, "precious", "keep.txt"), "not ours");
        Directory.CreateDirectory(area.SetupRoot);
        var unit = Path.Combine(area.SetupRoot, "1.0.0");
        SweepArea.MakeJunction(unit, Path.Combine(area.Base, "precious"));

        var result = SetupSweeper.Sweep(area.LocalAppData, area.Temp);

        Assert.False(result.Clean);
        Assert.Equal(0, result.Removed);
        var kept = Assert.Single(result.Kept);
        Assert.Equal(area.SetupRoot, kept.Path);
        Assert.Equal(SweepKeptReason.NotRemoved, kept.Reason);
        Assert.True((File.GetAttributes(unit) & FileAttributes.ReparsePoint) != 0, "the link stays a link");
        Assert.Equal("not ours", File.ReadAllText(precious));
        Assert.Equal([precious], Directory.GetFileSystemEntries(Path.Combine(area.Base, "precious")));
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
