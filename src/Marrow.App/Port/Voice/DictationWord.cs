// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5): one dictated word and its transitions.
using Marrow.Core.Port;

namespace Marrow.App.Port.Voice;

/// <summary>One dictated word: its text, whether it is still tentative, and the numbers its transitions move (the design's <c>.w</c> span).</summary>
internal sealed class DictationWord
{
    public DictationWord(string text, bool tentative)
    {
        Text = text;
        Tentative = tentative;
        Ink = new DictationTween(tentative ? 1 : 0);
    }

    /// <summary>The text the word has now (a revision changes it at once; the old text is in <see cref="Old"/> until it has gone).</summary>
    public string Text { get; internal set; }

    /// <summary>Whether the word is in the tentative tail (its colour may still be turning: <see cref="Ink"/>).</summary>
    public bool Tentative { get; internal set; }

    /// <summary>True from the Remove operation until the word has left (it still takes its place in the text meanwhile).</summary>
    public bool Leaving { get; internal set; }

    /// <summary>When a leaving word is gone.</summary>
    public ContinuousInstant GoneAt { get; internal set; }

    /// <summary>True when the layout puts the word in one piece (one hit-test rectangle): only such a word can be drawn on its own while it moves.</summary>
    public bool Separable { get; internal set; } = true;

    /// <summary>Where the word is drawn: its place in the layout, reached by FLIP.</summary>
    public DictationFlip Place { get; } = new();

    internal DictationTween Opacity = new(1);

    internal DictationTween Rise = new(0);

    internal DictationTween Ink;

    internal DictationTween TextOpacity = new(1);

    internal DictationTween TextRise = new(0);

    internal List<DictationOldText> Old { get; } = [];
}
