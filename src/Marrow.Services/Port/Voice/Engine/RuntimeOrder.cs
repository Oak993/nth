// PORT-ONLY, owner-ruled addition (OD-0091, OD-0092, OD-0099 Q-V-9 C; first-release voice typing, VT-6): the runtime order the loader is given (V-7.1; voice-pins.json cuda.feasibility.order).
namespace Marrow.Services.Port.Voice.Engine;

/// <summary>
/// "CUDA folder if present and NVIDIA, else Vulkan, else CPU": the installed runtimes in the order Cuda12, Vulkan, Cpu, with Cuda12 left out unless the GPU is NVIDIA.
/// Later runtimes stay in the list as the fallback (the loader falls back to the next one when a runtime cannot load: VT-0 probe cases 5 to 7).
/// </summary>
public static class RuntimeOrder
{
    /// <param name="facts">The installed runtimes and whether the GPU is NVIDIA.</param>
    /// <param name="useGpu">False asks for the processor only; null and true leave the choice to the order.</param>
    public static IReadOnlyList<RuntimeKind> For(RuntimeFacts facts, bool? useGpu)
    {
        var order = new List<RuntimeKind>(3);
        if (useGpu != false)
        {
            if (facts.NvidiaPresent && facts.Installed.Contains(RuntimeKind.Cuda12)) order.Add(RuntimeKind.Cuda12);
            if (facts.Installed.Contains(RuntimeKind.Vulkan)) order.Add(RuntimeKind.Vulkan);
        }
        if (facts.Installed.Contains(RuntimeKind.Cpu)) order.Add(RuntimeKind.Cpu);
        return order;
    }
}
