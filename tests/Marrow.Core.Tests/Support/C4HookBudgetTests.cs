using System.Runtime.CompilerServices;
using Marrow.Core.Tests.Fixtures;
using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: C4-u5-1 / ED-247, virtual-clock shell calls and an in-memory registry only.
public sealed class C4HookBudgetTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void After_update_reserves_time_for_the_final_uninstaller_copy_and_key()
    {
        var clock = new ManualTimeProvider();
        var shell = Machine(clock, 16);
        shell.Delay(FakeShellLinks.Call.UpdateLink, TimeSpan.FromSeconds(1));
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(FakeShellLinks.DefaultRoot);
        var copies = 0;
        var hooks = new InstallHooks(shell, registry, clock,
            copyUninstaller: _ => { copies++; clock.Advance(TimeSpan.FromSeconds(4)); return new(UninstallerCopyOutcome.Copied); },
            measureFootprintKb: _ => { clock.Advance(TimeSpan.FromSeconds(2)); return 430_080; }, fileExists: _ => true);

        var report = hooks.AfterUpdate(Context());

        Assert.Equal(1, copies);
        Assert.Equal(UninstallEntry.UninstallCommand(FakeShellLinks.DefaultRoot), registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));
        Assert.True(clock.Elapsed < InstallHooks.UpdateBudget, $"Hook spent {clock.Elapsed.TotalSeconds} seconds and performed {shell.CallCount(FakeShellLinks.Call.UpdateLink)} repairs.");
        Assert.InRange(shell.CallCount(FakeShellLinks.Call.UpdateLink), 0, 15);
        Assert.Contains(report.Steps, step => step.StartsWith("desktop-repair:", StringComparison.Ordinal) && step.Contains("scan capped", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Repair_deadline_uses_time_already_spent_in_earlier_hook_steps()
    {
        var clock = new ManualTimeProvider();
        var shell = Machine(clock, 16);
        shell.Seed(FakeShellLinks.Programs + @"\nth\nth.lnk", new ShellLink(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot), AppUserModelId: ShellLinkKeys.NthAppUserModelId));
        shell.Delay(FakeShellLinks.Call.MoveLink, TimeSpan.FromSeconds(6));
        shell.Delay(FakeShellLinks.Call.UpdateLink, TimeSpan.FromSeconds(1));
        var registry = new FakeUserRegistry();
        registry.WriteVelopackUninstallKey(FakeShellLinks.DefaultRoot);
        var copies = 0;
        var hooks = new InstallHooks(shell, registry, clock,
            copyUninstaller: _ => { copies++; clock.Advance(TimeSpan.FromSeconds(4)); return new(UninstallerCopyOutcome.Copied); },
            measureFootprintKb: _ => { clock.Advance(TimeSpan.FromSeconds(2)); return 430_080; }, fileExists: _ => true);

        _ = hooks.AfterUpdate(Context());

        Assert.Equal(1, copies);
        Assert.Equal(UninstallEntry.UninstallCommand(FakeShellLinks.DefaultRoot), registry.GetValue(UninstallEntry.KeyPath, "UninstallString"));
        Assert.True(clock.Elapsed < InstallHooks.UpdateBudget, clock.Elapsed.ToString());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Before_uninstall_stops_desktop_removal_in_time_to_remove_the_Run_value()
    {
        var clock = new ManualTimeProvider();
        var shell = Machine(clock, 64);
        shell.Delay(FakeShellLinks.Call.DeleteLink, TimeSpan.FromSeconds(1));
        var registry = new FakeUserRegistry();
        registry.Seed(UninstallEntry.RunKeyPath, "nth", "\"" + InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot) + "\" --at-sign-in");
        var report = new InstallHooks(shell, registry, clock).BeforeUninstall(Context());

        Assert.Null(registry.GetValue(UninstallEntry.RunKeyPath, "nth"));
        Assert.True(clock.Elapsed < InstallHooks.UninstallBudget, clock.Elapsed.ToString());
        Assert.InRange(shell.Deletes, 0, 29);
        Assert.Contains(report.Steps, step => step.Contains("scan capped", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_full_repair_keeps_its_unbounded_self_heal_behavior()
    {
        var clock = new ManualTimeProvider();
        var shell = Machine(clock, 16);
        shell.Delay(FakeShellLinks.Call.UpdateLink, TimeSpan.FromSeconds(1));
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).Repair(ShortcutScan.Full);
        Assert.Equal(16, result.Changed);
        Assert.False(result.Capped);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_hook_scan_time_cap_includes_listing_cloud_placeholders()
    {
        var clock = new ManualTimeProvider();
        var fake = new FakeShellLinks();
        for (var i = 0; i < 100; i++)
            fake.SeedPlaceholder(FakeShellLinks.OneDriveDesktop + @"\nth-" + i + ".lnk", new ShellLink(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot)));
        var shell = new C4ListingShellLinks(fake, clock, TimeSpan.FromMilliseconds(100));
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).FindMade(ShortcutScan.Hook);

        Assert.True(result.Capped);
        Assert.True(clock.Elapsed <= ShellLinkKeys.ScanCapTime + TimeSpan.FromMilliseconds(100), clock.Elapsed.ToString());
        Assert.Equal(0, fake.Reads);
        Assert.Empty(fake.Hydrated);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_single_slow_shell_read_cannot_outlive_the_remaining_scan_budget()
    {
        var clock = new ManualTimeProvider();
        var fake = Machine(clock, 1);
        var shell = new C4ListingShellLinks(fake, clock, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(4));
        var result = new DesktopShortcut(shell, FakeShellLinks.DefaultRoot, clock).FindMade(ShortcutScan.Hook);

        Assert.True(result.Capped);
        Assert.True(clock.Elapsed <= ShellLinkKeys.ScanCapTime, clock.Elapsed.ToString());
        Assert.Empty(result.Links);
        Assert.Equal(ShellLinkKeys.ScanCapTime - TimeSpan.FromMilliseconds(100), Assert.Single(shell.ReadAllowances));
        Assert.Equal(0, fake.Reads);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Production_shell_reads_forward_the_scan_allowance_to_the_STA_wait()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marrow.App", "Port", "ShellLinks.cs"));
        Assert.Contains("public ShellLink ReadLink(string path, TimeSpan timeout)", source, StringComparison.Ordinal);
        Assert.Contains("Invoke(() => WithLink(path, StgmRead, Read), timeout)", source, StringComparison.Ordinal);
        Assert.Contains("if (timeout > CallTimeout) timeout = CallTimeout;", source, StringComparison.Ordinal);
        Assert.Contains("call.Done.Wait(timeout)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("call.Done.Wait(CallTimeout)", source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Production_shell_listing_yields_files_instead_of_materializing_the_whole_directory()
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Marrow.App", "Port", "ShellLinks.cs"));
        var start = source.IndexOf("public IEnumerable<ShellLinkFile> EnumerateLinks", StringComparison.Ordinal);
        var end = source.IndexOf("public ShellLink ReadLink", start, StringComparison.Ordinal);
        var listing = source[start..end];
        Assert.Contains("yield return", listing, StringComparison.Ordinal);
        Assert.DoesNotContain("return [..", listing, StringComparison.Ordinal);
    }

    private static FakeShellLinks Machine(ManualTimeProvider clock, int count)
    {
        var shell = new FakeShellLinks(wait: duration => clock.Advance(duration));
        shell.AddFile(InstallLayout.CurrentExe(FakeShellLinks.DefaultRoot));
        for (var i = 0; i < count; i++)
            shell.Seed(FakeShellLinks.OneDriveDesktop + @"\nth-" + i + ".lnk",
                new ShellLink(FakeShellLinks.DefaultRoot + @"\current\gone-nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue));
        return shell;
    }

    private static InstallHookContext Context() => new(FakeShellLinks.DefaultRoot, FakeShellLinks.LocalAppData, null, "1.0.1");
    private static string RepositoryRoot([CallerFilePath] string path = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", "..", ".."));
}
