// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-8.1, V-8.3): where the dictated words sit in the draft.
namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>
/// The draft the insertion rules (<see cref="DictationInsertion"/>) make, with the places the dictation layer draws from: <see cref="Text"/>, the UTF-16 index of the first character of each
/// word (<see cref="WordStarts"/>), where the text after the insertion begins (<see cref="AfterStart"/>: after the spaces the insertion adds) and where the caret ends (<see cref="Caret"/>).
/// </summary>
public sealed record DictationPlacement(string Text, IReadOnlyList<int> WordStarts, int AfterStart, int Caret);
