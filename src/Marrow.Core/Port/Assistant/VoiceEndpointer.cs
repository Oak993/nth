// PORT-ONLY: Dextra (DESIGN.md rev 5, 2.4 "Press Ctrl+Alt+Enter ... listening ends when the owner stops speaking (1.2 s of silence)";
// unit AS-18; ED-NEW-dextra-as18-3). No Swift twin.
// Where one spoken request starts and ends, from the voice detector's probability for each 32 ms frame. It starts after two speech
// frames in a row, ends after 1.2 s without speech, and gives up after a stretch of nothing (toggle mode) or a too long request.
// Pure: it counts frames, so a test feeds it the numbers and no clock is read.
namespace Marrow.Core.Port.Assistant;

public enum EndpointKind
{
    None,

    /// <summary>Speech began; the frames since <see cref="VoiceEndpointer.StartFrames"/> ago are the start.</summary>
    SpeechStarted,

    /// <summary>1.2 s of silence after speech: the request is over.</summary>
    SpeechEnded,

    /// <summary>Nothing was said for the whole wait: listening stops.</summary>
    NoSpeech,

    /// <summary>The request ran as long as one request may.</summary>
    TooLong,
}

public sealed record VoiceEndpointerOptions
{
    public float StartProbability { get; init; } = 0.5f;

    public int StartFrames { get; init; } = 2;

    /// <summary>A frame under this ends a speech frame run (a lower bar than the start: a word's tail is still speech).</summary>
    public float EndProbability { get; init; } = 0.35f;

    /// <summary>1.2 s of silence ends the request (2.4).</summary>
    public TimeSpan Silence { get; init; } = TimeSpan.FromSeconds(1.2);

    /// <summary>How long a toggled listen waits for a first word before it gives up; null waits for ever (a held button).</summary>
    public TimeSpan? NoSpeechAfter { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>One request is at most this long (the speech engine reads 30 s at a time).</summary>
    public TimeSpan MostSpeech { get; init; } = TimeSpan.FromSeconds(28);
}

public sealed class VoiceEndpointer
{
    /// <summary>The voice detector's frame: 512 samples at 16 kHz.</summary>
    public static readonly TimeSpan Frame = TimeSpan.FromSeconds(512.0 / 16000);

    private readonly VoiceEndpointerOptions o;
    private readonly int silenceFrames;
    private readonly int waitFrames;
    private readonly int mostFrames;
    private int run;
    private int quiet;
    private int frames;
    private int spoken;
    private bool inSpeech;

    public VoiceEndpointer(VoiceEndpointerOptions? options = null)
    {
        o = options ?? new VoiceEndpointerOptions();
        silenceFrames = (int)Math.Ceiling(o.Silence / Frame);
        waitFrames = o.NoSpeechAfter is { } wait ? (int)Math.Ceiling(wait / Frame) : int.MaxValue;
        mostFrames = (int)Math.Ceiling(o.MostSpeech / Frame);
    }

    public int StartFrames => o.StartFrames;

    public bool InSpeech => inSpeech;

    /// <summary>Frames fed since the last reset.</summary>
    public int Frames => frames;

    public void Reset()
    {
        run = 0;
        quiet = 0;
        frames = 0;
        spoken = 0;
        inSpeech = false;
    }

    /// <summary>Begins already in speech (a barge-in: the owner is talking now, and the frames before it were kept).</summary>
    public void BeginInSpeech()
    {
        Reset();
        inSpeech = true;
    }

    public EndpointKind Feed(float probability)
    {
        frames++;
        if (!inSpeech)
        {
            run = probability >= o.StartProbability ? run + 1 : 0;
            if (run >= o.StartFrames)
            {
                inSpeech = true;
                quiet = 0;
                spoken = run;
                return EndpointKind.SpeechStarted;
            }
            return frames >= waitFrames ? EndpointKind.NoSpeech : EndpointKind.None;
        }
        spoken++;
        quiet = probability < o.EndProbability ? quiet + 1 : 0;
        if (quiet >= silenceFrames) return EndpointKind.SpeechEnded;
        return spoken >= mostFrames ? EndpointKind.TooLong : EndpointKind.None;
    }
}
