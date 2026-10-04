using Marrow.Core.Port.Voice.Seams;
using Marrow.Core.Port.Voice.Session;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5, V-T40 (voice-SPEC V-5.1, V-5.2): the amplitude that reaches the RECOGNISER. CaptureFormatMatrixTests averages the channels itself to check the capture; here the real VoiceSession does its own
// downmix and resampling on what the production capture delivers, and the audio its engine is handed in the final pass is measured. A session that weighted the channels wrongly (not the average) fails here.
public sealed class CaptureSessionDownmixTests
{
    private const double Frequency = 440;
    private const double Amplitude = 0.5;

    public static TheoryData<SampleEncoding, int, int> Formats()
    {
        var data = new TheoryData<SampleEncoding, int, int>();
        foreach (var encoding in new[] { SampleEncoding.Float32, SampleEncoding.Pcm16, SampleEncoding.Pcm24, SampleEncoding.Pcm32 })
        {
            foreach (var rate in new[] { 48000, 44100 })
            {
                for (var channels = 1; channels <= DeviceFormat.MaxChannels; channels++) data.Add(encoding, rate, channels);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")]
    public void The_recogniser_is_handed_the_average_of_the_channels_as_a_16_kHz_tone(SampleEncoding encoding, int rate, int channels)
    {
        using var rig = new CaptureSessionRig(new DeviceFormat(rate, channels, encoding));
        rig.Vad = ScriptedVad.Speech((0, 60)); // speech throughout, so the final pass has audio to decode
        rig.Session.Activate();
        rig.Runtime.RunUntilIdle();
        Assert.Equal(VoiceSessionState.Listening, rig.Session.State);
        rig.Feed(i => (float)(Amplitude * Math.Sin(2 * Math.PI * Frequency * i / rate)), 1.0, channelGain: c => (c + 1) / 8f); // every channel at its own level
        rig.Session.Activate(); // the second click: the final pass
        rig.Runtime.RunUntilIdle();
        var final = rig.Engine.Decodes.Last(d => d.Request.Mode == DecodeMode.Final);
        var audio = final.Audio.Span;
        Assert.InRange(audio.Length, 15000, 16000 + 4800 + 100); // 1 s less the filter's lookahead, plus the pass's 0.3 s of silence

        // 4000 samples from the middle hold 110 whole cycles of 440 Hz at 16 kHz: the Goertzel amplitude is the tone's, scaled by the mean channel gain (n + 1) / 16
        const int window = 4000;
        double re = 0, im = 0;
        for (var n = 0; n < window; n++)
        {
            var angle = 2 * Math.PI * Frequency * n / 16000;
            re += audio[1500 + n] * Math.Cos(angle);
            im += audio[1500 + n] * Math.Sin(angle);
        }
        var amplitude = 2 * Math.Sqrt(re * re + im * im) / window;
        var expected = Amplitude * (channels + 1) / 16.0;
        Assert.InRange(amplitude, expected * 0.97, expected * 1.03);
    }
}
