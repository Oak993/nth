// UE-4 test support: an Updates page runner whose rows the test sets by hand and whose actions it counts, for the cases the orchestrator cannot make (its item list is fixed): rows and sections that come and go.
using Marrow.App.Port.Settings;
using Marrow.Core.Port.Updates;

namespace Marrow.App.Tests.Fixtures;

internal sealed class ScriptedUpdatesRunner : IUpdatesRunner
{
    public IReadOnlyList<UpdateRow> Rows { get; set; } = [];

    public bool IsBusy { get; set; }

    public int Checks { get; private set; }

    public event Action? Changed;

    public void Raise() => Changed?.Invoke();

    public Task CheckNow()
    {
        Checks++;
        return Task.CompletedTask;
    }

    public Task UpdateEverything() => Task.CompletedTask;

    public Task UpdateOne(string id) => Task.CompletedTask;

    public Task Retry(string id) => Task.CompletedTask;

    public void Restart()
    {
    }
}
