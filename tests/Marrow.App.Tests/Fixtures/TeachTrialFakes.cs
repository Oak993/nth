using Marrow.App.Assistant;
using Marrow.Core.Port.Assistant;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-19: the owner as a trial sees him: he answers each step the trial puts to him as the test says, and the order of everything is kept.</summary>
internal sealed class FakeTrialHost : ITrialHost
{
    public List<string> Order { get; } = [];

    public Dictionary<int, TrialAnswer> Answers { get; } = [];

    public List<TrialProgress> Progresses { get; } = [];

    /// <summary>A step the host does not answer until the test lets it (the owner is thinking).</summary>
    public Dictionary<int, TaskCompletionSource<TrialAnswer>> Holds { get; } = [];

    public Action<TrialProgress>? OnProgress { get; set; }

    private readonly Dictionary<int, TaskCompletionSource> asked = [];

    /// <summary>Completes when the trial puts this step to the owner (asked or handed over).</summary>
    public Task Asked(int step)
    {
        lock (asked) return Marker(step).Task;
    }

    private TaskCompletionSource Marker(int step)
    {
        if (!asked.TryGetValue(step, out var marker)) asked[step] = marker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        return marker;
    }

    public Task<TrialAnswer> Ask(TeachStep step, TrialAsk why, bool doneByOwner, CancellationToken cancel)
    {
        lock (Order) Order.Add($"ask:{step.Number}:{why}:{(doneByOwner ? "owner" : "nth")}");
        return Answer(step, cancel);
    }

    public Task<TrialAnswer> HandOver(TeachStep step, CancellationToken cancel)
    {
        lock (Order) Order.Add($"hand:{step.Number}");
        return Answer(step, cancel);
    }

    public void Progress(TrialProgress progress)
    {
        lock (Progresses) Progresses.Add(progress);
        OnProgress?.Invoke(progress);
    }

    private Task<TrialAnswer> Answer(TeachStep step, CancellationToken cancel)
    {
        lock (asked) Marker(step.Number).TrySetResult();
        if (Holds.TryGetValue(step.Number, out var hold)) return hold.Task.WaitAsync(cancel);
        return Task.FromResult(Answers.GetValueOrDefault(step.Number, TrialAnswer.Continue));
    }
}

/// <summary>Dextra AS-19: Tactus as a trial sees it: it does a step on the screen, in the order the host also keeps.</summary>
internal sealed class FakeExecutor(FakeTrialHost host) : ITeachStepExecutor
{
    public Dictionary<int, string> Fail { get; } = [];

    public List<int> Performed { get; } = [];

    public Task<string?> Perform(TeachStep step, CancellationToken cancel)
    {
        lock (host.Order) host.Order.Add($"do:{step.Number}");
        lock (Performed) Performed.Add(step.Number);
        return Task.FromResult(Fail.GetValueOrDefault(step.Number));
    }
}
