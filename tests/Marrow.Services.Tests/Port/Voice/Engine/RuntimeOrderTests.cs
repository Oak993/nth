using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.1 + OD-0099 Q-V-9 C: which Whisper.net runtimes the engine offers the loader and in what order: the downloaded CUDA folder if it is complete and the
// GPU is NVIDIA, else Vulkan, else the processor; each later runtime stays in the list as the fallback (voice-pins.json cuda.feasibility.order).
public sealed class RuntimeOrderTests
{
    private static readonly RuntimeKind[] All = [RuntimeKind.Cpu, RuntimeKind.Vulkan, RuntimeKind.Cuda12];

    private static RuntimeKind[] Order(IReadOnlyList<RuntimeKind> installed, bool nvidia, bool? useGpu = null) =>
        [.. RuntimeOrder.For(new RuntimeFacts(installed, nvidia, "root"), useGpu)];

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void The_CUDA_folder_comes_first_on_an_NVIDIA_GPU_then_Vulkan_then_the_processor()
    {
        Assert.Equal([RuntimeKind.Cuda12, RuntimeKind.Vulkan, RuntimeKind.Cpu], Order(All, nvidia: true));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void Without_an_NVIDIA_GPU_the_CUDA_folder_is_left_out_even_when_it_is_installed()
    {
        Assert.Equal([RuntimeKind.Vulkan, RuntimeKind.Cpu], Order(All, nvidia: false));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void Without_the_CUDA_folder_an_NVIDIA_GPU_gets_Vulkan_then_the_processor()
    {
        Assert.Equal([RuntimeKind.Vulkan, RuntimeKind.Cpu], Order([RuntimeKind.Cpu, RuntimeKind.Vulkan], nvidia: true));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void Only_the_installed_runtimes_are_listed_and_the_processor_alone_when_nothing_else_is()
    {
        Assert.Equal([RuntimeKind.Cpu], Order([RuntimeKind.Cpu], nvidia: true));
        Assert.Equal([RuntimeKind.Vulkan], Order([RuntimeKind.Vulkan], nvidia: false));
        Assert.Empty(Order([], nvidia: true));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void The_GPU_switched_off_by_the_load_options_leaves_the_processor_only()
    {
        Assert.Equal([RuntimeKind.Cpu], Order(All, nvidia: true, useGpu: false));
        Assert.Equal([RuntimeKind.Cuda12, RuntimeKind.Vulkan, RuntimeKind.Cpu], Order(All, nvidia: true, useGpu: true));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public void A_scripted_runtime_list_in_any_order_gives_the_same_order()
    {
        Assert.Equal(
            [RuntimeKind.Cuda12, RuntimeKind.Vulkan, RuntimeKind.Cpu],
            Order([RuntimeKind.Vulkan, RuntimeKind.Cuda12, RuntimeKind.Cpu, RuntimeKind.Vulkan], nvidia: true));
    }
}
