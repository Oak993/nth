// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the backend check double (V-2.4, V-T45).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary><see cref="IGpuBackendCheck"/> that answers a scripted backend and records the adapters it was asked about. It loads no native library.</summary>
public sealed class ScriptedGpuBackendCheck(GpuBackend result) : IGpuBackendCheck
{
    public List<GpuAdapterInfo?> Checks { get; } = [];

    /// <summary>Called just before the check answers.</summary>
    public Action? Called { get; set; }

    public GpuBackend Check(GpuAdapterInfo? adapter)
    {
        Checks.Add(adapter);
        Called?.Invoke();
        return result;
    }
}
