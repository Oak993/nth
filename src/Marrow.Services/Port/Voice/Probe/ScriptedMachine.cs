// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): a scripted machine for the probe's test doubles (V-2.4 seam table, V-T45).
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>The values DXGI, Win32 and the registry would give, and the GPU backend the VAD load test would confirm.</summary>
public sealed record ScriptedMachine(string Processor, int Cores, long MemoryBytes, IReadOnlyList<GpuAdapterInfo> Adapters, string Driver, GpuBackend Backend);
