// PORT-ONLY, first-release UE-4 (BUILD-PLAN.md 2.4). What the Updates page asks of "Update everything": the actions of UE-2's orchestrator, plus the restart UE-1's engine provides. The page reads and
// drives this and nothing else, so it shares the app's one orchestrator through UE-2's types only (the pill of UE-5 reads the same rows).
using Marrow.App.Port.Updates;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Port.Settings;

internal interface IUpdatesRunner
{
    IReadOnlyList<UpdateRow> Rows { get; }

    bool IsBusy { get; }

    /// <summary>The release owned by this runner's Marrow install/restart row; never a global checker fallback.</summary>
    AvailableUpdate? MarrowOffer => null;

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
/// The runner the Updates page uses: the app's one <see cref="UpdateEverythingService"/> (UE-5), which owns the orchestrator the sidebar's pill reads too, so the page and the pill always show the same rows
/// (ED-343). A test replaces it for its own async flow.
/// </summary>
internal static class UpdatesRunnerSource
{
    private static readonly System.Threading.AsyncLocal<IUpdatesRunner?> Scoped = new();

    public static IUpdatesRunner Shared => Scoped.Value ?? UpdateEverythingService.Shared;

    /// <summary>For tests only: replaces <see cref="Shared"/> for the calling async flow.</summary>
    public static IDisposable OverrideShared(IUpdatesRunner runner)
    {
        var previous = Scoped.Value;
        Scoped.Value = runner;
        return new Restorer(() => Scoped.Value = previous);
    }

    private sealed class Restorer(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
