using Marrow.Setup.Support;

namespace Marrow.Setup.Tests.Fakes;

// Discovery can fail to describe a known live process. No OS process or pipe is involved.
internal sealed class UndescribedNthProcesses(RunningNthProcess original) : IRunningNthProcesses
{
    public bool DiscoveryAvailable { get; set; } = true;
    public List<RunningNthProcess> EndAttempts { get; } = [];

    public IReadOnlyList<RunningNthProcess> FindInside(string installRoot) => DiscoveryAvailable ? [original] : [];
    public bool IsRunning(RunningNthProcess process) => process == original;

    public ProcessEndResult End(RunningNthProcess process)
    {
        EndAttempts.Add(process);
        DiscoveryAvailable = false;
        return ProcessEndResult.Failed;
    }
}
