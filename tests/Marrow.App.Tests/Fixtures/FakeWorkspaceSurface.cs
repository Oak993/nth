using Marrow.App.Port.PcControl.Workspace;

namespace Marrow.App.Tests.Fixtures;

// PORT-ONLY: the workspace's window for ChildSessionHost tests (unit P18, ED-NEW-pcws-7). No window, no Remote Desktop
// control: Connect records the program line and answers what the test says (by default it runs OnConnect and connects);
// the test raises the session's end and the user's take-over.
internal sealed class FakeWorkspaceSurface : IWorkspaceSurface
{
    public List<string> Programs { get; } = [];

    /// <summary>What Connect answers: null connects; text is why not.</summary>
    public string? Problem { get; set; }

    /// <summary>Runs when Connect is called (the test starts the agent with the program line).</summary>
    public Action<string>? OnConnect { get; set; }

    public int Watches { get; private set; }

    public bool Disposed { get; private set; }

    public event Action? Ended;

    public event Action<bool>? TakenOver;

    public Task<string?> Connect(string startProgram, CancellationToken cancellationToken)
    {
        Programs.Add(startProgram);
        OnConnect?.Invoke(startProgram);
        return Task.FromResult(Problem);
    }

    public void Watch() => Watches++;

    public void End() => Ended?.Invoke();

    public void TakeOver(bool taken) => TakenOver?.Invoke(taken);

    public void Dispose() => Disposed = true;
}
