using Marrow.Core.Support;
using Marrow.Core.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

public sealed class C4cRepairBudgetTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_recalculates_its_mutation_wait_after_expected_fields_are_prepared()
        => AssertPreparationBudget(TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(4));

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_does_not_write_when_preparing_expected_fields_exhausted_its_allowance()
        => AssertPreparationBudget(TimeSpan.FromSeconds(3), TimeSpan.Zero);

    private static void AssertPreparationBudget(TimeSpan allowance, TimeSpan updateWait)
    {
        var clock = new ManualTimeProvider();
        var fake = new FakeShellLinks(wait: by => clock.Advance(by));
        fake.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        fake.Seed(FakeShellLinks.DesktopLink, new(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        var shell = new C4cPreparationShellLinks(fake, clock);
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).Repair(ShortcutScan.Hook, allowance);
        Assert.Equal(1, result.Failed);
        Assert.Equal(unchecked((int)0x800705B4), result.HResult);
        Assert.Equal(allowance, clock.Elapsed);
        Assert.Equal(updateWait, Assert.Single(shell.UpdateAllowances));
        Assert.Equal(0, fake.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_recalculates_its_Start_read_wait_after_path_preparation()
    {
        var clock = new ManualTimeProvider();
        var fake = new FakeShellLinks();
        fake.Seed(FakeShellLinks.DesktopLink, new(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        fake.Seed(FakeShellLinks.Programs + @"\nth.lnk", new(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot)));
        var shell = new C4cPreparationShellLinks(fake, clock, TimeSpan.FromSeconds(3));
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).Repair(ShortcutScan.Hook, TimeSpan.FromSeconds(7));
        Assert.Equal(TimeSpan.FromSeconds(7), clock.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(4), Assert.Single(shell.StartReadAllowances));
        Assert.True(result.Capped);
        Assert.Equal(0, result.Changed);
        Assert.Equal(0, fake.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Production_repair_mutation_forwards_the_remaining_wait_to_the_STA_queue()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var source = File.ReadAllText(Path.Combine(root, "src", "Marrow.App", "Port", "ShellLinks.cs"));
        Assert.Contains("public void UpdateLink(string path, ShellLinkChange change, TimeSpan timeout)", source, StringComparison.Ordinal);
        var start = source.IndexOf("public void UpdateLink(string path, ShellLinkChange change, TimeSpan timeout)", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var end = source.IndexOf("public void DeleteLink", start, StringComparison.Ordinal);
        Assert.Contains("}), timeout);", source[start..end], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_slow_repair_wait_uses_the_remaining_allowance_instead_of_overrunning_the_hook()
    {
        var clock = new ManualTimeProvider();
        var fake = new FakeShellLinks(wait: by => clock.Advance(by));
        fake.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        fake.Seed(FakeShellLinks.DesktopLink, new(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        fake.Delay(FakeShellLinks.Call.UpdateLink, TimeSpan.FromSeconds(6));
        var result = new DesktopShortcut(fake, FakeShellLinks.DefaultRoot, clock).Repair(ShortcutScan.Hook, TimeSpan.FromSeconds(4));
        Assert.Equal(1, result.Failed);
        Assert.Equal(unchecked((int)0x800705B4), result.HResult);
        Assert.Equal(TimeSpan.FromSeconds(4), clock.Elapsed);
        Assert.Equal(0, fake.Writes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_two_second_scan_still_repairs_within_the_after_update_hook_budget()
    {
        var clock = new ManualTimeProvider();
        var fake = new FakeShellLinks(wait: by => clock.Advance(by));
        fake.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        fake.Seed(FakeShellLinks.Programs + @"\nth\nth.lnk", new(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot), AppUserModelId: ShellLinkKeys.NthAppUserModelId));
        fake.Seed(FakeShellLinks.DesktopLink, new(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        fake.Delay(FakeShellLinks.Call.MoveLink, TimeSpan.FromMilliseconds(300));
        fake.Delay(FakeShellLinks.Call.UpdateLink, TimeSpan.FromSeconds(1));
        var shell = new C4ListingShellLinks(fake, clock, TimeSpan.FromSeconds(1));
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(FakeShellLinks.DefaultRoot);
        var copies = 0;
        var hooks = new InstallHooks(shell, registry, clock,
            copyUninstaller: _ => { copies++; clock.Advance(TimeSpan.FromSeconds(4)); return new(UninstallerCopyOutcome.Copied); },
            measureFootprintKb: _ => { clock.Advance(TimeSpan.FromSeconds(2)); return 430_080; }, fileExists: _ => true);
        var report = hooks.AfterUpdate(new(FakeShellLinks.DefaultRoot, FakeShellLinks.LocalAppData, null, "1.0.1"));
        Assert.Equal(@"%LOCALAPPDATA%\Programs\nth\current\nth.exe", fake.Peek(FakeShellLinks.DesktopLink)!.Target);
        Assert.Contains("desktop-repair: 1 changed of 1", report.Steps);
        Assert.Equal(1, copies);
        Assert.Equal(UninstallEntry.UninstallCommand(FakeShellLinks.DefaultRoot), registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));
        Assert.True(clock.Elapsed < InstallHooks.UpdateBudget);
    }
}
