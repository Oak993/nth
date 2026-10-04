// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): the words and operations of live dictation
// (voice-SPEC V-7.3, V-7.5). The motion of each operation is the DictationLayer's (VT-11); this file is what it is told.
namespace Marrow.Core.Port.Voice.Transcript;

/// <summary>A dictated word as the composer holds it: committed (solid) or in the tentative tail (drawn lighter, V-8.4).</summary>
public readonly record struct TranscriptWord(string Text, bool Tentative);

/// <summary>The four operations of V-7.5.</summary>
public enum WordOpKind
{
    /// <summary><c>Add(word, tentative)</c>: the word rises in.</summary>
    Add,

    /// <summary><c>Revise(index, text)</c>: the old text lifts away while the new rises in.</summary>
    Revise,

    /// <summary><c>Remove(index)</c>: a tail word the new hypothesis dropped (the withdraw motion of V-8.6).</summary>
    Remove,

    /// <summary><c>Confirm(index)</c>: tentative colour to solid.</summary>
    Confirm,
}

/// <summary>
/// One operation on the dictated words. <see cref="Index"/> counts words from the first dictated one. <see cref="Text"/> is set for
/// Add and Revise; <see cref="Tentative"/> is set for Add (a Revise keeps the word's state).
/// </summary>
public readonly record struct WordOp(WordOpKind Kind, int Index, string? Text = null, bool Tentative = false)
{
    public static WordOp Add(int index, string text, bool tentative) => new(WordOpKind.Add, index, text, tentative);

    public static WordOp Revise(int index, string text) => new(WordOpKind.Revise, index, text);

    public static WordOp Remove(int index) => new(WordOpKind.Remove, index);

    public static WordOp Confirm(int index) => new(WordOpKind.Confirm, index);
}

/// <summary>An operation due <see cref="At"/> after the moment its batch was handed over.</summary>
public readonly record struct ScheduledOp(TimeSpan At, WordOp Op);
