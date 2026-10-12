// PORT-ONLY: OD-0136 amendment 2, step 2 option B ("Thinking line", ED-NEW-nth-motion). The main working line under a turn
// (ThreadTimeline's WorkingIndicator): the three-arc glyph (14 px), the line's word in breathing letters (a new word arrives letter
// by letter) and the time since the turn started in rolling digits (mono, tabular, right-aligned, quieter than the word). The
// words, the summaries and the time's text are the line's own; only the drawing changes. The Hydra head panels keep
// HydraWorkingTitle, HydraElapsedTime and the activity chart as they are.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Marrow.Core.Port;
using Marrow.UI.Chrome;
using Marrow.UI.Common;
using Marrow.UI.Port;
using Marrow.UI.Port.Nth;
using Marrow.UI.Port.Nth.Beads;

namespace Marrow.App.Views.Chat;

/// <summary>A header child that knows where its text's first baseline is (the working line's header aligns its children on it).</summary>
internal interface IBaselineProvider
{
    double Baseline { get; }
}

/// <summary>The type and ink of an <see cref="NthLetters"/> in the working line.</summary>
internal static class NthLineType
{
    /// <summary>Gives the letters the font (the letters read it as a TextBlock would, from the TextElement properties).</summary>
    public static void Apply(NthLetters letters, SwiftFont font)
    {
        var resolved = font.Resolve();
        letters.SetValue(TextElement.FontFamilyProperty, resolved.FontFamily);
        letters.SetValue(TextElement.FontSizeProperty, resolved.FontSize);
        letters.SetValue(TextElement.FontWeightProperty, resolved.FontWeight);
        letters.SetValue(TextElement.FontStyleProperty, resolved.FontStyle);
        letters.SetValue(TextElement.FontFeaturesProperty, resolved.FontFeatures);
    }

    /// <summary>Where the first baseline of a line set in <paramref name="font"/> lies, from the top of the line.</summary>
    public static double Baseline(SwiftFont font)
    {
        var resolved = font.Resolve();
        using var layout = new TextLayout("Ag", new Typeface(resolved.FontFamily, resolved.FontStyle, resolved.FontWeight), resolved.FontSize, Brushes.Black, fontFeatures: resolved.FontFeatures);
        return layout.TextLines.Count == 0 ? layout.Height : layout.TextLines[0].Baseline;
    }

    /// <summary>Keeps the letters' ink in step with the theme of <paramref name="owner"/>.</summary>
    public static void Follow(Control owner, NthLetters letters, SwiftColor color)
    {
        void Paint() => letters.Foreground = new ImmutableSolidColorBrush(color.Resolve(owner.ActualThemeVariant));
        Paint();
        owner.ActualThemeVariantChanged += (_, _) => Paint();
        owner.AttachedToVisualTree += (_, _) => Paint();
    }
}

/// <summary>The running working line's title: the working beads (Calculi) and the line's word in breathing letters.</summary>
// PORT-ONLY: Calculi WU5 (ED-NEW-calculi-wu5-1; DESIGN.md sections 5.1, 6, 9 and 12). The beads replace the three-arc glyph: an 18 DIP n box in its
// 18.9 x 22.1 DIP motion envelope, centred on the word's line as the canvas's flex row centres it; the pill around the line is taller than the envelope
// (the row makes room with the pill's padding). The slot is always the envelope's size, whether the beads are in it yet or not, so the line's layout never depends on the beads.
internal sealed class NthWorkingTitle : Panel, IBaselineProvider
{
    private const double Gap = 8;
    private static readonly SwiftFont Font = SwiftFont.System(SwiftFont.TextStyle.Body, weight: SwiftFont.Weight.Medium); // :2123 the title's type, kept
    private static readonly Size Envelope = NthBeadsPainter.EnvelopeSize(NthWorkingBeads.GlyphSize);

    private readonly NthWorkingBeads? beads;
    private readonly Panel slot = new() { Width = Envelope.Width, Height = Envelope.Height };
    private readonly NthLetters letters = new(NthLettersMode.Breathe);
    private readonly WordClip word = new();

    public NthWorkingTitle(string word, NthWorkingBeads? beads = null)
    {
        // The word is clipped to its box, as the page's .wd is, so a long summary never leaves the pill. Only the word: the beads' visual draws past its
        // box while it glides after a layout move (ED-NEW-calculi-wu4-1 item 5), and no parent of theirs may cut a bead.
        this.beads = beads;
        NthLineType.Apply(letters, Font);
        NthLineType.Follow(this, letters, Chrome.SecondaryText);
        letters.SetTextInstantly(word);
        this.word.Child = letters;
        Children.Add(slot); Children.Add(this.word);
        if (beads is not null)
        {
            slot.AttachedToVisualTree += (_, _) => beads.Claim(slot);
            slot.DetachedFromVisualTree += (_, _) => beads.Unclaim(slot);
        }
    }

    /// <summary>The beads' slot (tests): it holds the thread's beads once they are here.</summary>
    public Panel Slot => slot;

    /// <summary>The beads, when they are in this title (tests).</summary>
    public NthBeadsControl? Glyph => beads is not null && ReferenceEquals(beads.Beads.Parent, slot) ? beads.Beads : null;

    /// <summary>Gives the beads up (this title is leaving with a fade they must not share).</summary>
    public void ReleaseBeads() { if (beads is not null) { beads.Unclaim(slot); if (ReferenceEquals(beads.Beads.Parent, slot)) slot.Children.Remove(beads.Beads); } }

    /// <summary>The word's letters (tests).</summary>
    public NthLetters Letters => letters;

    /// <summary>The line's word; a new one arrives letter by letter.</summary>
    public string Word { get => letters.Text; set => letters.Text = value; }

    public double Baseline => (DesiredSize.Height - letters.DesiredSize.Height) / 2 + NthLineType.Baseline(Font);

    // The title lays out as the word's line (its height is the word's, as before), and the beads' envelope is centred on that line, reaching past the
    // title's top and bottom by half the difference (3.2 DIP for the 13 pt word): the pill's own padding (8 or 11 DIP) is the room the row makes for it,
    // so the line's height, its baselines and everything under it stay where they were.
    protected override Size MeasureOverride(Size availableSize)
    {
        slot.Measure(Envelope);
        word.Measure(Size.Infinity);
        var width = Envelope.Width + Gap + letters.DesiredSize.Width;
        return new Size(Math.Min(width, availableSize.Width), letters.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        slot.Arrange(new Rect(0, (finalSize.Height - Envelope.Height) / 2, Envelope.Width, Envelope.Height));
        var left = Envelope.Width + Gap;
        word.Arrange(new Rect(left, (finalSize.Height - letters.DesiredSize.Height) / 2, Math.Max(0, Math.Min(letters.DesiredSize.Width, finalSize.Width - left)), letters.DesiredSize.Height));
        return finalSize;
    }

    // Clips the word to the room the title has, and lays the letters out at their own width as the title did (the old title clipped them itself).
    private sealed class WordClip : Decorator
    {
        public WordClip() => ClipToBounds = true;
        protected override Size MeasureOverride(Size availableSize) { Child?.Measure(Size.Infinity); return Child?.DesiredSize ?? default; }
        protected override Size ArrangeOverride(Size finalSize) { Child?.Arrange(new Rect(Child.DesiredSize)); return finalSize; }
    }
}

/// <summary>The working line's clock (HydraProgressBar.swift:541-562 as the line used it): the same text, ticking once a second while the turn runs, but drawn as rolling digits.</summary>
internal sealed class NthElapsedTime : Decorator, IBaselineProvider
{
    private static readonly SwiftFont Font = SwiftFont.System(SwiftFont.TextStyle.Subheadline, SwiftFont.Design.Monospaced, SwiftFont.Weight.Medium); // mono, tabular (the page: 500 11.5 mono)

    private readonly NthLetters letters = new(NthLettersMode.Roll) { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private DateTimeOffset startedAt;
    private DateTimeOffset? finishedAt;
    private bool isRunning;
    private ITimer? nextTick;
    private bool attached;
    private int ticking; // the generation of the tick in force: a tick of an earlier one does nothing

    public NthElapsedTime(DateTimeOffset startedAt, DateTimeOffset? finishedAt, bool isRunning)
    {
        NthLineType.Apply(letters, Font);
        NthLineType.Follow(this, letters, Chrome.PrimaryText.Opacity(0.58)); // --text3: quieter than the word
        Child = letters;
        this.startedAt = startedAt; this.finishedAt = finishedAt; this.isRunning = isRunning;
        if (!isRunning) letters.SetTextInstantly(Finished());
        letters.AttachedToVisualTree += (_, _) => { attached = true; if (this.isRunning) Tick(++ticking); };
        letters.DetachedFromVisualTree += (_, _) => { attached = false; ticking++; nextTick?.Dispose(); nextTick = null; };
    }

    /// <summary>The time's digits (tests).</summary>
    public NthLetters Letters => letters;

    /// <summary>The time shown.</summary>
    public string Text => letters.Text;

    public double Baseline => (DesiredSize.Height - letters.DesiredSize.Height) / 2 + NthLineType.Baseline(Font);

    private string Finished() => RelativeTime.Duration((finishedAt ?? SwiftDate.Now) - startedAt);

    private void Tick(int generation)
    {
        nextTick?.Dispose(); nextTick = null;
        if (!attached || !isRunning || generation != ticking) return;
        var now = SwiftDate.Now;
        var n = Math.Floor((now - startedAt).TotalSeconds);
        var gridDate = startedAt.AddSeconds(n);
        letters.Text = RelativeTime.Duration(gridDate - startedAt);
        nextTick = SwiftClock.Provider.Current.CreateTimer(_ => MainActor.Post(() => Tick(generation)), null,
            startedAt.AddSeconds(n + 1) - now, Timeout.InfiniteTimeSpan);
    }

    /// <summary>The clock handed its inputs again: the same text, now counting from <paramref name="nextStartedAt"/>, ticking only while it runs.
    /// When the words change the digits that differ roll (the letters' own motion).</summary>
    internal void Update(DateTimeOffset nextStartedAt, DateTimeOffset? nextFinishedAt, bool nextIsRunning)
    {
        if (nextStartedAt == startedAt && nextFinishedAt == finishedAt && nextIsRunning == isRunning) return;
        startedAt = nextStartedAt; finishedAt = nextFinishedAt; isRunning = nextIsRunning;
        var generation = ++ticking; nextTick?.Dispose(); nextTick = null;
        if (isRunning) letters.Text = RelativeTime.Duration(startedAt.AddSeconds(Math.Floor((SwiftDate.Now - startedAt).TotalSeconds)) - startedAt);
        else letters.Text = Finished();
        if (isRunning) Tick(generation);
    }
}
