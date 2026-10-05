// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5, V-8.6, V-8.7): the dictated words and the motion of each operation.
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Transcript;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The words a dictation holds and what each operation of V-7.5 does to them, as the voice design's <c>.txt .w</c> rules do (voice-composer.html:123-131, voice-composer.js:221-343):
/// <b>Add</b> a word rises in (opacity .3 s ease, 6 px to 0 over .4 s ez); <b>Confirm</b> the tentative colour turns solid (.45 s ease); <b>Revise</b> the old text lifts away (opacity .22 s ease,
/// 0 to -6 px over .3 s ez) while the new rises in (the same, from +6 px) and the old is gone .34 s after; <b>Remove</b> the word leaves (opacity .26 s ease, 5 px down over .3 s ez) and is gone .34 s
/// after, still taking its place in the text until then. Operations name words by their place among the logical words, those not leaving. It holds instants and answers for any instant
/// (<see cref="Look"/>), so a frame, a test and a replay read the same numbers; a value changed in flight goes on from where it is. <see cref="Still"/> (Reduce Motion) lands every
/// transition at once. Main thread.
/// </summary>
internal sealed class DictationWords
{
    private readonly List<DictationWord> words = [];

    /// <summary>True under Reduce Motion: every transition lands at once.</summary>
    public bool Still { get; set; }

    /// <summary>Every word in its place in the text, those leaving included (they take their place until gone).</summary>
    public IReadOnlyList<DictationWord> Words => words;

    /// <summary>The words that are not leaving: the dictation as the draft holds it.</summary>
    public IEnumerable<DictationWord> Logical => words.Where(word => !word.Leaving);

    /// <summary>The number of logical words.</summary>
    public int Count => words.Count(word => !word.Leaving);

    public void Apply(WordOp op, ContinuousInstant now)
    {
        switch (op.Kind)
        {
            case WordOpKind.Add:
                Add(op.Text ?? "", op.Tentative, now);
                break;
            case WordOpKind.Revise:
                if (LogicalAt(op.Index) is { } revised && op.Text is { } text) Revise(revised, text, now);
                break;
            case WordOpKind.Remove:
                if (LogicalAt(op.Index) is { } removed) Remove(removed, now);
                break;
            case WordOpKind.Confirm:
                if (LogicalAt(op.Index) is { } confirmed) Confirm(confirmed, now);
                break;
        }
    }

    /// <summary>
    /// Ends what has finished at <paramref name="now"/>: words that have left are removed from the text and old texts that have gone are dropped. Returns whether a word was removed (the
    /// text, and so the layout, changed).
    /// </summary>
    public bool Settle(ContinuousInstant now)
    {
        var removed = words.RemoveAll(word => word.Leaving && word.GoneAt <= now) > 0;
        foreach (var word in words) word.Old.RemoveAll(old => old.GoneAt <= now);
        return removed;
    }

    /// <summary>Whether anything is moving at <paramref name="now"/>: a transition under way, a word leaving, an old text still showing.</summary>
    public bool IsMoving(ContinuousInstant now)
    {
        foreach (var word in words)
        {
            if (word.Leaving && word.GoneAt > now) return true;
            if (word.Opacity.Running(now) || word.Rise.Running(now) || word.Ink.Running(now) || word.TextOpacity.Running(now) || word.TextRise.Running(now)) return true;
            if (word.Old.Any(old => old.GoneAt > now)) return true;
            if (word.Place.IsMoving(now)) return true;
        }
        return false;
    }

    /// <summary>How <paramref name="word"/> looks at <paramref name="now"/>.</summary>
    public DictationWordLook Look(DictationWord word, ContinuousInstant now)
    {
        IReadOnlyList<DictationLayerLook> old = word.Old.Count == 0
            ? []
            : word.Old.Where(layer => layer.GoneAt > now).Select(layer => new DictationLayerLook(layer.Text, layer.Opacity.Value(now), layer.Rise.Value(now))).ToList();
        return new DictationWordLook(word.Opacity.Value(now), word.Rise.Value(now), word.Ink.Value(now), word.TextOpacity.Value(now), word.TextRise.Value(now), old);
    }

    private DictationWord? LogicalAt(int index)
    {
        if (index < 0) return null;
        var seen = 0;
        foreach (var word in words)
        {
            if (word.Leaving) continue;
            if (seen++ == index) return word;
        }
        return null;
    }

    // The design's addWord: hidden (opacity 0, 6 px low) on its first frame, then rising in.
    private void Add(string text, bool tentative, ContinuousInstant now)
    {
        var word = new DictationWord(text, tentative)
        {
            Opacity = new DictationTween(0),
            Rise = new DictationTween(DictationMotion.RiseFrom),
        };
        word.Opacity.Go(1, now, DictationMotion.OpacityIn, Still);
        word.Rise.Go(0, now, DictationMotion.RiseIn, Still);
        words.Add(word);
    }

    private void Confirm(DictationWord word, ContinuousInstant now)
    {
        word.Tentative = false;
        word.Ink.Go(0, now, DictationMotion.Ink, Still);
    }

    // The design's reviseWord: the text that is showing becomes .old (it keeps the opacity and rise it has and goes to 0 and -6 px), a new text starts hidden at +6 px and comes in.
    private void Revise(DictationWord word, string text, ContinuousInstant now)
    {
        if (text == word.Text) return;
        if (!Still)
        {
            var opacity = new DictationTween(word.TextOpacity.Value(now));
            var rise = new DictationTween(word.TextRise.Value(now));
            opacity.Go(0, now, DictationMotion.ReviseOpacity, false);
            rise.Go(DictationMotion.ReviseUp, now, DictationMotion.ReviseRise, false);
            word.Old.Add(new DictationOldText(word.Text, opacity, rise, now + DictationMotion.GoneAfter));
        }
        word.Text = text;
        word.TextOpacity = new DictationTween(0);
        word.TextRise = new DictationTween(DictationMotion.RiseFrom);
        word.TextOpacity.Go(1, now, DictationMotion.ReviseOpacity, Still);
        word.TextRise.Go(0, now, DictationMotion.ReviseRise, Still);
    }

    private void Remove(DictationWord word, ContinuousInstant now)
    {
        word.Leaving = true;
        word.GoneAt = Still ? now : now + DictationMotion.GoneAfter;
        word.Opacity.Go(0, now, DictationMotion.OpacityOut, Still);
        word.Rise.Go(DictationMotion.LeaveDown, now, DictationMotion.RiseOut, Still);
    }
}
