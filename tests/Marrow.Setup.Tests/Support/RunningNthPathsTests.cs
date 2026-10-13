using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, unit U4 (ED-NEW-inst-u4-1). "Only processes inside the root matter": the one rule that
// tells an nth Velopack is about to stop from a portable or development copy elsewhere. Case, separators, dots, accents and drive roots.
public sealed class RunningNthPathsTests
{
    private const string Root = @"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth";

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth\current\nth.exe")]
    [InlineData(@"c:\users\ana-maría østergård-fernández\appdata\local\programs\NTH\Current\NTH.EXE")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth\Update.exe")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth\current\..\current\nth.exe")]
    [InlineData(@"C:/Users/Ana-María Østergård-Fernández/AppData/Local/Programs/nth/current/nth.exe")]
    public void A_file_below_the_root_is_inside_whatever_its_case_or_separators(string path) =>
        Assert.True(RunningNthPaths.IsInside(Root, path));

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth2\current\nth.exe")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth.old\nth.exe")]
    [InlineData(@"C:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth\..\other\nth.exe")]
    [InlineData(@"D:\Users\Ana-María Østergård-Fernández\AppData\Local\Programs\nth\current\nth.exe")]
    [InlineData(@"E:\Droppy\Installer\portable\nth.exe")]
    [InlineData("")]
    [InlineData("   ")]
    public void A_file_beside_above_or_elsewhere_is_not_inside(string path) =>
        Assert.False(RunningNthPaths.IsInside(Root, path));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_trailing_separator_on_the_root_does_not_matter_and_a_drive_root_holds_everything_on_the_drive()
    {
        Assert.True(RunningNthPaths.IsInside(Root + @"\", Root + @"\current\nth.exe"));
        Assert.True(RunningNthPaths.IsInside(@"D:\", @"D:\Apps\nth\current\nth.exe"));
        Assert.False(RunningNthPaths.IsInside(@"D:\", @"C:\Apps\nth\current\nth.exe"));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Nothing_is_inside_a_missing_root_and_a_path_that_is_not_a_path_is_inside_nothing()
    {
        Assert.False(RunningNthPaths.IsInside(null, @"C:\x\nth.exe"));
        Assert.False(RunningNthPaths.IsInside(" ", @"C:\x\nth.exe"));
        Assert.False(RunningNthPaths.IsInside(Root, null));
        Assert.False(RunningNthPaths.IsInside(Root, "C:\\bad\0path\\nth.exe"));
    }
}
