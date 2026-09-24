namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the non-parallel xUnit collection of Providers/PlanLimitsTests.cs (ED-190,
// R-TEST-5), on the ED-167 pattern. Its readClaude dispatch test drives the Claude stand-in through both internal paths
// and waits on the stand-in's control replies; in the full parallel Services run those waits starved behind other
// process-heavy test classes until the 120-150 s hang detector killed the test host, while the class passed alone
// (about 20 s). In this collection no other test class runs while PlanLimitsTests does.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PlanLimitsProcessCollection
{
    public const string Name = "PlanLimitsTests";
}
