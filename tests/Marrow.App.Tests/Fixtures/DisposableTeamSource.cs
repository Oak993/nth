using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-17: a team source that notes when it is let go, for the tests of who owns what.</summary>
public sealed class DisposableTeamSource(ITeamSource inner) : ITeamSource, IDisposable
{
    public bool Disposed { get; private set; }

    public event Action? Changed
    {
        add => inner.Changed += value;
        remove => inner.Changed -= value;
    }

    public TeamSnapshot Now() => inner.Now();

    public string PauseTasks(string assistantId) => inner.PauseTasks(assistantId);

    public string ResumeTasks(string assistantId) => inner.ResumeTasks(assistantId);

    public void Dispose() => Disposed = true;
}
