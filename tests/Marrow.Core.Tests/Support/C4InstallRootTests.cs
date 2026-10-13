using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: C4-u5-2 / ED-247, path strings and in-memory Run values only.
public sealed class C4InstallRootTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Drive_root_install_recognizes_descendant_targets()
        => Assert.True(InstallLayout.IsInside(@"D:\", @"D:\current\nth.exe"));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_drive_root_uninstall_removes_its_Run_value_and_keeps_other_programs()
    {
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", "\"D:\\current\\nth.exe\" --at-sign-in");
        registry.Seed(UninstallEntry.RunKeyPath, "Other", "\"C:\\Apps\\other.exe\"");
        Assert.True(UninstallEntry.RemoveSignInEntry(registry, @"D:\"));
        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.Equal("\"C:\\Apps\\other.exe\"", registry.GetValue(UninstallEntry.RunKeyPath, "Other"));
    }

    [Theory]
    [Trait("Decision", "ED-247")]
    [InlineData(@"D:\", @"D:\current\nth.exe", true)]
    [InlineData(@"D:\", @"C:\current\nth.exe", false)]
    [InlineData(@"D:\Apps\nth", @"D:\Apps\nth-old\nth.exe", false)]
    [InlineData(@"\\server\share\", @"\\server\share\current\nth.exe", true)]
    [InlineData(@"\\server\share\", @"\\server\share-other\current\nth.exe", false)]
    public void Ownership_keeps_separator_boundaries(string root, string target, bool owned)
        => Assert.Equal(owned, InstallLayout.IsInside(root, target));
}
