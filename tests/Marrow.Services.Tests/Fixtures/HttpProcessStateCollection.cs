namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the one non-parallel xUnit collection of the HTTP unit (R-TEST-5, ED-118
// item 6). Tests that change process-wide state (the cookie store and the response cache of URLSession.Shared) run
// in it, so no parallel test sees their cookies or cached answers while they run.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HttpProcessStateCollection
{
    public const string Name = "HttpProcessState";
}
