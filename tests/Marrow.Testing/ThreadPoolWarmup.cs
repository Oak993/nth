using System.Runtime.CompilerServices;

namespace Marrow.Testing;

/// <summary>
/// Raises the BCL ThreadPool's minimum thread counts once per test process, before any test runs (R-TEST-5).
///
/// Root cause this exists for: several App tests (UpdateChecker's background schedule, AppUpdater's install
/// flow, ThreadRuntime's document load) start real background work with <c>TaskHandle.Detached</c>/<c>Task.Run</c>
/// (R-ISO-2's @concurrent boundary) or a real loopback HTTP round trip, then a test thread blocks synchronously
/// inside <see cref="ManualMainLoop.RunUntil"/> or <see cref="ManualMainLoop.Run{T}"/> polling for that work to
/// post back. Each such wait occupies one xUnit worker thread (xUnit schedules test methods on the ThreadPool) for
/// up to tens of seconds. Under the full suite's default parallelism (every test collection runs concurrently,
/// bounded by Environment.ProcessorCount) plus an already-busy machine, enough worker threads end up parked in
/// these blocking waits that the CLR's default ThreadPool - which injects new threads slowly under contention -
/// cannot promptly run the very continuations (HTTP completion callbacks, Task.Run bodies) those waits are stuck
/// on. That starvation, not any timer or clock in product code, is what turns a sub-millisecond round trip into
/// "TimeoutException: The main loop did not reach the condition." only when the suite runs under load.
///
/// The fix is structural, not a raised test timeout: give the pool enough always-available worker and I/O
/// threads up front so blocking test-side waits never compete with the background work they are waiting for.
/// Nothing about test timing, an assertion or an expected value changes; this only changes how many threads the
/// process starts with.
/// </summary>
internal static class ThreadPoolWarmup
{
    // CA2255 warns that ModuleInitializer is meant for "application code or advanced source generator
    // scenarios": a test assembly IS the application here (there is no other entry point before xUnit starts
    // discovering and running tests), which is exactly the advanced scenario the rule's own doc calls out.
#pragma warning disable CA2255
    [ModuleInitializer]
    public static void EnsureMinThreads()
    {
        // Generous and cheap: idle threads above the pool's own steady-state usage cost memory, not CPU. 64 is
        // comfortably above the collection count any single Marrow test assembly runs at once, and independent of
        // core count so a small-core CI agent still gets enough headroom under run_suite.sh's five-way process
        // parallelism plus xUnit's own per-assembly collection parallelism.
        var floor = Math.Max(64, Environment.ProcessorCount * 8);
        ThreadPool.GetMinThreads(out var workerFloor, out var ioFloor);
        ThreadPool.SetMinThreads(Math.Max(workerFloor, floor), Math.Max(ioFloor, floor));
    }
#pragma warning restore CA2255
}
