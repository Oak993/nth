// PORT-ONLY, owner-ruled addition (OD-0106 Notes A Plain, OD-0099 Q-V-7 A; first-release voice typing, VT-17): one note: what happened, then one link that names the place that fixes it.
namespace Marrow.App.Port.Voice.Notes;

/// <summary>The words before the link, the link's words (drawn underlined, V-14.6's shape) and where the link opens.</summary>
public sealed record VoiceNote(VoiceNoteCase Case, string Before, string Link, VoiceNoteDestination Destination)
{
    /// <summary>The note as one line: the words, then the link's words.</summary>
    public string Text => Before + Link;
}
