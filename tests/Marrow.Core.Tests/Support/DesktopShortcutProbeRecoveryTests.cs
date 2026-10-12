using Marrow.Core.Support;
using Marrow.Testing;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY: installer C4b U8 finding 2, PLAN 8a. Shell memory only; no real desktop or COM.
public sealed class DesktopShortcutProbeRecoveryTests
{
    private const string Root = FakeShellLinks.DefaultRoot;
    private const string Path = FakeShellLinks.OneDriveDesktop + @"\nth.lnk";
    private static ShellLink Made() => new(Root + @"\current\nth.exe", CreatedBy: ShellLinkKeys.CreatedByValue);

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unreadable_changed_shortcut_does_not_certify_the_last_known_shortcut_absent()
    {
        var shell = new FakeShellLinks();
        shell.Seed(Path, Made());
        var probe = new DesktopShortcutProbe(shell, Root);
        Assert.True(probe.Probe().Present);
        shell.Seed(Path, Made());
        shell.Fail(FakeShellLinks.Call.ReadLink, unchecked((int)0x80070020), times: 1, path: Path);

        var result = probe.Probe();

        Assert.True(result.Present || result.Failed);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unreadable_changed_cached_link_is_retried_then_its_real_replacement_is_seen()
    {
        var shell = new FakeShellLinks();
        shell.Seed(Path, Made());
        var probe = new DesktopShortcutProbe(shell, Root);
        Assert.True(probe.Probe().Present);
        shell.Seed(Path, new ShellLink(@"C:\Foreign\other.exe"));
        shell.Fail(FakeShellLinks.Call.ReadLink, unchecked((int)0x80070020), times: 1, path: Path);

        var unreadable = probe.Probe();
        Assert.True(unreadable.Present || unreadable.Failed);
        shell.ResetCounters();
        var recovered = probe.Probe();

        Assert.False(recovered.Present);
        Assert.False(recovered.Failed);
        Assert.Equal(1, recovered.Reads);
        Assert.Equal(0, probe.Probe().Reads);
        Assert.Equal(0, shell.Changes);
    }
}
