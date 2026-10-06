using Marrow.Core.Port.Updates;
using Marrow.App.Port.Updates;
using Marrow.Core.Port;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY test fixture, no Swift twin (UE-5, OD-0100): an update item that runs nothing and reaches nothing. Its versions are set by the test; an update waits on
// Gate (when set) so a test can look at the pill while a run is in progress, and reports Steps through the progress it is given.
public sealed class FakeUpdateItem(string id, string title, UpdateItemKind kind, string? installed, string? latest) : IUpdateItem, IMarrowUpdateOffer
{
    // Legacy offer-view tests inject a cached release before constructing this fake. Capture that matching metadata once,
    // as the real Marrow item does; no singleton is consulted. Tests can supply their own offer explicitly.
    public AvailableUpdate? OfferedRelease { get; set; } = kind == UpdateItemKind.Marrow &&
        UserDefaults.Standard.Current.Data("updateChecker_release") is { } data &&
        SwiftTry.Optional(() => AvailableUpdate.DecodeFromBytes(data)) is { } offer && offer.Version == latest ? offer : null;

    public string Id { get; } = id;

    public string Title { get; } = title;

    public UpdateItemKind Kind { get; } = kind;

    public string? Installed { get; set; } = installed;

    public string? Latest { get; set; } = latest;

    public bool NeedsRestart { get; set; }

    public bool Fails { get; set; }

    public Task? Gate { get; set; }

    public double?[] Steps { get; set; } = [];

    public int Updates { get; private set; }

    public Task<string?> InstalledVersion(CancellationToken cancellation) => Task.FromResult(Installed);

    public Task<string?> LatestVersion(CancellationToken cancellation) => Task.FromResult(Latest);

    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        Updates++;
        foreach (var step in Steps) progress.Report(step);
        if (Gate is { } gate) await gate;
        if (Fails) return new UpdateOutcome(false, null, false, false, "the updater failed");
        Installed = Latest;
        return new UpdateOutcome(true, Latest, true, NeedsRestart, null);
    }
}
