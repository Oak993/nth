using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class C4cRepairExtrasTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Failed_update_keeps_hidden_icons_and_the_capped_scan_and_maps_a_nonnegative_HResult()
    {
        var shell = new FakeShellLinks { IconsHidden = true };
        shell.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        shell.Seed(FakeShellLinks.DesktopLink, new(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        shell.SeedMany(FakeShellLinks.OneDriveDesktop, ShellLinkKeys.ScanCapLinks + 1);
        shell.Fail(FakeShellLinks.Call.UpdateLink, 0);
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot).Apply(InstallOptions.ForUpdate)!;
        Assert.Equal(DesktopShortcutOutcome.Failed, result.DesktopShortcut);
        Assert.Equal(DesktopShortcut.UnknownFailureHResult, result.HResult);
        Assert.True(result.IconsHidden);
        Assert.True(result.ScanCapped);
    }
}
