// PORT-ONLY: Dextra (DESIGN.md rev 5, 10 step 1 "narration from the mic, through the existing speech engines, time-aligned to the steps", 12; unit AS-19; ED-NEW-dextra-as19-8). No Swift twin.
// What the owner says while he teaches, as lines of text with the time they began. It uses the voice seams the app already binds (the
// microphone capture, the voice activity detector, the local speech engine) and nothing else: the audio is cut into utterances at the
// pauses, each is recognised by the local engine, and only the text is kept. The audio itself is never written anywhere, and no cloud
// engine is ever used: voice is free and stays on this PC. A line that holds a secret (a password read aloud) is dropped. The times
// come from the number of samples heard, so they are exact and need no clock.
using System.Threading.Channels;
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Seams;

namespace Marrow.Services.Assistant;

public sealed class TeachNarrator : IAudioCaptureSink, IDisposable
{
    public const int SampleRate = 16000;

    /// <summary>Speech frames in a row that open an utterance.</summary>
    public const int StartFrames = 3;

    /// <summary>Frames kept from before the speech began, so the first word is not cut.</summary>
    public const int PreRollFrames = 8;

    /// <summary>Silence that ends an utterance.</summary>
    public static readonly TimeSpan EndAfterSilence = TimeSpan.FromMilliseconds(800);

    /// <summary>An utterance is cut here and the next begins at once.</summary>
    public static readonly TimeSpan LongestUtterance = TimeSpan.FromSeconds(30);

    /// <summary>How long a stop waits for what is still being recognised.</summary>
    public static readonly TimeSpan DrainFor = TimeSpan.FromSeconds(60);

    private const float SpeechAbove = 0.5f;

    private readonly VoiceSeamSet seams;
    private readonly Func<SpeechModel?> model;
    private readonly Func<string> language;
    private readonly Func<TimeSpan> clock;
    private readonly Action<TeachNarrationLine> heard;
    private readonly Action<string>? noted;
    private readonly object gate = new();
    private readonly Channel<Block> audio = Channel.CreateBounded<Block>(new BoundedChannelOptions(2000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly Channel<Utterance> utterances = Channel.CreateUnbounded<Utterance>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource stopping = new();
    private long heardFormat; // the sample rate and the channel count packed in one word, so a read and a write cannot tear
    private long nextOriginTicks;
    private Task? listening;
    private Task? recognising;
    private bool running;
    private bool paused;
    private bool disposed;
    private bool loaded;
    private bool secretNoted;
    private bool resampled;
    private readonly List<float> mono = [];

    private sealed record Utterance(TimeSpan Start, float[] Samples);

    // What the capture handed over, with the format it was in; an empty block is a mark: the clock starts again here, whatever was being said ends.
    private readonly record struct Block(float[] Samples, CaptureFormat Format);

    public TeachNarrator(VoiceSeamSet seams, Func<SpeechModel?> model, Func<string> language, Func<TimeSpan> clock, Action<TeachNarrationLine> heard, Action<string>? noted = null)
    {
        this.seams = seams ?? throw new ArgumentNullException(nameof(seams));
        this.model = model ?? throw new ArgumentNullException(nameof(model));
        this.language = language ?? throw new ArgumentNullException(nameof(language));
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        this.heard = heard ?? throw new ArgumentNullException(nameof(heard));
        this.noted = noted;
    }

    /// <summary>
    /// Opens the microphone. Returns null when it listens, else the reason in the owner's words (the microphone is blocked, there is none,
    /// no local speech model is set up): the recording goes on without narration and says so.
    /// </summary>
    public string? Start()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (running) return null;
            if (model() is null) return "No speech model on this PC is set up, so narration is off. The recording goes on without it.";
            switch (seams.Microphone.Status())
            {
                case MicrophoneAccessStatus.Denied: return "The microphone is blocked in Windows, so narration is off. The recording goes on without it.";
                case MicrophoneAccessStatus.NoDevice: return "There is no microphone, so narration is off. The recording goes on without it.";
            }
            if (!OpenCapture()) return "nth could not open the microphone, so narration is off. The recording goes on without it.";
            running = true;
            listening = Task.Run(Listen);
            recognising = Task.Run(Recognise);
            return null;
        }
    }

    // The mark goes in before the capture opens, so no block of the new run comes ahead of it; the Listen task owns the detector and the clock.
    private bool OpenCapture()
    {
        Volatile.Write(ref nextOriginTicks, clock().Ticks);
        audio.Writer.TryWrite(new Block([], default));
        try
        {
            seams.Capture.Start(this);
            return true;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return false;
        }
    }

    /// <summary>Stops listening for now; what is being said is ended and recognised.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (!running || paused) return;
            paused = true;
            try { seams.Capture.Stop(); }
            catch (Exception error) when (error is not OutOfMemoryException) { /* the capture is gone either way */ }
            audio.Writer.TryWrite(new Block([], default)); // an empty block ends the utterance in flight
        }
    }

    public void Resume()
    {
        lock (gate)
        {
            if (!running || !paused) return;
            if (!OpenCapture())
            {
                noted?.Invoke("nth could not open the microphone again, so narration stays off for the rest of this recording.");
                return;
            }
            paused = false;
        }
    }

    /// <summary>Stops, ends what is being said, and waits (up to a minute) for what is still being recognised.</summary>
    public async Task StopAsync()
    {
        Task? listen, recognise;
        lock (gate)
        {
            if (!running) return;
            running = false;
            try { seams.Capture.Stop(); }
            catch (Exception error) when (error is not OutOfMemoryException) { /* gone either way */ }
            audio.Writer.TryComplete();
            listen = listening;
            recognise = recognising;
        }
        try
        {
            if (listen is not null) await listen.WaitAsync(DrainFor).ConfigureAwait(false);
            utterances.Writer.TryComplete();
            if (recognise is not null) await recognise.WaitAsync(DrainFor).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            stopping.Cancel();
            noted?.Invoke("What you said last was still being recognised and was dropped.");
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (running)
            {
                running = false;
                try { seams.Capture.Stop(); }
                catch (Exception error) when (error is not OutOfMemoryException) { /* gone either way */ }
            }
        }
        stopping.Cancel();
        audio.Writer.TryComplete();
        utterances.Writer.TryComplete();
    }

    // ---- the capture's callbacks (a thread of the capture's own): copy and hand over, nothing else ----

    public void OnAudioBlock(CaptureFormat format, ReadOnlySpan<float> interleavedSamples) =>
        Interlocked.Exchange(ref heardFormat, ((long)format.SampleRate << 32) | (uint)format.Channels);

    public void OnAudio(ReadOnlySpan<float> interleavedSamples)
    {
        if (interleavedSamples.IsEmpty) return;
        var packed = Interlocked.Read(ref heardFormat);
        audio.Writer.TryWrite(new Block(interleavedSamples.ToArray(), new CaptureFormat((int)(packed >> 32), (int)(packed & 0xFFFFFFFF))));
    }

    public void OnDeviceEvent(CaptureDeviceEventKind kind)
    {
        lock (gate)
        {
            if (!running || paused) return;
        }
        noted?.Invoke(kind == CaptureDeviceEventKind.InUse
            ? "The microphone is in use by another app, so narration stopped."
            : "The microphone changed, so narration stopped. The recording goes on.");
        _ = Task.Run(StopAsync);
    }

    // ---- cutting speech into utterances ----

    private async Task Listen()
    {
        var vad = seams.Vad;
        var frameSamples = vad.FrameSamples;
        var preRoll = new Queue<float[]>();
        List<float>? current = null;
        TimeSpan startedAt = TimeSpan.Zero;
        var speechRun = 0;
        var quietRun = 0;
        var endQuiet = (int)Math.Ceiling(EndAfterSilence.TotalSeconds * SampleRate / frameSamples);
        var longest = (int)(LongestUtterance.TotalSeconds * SampleRate);
        var origin = TimeSpan.Zero;
        long processed = 0;

        void End()
        {
            if (current is { Count: > 0 }) utterances.Writer.TryWrite(new Utterance(startedAt, [.. current]));
            current = null;
            speechRun = 0;
            quietRun = 0;
            preRoll.Clear();
        }

        try
        {
            await foreach (var block in audio.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (block.Samples.Length == 0)
                {
                    End();
                    mono.Clear();
                    vad.Reset();
                    if (resampled) seams.Resampler.Reset();
                    processed = 0;
                    origin = TimeSpan.FromTicks(Volatile.Read(ref nextOriginTicks));
                    continue;
                }
                Mono(block);
                var taken = 0;
                while (mono.Count - taken >= frameSamples)
                {
                    var frame = mono.GetRange(taken, frameSamples).ToArray();
                    var frameStart = processed;
                    taken += frameSamples;
                    processed += frameSamples;
                    var speech = vad.Probability(frame) >= SpeechAbove;
                    if (current is null)
                    {
                        preRoll.Enqueue(frame);
                        while (preRoll.Count > PreRollFrames) preRoll.Dequeue();
                        speechRun = speech ? speechRun + 1 : 0;
                        if (speechRun < StartFrames) continue;
                        var behind = preRoll.Count;
                        startedAt = origin + TimeSpan.FromSeconds((frameStart + frameSamples - (long)behind * frameSamples) / (double)SampleRate);
                        current = [.. preRoll.SelectMany(f => f)];
                        preRoll.Clear();
                        quietRun = 0;
                        continue;
                    }
                    current.AddRange(frame);
                    quietRun = speech ? 0 : quietRun + 1;
                    if (quietRun >= endQuiet || current.Count >= longest) End();
                }
                mono.RemoveRange(0, taken);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            noted?.Invoke("Narration stopped because of an error (" + error.GetType().Name + "). The recording goes on.");
        }
        End();
    }

    private void Mono(Block block)
    {
        var format = block.Format;
        var channels = Math.Max(1, format.Channels);
        var downmixed = block.Samples;
        if (channels > 1)
        {
            downmixed = new float[block.Samples.Length / channels];
            for (var f = 0; f < downmixed.Length; f++)
            {
                float sum = 0;
                for (var c = 0; c < channels; c++) sum += block.Samples[f * channels + c];
                downmixed[f] = sum / channels;
            }
        }
        if (format.SampleRate is 0 or SampleRate)
        {
            mono.AddRange(downmixed);
            return;
        }
        resampled = true;
        seams.Resampler.Process(format.SampleRate, downmixed, mono);
    }

    // ---- recognising them, one at a time ----

    private async Task Recognise()
    {
        var engine = seams.Engine;
        try
        {
            await foreach (var utterance in utterances.Reader.ReadAllAsync(stopping.Token).ConfigureAwait(false))
            {
                try
                {
                    if (!loaded)
                    {
                        var speechModel = model();
                        if (speechModel is null) return;
                        await engine.LoadAsync(speechModel, new EngineLoadOptions(), stopping.Token).ConfigureAwait(false);
                        loaded = true;
                    }
                    var result = await engine.TranscribeAsync(utterance.Samples, new DecodeRequest(language(), "", DecodeMode.Final, utterance.Start), stopping.Token).ConfigureAwait(false);
                    var text = Joined(result);
                    if (text.Length == 0 || IsNoise(text)) continue;
                    if (MemoryGuard.HoldsSecret(text))
                    {
                        if (!secretNoted)
                        {
                            secretNoted = true;
                            noted?.Invoke("Something you said sounded like a password or a key, so that line was not kept.");
                        }
                        continue;
                    }
                    heard(new TeachNarrationLine(utterance.Start, utterance.Start + TimeSpan.FromSeconds(utterance.Samples.Length / (double)SampleRate), text));
                }
                catch (Exception error) when (error is not (OutOfMemoryException or OperationCanceledException))
                {
                    noted?.Invoke("A line you said could not be recognised: " + error.GetType().Name + ".");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // stopped while recognising: what was heard so far is kept
        }
    }

    private static string Joined(Hypothesis hypothesis) =>
        TeachDraft.Clean(string.Join(' ', hypothesis.Words.Select(w => w.Text.Trim()).Where(t => t.Length > 0)), TeachLimits.NarrationChars);

    // The recogniser answers silence and background sound with a bracketed tag: not words, not kept.
    private static bool IsNoise(string text) =>
        (text.StartsWith('[') && text.EndsWith(']')) || (text.StartsWith('(') && text.EndsWith(')')) || (text.StartsWith('*') && text.EndsWith('*'));
}
