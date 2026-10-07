// PORT-ONLY, owner-ruled addition (OD-0091; first-release voice typing, VT-3): where dictated words go in the draft (voice-SPEC V-8.1, V-8.6).
using System.Text;

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
    public DraftSelection Apply(IReadOnlyList<TranscriptWord> words)
    {
        if (words.Count == 0) return Original; // V-8.1: with no words the draft is untouched
        var place = Place(words);
        return new DraftSelection(place.Text, place.Caret, 0);
    }

    /// <summary>
    /// <see cref="Apply"/> with the places of the words (VT-11): the same text and caret, the index of each word's first character, and where the text after the insertion begins. With no
    /// words the draft is untouched: no places, the after text begins at the selection's end, the caret is where it was.
    /// </summary>
    public DictationPlacement Place(IReadOnlyList<TranscriptWord> words)
    {
        var text = Original.Text;
        // ED-292 (5): a selection outside the text is clamped at both ends, so one lying wholly outside becomes a caret at the edge.
        var start = Math.Clamp(Original.Start, 0, text.Length);
        var end = (int)Math.Clamp((long)Original.Start + Original.Length, start, text.Length);
        if (words.Count == 0) return new DictationPlacement(text, [], end, Original.Start);
        var before = text[..start];
        var after = text[end..];
        var run = string.Join(' ', words.Select(w => w.Text));
        var spaceBefore = before.Length > 0 && !IsWhitespace(LastRune(before)) && !IsPunctuation(FirstRune(words[0].Text));
        var spaceAfter = after.Length > 0 && !IsWhitespace(FirstRune(after)) && !IsPunctuation(FirstRune(after));
        var inserted = (spaceBefore ? " " : "") + run + (spaceAfter ? " " : "");
        var starts = new int[words.Count];
        var at = before.Length + (spaceBefore ? 1 : 0);
        for (var k = 0; k < words.Count; k++)
        {
            starts[k] = at;
            at += words[k].Text.Length + 1;
        }
        var afterStart = before.Length + inserted.Length;
        return new DictationPlacement(before + inserted + after, starts, afterStart, afterStart);
    }

    /// <summary>The draft as it was (text and selection), what cancel returns to.</summary>
    public DraftSelection Restore() => Original;

    private static Rune FirstRune(string text) => text.Length == 0 ? default : Rune.GetRuneAt(text, 0);

    private static Rune LastRune(string text)
    {
        Rune.DecodeLastFromUtf16(text, out var rune, out _);
        return rune;
    }

    private static bool IsWhitespace(Rune rune) => Rune.IsWhiteSpace(rune);

    private static bool IsPunctuation(Rune rune) => Rune.IsPunctuation(rune);
}
