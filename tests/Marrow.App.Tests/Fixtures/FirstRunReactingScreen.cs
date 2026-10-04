using Marrow.App.Port.FirstRun;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: an IFirstRunScreen that logs what it is told and lets the test react to each step event.</summary>
public sealed class FirstRunReactingScreen(List<string> log, Action<FirstRunProgress> onReport) : IFirstRunScreen
{
    public void Begin(IReadOnlyList<FirstRunStepInfo> steps) => log.Add("begin:" + string.Join(",", steps.Select(s => s.Step.Id)));

    public void Report(FirstRunProgress progress)
    {
        log.Add($"{progress.Step.Id}:{progress.State}:{progress.PartsDone}/{progress.Parts}");
        onReport(progress);
    }

    public Task EndAsync(FirstRunOutcome outcome)
    {
        log.Add("end:" + outcome.Kind);
        return Task.CompletedTask;
    }
}
