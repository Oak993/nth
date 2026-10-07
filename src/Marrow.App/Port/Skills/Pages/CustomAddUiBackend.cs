// PORT-ONLY RR-18: bind Check/Add to the exact validated service token, without catalogue or provider-import substitution.
using Marrow.Services.Port.Skills.Custom;
using ServiceCandidate = Marrow.Services.Port.Skills.Custom.CustomAddCandidate;
using ServiceSource = Marrow.Services.Port.Skills.Custom.CustomAddSource;

namespace Marrow.App.Port.Skills.Pages;

internal interface ICustomAddLifetime
{
    Task Discard(IReadOnlyList<CustomAddCandidate> candidates);
}

internal sealed class CustomAddUiBackend(CustomSources sources, CustomOwnModel own) : ICustomAddBackend, ICustomAddLifetime
{
    private readonly object gate = new();
    private readonly Dictionary<string, ServiceCandidate> checkedItems = [];
    public async Task<IReadOnlyList<CustomAddCandidate>> Check(CustomAddRequest request, CancellationToken cancel)
    {
        var source = request.Source switch { CustomAddSource.Folder => ServiceSource.Folder, CustomAddSource.Zip => ServiceSource.Zip, CustomAddSource.GitHub => ServiceSource.GitHub, _ => throw new ArgumentOutOfRangeException(nameof(request)) };
        var found = await sources.Check(new(CustomOwnModel.ServiceKind(request.Kind), source, request.Location, request.Name, request.Command), cancel);
        lock (gate) foreach (var candidate in found) checkedItems[candidate.Key] = candidate;
        return found.Select(c => new CustomAddCandidate(c.Key, c.Name, c.Description, c.Problem)).ToArray();
    }
    public async Task Add(CustomAddCandidate candidate, IProgress<double> progress, CancellationToken cancel)
    {
        ServiceCandidate exact;
        lock (gate)
        {
            if (!checkedItems.TryGetValue(candidate.Key, out exact!) || exact.Name != candidate.Name || exact.Description != candidate.Description || exact.Problem != candidate.Problem)
                throw new InvalidOperationException("Check this item before adding it.");
        }
        await sources.Add(exact, progress, cancel);
        lock (gate) checkedItems.Remove(candidate.Key);
        await own.Refresh();
    }
    public async Task Discard(IReadOnlyList<CustomAddCandidate> candidates)
    {
        ServiceCandidate[] discarded;
        lock (gate)
        {
            discarded = candidates.Select(c => checkedItems.GetValueOrDefault(c.Key)).OfType<ServiceCandidate>().ToArray();
            foreach (var candidate in discarded) checkedItems.Remove(candidate.Key);
        }
        await Task.Run(() => sources.Discard(discarded));
    }
}
