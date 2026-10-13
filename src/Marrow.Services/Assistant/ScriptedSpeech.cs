// PORT-ONLY: Dextra (DESIGN.md rev 5, 16 "done when" for AS-18; ED-NEW-dextra-as18-1). No Swift twin.
// The doubles of the speech seams, in the product assembly like the voice typing's Scripted* doubles (ScriptedCapture, ScriptedEngine), so the
// Services and App tests share them: a synth that makes sound from a text with no voice installed, and speakers that play on a clock the test moves.
// They open no device and make no sound.
namespace Marrow.Services.Assistant;

/// <summary>An <see cref="ISpeechSynth"/> that makes its sound from a function (a tone per word by default) and records what it was asked.</summary>
public sealed class ScriptedSpeechSynth : ISpeechSynth
{
    /// <summary>The voices this PC "has".</summary>
    public List<SynthVoice> Installed { get; } = [new SynthVoice("scripted-1", "Scripted Voice", "en-US")];

    /// <summary>Makes the sound of a text; null makes a tone for each word.</summary>
    public Func<string, SynthAudio>? Render { get; set; }

    /// <summary>When set, the next syntheses throw it (a voice that cannot be made).</summary>
    public Exception? Fail { get; set; }

    /// <summary>What was asked: the text, the voice it was asked in and the rate.</summary>
    public List<(string Text, string? VoiceId, double Rate)> Requests { get; } = [];

    public IReadOnlyList<SynthVoice> Voices() => [.. Installed];

    public Task<Synthesis> SynthesizeAsync(string text, SynthVoice? voice, double rate, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add((text, voice?.Id, rate));
        if (Fail is { } failure) throw failure;
        var cues = new List<SynthCue>();
        var audio = Render?.Invoke(text) ?? Tones(text, cues);
        if (cues.Count == 0)
        {
            var words = SpokenWordsOf(text);
            for (var i = 0; i < words.Count; i++) cues.Add(new SynthCue(words[i].Start, words[i].Length, TimeSpan.FromSeconds(audio.Duration.TotalSeconds * i / Math.Max(1, words.Count))));
        }
        return Task.FromResult(new Synthesis(audio, cues));
    }

    private static List<(int Start, int Length)> SpokenWordsOf(string text)
    {
        var list = new List<(int, int)>();
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            var space = i == text.Length || char.IsWhiteSpace(text[i]);
            if (!space && start < 0) start = i;
            if (space && start >= 0)
            {
                list.Add((start, i - start));
                start = -1;
            }
        }
        return list;
    }

    // 0.30 s of a tone per word and 0.05 s between, at 16 kHz.
    private static SynthAudio Tones(string text, List<SynthCue> cues)
    {
        const int rate = 16_000;
        const double word = 0.30;
        const double gap = 0.05;
        var words = SpokenWordsOf(text);
        var samples = new float[(int)Math.Round(Math.Max(1, words.Count) * (word + gap) * rate)];
        for (var w = 0; w < words.Count; w++)
        {
            var first = (int)(w * (word + gap) * rate);
            cues.Add(new SynthCue(words[w].Start, words[w].Length, TimeSpan.FromSeconds(w * (word + gap))));
            var length = (int)(word * rate);
            for (var n = 0; n < length && first + n < samples.Length; n++)
            {
                var fade = Math.Min(1.0, Math.Min(n, length - n) / (0.01 * rate));
                samples[first + n] = (float)(0.2 * fade * Math.Sin(Math.Tau * (300 + 25 * (w % 7)) * n / rate));
            }
        }
        return new SynthAudio(samples, rate);
    }
}

/// <summary>An <see cref="ISpeechPlayer"/> on a clock the test moves: <see cref="Advance"/> plays that much more and hands the card its buffer ahead.</summary>
public sealed class ScriptedSpeechPlayer(TimeProvider? time = null) : ISpeechPlayer
{
    private readonly Lock gate = new();
    private SynthAudio? audio;
    private Action<long>? handed;
    private TaskCompletionSource? done;
    private TimeSpan clock;
    private int stops;

    /// <summary>What the card holds ahead of what is audible.</summary>
    public TimeSpan Buffer { get; set; } = TimeSpan.FromMilliseconds(60);

    public TimeSpan Played
    {
        get
        {
            lock (gate) return clock;
        }
    }

    /// <summary>How many times <see cref="Stop"/> was called.</summary>
    public int StopCount => Volatile.Read(ref stops);

    /// <summary>When Stop was last called, on the clock the player was given; null before.</summary>
    public DateTimeOffset? StoppedAt { get; private set; }

    public bool Playing
    {
        get
        {
            lock (gate) return done is { Task.IsCompleted: false };
        }
    }

    public bool Disposed { get; private set; }

    public Task PlayAsync(SynthAudio sound, Action<long> handedOver, CancellationToken cancellationToken)
    {
        // continuations run inline, on the thread that stops or advances the player: a test sees the speech's end the moment it causes it
        var finished = new TaskCompletionSource();
        if (cancellationToken.IsCancellationRequested)
        {
            finished.SetCanceled(cancellationToken);
            return finished.Task;
        }
        lock (gate)
        {
            audio = sound;
            handed = handedOver;
            done = finished;
            clock = TimeSpan.Zero;
        }
        cancellationToken.Register(Stop);
        Hand(TimeSpan.Zero);
        return finished.Task;
    }

    /// <summary>The speakers play <paramref name="by"/> more of the sound (the card is kept filled to its buffer); done when it is all played.</summary>
    public void Advance(TimeSpan by) => Hand(by);

    public void Stop()
    {
        TaskCompletionSource? waiting;
        lock (gate) waiting = done;
        Interlocked.Increment(ref stops);
        StoppedAt = (time ?? TimeProvider.System).GetUtcNow();
        waiting?.TrySetResult();
    }

    public void Dispose() => Disposed = true;

    private void Hand(TimeSpan by)
    {
        Action<long>? tell;
        long count;
        TaskCompletionSource? waiting = null;
        lock (gate)
        {
            if (audio is null || done is null || done.Task.IsCompleted) return;
            clock += by;
            count = Math.Min(audio.Samples.Length, (long)Math.Round((clock + Buffer).TotalSeconds * audio.SampleRate));
            tell = handed;
            if (clock >= audio.Duration) waiting = done;
        }
        tell?.Invoke(count);
        waiting?.TrySetResult();
    }
}
