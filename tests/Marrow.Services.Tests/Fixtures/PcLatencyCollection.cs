namespace Marrow.Services.Tests.Fixtures;

// PORT-ONLY: Tactus, PC control (ED-NEW-pcctl-8; DESIGN.md 5.5 latency suite). The latency budgets are measured with a real
// Stopwatch, so those tests run alone, after the parallel tests, where other tests cannot steal their CPU time.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PcLatencyCollection
{
    public const string Name = "Tactus latency budgets";
}
