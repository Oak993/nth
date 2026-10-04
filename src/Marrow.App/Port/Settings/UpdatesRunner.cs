// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4). What the Updates page asks of "Update everything": the actions of UE-2's orchestrator, plus the restart UE-1's engine provides. The page reads and
// drives this and nothing else, so it shares the app's one orchestrator through UE-2's types only (the pill of UE-5 reads the same rows).
using Marrow.Core.Port;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Settings;

internal interface IUpdatesRunner
{
    IReadOnlyList<UpdateRow> Rows { get; }

    bool IsBusy { get; }

    /// <summary>Raised after every change of a row, on whatever thread made it.</summary>
    event Action? Changed;

    Task CheckNow();

    Task UpdateEverything();

    Task UpdateOne(string id);

    Task Retry(string id);

    /// <summary>"Restart Marrow": hands over to the engine that applies the downloaded package and relaunches.</summary>
    void Restart();
}

/// <summary>The orchestrator (UE-2) as the page's runner.</summary>
internal sealed class OrchestratorUpdatesRunner(UpdateOrchestrator orchestrator, Action restart) : IUpdatesRunner
{
    public IReadOnlyList<UpdateRow> Rows => orchestrator.Rows;

    public bool IsBusy => orchestrator.IsBusy;

    public event Action? Changed
    {
        add => orchestrator.Changed += value;
        remove => orchestrator.Changed -= value;
    }

    public Task CheckNow() => orchestrator.CheckNow();

    public Task UpdateEverything() => orchestrator.UpdateEverything();

    public Task UpdateOne(string id) => orchestrator.UpdateOne(id);

    public Task Retry(string id) => orchestrator.Retry(id);

    public void Restart() => restart();
}

/// <summary>
/// The runner the Updates page uses. Until the app gives it the real one (the unit that builds the app's items, UE-5's UpdateEverythingProduction, binds it at start-up with
/// <see cref="Bind"/>) it has no items, so the page lists nothing: the orchestrator is fed through UE-2's types only (ED-NEW-f-ue-4).
/// </summary>
internal static class UpdatesRunnerSource
{
    private static readonly Ambient<IUpdatesRunner> Running = new(() => new OrchestratorUpdatesRunner(new UpdateOrchestrator([]), () => { }));

    public static IUpdatesRunner Shared => Running.Current;

    /// <summary>App start-up: the one runner of the process.</summary>
    public static void Bind(IUpdatesRunner runner) => Running.SetProcessDefault(runner);

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    public static IDisposable OverrideShared(IUpdatesRunner runner) => Running.Override(runner);
}
