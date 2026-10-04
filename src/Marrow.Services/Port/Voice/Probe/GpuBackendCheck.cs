// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the real "GPU backend" step (V-10.1).
using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// Builds the runtime order for the chosen adapter (the CUDA folder counts only for an NVIDIA adapter), has the VAD load test run on it, and maps the runtime that loaded:
/// Cuda12 to CUDA, Vulkan to Vulkan, anything else, a failure, or no GPU runtime to try, to none (the processor). With no GPU runtime installed or no adapter nothing is loaded.
/// </summary>
public sealed class GpuBackendCheck(Func<RuntimeFacts> runtimeFacts, IVadLoadTest loadTest) : IGpuBackendCheck
{
    private const uint NvidiaVendorId = 0x10DE;

    public GpuBackend Check(GpuAdapterInfo? adapter)
    {
        if (adapter is null) return GpuBackend.None;
        var facts = runtimeFacts() with { NvidiaPresent = adapter.VendorId == NvidiaVendorId };
        var order = RuntimeOrder.For(facts, true);
        if (!order.Any(r => r is RuntimeKind.Cuda12 or RuntimeKind.Vulkan)) return GpuBackend.None;
        try
        {
            return loadTest.Run(order, facts.LibraryRoot) switch
            {
                RuntimeKind.Cuda12 => GpuBackend.Cuda,
                RuntimeKind.Vulkan => GpuBackend.Vulkan,
                _ => GpuBackend.None,
            };
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return GpuBackend.None;
        }
    }
}
