namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin: the non-parallel xUnit collection of Port/FileWatcherTests.cs (ED-167,
// R-TEST-5). Its forced-overflow test writes with Parallel.For on 16 writers and holds every thread-pool worker for
// about a second; a WorkingTreeWatch test running at the same moment had its watcher callbacks queued behind them
// until its 1 s cookie timeout (WorkingTreeWatch.swift:258-261) won. In this collection no other test class runs
// while FileWatcherTests does.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FileWatcherCollection
{
    public const string Name = "FileWatcherTests";
}
