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
    private static readonly Ambient<Func<AppModel, Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)>>> Source = new(() => model => Real(model));

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
        Remember(model);
        Observe(ConfigureSource(UpdateEverythingService.Shared, build!));
    }

    private static void Observe(Task pending)
    {
        if (pending.IsCompletedSuccessfully) return;
        MainActor.Spawn(async () =>
        {
            try { await pending; }
            catch (Exception error) when (error is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
            {
                Log.Error("Update inventory could not be configured: " + error.GetType().Name);
            }
        });
    }

    private static Func<Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)>>? build;

    /// <summary>PORT-ONLY (SK-16): keeps how the items are built (the source of this moment and the model) so <see cref="Refresh"/> can build them again; a test calls it instead of <see cref="Configure"/>.</summary>
    internal static void Remember(AppModel model)
    {
        var source = Source.Current;
        build = () => source(model);
    }

    /// <summary>PORT-ONLY (SK-16): a test leaves nothing behind for the next one.</summary>
    internal static void Forget()
    {
        build = null;
        if (waitingOn is not null) waitingOn.Changed -= WhenIdle;
        waitingOn = null;
    }

    /// <summary>
    /// PORT-ONLY (SK-16): gives the service its items again, as <see cref="Configure"/> built them, after the set of items changed (the skills pipeline installed or removed one). On the main actor. Does
    /// nothing before the launch has configured the service. ENGINEERING DECISION (review): a service that is running an update keeps its rows for the run; the change is remembered and given as soon as the
    /// service is idle again (its next change of rows that leaves it not busy), so the rows are never left on an old set.
    /// </summary>
    internal static void Refresh() => MainActor.Post(() => Observe(Reconcile()));

    private static UpdateEverythingService? waitingOn;

    private static async Task Reconcile()
    {
        if (build is not { } rebuild) return;
        var service = UpdateEverythingService.Shared;
        if (service.IsBusy)
        {
            if (ReferenceEquals(waitingOn, service)) return;
            if (waitingOn is not null) waitingOn.Changed -= WhenIdle;
            waitingOn = service;
            service.Changed += WhenIdle;
            return;
        }
        var (items, restart) = await rebuild();
        if (!ReferenceEquals(build, rebuild)) return;
        if (service.IsBusy)
        {
            if (!ReferenceEquals(waitingOn, service))
            {
                if (waitingOn is not null) waitingOn.Changed -= WhenIdle;
                waitingOn = service;
                service.Changed += WhenIdle;
            }
            return;
        }
        service.Configure(items, restart);
    }

    private static void WhenIdle() => MainActor.Post(() =>
    {
        if (waitingOn is not { } service || service.IsBusy) return;
        service.Changed -= WhenIdle;
        waitingOn = null;
        Observe(Reconcile());
    });

    /// <summary>UE-4/ED-343: the owning skills units add their SkillsAndTools items here.</summary>
    internal static Func<IEnumerable<IUpdateItem>> ExtraItems { get; set; } = () => [];

    /// <summary>PORT-ONLY (OD-0134, ED-NEW-ignis-settings): Ignis's model points, appended beside the skills rows (never in place of them) while the Ignis controller is bound.</summary>
    internal static IEnumerable<IUpdateItem> IgnisItems() =>
        Marrow.Core.Port.Ignis.IgnisSeams.Controller.Current() is { } ignis
            ? [new IgnisPointsUpdateItem(ignis.Points, () => ignis.Settings.Value.AutoUpdatePoints)]
            : [];

    /// <summary>The production inventory. The process/registry seams default to the real boundary; a test supplies recorded or fake answers, so no real tool ever runs.</summary>
    internal static async Task<(IReadOnlyList<IUpdateItem> Items, Action Restart)> Real(AppModel model,
        Func<DevToolCommand, CancellationToken, Task<DevToolCommandResult>>? run = null, Func<CancellationToken, Task<OfficeInstallation?>>? office = null)
    {
        run ??= new WindowsDevToolProcess(WindowsGitInstallerLog.Read).Run;
        office ??= WindowsOfficeInstallation.Read;
        var marrow = new MarrowUpdateItem(UpdateChecker.Shared, SelfUpdateEngine.Shared, (package, version) => AppUpdater.Shared.ApplyDownloaded(package, version));
        var items = ProviderCliUpdateItem.For(ProviderRegistry.UpdatableProviders, model.Providers, new FetchedVersionListing(HttpVersionFetch.Get)).Concat(ExtraItems()).Concat(IgnisItems()).Append(marrow).ToList();
        try
        {
            items.AddRange(await DevToolUpdateCatalog.Discover(run, office, inventoryFailure: (tool, error) => Log.Error("Developer-tool inventory is incomplete for " + tool + ": " + error.GetType().Name)));
        }
        catch (Exception error) when (error is InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A failed developer inventory must not remove the app's existing provider/self-update items.
            Log.Error("Developer-tool inventory is unavailable: " + error.GetType().Name);
        }
        return (items, marrow.Restart);
    }
}
