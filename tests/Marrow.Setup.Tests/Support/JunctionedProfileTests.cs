using Marrow.Core.Support;
using Marrow.Setup.Flow;
using Marrow.Setup.Tests.Fixtures;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers"); VELOPACK_CHECK.md finding 10 (ED-NEW-inst-fix-6). The link check
// refused every reparse point up to the drive, so a profile moved to another drive behind a junction (AppData\Local or the temp folder
// reached through one) could not stage, claim or sweep, and the setup failed. A junction above nth's own folder is the user's; nth's own
// folders are still refused as links. The junction here points back into the same disposable area, so both paths reach the same files.
public sealed class JunctionedProfileTests : IDisposable
{
    private readonly SweepArea area = new();
    private readonly string local;
    private readonly string temp;

    public JunctionedProfileTests()
    {
        // <area>\moved is a junction to <area>: <area>\moved\local is the same folder as <area>\local, reached through a link
        SweepArea.MakeJunction(Path.Combine(area.Base, "moved"), area.Base);
        local = Path.Combine(area.Base, "moved", "local");
        temp = Path.Combine(area.Base, "moved", "temp");
    }

    public void Dispose() => area.Dispose();

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_profile_reached_through_a_junction_still_stages_and_claims()
    {
        using var scope = StagingScope.Create(local, temp, "1.1.0", lastChance: false);

        var folder = scope.Ensure();

        Assert.Equal(InstallOptions.StagingFolder(local, "1.1.0"), folder);
        Assert.True(Directory.Exists(InstallOptions.StagingFolder(area.LocalAppData, "1.1.0")));
        using var claim = FolderClaim.Take(SetupFolders.RuntimeFolder(local, "4f9c2a0011223344"));
        Assert.True(File.Exists(Path.Combine(SetupFolders.RuntimeFolder(area.LocalAppData, "4f9c2a0011223344"), SetupFolders.ClaimFileName)));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_profile_reached_through_a_junction_is_still_swept()
    {
        area.PutInSetup("0.9.0", "nth Setup Engine.exe");
        area.PutInSetup("runtime", "81be07", "libSkiaSharp.dll");
        area.PutInUninstall("nth Uninstall.exe");

        var result = SetupSweeper.Sweep(local, temp);

        Assert.Equal(3, result.Removed);
        Assert.False(Directory.Exists(Path.Combine(area.SetupRoot, "0.9.0")));
        Assert.False(Directory.Exists(area.UninstallTemp));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nths_own_folder_as_a_link_is_still_refused_even_under_a_junctioned_profile()
    {
        var foreign = Directory.CreateDirectory(Path.Combine(area.Base, "foreign")).FullName;
        var precious = area.Put(Path.Combine(foreign, "0.9.0", "keep.txt"), "not ours");
        SweepArea.MakeJunction(area.SetupRoot, foreign);

        Assert.Throws<IOException>(() => SetupFolders.RequireNoLinks(Path.Combine(SetupFolders.Root(local), "0.9.0")));
        Assert.Throws<IOException>(() => SetupFolders.RequireNoLinks(Path.Combine(area.SetupRoot, "0.9.0", "keep.txt")));
        var result = SetupSweeper.Sweep(local, temp);

        Assert.Equal(0, result.Removed);
        Assert.Equal("not ours", File.ReadAllText(precious));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_link_inside_nths_folder_is_still_refused()
    {
        var foreign = Directory.CreateDirectory(Path.Combine(area.Base, "foreign")).FullName;
        Directory.CreateDirectory(area.SetupRoot);
        SweepArea.MakeJunction(Path.Combine(area.SetupRoot, "runtime"), foreign);

        Assert.Throws<IOException>(() => SetupFolders.RequireNoLinks(Path.Combine(SetupFolders.RuntimeFolder(local, "81be07"), "libSkiaSharp.dll")));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Users\a\AppData\Local\nth Setup\1.1.0\x", @"C:\Users\a\AppData\Local\nth Setup")]
    [InlineData(@"C:\Users\a\AppData\Local\Temp\nth Uninstall\nth Uninstall.exe", @"C:\Users\a\AppData\Local\Temp\nth Uninstall")]
    [InlineData(@"C:\Users\a\AppData\Local\nth Setup", @"C:\Users\a\AppData\Local\nth Setup")]
    [InlineData(@"D:\nth Setup\x\nth Setup\y", @"D:\nth Setup")]
    [InlineData(@"C:\Users\a\AppData\Local\Programs\nth", null)]
    public void Nths_own_top_folder_is_the_outermost_nth_Setup_or_nth_Uninstall(string path, string? top) =>
        Assert.Equal(top, SetupFolders.OwnTop(path));
}
