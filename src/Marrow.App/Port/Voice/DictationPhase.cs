// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-11, voice-SPEC V-8.3): where a dictation is in its life.
namespace Marrow.App.Port.Voice;

/// <summary>
/// The life of one dictation in the layer: <see cref="Idle"/> (nothing), <see cref="Armed"/> (listening, no word yet: the editor still draws), <see cref="Live"/> (words are being
/// written: the layer draws the text), <see cref="Landing"/> (the words are inking; the layer hands the text back to the editor when they have settled), <see cref="Withdrawing"/>
/// (cancelled: the words leave from the end, then the draft returns to what it was).
/// </summary>
internal enum DictationPhase { Idle, Armed, Live, Landing, Withdrawing }
