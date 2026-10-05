using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// VT-14 test double of the hardware probe: it reports the four rows of V-10.1 one at a time, each only when the test calls <see cref="Release"/>, so a test sees the card between rows;
/// then it answers the profile. With <paramref name="gated"/> false it runs straight through. A <paramref name="failure"/> is thrown after the rows it allows (none by default). No hardware is read.
/// </summary>
internal sealed class GatedHardwareProbe(HardwareProfile profile, IReadOnlyList<ProbeRow> rows, bool gated = true, Exception? failure = null) : IHardwareProbe
{
    private readonly SemaphoreSlim gates = new(0);

    public int Runs { get; private set; }

    /// <summary>The owner's desktop as V-10.1 reads it (scripted values, not measurements).</summary>
    public static GatedHardwareProbe OwnersDesktop(bool gated = true) => new(
        new HardwareProfile("AMD Ryzen 7 9800X3D 8-Core Processor", 8, 31, "NVIDIA GeForce RTX 4080", GpuVendor.Nvidia, 16L << 30, 14L << 30, "32.0.15.6094", GpuBackend.Vulkan),
        [new ProbeRow("Processor", "AMD Ryzen 7 9800X3D 8-Core Processor, 8 cores"), new ProbeRow("Memory", "31 GB"),
         new ProbeRow("Graphics", "NVIDIA GeForce RTX 4080, 16 GB"), new ProbeRow("GPU backend", "Vulkan, working")], gated);

    /// <summary>A laptop with no usable GPU backend (scripted values).</summary>
    public static GatedHardwareProbe CpuOnlyLaptop(bool gated = true) => new(
        new HardwareProfile("12th Gen Intel(R) Core(TM) i5-1235U", 10, 15, "Intel(R) Iris(R) Xe Graphics", GpuVendor.Intel, 128L << 20, 6L << 30, "31.0.101.4502", GpuBackend.None),
        [new ProbeRow("Processor", "12th Gen Intel(R) Core(TM) i5-1235U, 10 cores"), new ProbeRow("Memory", "15 GB"),
         new ProbeRow("Graphics", "Intel(R) Iris(R) Xe Graphics, shared memory"), new ProbeRow("GPU backend", "None working, runs on the processor")], gated);

    public HardwareProfile Profile => profile;

    /// <summary>Lets the next row (or the end) through.</summary>
    public void Release(int count = 1) => gates.Release(count);

    public async Task<HardwareProfile> RunAsync(Action<ProbeRow>? report, CancellationToken cancellationToken)
    {
        Runs++;
        foreach (var row in rows)
        {
            if (gated) await gates.WaitAsync(cancellationToken);
            report?.Invoke(row);
        }
        if (gated) await gates.WaitAsync(cancellationToken);
        if (failure is not null) throw failure;
        return profile;
    }
}
