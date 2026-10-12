using Marrow.Setup.Install;

namespace Marrow.Setup.Tests.Install;

public sealed partial class C7U3AttemptIdentityTests
{
    [Fact]
    [Trait("Decision", "ED-247")]
    public void An_unbound_post_exit_adapter_cannot_report_success()
    {
        Assert.Throws<InvalidOperationException>(() => new NoAfterSetupExit().Run(Path.Combine(area, "nth"), Path.Combine(area, "result.json")));
    }
}
