using Marrow.Core.Port.Voice.Seams;
using Marrow.Services.Port.Voice.Engine;

namespace Marrow.Services.Tests.Port.Voice.Engine;

// VT-6, V-6.1: SileroVad is an IVoiceActivityDetector over whisper.cpp's VAD (the five native exports). Whisper.net hands out no per-frame probability, and the native
// call resets Silero's state on every call, so each frame is answered from the call over the last few frames and the last probability is the frame's.
public sealed class SileroVadTests
{
    private static float[] Frame(float value) => Enumerable.Repeat(value, 512).ToArray();

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_frame_is_512_samples_at_16_kHz()
    {
        using var vad = new SileroVad(new ScriptedVadNative(_ => [0f]));
        Assert.Equal(512, vad.FrameSamples);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_first_frame_is_sent_alone_and_its_probability_is_the_answer()
    {
        var native = ScriptedVadNative.ByIndex(_ => 0.8f);
        using var vad = new SileroVad(native);
        Assert.Equal(0.8f, vad.Probability(Frame(0.1f)));
        Assert.Equal(512, Assert.Single(native.Windows).Length);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Each_call_sends_the_recent_frames_in_order_and_answers_with_the_last_probability_of_the_call()
    {
        var native = ScriptedVadNative.ByIndex(i => 0.1f * (i + 1));
        using var vad = new SileroVad(native, windowFrames: 3);
        vad.Probability(Frame(1));
        vad.Probability(Frame(2));
        var third = vad.Probability(Frame(3));
        var fourth = vad.Probability(Frame(4));
        Assert.Equal([512, 1024, 1536, 1536], native.Windows.Select(w => w.Length));
        // the fourth window holds frames 2, 3, 4 and the answer is the third probability of that call
        Assert.Equal([2f, 3f, 4f], new[] { native.Windows[3][0], native.Windows[3][512], native.Windows[3][1024] });
        Assert.Equal(0.3f, third, 4);
        Assert.Equal(0.3f, fourth, 4);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_window_defaults_to_eight_frames()
    {
        Assert.Equal(8, SileroVad.DefaultWindowFrames);
        var native = ScriptedVadNative.ByIndex(_ => 0.5f);
        using var vad = new SileroVad(native);
        for (var i = 0; i < 20; i++) vad.Probability(Frame(i));
        Assert.Equal(8 * 512, native.Windows[^1].Length);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Reset_forgets_the_window_so_the_next_session_starts_on_its_own_first_frame()
    {
        var native = ScriptedVadNative.ByIndex(_ => 0.5f);
        using var vad = new SileroVad(native, windowFrames: 4);
        vad.Probability(Frame(1));
        vad.Probability(Frame(2));
        vad.Reset();
        vad.Probability(Frame(9));
        Assert.Equal(512, native.Windows[^1].Length);
        Assert.Equal(9f, native.Windows[^1][0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(511)]
    [InlineData(513)]
    [InlineData(1024)]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_frame_of_any_other_length_is_refused_before_the_native_call(int samples)
    {
        var native = ScriptedVadNative.ByIndex(_ => 0.5f);
        using var vad = new SileroVad(native);
        Assert.Throws<ArgumentException>(() => vad.Probability(new float[samples]));
        Assert.Empty(native.Windows);
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void A_native_answer_with_no_probabilities_is_an_error_not_silence()
    {
        using var vad = new SileroVad(new ScriptedVadNative(_ => []));
        Assert.Throws<InvalidOperationException>(() => vad.Probability(Frame(0.2f)));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void Disposing_frees_the_native_context_once_and_later_calls_are_refused()
    {
        var native = ScriptedVadNative.ByIndex(_ => 0.5f);
        var vad = new SileroVad(native);
        vad.Dispose();
        vad.Dispose();
        Assert.Equal(1, native.Disposals);
        Assert.Throws<ObjectDisposedException>(() => vad.Probability(Frame(0.1f)));
    }

    [Fact]
    [Trait("Feature", "VT-6")]
    [Trait("Decision", "ED-267")]
    public void The_probabilities_are_what_the_session_feeds_to_the_speech_state_unchanged()
    {
        var script = new Queue<float>([0.1f, 0.5f, 0.34f, 0.99f]);
        using var vad = new SileroVad(new ScriptedVadNative(window => [.. Enumerable.Repeat(0f, window.Length / 512 - 1), script.Dequeue()]), windowFrames: 2);
        Assert.Equal([0.1f, 0.5f, 0.34f, 0.99f], Enumerable.Range(0, 4).Select(_ => vad.Probability(Frame(0))));
    }
}
