using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;
namespace Marrow.App.Tests.Fixtures;
internal sealed class CudaCancellingProbe(CancellationTokenSource cancellation) : IHardwareProbe
{
    public async Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken token)
    {
        var profile = await new ScriptedProbe(ScriptedMachines.OwnersDesktop).RunAsync(report, token);
        cancellation.Cancel(); // The probe completed; cancellation arrives at the validation boundary.
        return profile;
    }
}
