// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the hardware probe seam (V-2.4).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>The probe: each step is reported as it completes (no fake delays: OD-0096 "real progress"). Production: <see cref="HardwareProbe"/>. Tests: <see cref="ScriptedProbe"/>.</summary>
public interface IHardwareProbe
{
    Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken);
}
