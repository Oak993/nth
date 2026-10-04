using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-7.1, V-7.9: the engine's load. One model loaded at a time, from the model store's verified path; the runtime order from the scripted runtime list; the GPU
// on only when a GPU runtime loaded; a failed or cancelled load leaves nothing loaded.
public sealed class WhisperNetEngineLoadTests : IDisposable
{
    private readonly EngineRig rig = new();

    public void Dispose() => rig.Dispose();

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task A_load_gives_the_backend_the_path_the_runtime_order_and_the_library_root()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        var load = Assert.Single(rig.Backend.Loads);
        Assert.Equal("/models/large-v3-turbo-q8_0.bin", load.ModelPath);
        Assert.Equal([RuntimeKind.Vulkan, RuntimeKind.Cpu], load.RuntimeOrder);
        Assert.Equal("/data", load.LibraryRoot);
        Assert.True(load.UseGpu);
        Assert.True(rig.Engine.IsLoaded);
        Assert.Equal("large-v3-turbo-q8_0", rig.Engine.LoadedModelId);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task With_the_CUDA_folder_installed_on_an_NVIDIA_GPU_the_order_starts_with_CUDA()
    {
        rig.Facts = new RuntimeFacts([RuntimeKind.Cpu, RuntimeKind.Vulkan, RuntimeKind.Cuda12], true, "/data");
        rig.Backend.Reports = RuntimeKind.Cuda12;
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal([RuntimeKind.Cuda12, RuntimeKind.Vulkan, RuntimeKind.Cpu], rig.Backend.Loads[0].RuntimeOrder);
        Assert.Equal(RuntimeKind.Cuda12, rig.Engine.LoadedRuntime);
        Assert.True(rig.Engine.GpuActive);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task The_GPU_counts_as_on_only_when_a_GPU_runtime_is_the_one_that_loaded()
    {
        rig.Backend.Reports = RuntimeKind.Cpu;
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Small, new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal(RuntimeKind.Cpu, rig.Engine.LoadedRuntime);
        Assert.False(rig.Engine.GpuActive);
        rig.Backend.Reports = RuntimeKind.Vulkan;
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.True(rig.Engine.GpuActive);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task UseGpu_off_in_the_load_options_asks_for_the_processor_only()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Small, new EngineLoadOptions(UseGpu: false), CancellationToken.None));
        var load = Assert.Single(rig.Backend.Loads);
        Assert.False(load.UseGpu);
        Assert.Equal([RuntimeKind.Cpu], load.RuntimeOrder);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-2.2")]
    public async Task Threads_default_to_the_physical_cores_minus_two_and_the_options_can_set_them()
    {
        rig.Cores = 6;
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Small, new EngineLoadOptions(), CancellationToken.None));
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("en", "", DecodeMode.Live, TimeSpan.Zero), CancellationToken.None));
        Assert.Equal(4, rig.Backend.Decodes[0].Threads);

        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(Threads: 3), CancellationToken.None));
        await EngineRig.Guard(rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("en", "", DecodeMode.Live, TimeSpan.Zero), CancellationToken.None));
        Assert.Equal(3, rig.Backend.Decodes[1].Threads);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task Only_one_model_is_loaded_at_a_time_the_first_is_released_before_the_second_loads()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Small, new EngineLoadOptions(), CancellationToken.None));
        Assert.Equal(["load:/models/large-v3-turbo-q8_0.bin", "dispose:/models/large-v3-turbo-q8_0.bin", "load:/models/small.en-q8_0.bin"], rig.Backend.Log);
        Assert.Equal("small.en-q8_0", rig.Engine.LoadedModelId);
        Assert.Single(rig.Backend.Models, m => !m.Disposed);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task Loading_the_model_that_is_already_loaded_shares_it_the_hover_then_the_click_load_once()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.Single(rig.Backend.Loads);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task Two_loads_of_the_same_model_in_flight_run_the_backend_once()
    {
        var release = rig.Backend.HoldLoads();
        var first = rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None);
        var second = rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None);
        release();
        await EngineRig.Guard(Task.WhenAll(first, second));
        Assert.Single(rig.Backend.Loads);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.1")]
    public async Task A_model_without_a_path_is_refused_before_the_backend_is_touched()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => rig.Engine.LoadAsync(new SpeechModel("whisper-1", null), new EngineLoadOptions(), CancellationToken.None));
        Assert.Empty(rig.Backend.Loads);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task A_failed_load_throws_to_the_caller_and_leaves_nothing_loaded()
    {
        rig.Backend.FailLoads(new InvalidOperationException("Failed to load native whisper library"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        Assert.False(rig.Engine.IsLoaded);
        Assert.Null(rig.Engine.LoadedModelId);
        Assert.Null(rig.Engine.LoadedRuntime);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Engine.TranscribeAsync(EngineRig.Audio(1), new DecodeRequest("en", "", DecodeMode.Live, TimeSpan.Zero), CancellationToken.None));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task A_failed_load_of_a_second_model_does_not_keep_the_first()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        rig.Backend.FailLoads(new IOException("bad file"));
        await Assert.ThrowsAsync<IOException>(() => rig.Engine.LoadAsync(EngineRig.Small, new EngineLoadOptions(), CancellationToken.None));
        Assert.False(rig.Engine.IsLoaded);
        Assert.All(rig.Backend.Models, m => Assert.True(m.Disposed));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.10")]
    public async Task A_cancelled_load_throws_cancellation_and_leaves_nothing_loaded()
    {
        rig.Backend.HoldLoads();
        using var cts = new CancellationTokenSource();
        var load = rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), cts.Token);
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EngineRig.Guard(load));
        Assert.False(rig.Engine.IsLoaded);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task Unload_releases_the_model_now_and_a_second_unload_does_nothing()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        rig.Engine.Unload();
        Assert.False(rig.Engine.IsLoaded);
        Assert.True(rig.Backend.Models[0].Disposed);
        rig.Engine.Unload();
        Assert.Equal(1, rig.Backend.Log.Count(l => l.StartsWith("dispose:", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    [Trait("Spec", "V-7.9")]
    public async Task Disposing_the_engine_releases_the_model()
    {
        await EngineRig.Guard(rig.Engine.LoadAsync(EngineRig.Turbo, new EngineLoadOptions(), CancellationToken.None));
        rig.Engine.Dispose();
        Assert.True(rig.Backend.Models[0].Disposed);
    }
}
