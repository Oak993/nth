using Marrow.Core.Models;

namespace Marrow.Core.Tests.Fixtures;

/// <summary>A background-job source a test drives by hand: the jobs it lists, the actions it was asked for, and whether it accepts them or throws.</summary>
public sealed class FakeBackgroundJobSource(string name) : IBackgroundJobSource
{
    public List<BackgroundJob> Jobs { get; } = [];
    public List<string> Stopped { get; } = [];
    public List<string> Paused { get; } = [];
    public List<string> Resumed { get; } = [];
    public List<string> Dismissed { get; } = [];
    public bool Accept { get; set; } = true;
    public bool Throws { get; set; }
    public int Snapshots { get; private set; }

    public string Name => name;

    public IReadOnlyList<BackgroundJob> Snapshot(DateTimeOffset now)
    {
        Snapshots++;
        return Throws ? throw new InvalidOperationException("broken source") : Jobs.ToArray();
    }

    public bool Stop(string jobId) { Stopped.Add(jobId); return Accept; }

    public bool Pause(string jobId) { Paused.Add(jobId); return Accept; }

    public bool Resume(string jobId) { Resumed.Add(jobId); return Accept; }

    public void Dismiss(string jobId) => Dismissed.Add(jobId);
}
