// PORT-ONLY, owner-ruled addition (OD-0097; first-release voice typing, VT-1): the pill's word sets L1 to L6 (voice-SPEC V-12.9,
// from the Voice Options page's LSETS). Which state shows is the label rule of V-6.3 (SpeechLabel, a later unit); this file holds the words.
namespace Marrow.Core.Port.Voice;

/// <summary>The three states the pill's label shows (V-6.3): listening, transcribing, finishing.</summary>
public enum VoiceLabelState { Listening, Transcribing, Finishing }

/// <summary>One word set: the words drawn for each state.</summary>
public sealed record PillWords(string Listening, string Transcribing, string Finishing);

/// <summary>The six word sets of V-12.9. U+2026 HORIZONTAL ELLIPSIS ends every word except L4's "Speak now" and all of L6.</summary>
public static class VoicePillWordSets
{
    private const string Ellipsis = "\U00002026"; // U+2026 HORIZONTAL ELLIPSIS

    private static readonly PillWords[] Sets =
    [
        new("Listening" + Ellipsis, "Transcribing" + Ellipsis, "Finishing" + Ellipsis),
        new("Listening" + Ellipsis, "Writing" + Ellipsis, "Finishing" + Ellipsis),
        new("Hearing you" + Ellipsis, "Writing" + Ellipsis, "Tidying up" + Ellipsis),
        new("Speak now", "Transcribing" + Ellipsis, "Almost done" + Ellipsis),
        new("Listening" + Ellipsis, "Typing" + Ellipsis, "Wrapping up" + Ellipsis),
        new("Listening", "Transcribing", "Finishing"),
    ];

    /// <summary>The words of a set.</summary>
    public static PillWords For(VoicePillWords set) => Sets[(int)set];

    /// <summary>The word a set shows for a state.</summary>
    public static string Word(VoicePillWords set, VoiceLabelState state) => state switch
    {
        VoiceLabelState.Listening => For(set).Listening,
        VoiceLabelState.Transcribing => For(set).Transcribing,
        VoiceLabelState.Finishing => For(set).Finishing,
    };
}
