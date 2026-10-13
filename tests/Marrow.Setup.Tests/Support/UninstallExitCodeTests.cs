using Marrow.Core.Support;
using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Support;

// PORT-ONLY (no Swift twin): nth installer PLAN.md 3a, 8 and 10 ("Gate routing ... exit codes"), unit U10 (ED-NEW-inst-u10-1). A
// silent uninstall has no window, so its exit code is the only answer a script gets: 0 when nth was removed, 3 when a busy,
// silent or refusing nth was not closed, 4 when another setup holds the root, 1 for every failure. Every outcome the
// uninstaller can report has one of these, and the codes are the install gate's, not new ones.
public sealed class UninstallExitCodeTests
{
    [Theory]
    [InlineData(UninstallOutcome.Removed, 0)]
    [InlineData(UninstallOutcome.InProgress, 4)]
    [InlineData(UninstallOutcome.NthNotClosed, 3)]
    [InlineData(UninstallOutcome.NoUninstaller, 1)]
    [InlineData(UninstallOutcome.DidNotStart, 1)]
    [InlineData(UninstallOutcome.VelopackFailed, 1)]
    [Trait("Decision", "OD-0072")]
    public void Each_outcome_of_a_silent_uninstall_has_its_exit_code(UninstallOutcome outcome, int exitCode)
    {
        Assert.Equal(exitCode, Program.UninstallExitCode(outcome));
    }

    [Fact]
    [Trait("Decision", "OD-0072")]
    public void No_outcome_is_left_without_a_row_and_the_codes_are_the_gates()
    {
        var outcomes = Enum.GetValues<UninstallOutcome>();

        Assert.Equal(6, outcomes.Length); // a new outcome needs its code decided here
        Assert.All(outcomes, outcome => Assert.Contains(Program.UninstallExitCode(outcome),
            new[] { 0, InstallerGate.RefusedExitCode, InstallerGate.NotClosedExitCode, InstallerGate.InProgressExitCode }));
        Assert.Equal(4, Program.UninstallExitCode(UninstallOutcome.InProgress));
        Assert.Equal(InstallerGate.InProgressExitCode, Program.UninstallExitCode(UninstallOutcome.InProgress));
        Assert.Equal(InstallerGate.NotClosedExitCode, Program.UninstallExitCode(UninstallOutcome.NthNotClosed));
    }
}
