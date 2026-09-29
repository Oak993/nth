using Xunit;

// PORT-ONLY, no Swift twin: caps this assembly's own xUnit collection parallelism (R-TEST-5).
//
// Root cause this addresses: run_suite.sh runs five dotnet test processes at once (Core, UI, App, and Services
// twice, split by filter), and xUnit's own default parallelism inside EACH of those processes is bounded only by
// Environment.ProcessorCount. On a 16-core machine that means the suite can ask for up to 5 x 16 = 80 concurrently
// running test collections at once, oversubscribing the machine's actual core count by 5x even with no other load
// at all. That oversubscription, not any one test's own logic, is what still made UpdateCheckerSecondTests and
// AppModelTests.Bootstrap_starts_a_sweep_... (both bounded, deterministic waits for a real background hop) exceed
// their 20s/120s bounds in a bare run_suite.sh run with zero artificial load, alongside ThreadPoolWarmup already
// raising this same assembly's own ThreadPool floor.
//
// Capping each assembly's own parallelism keeps the five processes' combined demand within a sane multiple of the
// core count instead of a multiple of the assembly count times the core count, without touching any test's own
// timeout, assertion or expected value.
[assembly: CollectionBehavior(MaxParallelThreads = 4)]
