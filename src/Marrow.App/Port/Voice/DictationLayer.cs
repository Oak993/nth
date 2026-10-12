// PORT-ONLY, owner-ruled addition (OD-0091, OD-0076; first-release voice typing, VT-11, voice-SPEC V-7.5, V-8.1 to V-8.10): the composer's dictation layer.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Styling;
using Avalonia.Utilities;
using Marrow.App.Views.Composer;
using Marrow.Core.Port;
using Marrow.Core.Port.Voice.Session;
using Marrow.Core.Port.Voice.Transcript;
using Marrow.UI.Port;

namespace Marrow.App.Port.Voice;

/// <summary>
/// The layer that writes the dictated words over the composer's editor (V-8.3). While a dictation is live the editor keeps its own text, still and transparent, and this layer, laid exactly
/// over it, draws the text the insertion makes (<see cref="DictationInsertion"/>: the text before and after the caret still, the words between them) with the editor's own font, width,
/// wrapping, flow direction, alignment and scroll offset, so a word that is not moving is drawn by one <see cref="TextLayout"/> exactly as the editor will draw it. Words that are moving
/// (rising in, revised, leaving, following a change of place: <see cref="DictationWords"/>, <see cref="DictationFlip"/>) use complete shaped glyph clusters from that same paragraph,
/// each following its native position across wrapped lines. The tentative colour is a per-range brush; the caret follows the last character on its final line.
/// The draft follows live (<see cref="TextChanged"/>, V-8.2) while the editor's text stays the pre-dictation draft until Landing writes the whole insertion as one edit (V-8.7: one undo
/// step), and the layer hands the picture back to the editor, in one frame, when the words have settled (V-8.3: pixel-identical). A cancel plays the withdraw (V-8.6) and returns the draft.
/// Main thread.
/// </summary>
internal sealed class DictationLayer : Control
{
    private readonly ComposerNSTextView editor;
    private readonly ScrollViewer? scroll;
    private readonly List<(ContinuousInstant Due, WordOp Op)> pending = [];
    private readonly Dictionary<int, DictationPicture> contextPictures = [];
    private readonly Dictionary<DictationWord, DictationPicture> pictures = [];
    private readonly Dictionary<DictationOldText, DictationPicture> oldPictures = [];
    private readonly AnimatedScalar scrollOffset;
    private DictationWords words = new();
    private DictationInsertion insertion = new(new DraftSelection("", 0, 0));
    private DictationPlacement? placement;
    private TextLayout? target;
    private (int Start, int Length)[] contextTokens = [];
    private Rect caretLine;
    private int layoutStyleKey;
    private DraftSelection original;
    private DictationPhase phase;
    private DisplayLink? link;
    private int settledFrames;
    private string draftText = "";
    private TextLayout? baseLayout;
    private int baseKey;
    private int caretWord = -1;
    private readonly DictationFlip caretFlip = new();
    private bool caretPlaced;
    private ContinuousInstant? withdrawEndAt;
    private bool restoring;
    private ContinuousInstant blinkStart;
    private ContinuousInstant activatedAt;
    private ContinuousInstant landedAt;
    private bool ghostShown;
    private bool scrolling;
    private bool hadWords;
    private bool followPending;
    private double layoutWidth;

    public DictationLayer(ComposerNSTextView editor, ScrollViewer? scroll)
    {
        this.editor = editor;
        this.scroll = scroll;
        IsHitTestVisible = false;
        Focusable = false;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        scrollOffset = new AnimatedScalar(this, 0, value =>
        {
            if (scrolling && this.scroll is not null) this.scroll.Offset = new Vector(this.scroll.Offset.X, value);
        });
        DetachedFromVisualTree += (_, _) => HandOverNow();
        editor.TextChanged += (_, _) =>
        {
            // after the landing the draft may be written meanwhile (a send clears it): the layer's picture is no longer the text, so the editor takes it back at once
            if (phase == DictationPhase.Landing && (editor.Text ?? "") != (placement?.Text ?? "")) HandOverNow();
        };
    }

    /// <summary>Where the dictation is in its life.</summary>
    internal DictationPhase Phase => phase;

    /// <summary>True while the layer owns the picture of the text: from the first word until the hand-over (or the end of a withdraw).</summary>
    public bool IsActive => phase is DictationPhase.Live or DictationPhase.Landing or DictationPhase.Withdrawing;

    /// <summary>True from <see cref="Begin"/> until the dictation has ended, whether or not a word has come yet.</summary>
    public bool IsOpen => phase != DictationPhase.Idle;

    /// <summary>The text the layer draws now, the words included (those leaving too, until they are gone); empty when the layer is not active.</summary>
    internal string Text => IsActive ? placement?.Text ?? "" : "";

    /// <summary>The words, for the tests.</summary>
    internal DictationWords Words => words;

    /// <summary>The native layout's identity, for invalidation proofs without exposing its mutable resources.</summary>
    internal object? LayoutIdentity => target;

    /// <summary>The last frame the layer asked for has handed over: true once the picture is the editor's again.</summary>
    internal bool HandedOver => phase == DictationPhase.Idle;

    /// <summary>Raised when <see cref="IsActive"/> changes.</summary>
    public event Action? ActiveChanged;

    /// <summary>Raised with the draft's text each time the dictated words change it (V-8.2), and with the pre-dictation text when a cancel has finished (V-8.6).</summary>
    public event Action<string>? TextChanged;

    /// <summary>Raised when the first word arrives (true) and when no word is left (false): the placeholder gives way to the words and comes back.</summary>
    public event Action<bool>? HasWordsChanged;

    private bool Still =>
        EnvironmentValues.GetAccessibilityReduceMotion(this) || Marrow.UI.Chrome.Chrome.ReduceMotionPlatform.Current.AccessibilityDisplayShouldReduceMotion;

    // ---- the session's side ----------------------------------------------------------------------------------------------------------

    /// <summary>Listening has begun: the draft and selection as they are (V-8.1). The editor goes on drawing until the first word.</summary>
    public void Begin(DraftSelection draft)
    {
        if (phase != DictationPhase.Idle) HandOverNow();
        original = draft;
        insertion = new DictationInsertion(draft);
        words = new DictationWords();
        pending.Clear();
        DisposePictures();
        phase = DictationPhase.Armed;
        draftText = draft.Text;
        hadWords = false;
        settledFrames = 0;
    }

    /// <summary>The session's operations (V-7.5): each at its time after now. Landing starts the ink wave; Withdraw the withdraw.</summary>
    public void Apply(TranscriptUpdate update)
    {
        if (phase == DictationPhase.Idle) return;
        var now = SwiftClock.Now;
        switch (update.Kind)
        {
            case TranscriptUpdateKind.Live or TranscriptUpdateKind.Final:
                foreach (var scheduled in update.Ops) Queue(now + scheduled.At, scheduled.Op);
                break;
            case TranscriptUpdateKind.Landing:
                FlushForLanding(now);
                foreach (var scheduled in update.Ops) Queue(now + scheduled.At, scheduled.Op);
                if (phase != DictationPhase.Armed)
                {
                    phase = DictationPhase.Landing;
                    landedAt = now;
                    editor.LiveText = null; // the editor holds the landed text now (one edit, V-8.7): it measures its own height again and follows the draft again (a send clears it)
                }
                break;
            case TranscriptUpdateKind.Withdraw:
                // what is drawn leaves, from the end; whatever was still to come (operations not yet due) never arrives
                pending.Clear();
                if (phase == DictationPhase.Armed || words.Count == 0)
                {
                    EndQuietly();
                    break;
                }
                foreach (var scheduled in TranscriptOps.PlanWithdraw(words.Count)) Queue(now + scheduled.At, scheduled.Op);
                phase = DictationPhase.Withdrawing;
                break;
        }
        StartLink();
    }

    /// <summary>The session ended without landing or withdrawing (an error): any words leave as a cancel's do (V-8.6); with none, the layer simply closes.</summary>
    public void Abandon()
    {
        if (phase is DictationPhase.Idle or DictationPhase.Withdrawing or DictationPhase.Landing) return;
        if (phase == DictationPhase.Armed || words.Count == 0)
        {
            EndQuietly();
            return;
        }
        var now = SwiftClock.Now;
        pending.Clear();
        foreach (var scheduled in TranscriptOps.PlanWithdraw(words.Count)) Queue(now + scheduled.At, scheduled.Op);
        phase = DictationPhase.Withdrawing;
        StartLink();
    }

    /// <summary>Everything lands now and the editor draws again (the composer leaves, another dictation begins, the layer is taken off the screen).</summary>
    public void HandOverNow()
    {
        if (phase == DictationPhase.Idle) return;
        var wasActive = IsActive;
        // before the text has landed the draft goes back to what the editor holds (the session lands its words by one edit later); after it, the editor holds them
        var restore = phase is DictationPhase.Live or DictationPhase.Withdrawing;
        EndLife();
        if (restore) TextChanged?.Invoke(original.Text);
        if (wasActive) ActiveChanged?.Invoke();
    }

    // ---- time ------------------------------------------------------------------------------------------------------------------------

    private void Queue(ContinuousInstant due, WordOp op)
    {
        var at = pending.Count;
        while (at > 0 && pending[at - 1].Due > due) at--; // in time order, equal times in the order given
        pending.Insert(at, (due, op));
    }

    private void FlushForLanding(ContinuousInstant now)
    {
        for (var i = 0; i < pending.Count;)
        {
            if (pending[i].Op.Kind == WordOpKind.Confirm)
            {
                i++;
                continue;
            }
            Perform(pending[i].Op, now);
            pending.RemoveAt(i);
        }
        Relayout(now);
    }

    private void StartLink()
    {
        if (link is not null || !FrameClock.IsBound || this.VisualRoot is null) return;
        link = FrameClock.DisplayLink(Tick);
        link.Add();
    }

    private void StopLink()
    {
        link?.Invalidate();
        link = null;
    }

    private void Tick()
    {
        if (phase == DictationPhase.Idle)
        {
            StopLink();
            return;
        }
        var now = SwiftClock.Now;
        words.Still = Still;
        var changed = false;
        var at = now;
        while (pending.Count > 0 && pending[0].Due <= now)
        {
            var next = pending[0];
            pending.RemoveAt(0);
            Perform(next.Op, next.Due);
            at = next.Due; // the places that change start moving when the operation that changed them was due
            changed = true;
        }
        if (words.Settle(now)) changed = true;
        PrunePictures();
        // the editor's width changed (the window, or the field's scroll bar coming or going): the words wrap anew and glide to their new places
        if (IsActive && !changed && (Math.Abs(LayoutWidth() - layoutWidth) > 0.5 || LayoutSignature() != layoutStyleKey)) changed = true;
        if (followPending && !changed) FollowTheEnd();
        if (changed) Relayout(at);

        if (phase == DictationPhase.Withdrawing && pending.Count == 0 && words.Words.Count == 0)
        {
            // the last word has gone: the caret fades where the words were, and a replaced selection fades back in (the mirror of its going, V-8.1, V-8.6); only then is the editor's
            if (withdrawEndAt is null)
            {
                withdrawEndAt = now;
                restoring = original.Length > 0 && !Still;
                settledFrames = 0;
            }
            if (Still || (!Moving(now) && settledFrames++ >= 1))
            {
                FinishWithdraw();
                return;
            }
        }
        if (phase == DictationPhase.Landing)
        {
            if (pending.Count == 0 && !Moving(now))
            {
                // one more frame draws the settled text before the editor takes it back
                if (settledFrames >= 1)
                {
                    HandOver();
                    return;
                }
                settledFrames++;
            }
            else settledFrames = 0;
        }
        if (phase == DictationPhase.Armed && pending.Count == 0)
        {
            StopLink();
            return;
        }
        if (IsActive) InvalidateVisual();
    }

    private bool Moving(ContinuousInstant now)
    {
        if (words.IsMoving(now)) return true;
        if (pictures.Values.Concat(contextPictures.Values).Concat(oldPictures.Values).Any(picture => picture.IsMoving(now))) return true;
        if (phase == DictationPhase.Landing && (now - landedAt).TotalSeconds < DictationMotion.CaretFade.Duration) return true;
        if (withdrawEndAt is { } ended && (now - ended).TotalSeconds < Math.Max(DictationMotion.CaretFade.Duration, restoring ? DictationMotion.Ghost.Duration : 0)) return true;
        if (caretPlaced && caretFlip.IsMoving(now)) return true;
        return ghostShown && (now - activatedAt).TotalSeconds < DictationMotion.Ghost.Duration;
    }

    private void Perform(WordOp op, ContinuousInstant at)
    {
        if (op.Kind == WordOpKind.Add && phase == DictationPhase.Armed) Activate(at);
        if (phase == DictationPhase.Armed) return; // nothing is there to revise, remove or confirm
        words.Still = Still;
        DictationPicture? replaced = null;
        var revised = op.Kind == WordOpKind.Revise && op.Index >= 0 ? words.Logical.ElementAtOrDefault(op.Index) : null;
        if (!Still && revised is not null && op.Text is { } replacement && replacement != revised.Text)
        {
            // A batch may revise more than once. Capture the immediately preceding shaped picture without publishing an intermediate draft.
            Relayout(at, publish: false);
            if (pictures.Remove(revised, out replaced)) replaced.Capture(at);
        }
        if (op.Kind == WordOpKind.Add && words.Logical.LastOrDefault() is { } previous)
            previous.Ink.Go(0, at, DictationMotion.Ink, Still); // V-7.5: visual confirmation; the transcriber's logical tail remains revisable.
        words.Apply(op, at);
        if (replaced is not null && revised is not null) oldPictures.Add(revised.Old[^1], replaced);
    }

    private void Activate(ContinuousInstant at)
    {
        phase = DictationPhase.Live;
        activatedAt = at;
        blinkStart = at;
        editor.EnterDictation();
        editor.LiveText = original.Text;
        ghostShown = original.Length > 0;
        // the text after the selection starts where it was: its words follow to their new places by FLIP
        DisposePictures();
        using (var layout = MakeLayout(original.Text, Math.Max(1, LayoutWidth()), null))
        {
            var from = Math.Clamp(original.Start + original.Length, 0, original.Text.Length);
            var k = 0;
            foreach (var (start, length) in ContextTokens(original.Text, from))
            {
                contextPictures[k] = new DictationPicture(layout, start, length, at, still: false);
                k++;
            }
        }
        ActiveChanged?.Invoke();
        hadWords = true;
        HasWordsChanged?.Invoke(true);
    }

    private void HandOver()
    {
        EndLife();
        ActiveChanged?.Invoke();
    }

    private void FinishWithdraw()
    {
        EndLife();
        TextChanged?.Invoke(original.Text); // the draft returns to what it was
        ActiveChanged?.Invoke();
    }

    private void EndQuietly()
    {
        var wasActive = IsActive;
        EndLife();
        if (wasActive) ActiveChanged?.Invoke();
    }

    private void EndLife()
    {
        StopLink();
        var hadActive = IsActive;
        phase = DictationPhase.Idle;
        pending.Clear();
        DisposePictures();
        words = new DictationWords();
        placement = null;
        target?.Dispose();
        target = null;
        baseLayout?.Dispose();
        baseLayout = null;
        contextTokens = [];
        caretWord = -1;
        caretPlaced = false;
        withdrawEndAt = null;
        restoring = false;
        ghostShown = false;
        scrolling = false;
        if (hadActive)
        {
            editor.LiveText = null;
            editor.ExitDictation();
        }
        InvalidateMeasure();
        InvalidateVisual();
        if (hadWords)
        {
            hadWords = false;
            HasWordsChanged?.Invoke(false);
        }
    }

    // ---- the layout ------------------------------------------------------------------------------------------------------------------

    private double LayoutWidth() => Math.Max(0, (Bounds.Width > 0 ? Bounds.Width : editor.Bounds.Width) - editor.Padding.Left - editor.Padding.Right);

    private Typeface Face => new(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);

    private TextLayout MakeLayout(string text, double maxWidth, IReadOnlyList<ValueSpan<TextRunProperties>>? overrides) =>
        editor.CreateComposerLayout(text, maxWidth, Brushes.Black, overrides);

    private int LayoutSignature()
    {
        var key = new HashCode(); key.Add(Face); key.Add(editor.FontSize); key.Add(editor.TextAlignment); key.Add(editor.TextWrapping); key.Add(editor.FlowDirection);
        key.Add(editor.LineHeight); key.Add(Avalonia.Controls.Documents.TextElement.GetLetterSpacing(editor));
        if (Avalonia.Controls.Documents.TextElement.GetFontFeatures(editor) is { } features)
            foreach (var feature in features) key.Add(feature);
        return key.ToHashCode();
    }

    private static IEnumerable<(int Start, int Length)> Tokens(string text, int from)
    {
        var at = from;
        while (at < text.Length)
        {
            while (at < text.Length && char.IsWhiteSpace(text[at])) at++;
            var start = at;
            while (at < text.Length && !char.IsWhiteSpace(text[at])) at++;
            if (at > start) yield return (start, at - start);
        }
    }

    private IEnumerable<(int Start, int Length)> ContextTokens(string text, int afterStart)
    {
        var beforeEnd = Math.Clamp(original.Start, 0, text.Length);
        foreach (var (start, length) in Tokens(text, 0))
        {
            if (start >= beforeEnd) break;
            yield return (start, Math.Min(length, beforeEnd - start));
        }
        foreach (var token in Tokens(text, afterStart)) yield return token;
    }

    private void PrunePictures()
    {
        foreach (var word in pictures.Keys.Where(word => !words.Words.Contains(word)).ToArray()) { pictures.Remove(word, out var picture); picture!.Dispose(); }
        var liveOld = words.Words.SelectMany(word => word.Old).ToHashSet();
        foreach (var old in oldPictures.Keys.Where(old => !liveOld.Contains(old)).ToArray()) { oldPictures.Remove(old, out var picture); picture!.Dispose(); }
    }

    private void DisposePictures()
    {
        foreach (var picture in pictures.Values.Concat(contextPictures.Values).Concat(oldPictures.Values)) picture.Dispose();
        pictures.Clear(); contextPictures.Clear(); oldPictures.Clear();
    }

    // The text the words make, where each one and each token of the text after them sits (hit tests of the editor's own kind of layout), and the draft the composer holds.
    private void Relayout(ContinuousInstant now, bool publish = true)
    {
        var all = words.Words.Select(word => new TranscriptWord(word.Text, word.Tentative)).ToList();
        placement = insertion.Place(all);
        var logical = words.Logical.Select(word => new TranscriptWord(word.Text, word.Tentative)).ToList();
        var nextDraft = logical.Count == words.Words.Count ? placement.Text : insertion.Place(logical).Text;
        target?.Dispose();
        layoutWidth = LayoutWidth();
        layoutStyleKey = LayoutSignature();
        target = MakeLayout(placement.Text, Math.Max(1, layoutWidth), null);
        var still = Still;
        for (var i = 0; i < all.Count; i++)
        {
            var word = words.Words[i];
            pictures.Remove(word, out var previous);
            var picture = new DictationPicture(target, placement.WordStarts[i], word.Text.Length, now, still, previous?.DrawingOrder(now), word.Place);
            pictures.Add(word, picture); previous?.Dispose();
            word.Separable = picture.Fragments.Count > 0;
            if (picture.Fragments.Count > 0)
                foreach (var old in word.Old)
                    if (oldPictures.TryGetValue(old, out var oldPicture)) oldPicture.FollowAnchor(picture.Anchor, now, still);
        }
        contextTokens = ContextTokens(placement.Text, placement.AfterStart).ToArray();
        for (var k = 0; k < contextTokens.Length; k++)
        {
            contextPictures.Remove(k, out var previous);
            contextPictures[k] = new DictationPicture(target, contextTokens[k].Start, contextTokens[k].Length, now, still, previous?.DrawingOrder(now));
            previous?.Dispose();
        }
        foreach (var stale in contextPictures.Keys.Where(key => key >= contextTokens.Length).ToArray()) { contextPictures.Remove(stale, out var picture); picture!.Dispose(); }

        caretWord = -1;
        for (var i = words.Words.Count - 1; i >= 0; i--)
        {
            if (words.Words[i].Leaving) continue;
            caretWord = i;
            break;
        }
        PlaceCaret(now, still);
        if (phase != DictationPhase.Landing) editor.LiveText = placement.Text;
        InvalidateMeasure();
        followPending = true; // the scroll goes by the field's own extent, which is known after the next layout pass: the next frame
        if (publish && nextDraft != draftText)
        {
            draftText = nextDraft;
            TextChanged?.Invoke(draftText); // V-8.2: the draft follows live
        }
    }

    // The caret rides at the end of the last word drawn, those leaving too (the design keeps it after the last span until they are removed, voice-composer.js withdraw/caretOn), and where
    // the words were once none is left; its place glides like a word's (FLIP) instead of jumping from one word's end to the next.
    private void PlaceCaret(ContinuousInstant now, bool still)
    {
        if (target is null || placement is null) return;
        var end = words.Words.Count > 0 ? placement.WordStarts[^1] + words.Words[^1].Text.Length : Math.Clamp(placement.Caret, 0, placement.Text.Length);
        var hasText = words.Words.Count > 0 && words.Words[^1].Text.Length > 0;
        var terminalBreak = hasText && new Codepoint(words.Words[^1].Text[^1]).IsBreakChar;
        var lineIndex = target.GetLineIndexFromCharacterIndex(end, trailingEdge: hasText && !terminalBreak);
        var line = target.TextLines[lineIndex];
        var edge = hasText && !terminalBreak ? line.GetNextCaretCharacterHit(line.GetPreviousCaretCharacterHit(new CharacterHit(end))) : new CharacterHit(end);
        if (edge.FirstCharacterIndex + edge.TrailingLength != end) edge = new CharacterHit(end);
        var x = line.GetDistanceFromCharacterHit(edge);
        var rtl = edge.TrailingLength > 0 && x < line.GetDistanceFromCharacterHit(new CharacterHit(edge.FirstCharacterIndex));
        var top = target.TextLines.Take(lineIndex).Sum(textLine => textLine.Height);
        caretLine = new Rect(0, top, 0, line.Height);
        caretFlip.Place(new Point(x + (rtl ? -1 - DictationCaret.Width : 1), top + (line.Height - DictationCaret.Height) / 2), now, still);
        caretPlaced = true;
    }

    // V-8.10: when the end of the text passes the bottom of the field it is brought into view on the design's .36 s ez, never a jump.
    private void FollowTheEnd()
    {
        followPending = false;
        if (scroll is null || caretWord < 0) return;
        var bottom = caretLine.Bottom + editor.Padding.Top + editor.Padding.Bottom;
        var viewport = scroll.Viewport.Height;
        var farthest = Math.Max(0, scroll.Extent.Height - viewport); // the field cannot go past its own end
        if (viewport <= 0 || bottom <= scroll.Offset.Y + viewport + 0.5 || farthest <= scroll.Offset.Y + 0.5) return;
        scrolling = true;
        scrollOffset.Set(scroll.Offset.Y, null); // from where the view is
        scrollOffset.Set(Math.Min(bottom - viewport, farthest), DictationMotion.Scroll);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (!IsActive || placement is null) return default;
        var width = double.IsInfinity(availableSize.Width) ? Math.Max(1, editor.Bounds.Width) : availableSize.Width;
        using var layout = MakeLayout(placement.Text, Math.Max(1, width - editor.Padding.Left - editor.Padding.Right), null);
        // the same height the composer measures for its text (ComposerTextView.UpdateHeight): the content, at least 18, inside the editor's own padding
        return new Size(0, Math.Ceiling(Math.Max(18, layout.Height) + editor.Padding.Top + editor.Padding.Bottom));
    }

    // ---- drawing ---------------------------------------------------------------------------------------------------------------------

    private bool IsSeparate(DictationWord word, DictationWordLook look, ContinuousInstant now) =>
        look.Opacity < 1 || look.Rise != 0 || look.TextOpacity < 1 || look.TextRise != 0 || look.Old.Count > 0 || word.Leaving ||
        pictures.TryGetValue(word, out var picture) && picture.IsMoving(now);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsActive || placement is null || placement.WordStarts.Count != words.Words.Count) return; // between an operation and its layout there is nothing to draw
        var now = SwiftClock.Now;
        var ink = SwiftColor.LabelColor.Resolve(editor.ActualThemeVariant);
        var dark = editor.ActualThemeVariant == ThemeVariant.Dark;
        var origin = new Point(editor.Padding.Left, editor.Padding.Top);
        var width = Math.Max(1, LayoutWidth());
        var face = Face;
        var size = editor.FontSize;

        var overrides = new List<ValueSpan<TextRunProperties>>();
        var movers = new List<(int Index, DictationWord Word, DictationWordLook Look)>();
        var key = new HashCode();
        key.Add(placement.Text);
        key.Add(width);
        key.Add(layoutStyleKey); key.Add(ink);
        for (var i = 0; i < words.Words.Count; i++)
        {
            var word = words.Words[i];
            var look = words.Look(word, now);
            var start = placement.WordStarts[i];
            if (IsSeparate(word, look, now))
            {
                movers.Add((i, word, look));
                if (word.Text.Length > 0) overrides.Add(new ValueSpan<TextRunProperties>(start, word.Text.Length, Properties(face, size, Brushes.Transparent)));
                key.Add(start);
                key.Add(-1);
            }
            else if (word.Text.Length > 0 && look.Tentative > 0)
            {
                var alpha = Alpha(ink, DictationInk.AlphaOf(look.Tentative, dark));
                overrides.Add(new ValueSpan<TextRunProperties>(start, word.Text.Length, Properties(face, size, new ImmutableSolidColorBrush(alpha))));
                key.Add(start);
                key.Add(alpha.A);
            }
        }
        if (restoring)
        {
            // a replaced selection is coming back (a cancel): it is drawn by the ghost, fading in, not by the base layout
            var from = Math.Clamp(original.Start, 0, placement.Text.Length);
            var to = Math.Clamp(original.Start + original.Length, from, placement.Text.Length);
            if (to > from) overrides.Add(new ValueSpan<TextRunProperties>(from, to - from, Properties(face, size, Brushes.Transparent)));
            key.Add(-3);
            key.Add(from);
            key.Add(to);
        }
        var movingTokens = new List<int>();
        for (var k = 0; k < contextTokens.Length; k++)
        {
            if (!contextPictures.TryGetValue(k, out var picture) || !picture.IsMoving(now)) continue;
            movingTokens.Add(k);
            overrides.Add(new ValueSpan<TextRunProperties>(contextTokens[k].Start, contextTokens[k].Length, Properties(face, size, Brushes.Transparent)));
            key.Add(contextTokens[k].Start);
            key.Add(-2);
        }
        var wanted = key.ToHashCode();
        if (baseLayout is null || wanted != baseKey)
        {
            baseLayout?.Dispose();
            baseLayout = editor.CreateComposerLayout(placement.Text, width, new ImmutableSolidColorBrush(ink), overrides);
            baseKey = wanted;
        }

        var ghost = 0.0;
        if (restoring) ghost = DictationMotion.Ghost.Progress((now - withdrawEndAt!.Value).TotalSeconds);
        else if (ghostShown)
        {
            ghost = 1 - DictationMotion.Ghost.Progress((now - activatedAt).TotalSeconds);
            if (ghost <= 0 || Still)
            {
                ghostShown = false;
                ghost = 0;
            }
        }
        if (ghost > 0) DrawGhost(context, origin, face, size, ink, ghost);
        baseLayout.Draw(context, origin);

        foreach (var (index, word, look) in movers)
        {
            var inkAlpha = DictationInk.AlphaOf(look.Tentative, dark);
            if (pictures.TryGetValue(word, out var picture)) picture.Draw(context, origin, now, ink, inkAlpha * look.Opacity * look.TextOpacity, look.Rise + look.TextRise);
            foreach (var old in word.Old.Where(old => old.GoneAt > now))
                if (oldPictures.TryGetValue(old, out var oldPicture)) oldPicture.Draw(context, origin, now, ink, inkAlpha * look.Opacity * old.Opacity.Value(now), look.Rise + old.Rise.Value(now));
        }
        foreach (var k in movingTokens)
        {
            contextPictures[k].Draw(context, origin, now, ink, 1, 0);
        }
        DrawCaret(context, origin, ink, now);
    }

    // The selection a dictation replaces (V-8.1) fades out as the first word comes in, where it was, with its highlight; the editor stops drawing it that moment. A cancel brings it back the
    // same way, fading in, before the editor takes the picture over (no frame of it appears whole).
    private void DrawGhost(DrawingContext context, Point origin, Typeface face, double size, Color ink, double fade)
    {
        var from = Math.Clamp(original.Start, 0, original.Text.Length);
        var to = Math.Clamp(original.Start + original.Length, from, original.Text.Length);
        var selection = SwiftColor.SelectedTextBackgroundColor.Resolve(editor.ActualThemeVariant);
        var spans = new List<ValueSpan<TextRunProperties>>();
        if (from > 0) spans.Add(new ValueSpan<TextRunProperties>(0, from, Properties(face, size, Brushes.Transparent)));
        spans.Add(new ValueSpan<TextRunProperties>(from, to - from, Properties(face, size, new ImmutableSolidColorBrush(Alpha(ink, 1)))));
        if (to < original.Text.Length) spans.Add(new ValueSpan<TextRunProperties>(to, original.Text.Length - to, Properties(face, size, Brushes.Transparent)));
        using var layout = editor.CreateComposerLayout(original.Text, Math.Max(1, LayoutWidth()), Brushes.Transparent, spans);
        // ED-NEW-glass-c10-1: fade the composed selection and glyphs once; per-paint alpha makes their overlap nonlinear.
        using (context.PushRenderOptions(new RenderOptions { RequiresFullOpacityHandling = true }))
        using (context.PushOpacity(fade))
        {
            // Match TextPresenter.Render in Avalonia 12.1.2: the editor fills each selection rectangle after PixelRect.FromRect(rect, 1), before drawing the glyphs.
            // A run background keeps fractional edges instead, so the ghost's last frame differed at the right and bottom edges when the editor took over.
            var brush = new ImmutableSolidColorBrush(selection);
            foreach (var rect in layout.HitTestTextRange(from, to - from))
            {
                var snapped = PixelRect.FromRect(rect, 1).ToRect(1);
                context.FillRectangle(brush, new Rect(origin + snapped.TopLeft, snapped.Size));
            }
            layout.Draw(context, origin);
        }
    }

    // The caret after the last word, in the layer's own coordinates: 1.5 x 17, a pixel past the word's end (its left end for a right-to-left word), centred on the word's line, riding
    // along with the word's own place (V-8.3). Null when there is no word to follow.
    private Rect? CaretBox(Point origin, ContinuousInstant now)
    {
        if (!caretPlaced || (caretWord < 0 && phase != DictationPhase.Withdrawing)) return null;
        var place = origin + caretFlip.Displayed(now);
        return new Rect(place.X, place.Y, DictationCaret.Width, DictationCaret.Height);
    }

    /// <summary>Where the caret is at <paramref name="now"/> (its blink aside); null with no word to follow.</summary>
    internal Rect? CaretRect(ContinuousInstant now) => IsActive ? CaretBox(new Point(editor.Padding.Left, editor.Padding.Top), now) : null;

    /// <summary>Where word <paramref name="index"/> (its place among all the words, those leaving too) is drawn at <paramref name="now"/>, in the layer's coordinates before the editor's padding.</summary>
    internal Point DisplayedPlace(int index, ContinuousInstant now) => words.Words[index].Place.Displayed(now);

    /// <summary>
    /// The share of the caret's blink that shows at <paramref name="now"/>: 1 while words are being written; at Landing it leaves on .18 s ease (the editor's own caret is the editor's
    /// business once it has the text back); after a cancel it rides with the words that are leaving and fades on the same curve where they were (the design removes it after them).
    /// </summary>
    internal double CaretFade(ContinuousInstant now)
    {
        var share = 1.0;
        if (phase == DictationPhase.Landing) share *= 1 - DictationMotion.CaretFade.Progress((now - landedAt).TotalSeconds);
        if (withdrawEndAt is { } ended) share *= 1 - DictationMotion.CaretFade.Progress((now - ended).TotalSeconds);
        return share;
    }

    private void DrawCaret(DrawingContext context, Point origin, Color ink, ContinuousInstant now)
    {
        if (CaretBox(origin, now) is not { } box) return;
        var opacity = DictationCaret.Opacity((now - blinkStart).TotalSeconds, Still) * CaretFade(now);
        if (opacity <= 0) return;
        context.DrawRectangle(new ImmutableSolidColorBrush(Alpha(ink, opacity)), null, new RoundedRect(box, 1));
    }

    private static Color Alpha(Color color, double share) => Color.FromArgb((byte)Math.Round(Math.Clamp(color.A * share, 0, 255)), color.R, color.G, color.B);

    private GenericTextRunProperties Properties(Typeface face, double size, IBrush brush) =>
        new(face, size, null, brush, fontFeatures: Avalonia.Controls.Documents.TextElement.GetFontFeatures(editor));
}
