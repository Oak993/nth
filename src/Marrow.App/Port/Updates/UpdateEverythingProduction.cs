// PORT-ONLY: first-release feature unit UE-5 (OD-0100). Fills the shared UpdateEverythingService with the items the running app has. Not reached by a test (the service takes its items by hand there).
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Providers;

namespace Marrow.App.Port.Updates;

internal static class UpdateEverythingProduction
{
    /// <summary>The items and the restart the app gives the service. An Ambient so that a launch test can swap in none and never reach a CLI, a listing or the updater (the review of UE-5).</summary>
    private static readonly Ambient<Func<AppModel, (IReadOnlyList<IUpdateItem> Items, Action Restart)>> Source = new(() => Real);

    /// <summary>For tests only: replaces what <see cref="Configure"/> builds, for the calling async flow.</summary>
    internal static IDisposable OverrideSource(Func<AppModel, (IReadOnlyList<IUpdateItem> Items, Action Restart)> source) => Source.Override(source);

    /// <summary>
    /// The provider CLIs that have an updater (through the registry's own update run, ED-308) with their public listings, then Marrow itself over Velopack (UE-1). The skills and tools group
    /// (SK-8) and the user's developer tools (UE-3) join the list when their units do. Starts the service's background checks (never in a development run).
    /// </summary>
    public static void Configure(AppModel model)
    {
        var (items, restart) = Source.Current(model);
        var service = UpdateEverythingService.Shared;
        service.Configure(items, restart);
        service.StartBackgroundChecks();
    }

    /// <summary>
    /// PORT-ONLY (UE-4, ED-343): where the units that own other kinds of item add them, so the pill and the Updates page list them: SK-13/SK-16 sets this to its SkillsPipeline's
    /// <c>SkillUpdateItem</c>s (kind SkillsAndTools, the page's "Skills and tools" section) and UE-3 to its developer-tool items (kind DevTool, "Developer tools"). Read when the service is configured;
    /// nothing sets it yet, so the groups are empty.
    /// </summary>
    internal static Func<IEnumerable<IUpdateItem>> ExtraItems { get; set; } = () => [];

    private static (IReadOnlyList<IUpdateItem> Items, Action Restart) Real(AppModel model)
    {
        var marrow = new MarrowUpdateItem(UpdateChecker.Shared, SelfUpdateEngine.Shared, (package, version) => AppUpdater.Shared.ApplyDownloaded(package, version));
        var items = ProviderCliUpdateItem.For(ProviderRegistry.UpdatableProviders, model.Providers, new FetchedVersionListing(HttpVersionFetch.Get)).Concat(ExtraItems()).Append(marrow).ToList();
        return (items, marrow.Restart);
    }
}
