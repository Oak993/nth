// PORT-ONLY: Dextra (DESIGN.md rev 5, 15.2 SpeechOut.cs "Windows.Media.SpeechSynthesis, local voices only; no cloud voice", 15.6 "Voice", 2.4;
// 17 Q3 answered 2026-10-11: free, on the PC only; unit AS-18; ED-NEW-dextra-as18-1, -7). No Swift twin.
// Dextra's voice out. A reply is made into sound by a voice installed on this PC (ISpeechSynth: Windows.Media.SpeechSynthesis in the app, which
// lives in Marrow.App because the WinRT projection needs its Windows TFM), played on the speakers (ISpeechPlayer: NAudio WASAPI in the communications
// category), and handed to the echo reference as it goes, so the barge-in gate can tell it from the owner. Nothing here reaches the network: the
// seams take no address, key or account, and a voice that is not an installed one is refused. Nothing is spoken unprompted: SpeakAsync needs a turn
// that began with something the owner said. Stop is immediate and never waits: it flags, and stops the player.
using Marrow.Core.Port.Assistant;
using Marrow.Core.Port.Voice.Dsp;

namespace Marrow.Services.Assistant;

/// <summary>A voice installed on this PC. <see cref="Local"/> is false for anything that would be fetched from a service.</summary>
public sealed record SynthVoice(string Id, string Name, string Language, bool Local = true);

/// <summary>Mono sound: float samples at a sample rate.</summary>
public sealed record SynthAudio(float[] Samples, int SampleRate)
{
    public TimeSpan Duration => SampleRate <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Samples.Length / SampleRate);
}

/// <summary>A word of the text and when the voice starts it.</summary>
public readonly record struct SynthCue(int Start, int Length, TimeSpan At);

/// <summary>What a voice made of a text: the sound, and (when the voice gives it) where each word starts.</summary>
public sealed record Synthesis(SynthAudio Audio, IReadOnlyList<SynthCue> Cues);

/// <summary>The voices of this PC. Production: <c>WinRtSpeechSynth</c>. Tests: a fake.</summary>
public interface ISpeechSynth
{
    IReadOnlyList<SynthVoice> Voices();

    Task<Synthesis> SynthesizeAsync(string text, SynthVoice? voice, double rate, CancellationToken cancellationToken);
}

/// <summary>
/// The speakers. <see cref="PlayAsync"/> starts the sound and completes when it has played out or <see cref="Stop"/> cut it. <c>handed</c> is told, on the
/// device's thread, how many source samples have been handed to the sound card so far (it must not block). <see cref="Played"/> is how much the speakers
/// have actually played (what was handed, less what the card still holds).
/// </summary>
public interface ISpeechPlayer : IDisposable
{
    Task PlayAsync(SynthAudio audio, Action<long> handed, CancellationToken cancellationToken);

    TimeSpan Played { get; }

    /// <summary>Stops at once and drops what the card holds. Safe from any thread and when nothing plays.</summary>
    void Stop();
}

/// <summary>How a stretch of speech ended.</summary>
public enum SpeechEnd
{
    /// <summary>It played out.</summary>
    Finished,

    /// <summary>Cut short: barge-in, the Interrupt button, Stop all, the sheet closing.</summary>
    Cut,

    /// <summary>It could not be made or played (no voice, the device failed); the text stays on screen.</summary>
    Failed,
}

public sealed record SpeechOutOptions
{
    /// <summary>The voice's speaking rate, 1 is normal.</summary>
    public double Rate { get; init; } = 1.0;

    /// <summary>The voice the owner picked (its id or name); null picks one by language.</summary>
    public string? Voice { get; init; }

    /// <summary>The language the voice should speak (a BCP 47 tag); null takes the PC's own.</summary>
    public string? Language { get; init; }
}

/// <summary>Which of the PC's voices speaks. Only installed voices are ever chosen.</summary>
public static class SpeechVoices
{
    /// <summary>A voice that would be fetched from a service has "Online" or "Cloud" in its name or id; the installed ones never do.</summary>
    public static bool IsLocal(SynthVoice voice) =>
        voice.Local
        && !voice.Name.Contains("Online", StringComparison.OrdinalIgnoreCase) && !voice.Id.Contains("Online", StringComparison.OrdinalIgnoreCase)
        && !voice.Name.Contains("Cloud", StringComparison.OrdinalIgnoreCase) && !voice.Id.Contains("Cloud", StringComparison.OrdinalIgnoreCase)
        && !voice.Id.Contains("://", StringComparison.Ordinal);

    /// <summary>
    /// The voice to use: the one the owner picked when it is installed, else an installed voice for the language (a natural or neural one
    /// first), else the first installed one, else null (the text is shown and not spoken).
    /// </summary>
    public static SynthVoice? Choose(IReadOnlyList<SynthVoice> voices, string? preferred, string? language)
    {
        var local = voices.Where(IsLocal).ToList();
        if (local.Count == 0) return null;
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var picked = local.FirstOrDefault(v => string.Equals(v.Id, preferred, StringComparison.OrdinalIgnoreCase))
                ?? local.FirstOrDefault(v => string.Equals(v.Name, preferred, StringComparison.OrdinalIgnoreCase));
            if (picked is not null) return picked;
        }
        var tag = string.IsNullOrWhiteSpace(language) ? "en" : language;
        var wanted = tag.Split('-')[0];
        var sameLanguage = local.Where(v => v.Language.StartsWith(wanted, StringComparison.OrdinalIgnoreCase)).ToList();
        var pool = sameLanguage.Count > 0 ? sameLanguage : local;
        return pool.FirstOrDefault(v => string.Equals(v.Language, tag, StringComparison.OrdinalIgnoreCase) && IsNatural(v))
            ?? pool.FirstOrDefault(v => IsNatural(v))
            ?? pool.FirstOrDefault(v => string.Equals(v.Language, tag, StringComparison.OrdinalIgnoreCase))
            ?? pool[0];
    }

    private static bool IsNatural(SynthVoice voice) =>
        voice.Name.Contains("Natural", StringComparison.OrdinalIgnoreCase) || voice.Name.Contains("Neural", StringComparison.OrdinalIgnoreCase);
}

public sealed class SpeechOut : IDisposable
{
    /// <summary>How long a level reading looks back: the envelope is the loudest 40 ms around the audible position.</summary>
    private const double LevelWindow = 0.04;

    private readonly ISpeechSynth synth;
    private readonly Func<ISpeechPlayer> players;
    private readonly VoiceEchoReference reference;
    private readonly SpeechOutOptions options;
    private readonly Lock gate = new();
    private readonly List<string> spoken = [];
    private CancellationTokenSource? current;
    private ISpeechPlayer? player;
    private Playing? playing;
    private Turn? turn;
    private bool disposed;

    public SpeechOut(ISpeechSynth synth, Func<ISpeechPlayer> players, VoiceEchoReference reference, SpeechOutOptions? options = null)
    {
        this.synth = synth ?? throw new ArgumentNullException(nameof(synth));
        this.players = players ?? throw new ArgumentNullException(nameof(players));
        this.reference = reference ?? throw new ArgumentNullException(nameof(reference));
        this.options = options ?? new SpeechOutOptions();
    }

    /// <summary>Raised (on whatever thread changed it) when speech starts, ends, or moves to a new word.</summary>
    public event Action? Changed;

    /// <summary>The reference what is said goes into, for the barge-in gate.</summary>
    public VoiceEchoReference Reference => reference;

    /// <summary>Whether Dextra is speaking now.</summary>
    public bool Speaking
    {
        get
        {
            lock (gate) return playing is not null;
        }
    }

    /// <summary>Everything this has been asked to say, in order (the tests read it to show nothing was said unprompted).</summary>
    public IReadOnlyList<string> Said
    {
        get
        {
            lock (gate) return [.. spoken];
        }
    }

    /// <summary>The text being spoken, or "".</summary>
    public string Text
    {
        get
        {
            lock (gate) return playing?.Text ?? "";
        }
    }

    /// <summary>The index of the word being spoken now (into <see cref="SpokenText.Words"/> of <see cref="Text"/>); -1 when nothing speaks.</summary>
    public int WordIndex
    {
        get
        {
            Playing? now;
            lock (gate) now = playing;
            return now is null ? -1 : now.WordAt(Played(now));
        }
    }

    /// <summary>How loud the speech is at the speakers now, 0 to 1: the envelope of the sound at the audible position. Any thread.</summary>
    public double Level
    {
        get
        {
            Playing? now;
            lock (gate) now = playing;
            return now is null ? 0 : now.LevelAt(Played(now));
        }
    }

    /// <summary>
    /// Begins a turn: Dextra may speak now, because the owner has just said something to it. Nothing is spoken outside a turn. A new turn
    /// ends the old one. Dispose the result, or call <see cref="EndTurn"/>, when the answer is done.
    /// </summary>
    public IDisposable BeginTurn(string heard)
    {
        if (string.IsNullOrWhiteSpace(heard)) throw new ArgumentException("A turn begins with something the owner said.", nameof(heard));
        var begun = new Turn(this);
        lock (gate) turn = begun;
        return begun;
    }

    public void EndTurn()
    {
        lock (gate) turn = null;
    }

    /// <summary>
    /// Says <paramref name="text"/> (already made fit for speech, <see cref="SpokenText.ForSpeech"/>) and completes when it has played out or was cut. Throws
    /// when no turn is open: nothing is spoken unprompted (2.4).
    /// </summary>
    public async Task<SpeechEnd> SpeakAsync(string text, string? voiceName = null)
    {
        CancellationTokenSource cancellation;
        lock (gate)
        {
            if (disposed) return SpeechEnd.Failed;
            if (turn is null) throw new InvalidOperationException("Nothing is spoken unprompted: no turn is open.");
            if (string.IsNullOrWhiteSpace(text)) return SpeechEnd.Finished;
            StopLocked();
            cancellation = current = new CancellationTokenSource();
        }
        try
        {
            var voice = SpeechVoices.Choose(synth.Voices(), voiceName ?? options.Voice, options.Language);
            if (voice is null) return SpeechEnd.Failed;
            lock (gate) spoken.Add(text);
            var made = await synth.SynthesizeAsync(text, voice, options.Rate, cancellation.Token).ConfigureAwait(false);
            cancellation.Token.ThrowIfCancellationRequested();
            var run = new Playing(text, made, Resample(made.Audio));
            ISpeechPlayer speakers;
            lock (gate)
            {
                if (cancellation.IsCancellationRequested || disposed) return SpeechEnd.Cut;
                reference.Clear();
                playing = run;
                speakers = player = players();
            }
            Changed?.Invoke();
            var handed = 0L;
            try
            {
                await speakers.PlayAsync(made.Audio, count =>
                {
                    var from = (int)(handed * VoiceEchoReference.Rate / made.Audio.SampleRate);
                    var to = (int)Math.Min(run.Reference.Length, count * VoiceEchoReference.Rate / made.Audio.SampleRate);
                    handed = count;
                    if (to > from) reference.Append(run.Reference.AsSpan(from, to - from));
                }, cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return SpeechEnd.Cut;
            }
            return cancellation.IsCancellationRequested ? SpeechEnd.Cut : SpeechEnd.Finished;
        }
        catch (OperationCanceledException)
        {
            return SpeechEnd.Cut;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            return SpeechEnd.Failed;
        }
        finally
        {
            Finish(cancellation);
        }
    }

    /// <summary>Cuts the speech now: flags, then stops the player. Never waits, and is safe from any thread (the barge-in's audio thread, the stop chord's).</summary>
    public void Stop()
    {
        lock (gate) StopLocked();
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            turn = null;
            StopLocked();
        }
    }

    private void StopLocked()
    {
        current?.Cancel();
        player?.Stop();
    }

    private void Finish(CancellationTokenSource cancellation)
    {
        ISpeechPlayer? done = null;
        bool changed;
        lock (gate)
        {
            changed = playing is not null && ReferenceEquals(current, cancellation);
            if (ReferenceEquals(current, cancellation))
            {
                current = null;
                playing = null;
                done = player;
                player = null;
            }
        }
        done?.Dispose();
        cancellation.Dispose();
        if (changed) Changed?.Invoke();
    }

    private TimeSpan Played(Playing run)
    {
        ISpeechPlayer? speakers;
        lock (gate) speakers = player;
        return speakers?.Played ?? TimeSpan.Zero;
    }

    private static float[] Resample(SynthAudio audio)
    {
        var output = new List<float>((int)(audio.Samples.Length * (double)VoiceEchoReference.Rate / Math.Max(1, audio.SampleRate)) + 16);
        new PolyphaseResampler().Process(audio.SampleRate, audio.Samples, output);
        return [.. output];
    }

    private sealed class Turn(SpeechOut owner) : IDisposable
    {
        public void Dispose()
        {
            lock (owner.gate)
            {
                if (ReferenceEquals(owner.turn, this)) owner.turn = null;
            }
        }
    }

    // What is being spoken: its text and words, the cues, and the sound's envelope at 10 ms.
    private sealed class Playing
    {
        private const int Step = 10;
        private readonly SpokenWord[] words;
        private readonly SynthCue[] cues;
        private readonly int[] cueWord;
        private readonly float[] envelope;
        private readonly TimeSpan duration;

        public Playing(string text, Synthesis made, float[] reference)
        {
            Text = text;
            Reference = reference;
            words = [.. SpokenText.Words(text)];
            cues = [.. made.Cues.OrderBy(c => c.At)];
            cueWord = [.. cues.Select(c => WordStartingAt(c.Start))];
            duration = made.Audio.Duration;
            var perStep = Math.Max(1, made.Audio.SampleRate * Step / 1000);
            envelope = new float[made.Audio.Samples.Length / perStep + 1];
            var peak = 1e-6f;
            for (var i = 0; i < envelope.Length; i++)
            {
                var from = i * perStep;
                var to = Math.Min(made.Audio.Samples.Length, from + perStep);
                var sum = 0f;
                for (var n = from; n < to; n++) sum += made.Audio.Samples[n] * made.Audio.Samples[n];
                envelope[i] = to > from ? MathF.Sqrt(sum / (to - from)) : 0f;
                peak = Math.Max(peak, envelope[i]);
            }
            for (var i = 0; i < envelope.Length; i++) envelope[i] = MathF.Pow(Math.Clamp(envelope[i] / peak, 0f, 1f), 0.7f);
        }

        public string Text { get; }

        public float[] Reference { get; }

        public int WordAt(TimeSpan played)
        {
            if (words.Length == 0) return -1;
            if (cues.Length > 0)
            {
                var index = -1;
                for (var i = 0; i < cues.Length; i++)
                {
                    if (cues[i].At > played) break;
                    index = cueWord[i];
                }
                return Math.Max(index, 0);
            }
            // No cues from the voice: the words share the sound by their length.
            var total = words.Sum(w => w.Length + 1);
            var at = duration <= TimeSpan.Zero ? 0 : played / duration * total;
            var run = 0;
            for (var i = 0; i < words.Length; i++)
            {
                run += words[i].Length + 1;
                if (at < run) return i;
            }
            return words.Length - 1;
        }

        public double LevelAt(TimeSpan played)
        {
            var centre = (int)(played.TotalMilliseconds / Step);
            var reach = (int)(LevelWindow * 1000 / Step / 2);
            var best = 0f;
            for (var i = Math.Max(0, centre - reach); i <= Math.Min(envelope.Length - 1, centre + reach); i++) best = Math.Max(best, envelope[i]);
            return best;
        }

        private int WordStartingAt(int start)
        {
            var best = 0;
            for (var i = 0; i < words.Length; i++)
            {
                if (words[i].Start <= start) best = i;
                else break;
            }
            return best;
        }
    }
}
