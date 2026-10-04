using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, WINDOWS-ONLY: the real native loads. They need the shipped whisper.cpp natives for win-x64 (CPU and Vulkan), so on any other system they are reported as
// skipped with that reason, never as passed. No model is downloaded: the VAD model and the self-test clip are the two files that ship in the app folder.
public sealed class WhisperNativeWindowsTests
{
    private static string Assets() =>
        Path.Combine(AppContext.BaseDirectory, "Port", "Voice", "Assets");

    private static string VadModel => Path.Combine(Assets(), "ggml-silero-v6.2.0.bin");

    private static float[] Clip()
    {
        var bytes = File.ReadAllBytes(Path.Combine(Assets(), "selftest-clip.wav"));
        var samples = new float[(bytes.Length - 44) / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(bytes, 44 + i * 2) / 32768f;
        return samples;
    }

    [WindowsOnlyFact("loads the real whisper.dll P/Invoke exports")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_real_SileroVad_gives_a_probability_per_frame_that_is_high_in_the_clip_speech_and_low_in_silence()
    {
        using var vad = SileroVad.Open(VadModel, FileRuntimeInventory.AppRuntimeLibrary(AppContext.BaseDirectory, RuntimeKind.Cpu), useGpu: false, threads: 2);
        var clip = Clip();
        var probabilities = new List<float>();
        for (var at = 0; at + vad.FrameSamples <= clip.Length; at += vad.FrameSamples) probabilities.Add(vad.Probability(clip.AsSpan(at, vad.FrameSamples)));
        Assert.All(probabilities, p => Assert.InRange(p, 0f, 1f));
        Assert.Contains(probabilities, p => p >= 0.5f);
        vad.Reset();
        var quiet = new float[vad.FrameSamples];
        for (var i = 0; i < 20; i++) Assert.True(vad.Probability(quiet) < 0.35f);
    }

    // Windows review: whisper.cpp's VAD with use_gpu on aborts the whole process on a Vulkan device (ggml-backend.cpp: "pre-allocated tensor (leaf_0) in a buffer (Vulkan0) that
    // cannot run the operation"). Nothing can catch that, so the VAD is refused the GPU before anything is loaded.
    [WindowsOnlyFact("the refusal comes before any native is loaded")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_VAD_is_never_given_the_GPU_because_whisper_cpp_aborts_the_process_on_it()
    {
        var cpu = FileRuntimeInventory.AppRuntimeLibrary(AppContext.BaseDirectory, RuntimeKind.Cpu);
        Assert.Throws<ArgumentException>(() => SileroVad.Open(VadModel, cpu, useGpu: true, threads: 2));
        Assert.Throws<ArgumentException>(() => WhisperVadNative.Open(VadModel, cpu, useGpu: true, threads: 2));
    }

    [WindowsOnlyFact("loads Whisper.net's native runtimes")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_real_VAD_load_test_reports_a_runtime_from_the_order_it_was_given()
    {
        var result = new WhisperNetVadLoadTest(VadModel).Run([RuntimeKind.Vulkan, RuntimeKind.Cpu], null);
        Assert.NotNull(result);
        Assert.Contains(result.Value, new[] { RuntimeKind.Vulkan, RuntimeKind.Cpu });
    }

    [WindowsOnlyFact("loads the CPU runtime of Whisper.net")]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public async Task The_real_backend_refuses_a_missing_model_file_and_the_engine_stays_unloaded()
    {
        using var engine = new WhisperNetEngine(new WhisperNetBackend(), () => new RuntimeFacts([RuntimeKind.Cpu], false, null), () => 4);
        var missing = Path.Combine(AppContext.BaseDirectory, "no-such-model.bin");
        await Assert.ThrowsAnyAsync<Exception>(() => engine.LoadAsync(new SpeechModel("missing", missing), new EngineLoadOptions(UseGpu: false), CancellationToken.None));
        Assert.False(engine.IsLoaded);
    }
}
