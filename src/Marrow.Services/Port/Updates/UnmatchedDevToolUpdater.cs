// PORT-ONLY: UE-3; an installed CLI with no winget match stays visible but cannot cause a guessed install.
using Marrow.Core.Port.Updates;

namespace Marrow.Services.Port.Updates;

internal sealed class UnmatchedDevToolUpdater(string id, string title, string version) : IUpdateItem
{
    public string Id => "dev:unmatched:" + id;
    public string Title => title;
    public UpdateItemKind Kind => UpdateItemKind.DevTool;
    public Task<string?> InstalledVersion(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult<string?>(version); }
    public Task<string?> LatestVersion(CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); return Task.FromResult<string?>(null); }
    public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult(new UpdateOutcome(false, version, false, false, "This installed tool has no winget match; no upgrade command was run."));
    }
}
