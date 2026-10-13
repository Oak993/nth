// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Barge-in": the speech is played through the communications path so Windows' echo cancellation has it as
// its reference; unit AS-18; ED-NEW-dextra-as18-1). No Swift twin.
// The speakers: NAudio's WASAPI player, shared mode, event-driven, in the communications category (the same category the microphone is opened in,
// so the system's echo canceller sees this sound as the one to take out). Stop flushes the card's buffer at once. Never used by automated tests: a
// test binds a fake ISpeechPlayer; the Native test (VoiceConversationNativeTests) plays through the real speakers.
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Marrow.Services.Assistant;

public sealed class WasapiSpeechPlayer : ISpeechPlayer
{
    /// <summary>The sound card's buffer: short, so Stop has little to drop and the reference stays close to what is audible.</summary>
    public const int LatencyMs = 60;

    private readonly Lock gate = new();
    private WasapiPlayer? player;
    private Source? source;
    private TaskCompletionSource? done;
    private bool stopped;
    private bool disposed;

    public TimeSpan Played
    {
        get
        {
            Source? now;
            lock (gate) now = source;
            if (now is null) return TimeSpan.Zero;
            var handed = TimeSpan.FromSeconds((double)now.Handed / now.Rate);
            var audible = handed - TimeSpan.FromMilliseconds(LatencyMs);
            return audible < TimeSpan.Zero ? TimeSpan.Zero : audible;
        }
    }

    public Task PlayAsync(SynthAudio audio, Action<long> handed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(handed);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (cancellationToken.IsCancellationRequested || audio.Samples.Length == 0)
        {
            finished.SetResult();
            return finished.Task;
        }
        WasapiPlayer wasapi;
        Source feed;
        lock (gate)
        {
            if (stopped || disposed)
            {
                finished.SetResult();
                return finished.Task;
            }
            feed = new Source(audio, handed, () => EndOfSound(audio));
            wasapi = new WasapiPlayerBuilder().WithSharedMode().WithEventSync().WithCategory(AudioStreamCategory.Communications).WithLatency(LatencyMs).Build();
            player = wasapi;
            source = feed;
            done = finished;
        }
        try
        {
            wasapi.Init(feed);
            wasapi.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is { } error) finished.TrySetException(error);
                else finished.TrySetResult();
            };
            cancellationToken.Register(Stop);
            wasapi.Play();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            finished.TrySetException(error);
        }
        return finished.Task;
    }

    public void Stop()
    {
        WasapiPlayer? now;
        TaskCompletionSource? waiting;
        lock (gate)
        {
            stopped = true;
            now = player;
            waiting = done;
        }
        try
        {
            now?.Stop();
        }
        catch (Exception error) when (error is InvalidOperationException or COMException or ObjectDisposedException)
        {
            // The card is gone already: there is nothing left to stop.
        }
        waiting?.TrySetResult();
    }

    public void Dispose()
    {
        WasapiPlayer? now;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            now = player;
            player = null;
        }
        Stop();
        now?.Dispose();
    }

    // The source has handed over its last sample: the card still holds up to its buffer, so playback ends a little later than the data.
    private void EndOfSound(SynthAudio audio)
    {
        _ = Task.Delay(TimeSpan.FromMilliseconds(LatencyMs * 2)).ContinueWith(_ => Stop(), TaskScheduler.Default);
    }

    // The sound as the card reads it: mono 32-bit float at the voice's own rate (the shared-mode engine converts), one callback per block handed over.
    private sealed class Source : IWaveProvider
    {
        private readonly float[] samples;
        private readonly Action<long> handedOver;
        private readonly Action ended;
        private long position;
        private int endSent;

        public Source(SynthAudio audio, Action<long> handed, Action ended)
        {
            samples = audio.Samples;
            Rate = audio.SampleRate;
            handedOver = handed;
            this.ended = ended;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, 1);
        }

        public int Rate { get; }

        public WaveFormat WaveFormat { get; }

        public long Handed => Volatile.Read(ref position);

        public int Read(Span<byte> buffer)
        {
            var left = samples.Length - (int)Volatile.Read(ref position);
            if (left <= 0)
            {
                if (Interlocked.Exchange(ref endSent, 1) == 0) ended();
                return 0;
            }
            var count = Math.Min(left, buffer.Length / sizeof(float));
            MemoryMarshal.AsBytes(samples.AsSpan((int)position, count)).CopyTo(buffer);
            var next = Volatile.Read(ref position) + count;
            Volatile.Write(ref position, next);
            handedOver(next);
            return count * sizeof(float);
        }
    }
}
