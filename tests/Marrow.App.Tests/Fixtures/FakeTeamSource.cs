using Marrow.App.Assistant.Views;

namespace Marrow.App.Tests.Fixtures;

/// <summary>Dextra AS-17: a team source the test sets; every read and every Pause or Resume is recorded and answered with a fixed line.</summary>
public sealed class FakeTeamSource : ITeamSource
{
    public TeamSnapshot Current { get; set; } = TeamSnapshot.Empty;

    public List<string> Calls { get; } = [];

    public int Reads { get; private set; }

    public event Action? Changed;

    public TeamSnapshot Now()
    {
        Reads++;
        return Current;
    }

    /// <summary>Sets the team and says it changed.</summary>
    public void Set(TeamSnapshot snapshot)
    {
        Current = snapshot;
        Changed?.Invoke();
    }

    public void Raise() => Changed?.Invoke();

    /// <summary>Which Astur owns a task: nobody's by default, so Dextra's Missions group lists every task.</summary>
    public Func<Guid, string?> Owners { get; set; } = _ => null;

    public string? AsturOf(Guid mission) => Owners(mission);

    public string PauseTasks(string assistantId)
    {
        Calls.Add("Pause " + assistantId);
        return "Pausing " + assistantId + ".";
    }

    public string ResumeTasks(string assistantId)
    {
        Calls.Add("Resume " + assistantId);
        return "Resumed " + assistantId + ".";
    }
}
