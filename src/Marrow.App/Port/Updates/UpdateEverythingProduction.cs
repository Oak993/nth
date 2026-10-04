// PORT-ONLY: first-release feature unit UE-5 (OD-0100). Fills the shared UpdateEverythingService with the items the running app has. Not reached by a test (the service takes its items by hand there).
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Providers;

namespace Marrow.App.Port.Updates;

internal static class UpdateEverythingProduction
{
    /// <summary>
    /// The provider CLIs that have an updater (through the registry's own update run, ED-308) with their public listings, then Marrow itself over Velopack (UE-1). The skills and tools group
    /// (SK-8) and the user's developer tools (UE-3) join the list when their units do. Starts the service's background checks (never in a development run).
    /// </summary>
    public static void Configure(AppModel model)
    {
        var marrow = new MarrowUpdateItem(UpdateChecker.Shared, SelfUpdateEngine.Shared, (package, version) => AppUpdater.Shared.ApplyDownloaded(package, version));
        var items = ProviderCliUpdateItem.For(ProviderRegistry.UpdatableProviders, model.Providers, new FetchedVersionListing(HttpVersionFetch.Get)).Append(marrow);
        var service = UpdateEverythingService.Shared;
        service.Configure(items, marrow.Restart);
        service.StartBackgroundChecks();
    }
}
