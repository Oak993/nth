using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

// Matches the production lifetime comparison without querying or launching a process.
internal sealed class LifetimeNthProcesses(FakeNth nth) : IRunningNthProcesses
{
    public IReadOnlyList<RunningNthProcess> FindInside(string installRoot) => nth.FindInside(installRoot);
    public bool IsRunning(RunningNthProcess process) => nth.InRoot.Any(current =>
        current.ProcessId == process.ProcessId && (process.StartedAtTicks == 0 || current.StartedAtTicks == process.StartedAtTicks));
    public ProcessEndResult End(RunningNthProcess process) => nth.End(process);
}
