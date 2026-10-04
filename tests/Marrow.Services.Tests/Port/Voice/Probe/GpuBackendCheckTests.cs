using Marrow.Core.Port.Voice;
using Marrow.Services.Port.Voice.Engine;
using Marrow.Services.Port.Voice.Probe;

namespace Marrow.Services.Tests.Port.Voice.Probe;

// VT-6, V-10.1 "GPU backend": which Whisper.net runtime loaded, confirmed by a tiny load test of the VAD model on it. The load test is a seam; the real one is Windows-only.
public sealed class GpuBackendCheckTests
{
    private static readonly GpuAdapterInfo Nvidia = new("NVIDIA GeForce RTX 4080", 0x10DE, 0x2704, 16L << 30, 14L << 30, false);
    private static readonly GpuAdapterInfo Amd = new("AMD Radeon RX 7800 XT", 0x1002, 0x747E, 16L << 30, 14L << 30, false);

    private static RuntimeFacts Facts(params RuntimeKind[] installed) => new(installed, false, "/data");

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_runtime_that_loaded_decides_the_backend()
    {
        Assert.Equal(GpuBackend.Vulkan, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan), new ScriptedVadLoadTest(RuntimeKind.Vulkan)).Check(Amd));
        Assert.Equal(GpuBackend.Cuda, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan, RuntimeKind.Cuda12), new ScriptedVadLoadTest(RuntimeKind.Cuda12)).Check(Nvidia));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_processor_runtime_loading_means_no_working_GPU_backend()
    {
        Assert.Equal(GpuBackend.None, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan), new ScriptedVadLoadTest(RuntimeKind.Cpu)).Check(Amd));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_failed_or_empty_load_test_means_no_working_GPU_backend()
    {
        Assert.Equal(GpuBackend.None, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan), new ScriptedVadLoadTest(new InvalidOperationException("Failed to load native whisper library"))).Check(Amd));
        Assert.Equal(GpuBackend.None, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan), new ScriptedVadLoadTest((RuntimeKind?)null)).Check(Amd));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_load_test_gets_the_runtime_order_and_the_library_root_with_NVIDIA_taken_from_the_adapter()
    {
        var test = new ScriptedVadLoadTest(RuntimeKind.Cuda12);
        var facts = Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan, RuntimeKind.Cuda12);
        new GpuBackendCheck(() => facts, test).Check(Nvidia);
        new GpuBackendCheck(() => facts, test).Check(Amd);
        Assert.Equal([RuntimeKind.Cuda12, RuntimeKind.Vulkan, RuntimeKind.Cpu], test.Calls[0].Order);
        Assert.Equal([RuntimeKind.Vulkan, RuntimeKind.Cpu], test.Calls[1].Order);
        Assert.All(test.Calls, c => Assert.Equal("/data", c.LibraryRoot));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void With_no_GPU_runtime_installed_or_no_adapter_nothing_is_loaded()
    {
        var test = new ScriptedVadLoadTest(RuntimeKind.Vulkan);
        Assert.Equal(GpuBackend.None, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu), test).Check(Amd));
        Assert.Equal(GpuBackend.None, new GpuBackendCheck(() => Facts(RuntimeKind.Cpu, RuntimeKind.Vulkan), test).Check(null));
        Assert.Empty(test.Calls);
    }
}
