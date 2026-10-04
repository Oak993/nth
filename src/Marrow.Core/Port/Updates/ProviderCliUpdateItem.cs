// PORT-ONLY: first-release feature unit UE-2. A provider CLI as an update item (OD-0100: "every installed provider CLI that has an updater").
using Marrow.Core.Models;

namespace Marrow.Core.Port.Updates;

public sealed class ProviderCliUpdateItem(ProviderKind provider, ICliUpdateRunner runner, IVersionListing listing) : IUpdateItem
{
    public string Id { get; } = "cli:" + SwiftEnum<ProviderKind>.RawValue(provider);

    public string Title => provider.DisplayName;

    public UpdateItemKind Kind => UpdateItemKind.ProviderCli;

    public Task<string?> InstalledVersion(CancellationToken cancellation) => runner.InstalledCLIVersion(provider);

    public Task<string?> LatestVersion(CancellationToken cancellation) => listing.Latest(SwiftEnum<ProviderKind>.RawValue(provider), cancellation);

    /// <summary>The CLI's own updater has no progress to report, so the row stays indeterminate (BUILD-PLAN.md 2.4).</summary>
    public async Task<UpdateOutcome> Update(IProgress<double?> progress, CancellationToken cancellation)
    {
        var result = await runner.UpdateCLI(provider);
        return result.Status switch
        {
            CliUpdateStatus.Updated => new UpdateOutcome(true, result.After, true, false, null),
            CliUpdateStatus.Unchanged => new UpdateOutcome(true, null, false, false, null),
            CliUpdateStatus.Skipped => new UpdateOutcome(true, null, false, false, "uses a binary path set in Settings"),
            CliUpdateStatus.NotInstalled => new UpdateOutcome(false, null, false, false, "not installed"),
            CliUpdateStatus.Failed => new UpdateOutcome(false, null, false, false, "the updater failed or the version could not be read afterwards"),
        };
    }

    public static IReadOnlyList<IUpdateItem> For(IEnumerable<ProviderKind> providers, ICliUpdateRunner runner, IVersionListing listing) =>
        providers.Select(p => (IUpdateItem)new ProviderCliUpdateItem(p, runner, listing)).ToList();
}
