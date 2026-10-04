using Marrow.Core.Port.Voice.Dsp;
using Marrow.Services.Port.Voice.Capture;
using Marrow.Services.Tests.Fixtures;
using Marrow.Testing;

namespace Marrow.Services.Tests.Port.Voice.Capture;

// VT-5, V-T40 (voice-SPEC V-4.2, V-5.1, V-5.2): float and PCM16/24 (and 32) at 48 kHz and 44.1 kHz with 1 to 8 channels go through the production capture and arrive as
// interleaved float in the device's format, which the session's downmix and the polyphase resampler (VT-2) turn into the 16 kHz mono tone that was played.
public sealed class CaptureFormatMatrixTests
{
    private const double Frequency = 440;
    private const double Amplitude = 0.5;

    public static TheoryData<SampleEncoding, int, int> Formats()
    {
        var data = new TheoryData<SampleEncoding, int, int>();
        foreach (var encoding in Enum.GetValues<SampleEncoding>())
        {
            foreach (var rate in new[] { 48000, 44100 })
            {
                for (var channels = 1; channels <= DeviceFormat.MaxChannels; channels++) data.Add(encoding, rate, channels);
            }
        }
        return data;
    }

    private static float Gain(int channel) => (channel + 1) / 8f; // every channel carries the tone at its own level, so a swapped or merged channel shows

    [Theory]
    [MemberData(nameof(Formats))]
    [Trait("Feature", "VT-5")]
    [Trait("Spec", "V-T40")]
    [Trait("Decision", "ED-267")]
    public void A_tone_played_in_the_format_arrives_interleaved_and_becomes_the_same_tone_at_16_kHz_mono(SampleEncoding encoding, int rate, int channels)
    {
        using var runtime = new TestRuntime();
        var host = new ScriptedCaptureHost(("mic", "Mic"));
        var format = new DeviceFormat(rate, channels, encoding);
        host.SetFormat("mic", format);
        var capture = new WasapiCapture(host, () => null, runThread: false);
        var sink = new CollectingSink();
        var reported = capture.Start(sink);
        Assert.Equal(new Marrow.Core.Port.Voice.Seams.CaptureFormat(rate, channels), reported);

        var frames = rate / 2; // half a second
        var played = new float[frames * channels];
        for (var f = 0; f < frames; f++)
        {
            var tone = (float)(Amplitude * Math.Sin(2 * Math.PI * Frequency * f / rate));
            for (var c = 0; c < channels; c++) played[f * channels + c] = tone * Gain(c);
        }
        var device = host.LastDevice!;
        var chunk = rate / 100 * channels; // 10 ms buffers
        for (var at = 0; at < played.Length; at += chunk) device.Push(played.AsSpan(at, Math.Min(chunk, played.Length - at)));
        capture.Stop();

        // interleaved, in the device's format, within the encoding's quantisation
        Assert.Equal(played.Length, sink.Samples.Count);
        var tolerance = encoding == SampleEncoding.Pcm16 ? 1.0 / 32768 : 1e-6;
        for (var i = 0; i < played.Length; i++)
        {
            if (Math.Abs(sink.Samples[i] - played[i]) > tolerance) Assert.Fail($"sample {i} (channel {i % channels}): {sink.Samples[i]} instead of {played[i]}");
        }

        // V-5.1 downmix (average) then V-5.2 resample, as the session does
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++) sum += sink.Samples[f * channels + c];
            mono[f] = sum / channels;
        }
        var output = new List<float>();
        new PolyphaseResampler().Process(rate, mono, output);
        Assert.InRange(output.Count, 8000 - 80, 8000);

        // 4000 samples from the middle hold 110 whole cycles of 440 Hz at 16 kHz: the Goertzel amplitude is the tone's, scaled by the mean channel gain
        const int window = 4000;
        double re = 0, im = 0;
        for (var n = 0; n < window; n++)
        {
            var angle = 2 * Math.PI * Frequency * n / 16000;
            re += output[1500 + n] * Math.Cos(angle);
            im += output[1500 + n] * Math.Sin(angle);
        }
        var amplitude = 2 * Math.Sqrt(re * re + im * im) / window;
        var expected = Amplitude * (channels + 1) / 16.0;
        Assert.InRange(amplitude, expected * 0.97, expected * 1.03);
    }
}
