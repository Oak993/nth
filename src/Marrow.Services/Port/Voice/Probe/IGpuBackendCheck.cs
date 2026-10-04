// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the "GPU backend" step of the probe (V-10.1).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>Which GPU backend of Whisper.net loaded and passed a tiny load test on the chosen adapter (null: no usable adapter).</summary>
public interface IGpuBackendCheck
{
    GpuBackend Check(GpuAdapterInfo? adapter);
}
