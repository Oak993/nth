using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: C4-u5-3 / ED-247, fake-only shell with temp options/result files.
public sealed class C4RepairResultTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Setup_upgrade_reports_failed_when_the_only_desktop_link_stays_broken()
    {
        var shell = BrokenMachine();
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot).Apply(InstallOptions.ForUpdate);
        Assert.Equal(DesktopShortcutOutcome.Failed, result!.DesktopShortcut);
        Assert.Equal(unchecked((int)0x80070005), result.HResult);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_install_hook_persists_the_failed_upgrade_result_for_Ready()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var options = InstallOptions.DefaultOptionsPath(temp, "1.0.1");
            InstallOptions.ForUpdate.Write(options);
            var hooks = new InstallHooks(BrokenMachine(), new FakeUserRegistry(),
                copyUninstaller: _ => new(UninstallerCopyOutcome.Copied));
            var report = hooks.AfterInstall(new(FakeShellLinks.DefaultRoot, temp, options, "1.0.1"));
            var persisted = InstallResult.ReadForReady(InstallOptions.ResultPathFor(options));
            Assert.Equal(DesktopShortcutOutcome.Failed, persisted.DesktopShortcut);
            Assert.Equal(unchecked((int)0x80070005), persisted.HResult);
            Assert.Equal(report.Result, persisted);
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private static FakeShellLinks BrokenMachine()
    {
        var shell = new FakeShellLinks();
        shell.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        shell.Seed(FakeShellLinks.DesktopLink,
            new ShellLink(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        shell.Fail(FakeShellLinks.Call.UpdateLink, unchecked((int)0x80070005));
        return shell;
    }
}
