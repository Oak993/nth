// PORT-ONLY, owner-ruled addition (OD-0091, OD-0097; first-release voice typing, VT-3): the pill's Listening / Transcribing / Finishing label
// (voice-SPEC V-6.3, DESIGN voice-composer.js:385-393, 753-756).
namespace Marrow.Core.Port.Voice.Session;

/// <summary>
/// The label rule of V-6.3: <c>speechAt = now</c> whenever the speech state is on or a word arrived less than 0.35 s ago; the label is
/// Transcribing while <c>now - speechAt &lt; 0.6 s</c>, else Listening; Finishing from the second click until the final pass completes.
/// The design evaluates this once per animation frame; here the same rule is evaluated exactly, whenever it is read, so the answer does
/// not depend on how often the pill asks. The words drawn for each state come from the picked set (V-12.9, <see cref="VoicePillWordSets"/>).
/// </summary>
public sealed class SpeechLabel
{
    /// <summary>A word counts as speech for this long after it arrived (V-6.3): 0.35 s.</summary>
    public static readonly TimeSpan WordWindow = TimeSpan.FromSeconds(0.35);

    /// <summary>The label stays Transcribing this long after the last moment that counted as speech (V-6.3): 0.6 s.</summary>
    public static readonly TimeSpan SpeechWindow = TimeSpan.FromSeconds(0.6);

    private bool speechOn;
    private bool finishing;
    private ContinuousInstant? speechAt;
    private ContinuousInstant? lastWordAt;

    /// <summary>The state to draw at <paramref name="now"/>.</summary>
    public VoiceLabelState State(ContinuousInstant now)
    {
        if (finishing) return VoiceLabelState.Finishing; // V-6.3: from the second click until the final pass completes
        Refresh(now);
        return speechAt is { } at && now - at < SpeechWindow ? VoiceLabelState.Transcribing : VoiceLabelState.Listening;
    }

    /// <summary>The VAD's speech state (V-6.2) changed, or was read, at <paramref name="now"/>.</summary>
    public void SetSpeech(bool inSpeech, ContinuousInstant now)
    {
        Refresh(now);
        speechOn = inSpeech;
        if (inSpeech) speechAt = now;
    }

    /// <summary>A word arrived (an Add of the live words) at <paramref name="now"/>.</summary>
    public void WordArrived(ContinuousInstant now)
    {
        Refresh(now);
        lastWordAt = now;
    }

    /// <summary>The second click: the label reads Finishing until <see cref="Reset"/>.</summary>
    public void Finish() => finishing = true;

    /// <summary>A new session: Listening, no speech heard.</summary>
    public void Reset()
    {
        speechOn = false;
        finishing = false;
        speechAt = null;
        lastWordAt = null;
    }

    /// <summary>The word the picked set shows at <paramref name="now"/>.</summary>
    public string Words(VoicePillWords set, ContinuousInstant now) => VoicePillWordSets.Word(set, State(now));

    // Brings speechAt up to date as a continuous evaluation would have: while speech is on it is "now"; a word counts as speech for 0.35 s.
    private void Refresh(ContinuousInstant now)
    {
        if (speechOn) speechAt = now;
        if (lastWordAt is { } word)
        {
            var coverage = word + WordWindow < now ? word + WordWindow : now;
            if (speechAt is not { } current || coverage > current) speechAt = coverage;
        }
    }
}
