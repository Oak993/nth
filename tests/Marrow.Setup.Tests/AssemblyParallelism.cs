using Xunit;

// PORT-ONLY, no Swift twin: caps this assembly's own xUnit collection parallelism (R-TEST-5), as every Marrow test
// project does (see tests/Marrow.Core.Tests/AssemblyParallelism.cs for the reason).
[assembly: CollectionBehavior(MaxParallelThreads = 4)]
