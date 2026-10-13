using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Assistant;

// PORT-ONLY: the Native gate of DESIGN.md 16.1 for AS-20 (ED-NEW-dextra-as20-12). The lead runs it alone, by hand, with nth's test
// window focused: the real Windows Hello is asked twice. The first time the owner verifies (face, fingerprint or PIN; or, on a PC
// without Hello, the signed-in account's Windows password in Windows' own prompt) and it reads Verified; the second time he
// cancels and it reads Cancelled. Never run by unit agents.
[Trait("Decision", "ED-NEW-dextra-as20-12")]
[Trait("Feature", "AS-20")]
[Trait("Platform", "WINDOWS-ONLY")]
public sealed class HelloGateNativeTests
{
    [Fact]
    public async Task Windows_hello_verifies_the_owner_and_a_cancelled_check_reads_cancelled()
    {
        var gate = new WindowsHelloGate(() => 0);
        Assert.Equal(HelloResult.Verified, await gate.Verify("verify now (native test 1 of 2)", CancellationToken.None));
        Assert.Equal(HelloResult.Cancelled, await gate.Verify("cancel this one (native test 2 of 2)", CancellationToken.None));
    }
}
