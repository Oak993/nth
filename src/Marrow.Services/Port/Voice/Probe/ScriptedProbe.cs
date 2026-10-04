// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the probe double of the seam table (V-2.4).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary><see cref="IHardwareProbe"/> for a <see cref="ScriptedMachine"/>: the real <see cref="HardwareProbe"/> over scripted reads, so rows and the profile are formatted by the production code.</summary>
public sealed class ScriptedProbe(ScriptedMachine machine) : IHardwareProbe
{
    public Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken) =>
        new HardwareProbe(new ScriptedHardwareSource(machine), new ScriptedGpuBackendCheck(machine.Backend)).RunAsync(report, cancellationToken);
}
