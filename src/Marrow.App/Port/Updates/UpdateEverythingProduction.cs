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
        Remember(model);
        var (items, restart) = build!();
        var service = UpdateEverythingService.Shared;
        service.Configure(items, restart);
        service.StartBackgroundChecks();
    }

    private static Func<(IReadOnlyList<IUpdateItem> Items, Action Restart)>? build;

    /// <summary>PORT-ONLY (SK-16): keeps how the items are built (the source of this moment and the model) so <see cref="Refresh"/> can build them again; a test calls it instead of <see cref="Configure"/>.</summary>
    internal static void Remember(AppModel model)
    {
        var source = Source.Current;
        build = () => source(model);
    }

    /// <summary>PORT-ONLY (SK-16): a test leaves nothing behind for the next one.</summary>
    internal static void Forget() => build = null;

    /// <summary>
    /// PORT-ONLY (SK-16): gives the service its items again, as <see cref="Configure"/> built them, after the set of items changed (the skills pipeline installed or removed one). On the main actor. Does
    /// nothing before the launch has configured the service, and leaves a service that is running an update alone (the run keeps its rows; the next change reads the new set).
    /// </summary>
    internal static void Refresh() => MainActor.Post(() =>
    {
        if (build is not { } rebuild) return;
        var service = UpdateEverythingService.Shared;
        if (service.IsBusy) return;
        var (items, restart) = rebuild();
        service.Configure(items, restart);
    });

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
