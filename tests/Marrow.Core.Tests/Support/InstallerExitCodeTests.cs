using Marrow.Core.Support;

namespace Marrow.Core.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a and 13, unit U0 (ED-NEW-inst-u0-1; OD-0072). nth Setup's exit codes
// are one contract: 0 for done or already up to date, 1 for refused or failed (OD-0072, Velopack's own failure code), and
// the three silent outcomes the running-nth handshake and the install hold add (taken from unit/installer-close): 2
// cancelled, 3 nth not closed, 4 another installation in progress. Every code is distinct, so a script can tell them apart.
public sealed class InstallerExitCodeTests
{
    [Fact]
    [Trait("Decision", "OD-0072")]
    [Trait("Decision", "ED-247")]
    public void Every_setup_outcome_has_its_own_exit_code()
    {
        int[] codes = [0, InstallerGate.RefusedExitCode, InstallerGate.CancelledExitCode, InstallerGate.NotClosedExitCode, InstallerGate.InProgressExitCode];

        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, codes);
        Assert.Equal(codes.Length, codes.Distinct().Count());
    }
}
