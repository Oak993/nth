using Marrow.Core.Port;

namespace Marrow.Core.Tests.Port;

// PORT-ONLY, R-TEST-10: the shell seams (a browser, File Explorer, another app) fail closed inside a test host.
public sealed class ShellGuardTests
{
    [Fact]
    [Trait("Rule", "R-TEST-10")]
    public void A_test_process_is_a_test_host_so_the_shell_seams_do_nothing_in_it() => Assert.True(ShellGuard.InTestHost);
}
