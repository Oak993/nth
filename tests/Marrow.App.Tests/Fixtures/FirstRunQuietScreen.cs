using Marrow.App.Port.FirstRun;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: an IFirstRunScreen that ignores everything it is told.</summary>
public sealed class FirstRunQuietScreen : IFirstRunScreen
{
    public void Begin(IReadOnlyList<FirstRunStepInfo> steps) { }
    public void Report(FirstRunProgress progress) { }
    public Task EndAsync(FirstRunOutcome outcome) => Task.CompletedTask;
}
