using Marrow.App.Port.FirstRun;

namespace Marrow.App.Tests.Fixtures;

/// <summary>GR-1 tests: an IFirstRunScreen that logs what it is told and keeps every step event.</summary>
public sealed class FirstRunRecordingScreen(List<string> log) : IFirstRunScreen
{
    public List<FirstRunProgress> Reports { get; } = [];

    public void Begin(IReadOnlyList<FirstRunStepInfo> steps) => log.Add("begin:" + string.Join(",", steps.Select(s => s.Step.Id)));

    public void Report(FirstRunProgress progress)
    {
        Reports.Add(progress);
        log.Add($"{progress.Step.Id}:{progress.State}:{progress.PartsDone}/{progress.Parts}");
    }

    public Task EndAsync(FirstRunOutcome outcome)
    {
        log.Add("end:" + outcome.Kind);
        return Task.CompletedTask;
    }
}
