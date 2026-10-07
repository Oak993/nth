using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Fixtures;

public sealed class HeldVoiceProbe : IHardwareProbe
{
    private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Started { get; private set; }
    public void Complete() => gate.SetResult();
    public async Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken)
    {
        Started = true;
        await gate.Task.WaitAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return await new ScriptedProbe(ScriptedMachines.OwnersDesktop).RunAsync(report, cancellationToken);
    }
}
