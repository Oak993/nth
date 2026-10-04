// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): where dictated words go in the draft (voice-SPEC V-8.1, V-8.6).
namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>A draft with a selection: <see cref="Text"/>, the selection's UTF-16 start and length (a caret has length 0).</summary>
public readonly record struct DraftSelection(string Text, int Start, int Length);

/// <summary>
/// The insertion rules of V-8.1, as a pure function of the draft as it was when the session began (so a live update never reads its own
/// output): the words replace the selection, with a space before them when the preceding character is not whitespace and the first word
/// does not begin with punctuation, and after them when the following character is not whitespace or punctuation; the caret ends after
/// the inserted text. With no words the draft is untouched. <see cref="Original"/> is what Cancel restores (V-8.6).
/// </summary>
public sealed class DictationInsertion(DraftSelection original)
{
    public DraftSelection Original { get; } = original;

    /// <summary>The draft with <paramref name="words"/> (committed and tentative, in order) inserted.</summary>
    public DraftSelection Apply(IReadOnlyList<TranscriptWord> words) => throw new NotImplementedException();

    /// <summary>The draft as it was (text and selection), what cancel returns to.</summary>
    public DraftSelection Restore() => throw new NotImplementedException();
}
