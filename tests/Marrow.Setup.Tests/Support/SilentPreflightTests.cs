using Marrow.Core.Support;
using Marrow.Setup.Support;
using Marrow.Setup.Tests.Fakes;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3 and 3a, unit U4 (ED-NEW-inst-u4-2 and -3; OD-0072; VELOPACK_CHECK.md "Old branches" items 2-3).
// What a --silent setup does before it runs Velopack, with nobody to ask: another install in progress is exit 4; a running nth that is idle
// quits and the install goes ahead holding the root; one that is busy, hung or refusing is exit 3 and nothing was closed or ended; cancelled
// is exit 2. The hold roots are random temporary paths, so no test touches a real install.
public sealed class SilentPreflightTests
{
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "nth-preflight-test-" + Guid.NewGuid().ToString("N"), "Programs", "nth");

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task No_nth_running_goes_ahead_and_holds_the_root_until_the_install_is_over()
    {
        var root = NewRoot();
        var rig = new HandshakeRig(running: false);

        using (var preflight = await SilentPreflight.RunAsync(root, rig.Handshake))
        {
            Assert.Null(preflight.ExitCode);
            Assert.NotNull(preflight.Hold);
            Assert.True(InstallHold.IsHeld(root));
            Assert.Null(InstallHold.TryAcquire(root)); // a second setup would be told it is not alone
        }

        Assert.False(InstallHold.IsHeld(root));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task An_idle_nth_is_asked_to_quit_and_the_install_goes_ahead()
    {
        var root = NewRoot();
        var rig = new HandshakeRig();

        using var preflight = await Task.Run(() => rig.Time.RunAsync(SilentPreflight.RunAsync(root, rig.Handshake)));

        Assert.Null(preflight.ExitCode);
        Assert.False(rig.Nth.Running);
        Assert.Empty(rig.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task Another_install_in_progress_is_exit_4_and_nothing_is_asked_run_or_changed()
    {
        var root = NewRoot();
        using var other = InstallHold.TryAcquire(root);
        var rig = new HandshakeRig();

        using var preflight = await SilentPreflight.RunAsync(root, rig.Handshake);

        Assert.Equal(InstallerGate.InProgressExitCode, preflight.ExitCode);
        Assert.Equal(4, preflight.ExitCode);
        Assert.Null(preflight.Hold);
        Assert.Empty(rig.Nth.Sent);
        Assert.True(rig.Nth.Running);
        Assert.True(InstallHold.IsHeld(root)); // the other setup still holds it
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_busy_nth_is_exit_3_a_silent_setup_never_stops_anyones_work_and_the_hold_is_let_go()
    {
        var root = NewRoot();
        var rig = new HandshakeRig();
        rig.Nth.Work = _ => HandshakeRig.TwoThreads;

        using var preflight = await SilentPreflight.RunAsync(root, rig.Handshake);

        Assert.Equal(InstallerGate.NotClosedExitCode, preflight.ExitCode);
        Assert.Equal(3, preflight.ExitCode);
        Assert.Equal(["status", "quit:if-idle"], rig.Nth.Sent);
        Assert.True(rig.Nth.Running);
        Assert.False(InstallHold.IsHeld(root));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_hung_or_refusing_nth_is_exit_3_and_is_never_ended_without_anyone_to_say_yes()
    {
        var hung = new HandshakeRig();
        hung.Nth.ExitDelay = null;
        var refusing = new HandshakeRig();
        refusing.Nth.RefuseWith = "not-allowed";

        using var first = await Task.Run(() => hung.Time.RunAsync(SilentPreflight.RunAsync(NewRoot(), hung.Handshake)));
        using var second = await SilentPreflight.RunAsync(NewRoot(), refusing.Handshake);

        Assert.Equal(3, first.ExitCode);
        Assert.Equal(3, second.ExitCode);
        Assert.Empty(hung.Nth.Ended);
        Assert.Empty(refusing.Nth.Ended);
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public async Task A_cancelled_check_is_exit_2()
    {
        var root = NewRoot();
        var rig = new HandshakeRig();
        rig.Nth.Pipe = FakePipe.Missing;
        using var cancel = new CancellationTokenSource();
        var preflight = SilentPreflight.RunAsync(root, rig.Handshake, cancel.Token);
        await rig.Time.AdvanceUntilAsync(() => false, TimeSpan.FromSeconds(1));

        cancel.Cancel();
        using var result = await rig.Time.RunAsync(preflight);

        Assert.Equal(InstallerGate.CancelledExitCode, result.ExitCode);
        Assert.Equal(2, result.ExitCode);
        Assert.False(InstallHold.IsHeld(root));
    }
}
