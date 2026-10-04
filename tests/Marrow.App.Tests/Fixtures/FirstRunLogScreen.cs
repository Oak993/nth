using Marrow.App.Port.FirstRun;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: an IFirstRunScreen that writes what it is told into a log (begin, each step event, the end).</summary>
public sealed class FirstRunLogScreen(List<string> log) : IFirstRunScreen
{
    public void Begin(IReadOnlyList<FirstRunStepInfo> steps) => log.Add("begin:" + string.Join(",", steps.Select(s => s.Step.Id)));
    public void Report(FirstRunProgress progress) => log.Add($"{progress.Step.Id}:{progress.State}:{progress.PartsDone}/{progress.Parts}");

    public Task EndAsync(FirstRunOutcome outcome)
    {
        log.Add("end:" + outcome.Kind);
        return Task.CompletedTask;
    }
}
