namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the non-parallel xUnit collection of GitSecondTests (R-TEST-5).
// Git.worktreeIndexPaths (Git.swift:507-509) is one process-wide map, and its whole-map wipe at 64 entries
// (Git.swift:533) is only observable while no other test class writes to it. In this collection no other test class
// runs while GitSecondTests does.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitIndexPathCacheCollection
{
    public const string Name = "GitIndexPathCache";
}
