using Marrow.Core.Port;

namespace Marrow.Services.Tests.Fixtures;

// A scripted stand-in for a running Foundation Process (IProcessControl), for MCPHubTests'
// MCPLiveProcesses / TerminationLadder coverage (PR-MCP-191). Mirrors the shape of
// Marrow.Core.Tests/Fixtures/ProcessSamples.cs's FakeProcessControl, duplicated here because
// Marrow.Services.Tests does not reference the Marrow.Core.Tests project. Nothing here launches a real
// process (R-TEST-10).
public sealed class FakeProcessControl(string name = "process") : IProcessControl
{
    private readonly object gate = new();
    private readonly List<string> calls = [];
    private bool running = true;

    public string Name { get; } = name;

    public static FakeProcessControl Exited(string name = "process")
    {
        var process = new FakeProcessControl(name);
        process.Exit();
        return process;
    }

    /// <summary>When true, the graceful step ends the process at once (a child that honours it).</summary>
    public bool ExitsOnTerminate { get; set; }

    public IReadOnlyList<string> Calls
    {
        get { lock (gate) return [.. calls]; }
    }

    public void Exit()
    {
        lock (gate) running = false;
    }

    public bool IsRunning
    {
        get { lock (gate) { calls.Add("isRunning"); return running; } }
    }

    public void Terminate() => Record("terminate", end: ExitsOnTerminate);

    public void KillTree() => Record("killTree", end: true);

    public void KillProcess() => Record("killProcess", end: true);

    private void Record(string call, bool end)
    {
        lock (gate)
        {
            calls.Add(call);
            if (end) running = false;
        }
    }
}
