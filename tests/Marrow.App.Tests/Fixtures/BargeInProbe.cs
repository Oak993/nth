using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Dsp;
using Marrow.Core.Port.Voice.Seams;
using Marrow.Testing;

namespace Marrow.App.Tests.Fixtures;

/// <summary>
/// Dextra AS-18 (for the Native echo test): a microphone sink that runs what the conversation runs on the processing thread, without the conversation: it turns the blocks into 32 ms
/// frames at 16 kHz, asks an energy detector (which, like a real one, calls an echo of speech speech), and gives each frame to the barge-in gate against the given reference. It counts
/// how the frames were judged and how many times the gate fired.
/// </summary>
internal sealed class BargeInProbe(VoiceEchoReference reference) : IAudioCaptureSink
{
    private readonly VoiceBargeIn gate = new(reference);
    private readonly PolyphaseResampler resampler = new();
    private readonly List<float> pending = [];
    private readonly List<float> mono = [];
    private readonly Lock lockObject = new();
    private int echo;
    private int nearEnd;
    private int residual;
    private int quiet;
    private int triggers;
    private int blocks;
    private double peak;

    public int Triggers => Volatile.Read(ref triggers);

    public int Blocks => Volatile.Read(ref blocks);

    /// <summary>The loudest sample the microphone delivered (0 means it heard nothing at all).</summary>
    public double Peak
    {
        get
        {
            lock (lockObject) return peak;
        }
    }

    /// <summary>How the frames were judged: Echo, NearEnd, Residual, Quiet.</summary>
    public string Verdicts
    {
        get
        {
            lock (lockObject) return $"echo {echo}, near-end {nearEnd}, residue {residual}, quiet {quiet}, triggers {triggers}, peak {peak:F3}";
        }
    }

    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
    }

    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
    }

    public void OnAudioBlock(CaptureFormat format, ReadOnlySpan<float> interleavedSamples)
    {
        Interlocked.Increment(ref blocks);
        var channels = Math.Max(1, format.Channels);
        lock (lockObject)
        {
            mono.Clear();
            for (var i = 0; i + channels <= interleavedSamples.Length; i += channels)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++) sum += interleavedSamples[i + c];
                mono.Add(sum / channels);
                peak = Math.Max(peak, Math.Abs(sum / channels));
            }
            resampler.Process(format.SampleRate, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(mono), pending);
            while (pending.Count >= VoiceBargeIn.FrameSamples)
            {
                var frame = pending.GetRange(0, VoiceBargeIn.FrameSamples).ToArray();
                pending.RemoveRange(0, VoiceBargeIn.FrameSamples);
                var fired = gate.Process(frame, SyntheticSpeech.EnergyProbability(frame));
                switch (gate.Last)
                {
                    case EchoVerdict.Echo: echo++; break;
                    case EchoVerdict.NearEnd: nearEnd++; break;
                    case EchoVerdict.Residual: residual++; break;
                    default: quiet++; break;
                }
                if (fired) Interlocked.Increment(ref triggers);
            }
        }
    }
}
