using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Fixtures;

public sealed class BackgroundUpdateItem : IUpdateItem
{
    public string Id => "dev:office";
    public string Title => "Office";
    public UpdateItemKind Kind => UpdateItemKind.DevTool;
    public string Version { get; set; } = "16.0.20430.20092";
    public int Reads { get; private set; }
    public int Updates { get; private set; }
    public Task<string?> InstalledVersion(CancellationToken cancellation) { Reads++; return Task.FromResult<string?>(Version); }
    public Task<string?> LatestVersion(CancellationToken cancellation) => Task.FromResult<string?>(null);
    public Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        Updates++;
        return Task.FromResult(new UpdateOutcome(true, Version, false, false, "Updating in the background", UpdatingInBackground: true));
    }
}
