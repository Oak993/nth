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
        var text = Original.Text;
        // ED-NEW-f-vt-3 (5): a selection outside the text is clamped at both ends, so one lying wholly outside becomes a caret at the edge.
        var start = Math.Clamp(Original.Start, 0, text.Length);
        var end = Math.Clamp((long)Original.Start + Original.Length, start, text.Length);
        var before = text[..start];
        var after = text[(int)end..];
        var run = string.Join(' ', words.Select(w => w.Text));
        var spaceBefore = before.Length > 0 && !IsWhitespace(LastRune(before)) && !IsPunctuation(FirstRune(words[0].Text));
        var spaceAfter = after.Length > 0 && !IsWhitespace(FirstRune(after)) && !IsPunctuation(FirstRune(after));
        var inserted = (spaceBefore ? " " : "") + run + (spaceAfter ? " " : "");
        return new DraftSelection(before + inserted + after, before.Length + inserted.Length, 0);
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
