using Xunit;

// PORT-ONLY, no Swift twin: the App integration fixtures share one process, not independent apps (R-TEST-5).
// ThemeManager.Current and its event, DocumentPrefetch.Shared and DiskWriter.Shared are process-wide,
// while each AppModelHarness owns a different manual main loop and ambient storage scope. An independent
// test collection is not a boundary around those shared services. Ordinary whole-assembly runs exposed
// cross-thread theme delivery and repeated non-completing parallel collections; single-test runs did not.
//
// Schedule collections sequentially in the ordinary gate. This is fixture isolation, not a claim that a
// particular production deadlock was diagnosed or repaired. Tests still execute their explicit concurrent
// jobs, threads, child processes, merge waiters and native input. No test, oracle or timeout is removed.
// The old four-collection cap remains in history; runner-side opt-ins are not required for this policy.
// https://xunit.net/docs/running-tests-in-parallel
[assembly: CollectionBehavior(DisableTestParallelization = true)]
