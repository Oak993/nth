using Marrow.Testing;

namespace Marrow.App.Tests;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 8a, "Canary", unit U10 (ED-NEW-inst-u10-2). This assembly drives the real shell-link
// code only in temp folders (ShellLinksRoundTripTests) and everything else on fakes, so the real Desktop and the real Start
// menu must look exactly as they did when the run began. The collection runs after every other test; the listing is names,
// write times and sizes only (nothing is opened).
[Collection("RealShellCanary")]
public sealed class RealShellCanaryTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    [Trait("Platform", "WINDOWS-ONLY")]
    public void The_real_Desktop_and_Start_menu_hold_the_same_nth_shortcuts_as_when_the_run_began()
    {
        Assert.True(RealShellCanary.HasBaseline, "the baseline is taken by this assembly's module initializer, before any test");

        Assert.Empty(RealShellCanary.Differences());
    }
}
