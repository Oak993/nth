namespace Marrow.Testing;

/// <summary>
/// Speech-like audio for the voice tests (Dextra AS-18), 16 kHz mono floats, the same bytes for the same seed. A voice is a gliding
/// pitch with its harmonics weighted by two moving formants, in syllables about 3 a second with short pauses between them and a
/// little breath noise, so it has what the barge-in gate looks at: bursts and pauses, a spectrum that is not a tone, and a
/// waveform that matches only itself. Two seeds are two different speakers (they never correlate). No file, no microphone.
/// </summary>
public static class SyntheticSpeech
{
    public const int Rate = 16_000;

    /// <summary>A voice of <paramref name="seconds"/> at <paramref name="level"/> RMS during its syllables.</summary>
    public static float[] Voice(int seed, double seconds, double level = 0.1, double pitch = 0)
    {
        var random = new Random(seed);
        var count = (int)(seconds * Rate);
        var samples = new float[count];
        var f0 = pitch > 0 ? pitch : 95 + random.NextDouble() * 120;
        var glide = 0.5 + random.NextDouble() * 0.6;
        var rhythm = 2.6 + random.NextDouble() * 1.2;
        var formantOne = 0.4 + random.NextDouble() * 0.3;
        var formantTwo = 0.9 + random.NextDouble() * 0.7;
        var shift = random.NextDouble() * Math.Tau;
        var phase = 0.0;
        var sum = 0.0;
        var voiced = 0;
        for (var n = 0; n < count; n++)
        {
            var t = (double)n / Rate;
            var f = f0 * (1 + 0.14 * Math.Sin(Math.Tau * glide * t + shift));
            phase += Math.Tau * f / Rate;
            var syllable = Math.Sin(Math.Tau * rhythm * t + shift);
            var envelope = syllable > 0.12 ? Math.Pow((syllable - 0.12) / 0.88, 0.7) : 0.0;
            var centreOne = 500 + 250 * Math.Sin(Math.Tau * formantOne * t + shift);
            var centreTwo = 1500 + 600 * Math.Sin(Math.Tau * formantTwo * t + 1.3 * shift);
            var value = 0.0;
            for (var k = 1; k <= 24; k++)
            {
                var hz = k * f;
                if (hz > 7000) break;
                var weight = 1.0 / k + 2.2 * Gain(hz, centreOne, 160) + 1.6 * Gain(hz, centreTwo, 260);
                value += weight * Math.Sin(k * phase + 0.7 * k * k * shift);
            }
            var breath = (random.NextDouble() * 2 - 1) * 0.06;
            var sample = (value * 0.35 + breath) * envelope;
            samples[n] = (float)sample;
            if (envelope > 0.2)
            {
                sum += sample * sample;
                voiced++;
            }
        }
        var rms = voiced == 0 ? 1.0 : Math.Sqrt(sum / voiced);
        var scale = (float)(level / rms);
        for (var n = 0; n < count; n++) samples[n] *= scale;
        return samples;
    }

    /// <summary>Low-level noise, for a room.</summary>
    public static float[] Noise(int seed, double seconds, double level)
    {
        var random = new Random(seed);
        var samples = new float[(int)(seconds * Rate)];
        for (var n = 0; n < samples.Length; n++) samples[n] = (float)((random.NextDouble() * 2 - 1) * level * 1.7320508);
        return samples;
    }

    /// <summary>
    /// What a microphone hears of a speaker playing <paramref name="played"/>: the sound <paramref name="gain"/> times as loud
    /// (0.5 is -6 dB) after <paramref name="delaySamples"/>, with one weaker reflection <paramref name="reflection"/> times that, a
    /// few milliseconds later. The output is as long as the input.
    /// </summary>
    public static float[] Echo(float[] played, double gain, int delaySamples, double reflection = 0.25, int reflectionAfter = 190)
    {
        var heard = new float[played.Length];
        for (var n = 0; n < heard.Length; n++)
        {
            var direct = n - delaySamples;
            var late = direct - reflectionAfter;
            var value = 0.0;
            if (direct >= 0) value += gain * played[direct];
            if (late >= 0) value += gain * reflection * played[late];
            heard[n] = (float)value;
        }
        return heard;
    }

    /// <summary>The sum of the signals, as long as the longest.</summary>
    public static float[] Mix(params float[][] signals)
    {
        var mixed = new float[signals.Max(s => s.Length)];
        foreach (var signal in signals)
        {
            for (var n = 0; n < signal.Length; n++) mixed[n] += signal[n];
        }
        return mixed;
    }

    /// <summary><paramref name="signal"/> placed at <paramref name="startSample"/> in a silence of <paramref name="length"/> samples.</summary>
    public static float[] At(float[] signal, int startSample, int length)
    {
        var placed = new float[length];
        for (var n = 0; n < signal.Length && startSample + n < length; n++) placed[startSample + n] = signal[n];
        return placed;
    }

    public static float Scale(float[] signal, int from, int count)
    {
        var sum = 0.0;
        for (var n = from; n < from + count && n < signal.Length; n++) sum += signal[n] * signal[n];
        return (float)Math.Sqrt(sum / Math.Max(1, count));
    }

    /// <summary>A stand-in for the voice detector: speech where the frame is audible (RMS over 0.01).</summary>
    public static float EnergyProbability(ReadOnlySpan<float> frame)
    {
        var sum = 0.0;
        foreach (var s in frame) sum += s * s;
        return Math.Sqrt(sum / Math.Max(1, frame.Length)) >= 0.01 ? 0.95f : 0.02f;
    }

    private static double Gain(double hz, double centre, double width) => Math.Exp(-0.5 * Math.Pow((hz - centre) / width, 2));
}
