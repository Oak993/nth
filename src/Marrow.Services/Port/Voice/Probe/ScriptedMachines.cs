// PORT-ONLY, owner-ruled addition (OD-0092, OD-0096; first-release voice typing, VT-6): the four machines of the seam table (V-2.4: "the owner's desktop, a CPU-only laptop, a 4-core machine, a 2 GB VRAM card").
using Marrow.Core.Port.Voice;

namespace Marrow.Services.Port.Voice.Probe;

/// <summary>
/// Scripted values, not measurements, except that the owner's desktop has the owner PC's processor, graphics card and 16 GB of video memory (voice-pins.json measuredOn; the
/// Core tests' hardware key). Sizes are chosen so the rows read as V-10.1's examples do (the 32 GB PC reads 31 GB).
/// </summary>
public static class ScriptedMachines
{
    public static ScriptedMachine OwnersDesktop { get; } = new("AMD Ryzen 7 9800X3D 8-Core Processor", 8, 33_466_892_288L,
        [new GpuAdapterInfo("NVIDIA GeForce RTX 4080", 0x10DE, 0x2704, 17_171_480_576L, 15_032_385_536L, false)], "32.0.15.6094", GpuBackend.Vulkan);

    public static ScriptedMachine CpuOnlyLaptop { get; } = new("12th Gen Intel(R) Core(TM) i5-1235U", 10, 16_500_000_000L,
        [new GpuAdapterInfo("Intel(R) Iris(R) Xe Graphics", 0x8086, 0x46A8, 134_217_728L, 6_000_000_000L, false)], "31.0.101.4502", GpuBackend.None);

    public static ScriptedMachine FourCoreMachine { get; } = new("Intel(R) Core(TM) i5-6500 CPU @ 3.20GHz", 4, 8_480_000_000L,
        [new GpuAdapterInfo("Intel(R) HD Graphics 530", 0x8086, 0x1912, 134_217_728L, 3_500_000_000L, false)], "31.0.101.2111", GpuBackend.None);

    public static ScriptedMachine TwoGbVramCard { get; } = new("Intel(R) Core(TM) i5-9400F CPU @ 2.90GHz", 6, 16_900_000_000L,
        [new GpuAdapterInfo("NVIDIA GeForce GTX 1050", 0x10DE, 0x1C81, 2_147_483_648L, 1_717_986_918L, false)], "31.0.15.5161", GpuBackend.Vulkan);
}
