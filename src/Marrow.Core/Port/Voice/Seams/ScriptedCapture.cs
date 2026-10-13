// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the capture double (voice-SPEC V-2.4, V-14.8). It opens
// no device. Samples are pushed by hand (Push) or by a timer on SwiftClock (Play), so a test or the Customization preview drives
// them with the manual or frame clock; device events are raised on demand.
namespace Marrow.Core.Port.Voice.Seams;

/// <summary>The capture double: <see cref="IAudioCapture"/> over synthesised samples on the clock.</summary>
public sealed class ScriptedCapture : IAudioCapture
{
    private static readonly TimeSpan DefaultChunk = TimeSpan.FromMilliseconds(10);

    private IAudioCaptureSink? sink;
    private ITimer? player;
    private (Func<long, float> Signal, TimeSpan Duration)? playOnStart;

    public ScriptedCapture(CaptureFormat? format = null) => Format = format ?? CaptureFormat.Speech16kMono;

    /// <summary>The mix format <see cref="Start"/> reports.</summary>
    public CaptureFormat Format { get; set; }

    public bool IsRunning => sink is not null;

    public int StartCount { get; private set; }

    public int StopCount { get; private set; }

    /// <summary>When set, the next <see cref="Start"/> throws it (a device that cannot be opened).</summary>
    public Exception? FailStart { get; set; }

    /// <summary>Frames delivered so far (a frame is one sample per channel).</summary>
    public long FramesDelivered { get; private set; }

    public CaptureFormat Start(IAudioCaptureSink sink)
    {
        if (FailStart is { } failure) throw failure;
        StartCount += 1;
        FramesDelivered = 0;
        this.sink = sink;
        if (playOnStart is { } play) Play(play.Signal, play.Duration);
        return Format;
    }

    public void Stop()
    {
        StopCount += 1;
        player?.Dispose();
        player = null;
        sink = null;
    }

    /// <summary>Makes every <see cref="Start"/> begin <see cref="Play"/> at once (the preview's speaker).</summary>
    public void PlayOnStart(Func<long, float> signal, TimeSpan duration) => playOnStart = (signal, duration);

    /// <summary>Delivers interleaved samples now. Nothing is delivered when the capture is stopped.</summary>
    public void Push(ReadOnlySpan<float> interleavedSamples)
    {
        var target = sink; // a sink may stop its own capture from inside the tap (Dextra AS-18: the conversation shuts the microphone when nothing is said)
        if (target is null) return;
        FramesDelivered += interleavedSamples.Length / Format.Channels;
        target.OnAudioBlock(Format, interleavedSamples); // the processing thread's tap, which a scripted capture runs on its caller's
        if (sink is not null) target.OnAudio(interleavedSamples);
    }

    /// <summary>Delivers <paramref name="duration"/> of <paramref name="signal"/> now, from this capture's own frame counter.</summary>
    public void PushNow(Func<long, float> signal, TimeSpan duration)
    {
        var frames = (int)Math.Round(duration.TotalSeconds * Format.SampleRate);
        Push(Render(signal, FramesDelivered, frames));
    }

    /// <summary>
    /// Delivers <paramref name="signal"/> for <paramref name="duration"/> from a timer on <see cref="SwiftClock"/>, one chunk (10 ms
    /// unless given) per tick, each chunk handed to the sink on the main loop. Stopping the capture ends it.
    /// </summary>
    public void Play(Func<long, float> signal, TimeSpan duration, TimeSpan? chunk = null)
    {
        player?.Dispose();
        var step = chunk ?? DefaultChunk;
        var framesPerChunk = (int)Math.Round(step.TotalSeconds * Format.SampleRate);
        var totalFrames = (long)Math.Round(duration.TotalSeconds * Format.SampleRate);
        long produced = 0;
        var started = FramesDelivered;
        ITimer? timer = null;
        timer = SwiftClock.Provider.Current.CreateTimer(_ =>
        {
            if (produced >= totalFrames)
            {
                timer?.Dispose();
                return;
            }
            var count = (int)Math.Min(framesPerChunk, totalFrames - produced);
            var samples = Render(signal, started + produced, count);
            produced += count;
            MainActor.Post(() => Push(samples));
        }, null, step, step);
        player = timer;
    }

    /// <summary>Raises a device event on the sink, as the device notifications of V-4.4 do.</summary>
    public void RaiseDeviceEvent(CaptureDeviceEventKind kind) => sink?.OnDeviceEvent(kind);

    private float[] Render(Func<long, float> signal, long firstFrame, int frames)
    {
        var samples = new float[frames * Format.Channels];
        for (var f = 0; f < frames; f++)
        {
            var value = signal(firstFrame + f);
            for (var c = 0; c < Format.Channels; c++) samples[f * Format.Channels + c] = value;
        }
        return samples;
    }
}
