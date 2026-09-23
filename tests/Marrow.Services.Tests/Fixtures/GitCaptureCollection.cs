namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the non-parallel xUnit collection of GitCaptureTests (R-TEST-5). The
// working-tree watches are one process-wide registry that tears the oldest down past 32 (WorkingTreeWatch.swift:92-108),
// and a torn-down watch makes every capture through it throw (OD-0008 A3). A test that reads whether a capture proved
// itself must not have its watch evicted by another test class creating watches at the same moment, so in this
// collection no other test class runs while GitCaptureTests does.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitCaptureCollection
{
    public const string Name = "GitCapture";
}
