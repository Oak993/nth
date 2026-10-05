// PORT-ONLY: first-release feature unit UE-5 (BUILD-PLAN.md 2.4 UE-5, OD-0100 place D). The app's one "Update everything": the orchestrator (UE-2) with its pill state. UE-4's Updates page reads the same service.
using Marrow.App.Port.Settings;
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;
using Marrow.Core.Support;

namespace Marrow.App.Port.Updates;

/// <summary>
/// Owns the <see cref="UpdateOrchestrator"/> and what the sidebar's pill and its popover show of it (<see cref="State"/>), and runs the pill's actions. One per process
/// (<see cref="Shared"/>, an Ambient a test replaces); it starts with no items and so shows nothing, until the app gives it its items (<see cref="Configure"/>). ENGINEERING DECISIONS
/// (ED-344): the background check starts after a launch delay and repeats on the same interval as <see cref="UpdateChecker"/>'s, and never in a development run or while a
/// run goes; a press while a run goes does nothing; the restart is a seam the app fills (Marrow's item applies its downloaded package).
/// </summary>
public sealed class UpdateEverythingService : MainActorModel, IUpdatesRunner
{
    /// <summary>Seconds after launch before the first check: after <see cref="UpdateChecker"/>'s own launch check (8 s), so the two do not ask the feed at once.</summary>
    public const double LaunchDelaySeconds = 15;

    /// <summary>Seconds between checks: <see cref="UpdateChecker"/>'s interval (6 hours).</summary>
    public const double CheckIntervalSeconds = 6 * 60 * 60;

    private static readonly Ambient<UpdateEverythingService> Running = new(() => new UpdateEverythingService([], () => { }));

    public static UpdateEverythingService Shared => Running.Current;

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    public static IDisposable OverrideShared(UpdateEverythingService service) => Running.Override(service);

    private UpdateOrchestrator orchestrator;
    private UpdatePillProjector projector = new();
    private Action restart;
    private UpdatePillState state = UpdatePillState.Hidden;
    private TaskHandle? backgroundTask;

    public UpdateEverythingService(IEnumerable<IUpdateItem> items, Action restart)
    {
        this.restart = restart;
        orchestrator = Attach(new UpdateOrchestrator(items));
    }

    /// <summary>What the pill and its popover show now; observable.</summary>
    public UpdatePillState State { get => Read(state); private set => Set(ref state, value); }

    /// <summary>The orchestrator's rows, in run order (the Updates page of UE-4 lists these).</summary>
    public IReadOnlyList<UpdateRow> Rows => orchestrator.Rows;

    public bool IsBusy => orchestrator.IsBusy;

    /// <summary>Gives the service the app's items and the restart ("Restart Marrow"). Replaces what it had; a run in progress is not interrupted (the old orchestrator finishes unobserved).</summary>
    public void Configure(IEnumerable<IUpdateItem> items, Action restart)
    {
        orchestrator.Changed -= OnChanged;
        this.restart = restart;
        projector = new UpdatePillProjector();
        orchestrator = Attach(new UpdateOrchestrator(items));
        Refresh();
    }

    private UpdateOrchestrator Attach(UpdateOrchestrator created)
    {
        created.Changed += OnChanged;
        return created;
    }

    /// <summary>Raised after every change of a row, on whatever thread made it (UE-4: the Updates page follows the same orchestrator as the pill).</summary>
    public event Action? Changed;

    private void OnChanged()
    {
        Changed?.Invoke();
        if (MainActor.IsIsolated) Refresh();
        else MainActor.Post(Refresh);
    }

    private void Refresh() => State = projector.Project(orchestrator.Rows, orchestrator.IsBusy);

    /// <summary>"Check now": reads every item's versions. Does nothing while a run goes.</summary>
    public async Task CheckNow()
    {
        var current = orchestrator;
        if (current.IsBusy) return;
        try { await current.CheckNow(); }
        catch (InvalidOperationException) { } // a run began between the test above and the start
        finally { OnChanged(); }
    }

    // UE-4: the Updates page drives the same orchestrator through IUpdatesRunner (one service, one set of rows for the pill and the page). The run's own refusals (a run is going) are the caller's to catch.
    Task IUpdatesRunner.UpdateEverything() => orchestrator.UpdateEverything();

    Task IUpdatesRunner.UpdateOne(string id) => orchestrator.UpdateOne(id);

    Task IUpdatesRunner.Retry(string id) => orchestrator.Retry(id);

    void IUpdatesRunner.Restart() => restart();

    /// <summary>What a press of the pill's button does (<see cref="UpdatePillState.Action"/>).</summary>
    public void Perform(UpdatePillAction action)
    {
        switch (action)
        {
            case UpdatePillAction.UpdateEverything:
                Start(c => c.UpdateEverything());
                break;
            case UpdatePillAction.Retry:
                if (orchestrator.Rows.FirstOrDefault(r => r.State == UpdateRowState.Failed)?.Id is { } id) Start(c => c.Retry(id));
                break;
            case UpdatePillAction.RestartMarrow:
                restart();
                break;
            case UpdatePillAction.CheckNow:
                MainActor.Spawn(async () => await CheckNow());
                break;
        }
    }

    /// <summary>The user has seen the done phase (its popover was closed): the pill leaves until the next run.</summary>
    public void AcknowledgeDone()
    {
        projector.Acknowledge();
        Refresh();
    }

    private void Start(Func<UpdateOrchestrator, Task> run)
    {
        var current = orchestrator;
        if (current.IsBusy) return;
        MainActor.Spawn(async () =>
        {
            try { await run(current); }
            catch (InvalidOperationException) { } // busy, or nothing to retry
            catch (OperationCanceledException) { }
            finally { OnChanged(); }
        });
    }

    /// <summary>Checks after the launch delay and then every interval, as <see cref="UpdateChecker.StartBackgroundChecks"/> does; never in a development run.</summary>
    public void StartBackgroundChecks()
    {
        if (backgroundTask is not null || AppInfo.Current.IsDevelopment) return;
        backgroundTask = MainActor.Spawn(async () =>
        {
            await SwiftTask.TrySleep(TimeSpan.FromSeconds(LaunchDelaySeconds));
            while (!SwiftTask.IsCancelled)
            {
                await CheckNow();
                await SwiftTask.TrySleep(TimeSpan.FromSeconds(CheckIntervalSeconds));
            }
        });
    }
}
