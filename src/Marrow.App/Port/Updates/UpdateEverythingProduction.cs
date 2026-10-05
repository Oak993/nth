// PORT-ONLY: UE-3/UE-5 (OD-0100). Supplies the shared update service with the running app's installed items.
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Services.Port.Updates;
using Marrow.Services.Providers;

namespace Marrow.App.Port.Updates;

internal static class UpdateEverythingProduction
{
    private static readonly Logger Log = new("updates", "inventory");
    /// <summary>An Ambient so launch tests can supply items without reaching any production process or registry reader.</summary>
    private static readonly Ambient<Func<AppModel, Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)>>> Source = new(() => Real);

    internal static IDisposable OverrideSource(Func<AppModel, (IReadOnlyList<IUpdateItem> Items, Action Restart)> source) => Source.Override(model => Task.FromResult(source(model)));

    /// <summary>Waits for the installed inventory before replacing the service's rows. A failed source preserves its previous rows.</summary>
    internal static async Task ConfigureSource(UpdateEverythingService service, Func<Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)>> source)
    {
        var (items, restart) = await source();
        service.Configure(items, restart);
        service.StartBackgroundChecks();
    }

    public static void Configure(AppModel model)
    {
        var pending = ConfigureSource(UpdateEverythingService.Shared, () => Source.Current(model));
        if (pending.IsCompletedSuccessfully) return;
        MainActor.Spawn(async () =>
        {
            try { await pending; }
            catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                Log.Error("Update inventory could not be configured: " + error.GetType().Name);
            }
        });
    }

    /// <summary>UE-4/ED-343: the owning skills units add their SkillsAndTools items here.</summary>
    internal static Func<IEnumerable<IUpdateItem>> ExtraItems { get; set; } = () => [];

    private static async Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)> Real(AppModel model)
    {
        var marrow = new MarrowUpdateItem(UpdateChecker.Shared, SelfUpdateEngine.Shared, (package, version) => AppUpdater.Shared.ApplyDownloaded(package, version));
        var items = ProviderCliUpdateItem.For(ProviderRegistry.UpdatableProviders, model.Providers, new FetchedVersionListing(HttpVersionFetch.Get)).Concat(ExtraItems()).Append(marrow).ToList();
        try
        {
            items.AddRange(await DevToolUpdateCatalog.Discover(new WindowsDevToolProcess().Run, WindowsOfficeInstallation.Read, inventoryFailure: (tool, error) => Log.Error("Developer-tool inventory is incomplete for " + tool + ": " + error.GetType().Name)));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A failed developer inventory must not remove the app's existing provider/self-update items.
            Log.Error("Developer-tool inventory is unavailable: " + error.GetType().Name);
        }
        return (items, marrow.Restart);
    }
}
