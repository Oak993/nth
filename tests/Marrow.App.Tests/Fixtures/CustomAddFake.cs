using Marrow.App.Port.Skills.Pages;

namespace Marrow.App.Tests.Fixtures;

internal sealed class CustomAddFake : ICustomAddBackend
{
    public IReadOnlyList<CustomAddCandidate> Found { get; set; } = [new("ok", "ok", "A fixture skill", null), new("bad", "bad", "", "No SKILL.md")];
    public List<CustomAddRequest> Checks { get; } = [];
    public List<string> Adds { get; } = [];
    public TaskCompletionSource? Release { get; set; }
    public Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel)
    {
        Checks.Add(request);
        return Task.FromResult(Found);
    }
    public async Task Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel)
    {
        Adds.Add(candidate.Key);
        progress.Report(.5);
        if (Release is { } release) await release.Task.WaitAsync(cancel);
        progress.Report(1);
    }
}
