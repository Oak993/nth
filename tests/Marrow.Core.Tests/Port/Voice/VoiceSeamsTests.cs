using System.Reflection;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Tests.Fixtures;

namespace Marrow.Core.Tests.Port.Voice;

// VT-3 (voice-SPEC V-2.5, V-T46): every seam's ambient default throws when touched, so a test cannot reach a microphone, a model, a download
// or a credential store by omission.
public sealed class VoiceSeamsTests
{
    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the audio seam
    public void The_default_capture_opens_nothing()
    {
        var seam = VoiceSeams.Capture.Current;
        Assert.IsType<FailClosedAudioCapture>(seam);
        Assert.Throws<SeamNotBoundException>(() => seam.Start(new NullCaptureSink()));
        Assert.Throws<SeamNotBoundException>(() => seam.Stop());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the VAD seam
    public void The_default_detector_answers_nothing()
    {
        var seam = VoiceSeams.Vad.Current;
        Assert.IsType<FailClosedVoiceActivityDetector>(seam);
        Assert.Throws<SeamNotBoundException>(() => seam.FrameSamples);
        Assert.Throws<SeamNotBoundException>(() => seam.Probability(new float[512]));
        Assert.Throws<SeamNotBoundException>(() => seam.Reset());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the engine seam throws on the call itself, not later inside a faulted task
    public void The_default_engine_loads_and_decodes_nothing_and_throws_on_the_call()
    {
        var seam = VoiceSeams.Engine.Current;
        Assert.IsType<FailClosedSpeechEngine>(seam);
        Assert.Throws<SeamNotBoundException>(() => { _ = seam.LoadAsync(new SpeechModel("tiny", null), new EngineLoadOptions(), CancellationToken.None); });
        var request = new DecodeRequest("auto", "", DecodeMode.Live, TimeSpan.Zero);
        Assert.Throws<SeamNotBoundException>(() => { _ = seam.TranscribeAsync(new float[16], request, CancellationToken.None); });
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the permission seam
    public void The_default_microphone_access_answers_nothing()
    {
        var seam = VoiceSeams.Microphone.Current;
        Assert.IsType<FailClosedMicrophoneAccess>(seam);
        Assert.Throws<SeamNotBoundException>(() => seam.Status());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the resampler seam
    public void The_default_resampler_converts_nothing()
    {
        var seam = VoiceSeams.Resampler.Current;
        Assert.IsType<FailClosedStreamingResampler>(seam);
        Assert.Throws<SeamNotBoundException>(() => seam.Process(48000, new float[48], []));
        Assert.Throws<SeamNotBoundException>(() => seam.Reset());
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the model download seam: nothing is fetched
    public void The_default_model_transport_fetches_nothing_and_throws_on_the_call()
    {
        var seam = VoiceSeams.ModelTransport.Current;
        Assert.IsType<FailClosedModelTransport>(seam);
        Assert.Throws<SeamNotBoundException>(() => { _ = seam.GetRange(new Uri("https://example.invalid/ggml-tiny.bin"), 0, CancellationToken.None); });
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: the registry lists every ambient seam, so a seam added later cannot skip this test
    public void The_registry_lists_every_ambient_seam_and_every_one_is_fail_closed()
    {
        var ambientTypes = typeof(VoiceSeams).GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType.IsGenericType && p.PropertyType.GetGenericTypeDefinition().Name.StartsWith("Ambient", StringComparison.Ordinal))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();
        var registered = VoiceSeams.All.Select(e => e.Interface).ToHashSet();
        Assert.Equal(ambientTypes, registered);
        foreach (var (seamInterface, failClosed) in VoiceSeams.All)
        {
            Assert.True(seamInterface.IsInstanceOfType(failClosed), seamInterface.Name);
            Assert.StartsWith("FailClosed", failClosed.GetType().Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Feature", "VT-3")]
    [Trait("Spec", "V-T46")]
    [Trait("Decision", "ED-267")] // V-2.5: a set of ambient seams touches nothing until a member is used
    public void The_ambient_seam_set_resolves_when_used_not_when_built()
    {
        var set = VoiceSeamSet.Ambient;
        Assert.IsType<FailClosedAudioCapture>(set.Capture);
        Assert.IsType<FailClosedSpeechEngine>(set.Engine);
        Assert.Throws<SeamNotBoundException>(() => set.Microphone.Status());
    }
}
