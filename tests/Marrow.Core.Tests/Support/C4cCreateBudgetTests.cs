using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class C4cCreateBudgetTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Expired_create_allowance_never_claims_created_or_starts_a_write()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(wait: by => clock.Advance(by));
        shell.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        shell.Delay(FakeShellLinks.Call.TryEnterWriter, TimeSpan.FromSeconds(2));
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).Create(ShortcutScan.Hook, TimeSpan.FromSeconds(1));
        Assert.Equal(DesktopShortcutOutcome.Failed, result.DesktopShortcut);
        Assert.Equal(DesktopShortcut.WriterBusyHResult, result.HResult);
        Assert.True(result.ScanCapped);
        Assert.Equal(0, shell.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Install_passes_its_actual_remaining_allowance_and_preserves_the_final_copy()
    {
        var temp = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "nth-tests", Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var clock = new ManualTimeProvider();
            var fake = new FakeShellLinks(wait: by => clock.Advance(by));
            fake.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
            fake.Seed(FakeShellLinks.Programs + @"\nth\nth.lnk", new(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot), AppUserModelId: ShellLinkKeys.NthAppUserModelId));
            fake.Seed(FakeShellLinks.OneDriveDesktop + @"\foreign.lnk", new(@"C:\Foreign\other.exe"));
            fake.Delay(FakeShellLinks.Call.MoveLink, TimeSpan.FromSeconds(18));
            fake.Delay(FakeShellLinks.Call.TryEnterWriter, TimeSpan.FromSeconds(2));
            var shell = new C4ListingShellLinks(fake, clock, TimeSpan.FromSeconds(1));
            var copies = 0;
            var options = InstallOptions.DefaultOptionsPath(temp, "1.0.1");
            InstallOptions.ForInstall(desktopShortcut: true).Write(options);
            var hooks = new InstallHooks(shell, new FakeUserRegistry(), clock,
                copyUninstaller: _ => { copies++; clock.Advance(TimeSpan.FromSeconds(4)); return new(UninstallerCopyOutcome.Copied); });
            var report = hooks.AfterInstall(new(FakeShellLinks.DefaultRoot, temp, options, "1.0.1"));
            Assert.Equal(DesktopShortcutOutcome.Failed, report.Result!.DesktopShortcut);
            Assert.True(report.Result.ScanCapped);
            Assert.Equal(1, copies);
            Assert.True(clock.Elapsed < InstallHooks.InstallBudget);
            Assert.Null(fake.Peek(FakeShellLinks.DesktopLink));
            Assert.Equal(report.Result, InstallResult.ReadForReady(InstallOptions.ResultPathFor(options)));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }
}
