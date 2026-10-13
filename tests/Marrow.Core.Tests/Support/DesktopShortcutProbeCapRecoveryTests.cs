using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: C4c I4, PLAN 8a. Fake shell and manual clock; no real desktop or COM.
public sealed class DesktopShortcutProbeCapRecoveryTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private static ShellLink Made() => new(Root + @"\current\nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Time_cap_in_an_earlier_folder_keeps_a_changed_cached_marker_until_replacement_is_read()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(by => clock.Advance(by));
        var path = FakeShellLinks.ProfileDesktop + @"\nth.lnk";
        shell.Seed(path, Made());
        var probe = new DesktopShortcutProbe(shell, Root, clock);
        Assert.True(probe.Probe().Present);

        shell.Seed(path, new ShellLink(@"C:\Foreign\other.exe"));
        shell.Seed(FakeShellLinks.OneDriveDesktop + @"\nth-unrelated.lnk", new ShellLink(@"C:\Foreign\other.exe"));
        shell.Delay(FakeShellLinks.Call.ReadLink, TimeSpan.FromSeconds(3));
        shell.ResetCounters();

        var capped = probe.Probe();

        Assert.True(capped.Present);
        Assert.False(capped.Failed);
        Assert.True(capped.Capped);
        Assert.Equal(1, capped.Reads);
        Assert.DoesNotContain(path, shell.ReadPaths);
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Elapsed);
        shell.ClearFaults();
        shell.ResetCounters();

        var recovered = probe.Probe();

        Assert.False(recovered.Present);
        Assert.False(recovered.Failed);
        Assert.False(recovered.Capped);
        Assert.Equal(1, recovered.Reads);
        Assert.Equal(path, Assert.Single(shell.ReadPaths));
        Assert.Equal(0, probe.Probe().Reads);
        Assert.Equal(0, shell.Changes);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Link_cap_keeps_a_changed_renamed_cached_marker_then_retry_and_deletion_show_the_truth()
    {
        var clock = new ManualTimeProvider();
        var shell = new FakeShellLinks(by => clock.Advance(by));
        var path = FakeShellLinks.OneDriveDesktop + @"\z-renamed.lnk";
        shell.Seed(path, Made());
        var probe = new DesktopShortcutProbe(shell, Root, clock);
        Assert.True(probe.Probe().Present);

        shell.Seed(path, Made());
        shell.SeedMany(FakeShellLinks.OneDriveDesktop, 2000);
        probe.Invalidate(); // Seed mimics an outside write without changing the fake folder timestamp.
        shell.ResetCounters();

        var capped = probe.Probe();

        Assert.True(capped.Present);
        Assert.False(capped.Failed);
        Assert.True(capped.Capped);
        Assert.Equal(2000, capped.Reads);
        Assert.DoesNotContain(path, shell.ReadPaths);
        shell.ResetCounters();

        var recovered = probe.Probe();

        Assert.True(recovered.Present);
        Assert.False(recovered.Failed);
        Assert.False(recovered.Capped);
        Assert.Equal(1, recovered.Reads);
        Assert.Equal(path, Assert.Single(shell.ReadPaths));
        Assert.Equal(0, shell.Changes);
        shell.DeleteLink(path); // Outside deletion in fake memory only.
        shell.ResetCounters();
        var deleted = probe.Probe();
        Assert.False(deleted.Present);
        Assert.False(deleted.Failed);
        Assert.False(deleted.Capped);
        Assert.Equal(0, deleted.Reads);
        Assert.Equal(0, shell.Changes);
    }
}
