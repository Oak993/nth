using Marrow.Core.Support;
using Marrow.Setup.Platform;

namespace Marrow.Setup.Tests.Platform;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 5 ("Leftovers": nth deletes the setup's folder "when no setup is running"),
// unit U9 (ED-NEW-inst-u9-1). A setup (or the uninstaller) is running while its named lock exists; the probe only opens the
// lock, so it never takes it. The tests use lock names of their own and never the real Local\nth Setup, which a setup running
// on this PC may hold.
public sealed class NamedLockSetupProbeTests
{
    private static string TestName() => @"Local\nth-test-lock-" + Guid.NewGuid().ToString("N");

    [Fact]
    [Trait("Decision", "ED-247")]
    public void No_lock_means_no_setup_is_running()
    {
        Assert.False(new NamedLockSetupProbe(TestName()).IsSetupRunning());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_setups_own_lock_means_a_setup_is_running_until_it_ends()
    {
        var name = TestName();
        var probe = new NamedLockSetupProbe(name);

        var instance = SingleInstance.TryAcquire(name);
        Assert.NotNull(instance);
        Assert.True(probe.IsSetupRunning());
        Assert.True(probe.IsSetupRunning(), "looking does not take the lock or release it");

        instance.Dispose();
        Assert.False(probe.IsSetupRunning());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void Looking_for_the_lock_does_not_stop_a_setup_from_taking_it()
    {
        var name = TestName();

        Assert.False(new NamedLockSetupProbe(name).IsSetupRunning());
        using var instance = SingleInstance.TryAcquire(name);

        Assert.NotNull(instance);
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void A_name_that_cannot_be_looked_up_counts_as_running_so_nothing_is_swept_on_a_guess()
    {
        Assert.True(new NamedLockSetupProbe("").IsSetupRunning());
    }

    [Fact]
    [Trait("Decision", "ED-247")]
    public void The_default_probe_looks_for_the_name_the_setup_takes()
    {
        var real = typeof(NamedLockSetupProbe).GetConstructors().Single().GetParameters().Single();

        Assert.Equal(SetupFolders.SetupLockName, real.DefaultValue);
        Assert.Equal(SingleInstance.SetupLockName, real.DefaultValue);
    }
}
